using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using DocuArchivePro.Data;
using DocuArchivePro.Models;
using DocuArchivePro.Services;

namespace DocuArchivePro.Views
{
    public partial class VaultSettingsDialog : Window
    {
        private readonly IFileStorageService _fileStorageService;
        public ILocalizationService Loc => LocalizationService.Instance;
        public ObservableCollection<DocumentAttachment> AttachmentsList { get; } = new();

        public VaultSettingsDialog(IFileStorageService fileStorageService)
        {
            InitializeComponent();
            DataContext = this;
            _fileStorageService = fileStorageService;
            DgAttachments.ItemsSource = AttachmentsList;

            Loaded += async (s, e) => await LoadVaultDataAsync();
        }

        private async Task LoadVaultDataAsync()
        {
            TxtVaultPath.Text = _fileStorageService.StorageRootDirectory;

            using var db = new ArchiveDbContext();
            var list = await db.Attachments.OrderByDescending(a => a.UploadedAt).ToListAsync();
            AttachmentsList.Clear();
            foreach (var a in list) AttachmentsList.Add(a);

            long bytes = _fileStorageService.GetTotalStorageSizeBytes();
            string mb = (bytes / (1024.0 * 1024.0)).ToString("F2", CultureInfo.InvariantCulture);
            bool isAr = Loc.IsArabic;

            string statsText = isAr
                ? $"إجمالي الملفات المؤرشفة المشفرة: {AttachmentsList.Count} ملف  |  الحجم الإجمالي: {mb} م.ب  |  التشفير: AES-256 (.denc)"
                : $"Total Encrypted Archived Files: {AttachmentsList.Count} files  |  Total Size: {mb} MB  |  Encryption: AES-256 (.denc)";

            TxtVaultStats.Text = DateTimeHelper.FormatDigits(statsText, isAr);
        }

        private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = Loc.IsArabic ? "اختر مجلد حفظ وتشفير الصور والملفات المؤرشفة" : "Select Folder for Encrypted Archive Vault",
                InitialDirectory = Directory.Exists(TxtVaultPath.Text) ? TxtVaultPath.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dlg.ShowDialog(this) == true)
            {
                TxtVaultPath.Text = dlg.FolderName;
            }
        }

        private async void BtnSaveVaultPath_Click(object sender, RoutedEventArgs e)
        {
            bool isAr = Loc.IsArabic;
            string newPath = TxtVaultPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(newPath))
            {
                MessageBox.Show(
                    isAr ? "يرجى اختيار أو كتابة مسار صالح لحفظ الملفات المؤرشفة." : "Please select or enter a valid folder path.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string title = isAr ? "تغيير مكان حفظ الملفات المؤرشفة" : "Change Encrypted Vault Path";
            string msg = isAr
                ? $"هل أنت متأكد من رغبتك في اعتماد المسار الجديد لحفظ وتشفير الملفات المؤرشفة؟\n\nالمسار الجديد:\n{newPath}"
                : $"Are you sure you want to adopt the new vault directory for encrypted files?\n\nNew Path:\n{newPath}";

            if (!ActionPinDialog.ConfirmWarningAndVerifyPin(title, msg, this))
            {
                return;
            }

            try
            {
                int migrated = await _fileStorageService.ChangeVaultDirectoryAsync(newPath, ChkMigrateExisting.IsChecked == true);
                await LoadVaultDataAsync();
                string okMsg = isAr
                    ? $"تم حفظ مسار المستودع الجديد بنجاح.\nعدد الملفات المشفرة المنقولة: {DateTimeHelper.FormatNumber(migrated, true)}"
                    : $"New vault directory saved successfully.\nEncrypted files migrated: {migrated}";
                MessageBox.Show(okMsg, Loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    isAr ? $"حدث خطأ أثناء تغيير المسار:\n{ex.Message}" : $"Error changing vault path:\n{ex.Message}",
                    Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnEncryptAllNow_Click(object sender, RoutedEventArgs e)
        {
            bool isAr = Loc.IsArabic;
            try
            {
                int count = await _fileStorageService.EncryptExistingUnencryptedFilesAsync();
                await LoadVaultDataAsync();
                string msg = isAr
                    ? $"تم فحص المستودع وتشفير الملفات بنجاح (عدد الملفات التي تم تحويلها للتشفير الآن: {DateTimeHelper.FormatNumber(count, true)}).\nلا يمكن لأي مستخدم فتح الملفات من خارج البرنامج."
                    : $"Vault scan and encryption complete (newly encrypted files: {count}).\nFiles cannot be opened outside the application.";
                MessageBox.Show(msg, Loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    isAr ? $"خطأ أثناء التشفير:\n{ex.Message}" : $"Encryption error:\n{ex.Message}",
                    Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(_fileStorageService.StorageRootDirectory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _fileStorageService.StorageRootDirectory,
                    UseShellExecute = true
                });
            }
        }

        private void BtnPreviewSelected_Click(object sender, RoutedEventArgs e)
        {
            if (DgAttachments.SelectedItem is DocumentAttachment att)
            {
                _fileStorageService.OpenAttachmentInApp(att);
            }
            else
            {
                MessageBox.Show(
                    Loc.IsArabic ? "يرجى اختيار ملف مؤرشف من الجدول لاستعراضه داخل البرنامج." : "Please select an archived file from the table to preview.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void DgAttachments_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DgAttachments.SelectedItem is DocumentAttachment att)
            {
                _fileStorageService.OpenAttachmentInApp(att);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}