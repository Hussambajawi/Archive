using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocuArchivePro.Models;
using DocuArchivePro.Services;

namespace DocuArchivePro.Views
{
    public partial class AttachmentViewerDialog : Window
    {
        private byte[] _decryptedBytes = Array.Empty<byte>();
        private string _originalFileName = string.Empty;
        private string _extension = string.Empty;
        private string? _tempViewingFilePath;
        private bool _keepTempFileForExternalProcess;
        private BitmapImage? _loadedBitmap;
        private FlowDocument? _loadedFlowDoc;
        private double _zoomFactor = 1.0;
        private double _rotationAngle = 0;

        private readonly List<DocumentAttachment> _attachments = new();
        private int _currentIndex;
        private bool _isUpdatingSelection;

        public ILocalizationService Loc => LocalizationService.Instance;

        public AttachmentViewerDialog(byte[] decryptedBytes, string originalFileName, string fileHash = "")
        {
            InitializeComponent();
            DataContext = this;

            UpdateHeaderAndContentState(decryptedBytes, originalFileName, fileHash);

            TryCleanStaleTempFiles();
            Loaded += AttachmentViewerDialog_Loaded;
            Closed += AttachmentViewerDialog_Closed;
        }

        public AttachmentViewerDialog(IReadOnlyList<DocumentAttachment> attachments, int initialIndex = 0)
        {
            InitializeComponent();
            DataContext = this;

            TryCleanStaleTempFiles();

            if (attachments != null && attachments.Count > 0)
            {
                _attachments.AddRange(attachments);
                _currentIndex = Math.Clamp(initialIndex, 0, _attachments.Count - 1);
            }

            if (_attachments.Count > 1)
            {
                AttachmentNavBar.Visibility = Visibility.Visible;
                _isUpdatingSelection = true;
                CmbAttachmentSelector.ItemsSource = _attachments;
                CmbAttachmentSelector.SelectedIndex = _currentIndex;
                _isUpdatingSelection = false;
                UpdateNavigationState();
            }

            Loaded += AttachmentViewerDialog_Loaded;
            Closed += AttachmentViewerDialog_Closed;
        }

        private bool _lastLoadedHashValid = true;

        private void UpdateHeaderAndContentState(byte[] decryptedBytes, string originalFileName, string fileHash)
        {
            _decryptedBytes = decryptedBytes ?? Array.Empty<byte>();
            _originalFileName = originalFileName ?? string.Empty;
            _extension = Path.GetExtension(_originalFileName).ToLowerInvariant();

            string computedHash = _decryptedBytes.Length > 0
                ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_decryptedBytes)).ToLowerInvariant()
                : string.Empty;

            _lastLoadedHashValid = string.IsNullOrWhiteSpace(fileHash) ||
                                   string.Equals(computedHash, fileHash.Trim(), StringComparison.OrdinalIgnoreCase);

            string effectiveHash = !string.IsNullOrWhiteSpace(computedHash) ? computedHash : fileHash;

            TxtFileName.Text = _originalFileName;
            string kb = (_decryptedBytes.Length / 1024.0).ToString("F1", CultureInfo.InvariantCulture);
            bool isAr = Loc.IsArabic;
            string shortHash = string.IsNullOrEmpty(effectiveHash)
                ? (isAr ? "تم التحقق" : "Verified")
                : effectiveHash.Substring(0, Math.Min(20, effectiveHash.Length)) + "...";

            string hashDisplay = _lastLoadedHashValid
                ? (isAr ? $"✅ مطابقة ({shortHash})" : $"✅ Verified ({shortHash})")
                : (isAr ? $"⚠️ غير متطابقة! ({shortHash})" : $"⚠️ Hash Mismatch! ({shortHash})");

            TxtFileMeta.Foreground = _lastLoadedHashValid
                ? new SolidColorBrush(Color.FromRgb(148, 163, 184))
                : new SolidColorBrush(Color.FromRgb(248, 113, 113));

            string meta = isAr
                ? $"الحجم الأصلي: {kb} ك.ب  |  النوع: {_extension.ToUpperInvariant()}  |  البصمة الرقمية SHA-256: {hashDisplay}"
                : $"Original Size: {kb} KB  |  Type: {_extension.ToUpperInvariant()}  |  SHA-256 Digest: {hashDisplay}";

            TxtFileMeta.Text = DateTimeHelper.FormatDigits(meta, isAr);
        }

        private void UpdateNavigationState()
        {
            if (_attachments.Count <= 1)
            {
                AttachmentNavBar.Visibility = Visibility.Collapsed;
                return;
            }

            AttachmentNavBar.Visibility = Visibility.Visible;
            BtnPrevAttachment.IsEnabled = _currentIndex > 0;
            BtnNextAttachment.IsEnabled = _currentIndex < _attachments.Count - 1;

            bool isAr = Loc.IsArabic;
            string posText = isAr
                ? $"مرفق {_currentIndex + 1} من {_attachments.Count}"
                : $"Attachment {_currentIndex + 1} of {_attachments.Count}";
            TxtAttachmentPosition.Text = DateTimeHelper.FormatDigits(posText, isAr);

            _isUpdatingSelection = true;
            CmbAttachmentSelector.SelectedIndex = _currentIndex;
            _isUpdatingSelection = false;
        }

        private void AttachmentViewerDialog_Loaded(object sender, RoutedEventArgs e)
        {
            if (_attachments.Count > 0)
            {
                LoadAttachmentAtIndex(_currentIndex);
            }
            else
            {
                RenderCurrentContent();
            }
        }

        private void LoadAttachmentAtIndex(int index)
        {
            if (index < 0 || index >= _attachments.Count) return;

            _currentIndex = index;
            UpdateNavigationState();
            CleanupCurrentTempFileIfUnused();

            var att = _attachments[_currentIndex];
            try
            {
                if (!File.Exists(att.StoragePath))
                {
                    MessageBox.Show(
                        Loc.IsArabic
                            ? $"الملف المؤرشف غير موجود في المسار:\n{att.StoragePath}"
                            : $"Archived file not found at path:\n{att.StoragePath}",
                        Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                byte[] diskBytes = File.ReadAllBytes(att.StoragePath);
                byte[] decrypted = FileStorageService.DecryptBytes(diskBytes);
                UpdateHeaderAndContentState(decrypted, att.FileName, att.FileHashSha256);
                RenderCurrentContent();

                if (!_lastLoadedHashValid)
                {
                    MessageBox.Show(
                        Loc.IsArabic
                            ? $"⚠️ تحذير أمني: البصمة الرقمية SHA-256 للملف ({att.FileName}) لا تتطابق مع البصمة المسجلة عند الأرشفة!\nقد يكون الملف قد تعرض للتلف الجزئي أو التعديل على القرص."
                            : $"⚠️ Security Warning: SHA-256 digest for ({att.FileName}) does not match the archived hash!\nThe file may be partially corrupted or modified on disk.",
                        Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Loc.IsArabic ? $"تعذر فك تشفير أو استعراض الملف:\n{ex.Message}" : $"Failed to decrypt or preview file:\n{ex.Message}",
                    Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RenderCurrentContent()
        {
            // Reset viewer state cleanly before loading new attachment
            _zoomFactor = 1.0;
            _rotationAngle = 0;
            if (ImgScale != null) { ImgScale.ScaleX = 1.0; ImgScale.ScaleY = 1.0; }
            if (ImgRotate != null) { ImgRotate.Angle = 0; }
            _loadedBitmap = null;
            ImgDecrypted.Source = null;
            _loadedFlowDoc = null;
            WordDocViewer.Document = null;

            ImageScrollViewer.Visibility = Visibility.Collapsed;
            TxtDecryptedContent.Visibility = Visibility.Collapsed;
            DocPreviewPanel.Visibility = Visibility.Collapsed;
            PdfWebBrowser.Visibility = Visibility.Collapsed;
            WordDocViewer.Visibility = Visibility.Collapsed;
            BtnRotate.Visibility = Visibility.Collapsed;

            try
            {
                if (IsImageExtension(_extension))
                {
                    BtnRotate.Visibility = Visibility.Visible;
                    BtnZoomIn.Visibility = Visibility.Visible;
                    BtnZoomOut.Visibility = Visibility.Visible;
                    BtnZoomReset.Visibility = Visibility.Visible;
                    ImageScrollViewer.Visibility = Visibility.Visible;

                    using var ms = new MemoryStream(_decryptedBytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();

                    _loadedBitmap = bmp;
                    ImgDecrypted.Source = _loadedBitmap;
                }
                else if (IsTextExtension(_extension))
                {
                    TxtDecryptedContent.Visibility = Visibility.Visible;
                    BtnZoomIn.Visibility = Visibility.Collapsed;
                    BtnZoomOut.Visibility = Visibility.Collapsed;
                    BtnZoomReset.Visibility = Visibility.Collapsed;
                    TxtDecryptedContent.Text = Encoding.UTF8.GetString(_decryptedBytes);
                }
                else if (DocumentPrintExportService.IsWordExtension(_extension))
                {
                    DocPreviewPanel.Visibility = Visibility.Visible;
                    BtnZoomIn.Visibility = Visibility.Collapsed;
                    BtnZoomOut.Visibility = Visibility.Collapsed;
                    BtnZoomReset.Visibility = Visibility.Collapsed;

                    CreateTempSessionFile();
                    PdfWebBrowser.Visibility = Visibility.Collapsed;
                    WordDocViewer.Visibility = Visibility.Visible;

                    _loadedFlowDoc = DocumentPrintExportService.LoadWordFlowDocument(_decryptedBytes, _extension, _originalFileName);
                    WordDocViewer.Document = _loadedFlowDoc;
                }
                else
                {
                    DocPreviewPanel.Visibility = Visibility.Visible;
                    BtnZoomIn.Visibility = Visibility.Collapsed;
                    BtnZoomOut.Visibility = Visibility.Collapsed;
                    BtnZoomReset.Visibility = Visibility.Collapsed;

                    CreateTempSessionFile();
                    if (_tempViewingFilePath != null && _extension == ".pdf")
                    {
                        WordDocViewer.Visibility = Visibility.Collapsed;
                        PdfWebBrowser.Visibility = Visibility.Visible;
                        PdfWebBrowser.Navigate(new Uri(_tempViewingFilePath));
                    }
                    else
                    {
                        PdfWebBrowser.Visibility = Visibility.Collapsed;
                        WordDocViewer.Visibility = Visibility.Visible;
                        _loadedFlowDoc = DocumentPrintExportService.LoadWordFlowDocument(_decryptedBytes, _extension, _originalFileName);
                        WordDocViewer.Document = _loadedFlowDoc;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Loc.IsArabic ? $"تعذر عرض الملف المؤرشف:\n{ex.Message}" : $"Failed to preview archived file:\n{ex.Message}",
                    Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnPrevAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex > 0)
            {
                LoadAttachmentAtIndex(_currentIndex - 1);
            }
        }

        private void BtnNextAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex < _attachments.Count - 1)
            {
                LoadAttachmentAtIndex(_currentIndex + 1);
            }
        }

        private void CmbAttachmentSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingSelection) return;
            int idx = CmbAttachmentSelector.SelectedIndex;
            if (idx >= 0 && idx != _currentIndex && idx < _attachments.Count)
            {
                LoadAttachmentAtIndex(idx);
            }
        }

        private static bool IsImageExtension(string ext)
        {
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif" || ext == ".tiff" || ext == ".tif";
        }

        private static bool IsTextExtension(string ext)
        {
            return ext == ".txt" || ext == ".csv" || ext == ".json" || ext == ".xml" || ext == ".log" || ext == ".md";
        }

        private void CreateTempSessionFile()
        {
            if (_tempViewingFilePath != null && File.Exists(_tempViewingFilePath)) return;

            string tempDir = Path.Combine(Path.GetTempPath(), "DocuArchive_SecureSession");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            _tempViewingFilePath = Path.Combine(tempDir, $"View_{Guid.NewGuid():N}{_extension}");
            File.WriteAllBytes(_tempViewingFilePath, _decryptedBytes);
            _keepTempFileForExternalProcess = false;
        }

        private void CleanupCurrentTempFileIfUnused()
        {
            try
            {
                if (PdfWebBrowser != null)
                {
                    PdfWebBrowser.Navigate("about:blank");
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(_tempViewingFilePath) && !_keepTempFileForExternalProcess)
            {
                string pathToDelete = _tempViewingFilePath;
                _tempViewingFilePath = null;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    for (int i = 0; i < 5; i++)
                    {
                        try
                        {
                            if (File.Exists(pathToDelete))
                            {
                                File.Delete(pathToDelete);
                            }
                            break;
                        }
                        catch (IOException)
                        {
                            await System.Threading.Tasks.Task.Delay(300);
                        }
                        catch
                        {
                            break;
                        }
                    }
                });
            }
            else
            {
                _tempViewingFilePath = null;
            }

            _keepTempFileForExternalProcess = false;
        }

        private static void TryCleanStaleTempFiles()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "DocuArchive_SecureSession");
                    if (Directory.Exists(tempDir))
                    {
                        foreach (var file in Directory.GetFiles(tempDir))
                        {
                            try
                            {
                                if (File.GetCreationTime(file) < DateTime.Now.AddMinutes(-30))
                                {
                                    File.Delete(file);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            });
        }

        private void BtnRotate_Click(object sender, RoutedEventArgs e)
        {
            _rotationAngle = (_rotationAngle + 90) % 360;
            if (ImgRotate != null)
            {
                ImgRotate.Angle = _rotationAngle;
            }
        }

        private bool EnsurePrintOrExportPermission()
        {
            var user = SecurityService.Instance.CurrentUser;
            if (user == null || (!user.IsAdmin && !user.CanPrint))
            {
                MessageBox.Show(
                    Loc.IsArabic
                        ? "عذراً، طباعة أو تصدير أو فتح نسخ من المرفقات خارج البرنامج يتطلب صلاحية الطباعة والتصدير."
                        : "Sorry, printing, exporting, or opening attachments externally requires Print/Export permission.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private void BtnSaveCopy_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsurePrintOrExportPermission()) return;

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                FileName = _originalFileName,
                Filter = $"{_extension.ToUpperInvariant().TrimStart('.')} Files (*{_extension})|*{_extension}|All Files (*.*)|*.*",
                Title = Loc.IsArabic ? "حفظ وتصدير نسخة من المرفق" : "Save Copy of Attachment"
            };

            if (sfd.ShowDialog(this) == true)
            {
                try
                {
                    File.WriteAllBytes(sfd.FileName, _decryptedBytes);
                    _ = new DocumentService(new FileStorageService()).LogAuditAsync("تصدير نسخة مرفق / Export Attachment Copy", $"{_originalFileName} -> {sfd.FileName}");
                    MessageBox.Show(
                        Loc.IsArabic ? $"تم حفظ نسخة من المرفق بنجاح إلى:\n{sfd.FileName}" : $"Attachment copy saved successfully to:\n{sfd.FileName}",
                        Loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        Loc.IsArabic ? $"تعذر حفظ الملف:\n{ex.Message}" : $"Failed to save file:\n{ex.Message}",
                        Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnOpenExternal_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsurePrintOrExportPermission()) return;

            try
            {
                CreateTempSessionFile();
                if (_tempViewingFilePath != null && File.Exists(_tempViewingFilePath))
                {
                    _keepTempFileForExternalProcess = true;
                    _ = new DocumentService(new FileStorageService()).LogAuditAsync("فتح مرفق بتطبيق خارجي / Open Attachment Externally", _originalFileName);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _tempViewingFilePath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Loc.IsArabic ? $"تعذر فتح الملف بالتطبيق الافتراضي:\n{ex.Message}" : $"Unable to open file in default app:\n{ex.Message}",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnOpenTempProtected_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsurePrintOrExportPermission()) return;

            try
            {
                CreateTempSessionFile();
                if (_tempViewingFilePath != null && File.Exists(_tempViewingFilePath))
                {
                    _keepTempFileForExternalProcess = true;
                    _ = new DocumentService(new FileStorageService()).LogAuditAsync("فتح مرفق بتطبيق خارجي / Open Attachment Externally", _originalFileName);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _tempViewingFilePath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Loc.IsArabic ? $"تعذر فتح العرض المؤقت: {ex.Message}" : $"Unable to open protected session: {ex.Message}",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _zoomFactor = Math.Min(4.0, _zoomFactor + 0.25);
            ImgScale.ScaleX = _zoomFactor;
            ImgScale.ScaleY = _zoomFactor;
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _zoomFactor = Math.Max(0.25, _zoomFactor - 0.25);
            ImgScale.ScaleX = _zoomFactor;
            ImgScale.ScaleY = _zoomFactor;
        }

        private void BtnZoomReset_Click(object sender, RoutedEventArgs e)
        {
            _zoomFactor = 1.0;
            ImgScale.ScaleX = 1.0;
            ImgScale.ScaleY = 1.0;
        }

        private FlowDocument BuildTextFlowDocument()
        {
            return new FlowDocument(new Paragraph(new Run(TxtDecryptedContent.Text)))
            {
                Background = Brushes.White,
                Foreground = Brushes.Black,
                FlowDirection = Loc.CurrentFlowDirection,
                PagePadding = new Thickness(40),
                FontFamily = Application.Current?.Resources["AppFontFamily"] as FontFamily ?? new FontFamily("Segoe UI, Tahoma, Arial")
            };
        }

        private void BtnPrintAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsurePrintOrExportPermission()) return;

            var printDlg = new PrintDialog();
            if (printDlg.ShowDialog() != true) return;

            try
            {
                _ = new DocumentService(new FileStorageService()).LogAuditAsync("طباعة مرفق / Print Attachment", _originalFileName);
                // 1. PDF Attachment: preserve exact PDF bytes or send PDF to physical printer
                if (_extension == ".pdf")
                {
                    if (DocumentPrintExportService.IsVirtualPdfPrinter(printDlg.PrintQueue))
                    {
                        DocumentPrintExportService.SavePdfFromBytesFileDialog(this, _decryptedBytes, _originalFileName);
                    }
                    else
                    {
                        CreateTempSessionFile();
                        if (!string.IsNullOrEmpty(_tempViewingFilePath))
                        {
                            _keepTempFileForExternalProcess = true;
                            DocumentPrintExportService.PrintExistingPdfToPhysicalPrinter(printDlg, _tempViewingFilePath);
                        }
                    }
                    return;
                }

                // 2. Image Attachment
                if (_loadedBitmap != null)
                {
                    if (DocumentPrintExportService.IsVirtualPdfPrinter(printDlg.PrintQueue))
                    {
                        DocumentPrintExportService.SaveBitmapToPdfFileDialog(this, _loadedBitmap, _originalFileName);
                    }
                    else
                    {
                        var vis = new DrawingVisual();
                        using (var dc = vis.RenderOpen())
                        {
                            double pageW = printDlg.PrintableAreaWidth;
                            double pageH = printDlg.PrintableAreaHeight;
                            double margin = 30;
                            Rect rect = new Rect(margin, margin, Math.Max(100, pageW - margin * 2), Math.Max(100, pageH - margin * 2));
                            dc.DrawImage(_loadedBitmap, rect);
                        }
                        string safeTitle = DocumentPrintExportService.SanitizePrintJobTitle($"{(Loc.IsArabic ? "طباعة مرفق" : "Print Attachment")} - {_originalFileName}");
                        printDlg.PrintVisual(vis, safeTitle);
                    }
                    return;
                }

                // 3. Word (.docx / .doc / .rtf / .odt / .html) Attachment
                if (_loadedFlowDoc != null)
                {
                    DocumentPrintExportService.PrintOrExportFlowDocument(
                        printDlg,
                        _loadedFlowDoc,
                        _originalFileName,
                        this,
                        freshDocFactory: () => DocumentPrintExportService.LoadWordFlowDocument(_decryptedBytes, _extension, _originalFileName),
                        rawWordBytes: _decryptedBytes,
                        wordExt: _extension);
                    return;
                }

                // 4. Plain Text Attachment
                var textDoc = BuildTextFlowDocument();
                DocumentPrintExportService.PrintOrExportFlowDocument(
                    printDlg,
                    textDoc,
                    _originalFileName,
                    this,
                    freshDocFactory: BuildTextFlowDocument);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Loc.IsArabic ? $"تعذر تنفيذ الطباعة أو الحفظ:\n{ex.Message}" : $"Failed to print or export:\n{ex.Message}",
                    Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void AttachmentViewerDialog_Closed(object? sender, EventArgs e)
        {
            try
            {
                PdfWebBrowser?.Navigate("about:blank");
                PdfWebBrowser?.Dispose();
            }
            catch { }

            CleanupCurrentTempFileIfUnused();
        }
    }
}