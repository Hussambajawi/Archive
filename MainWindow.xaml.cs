using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using DocuArchivePro.Models;
using DocuArchivePro.Services;
using DocuArchivePro.ViewModels;
using DocuArchivePro.Views;

namespace DocuArchivePro
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly IDocumentService _docService;
        private readonly IScannerService _scannerService;
        private readonly IFileStorageService _storageService;
        private readonly ILocalizationService _locService;

        private bool _autoBackupCompletedOnClose;
        private bool _isRunningCloseBackup;

        public MainWindow(
            MainViewModel vm,
            IDocumentService docService,
            IScannerService scannerService,
            IFileStorageService storageService,
            ILocalizationService locService)
        {
            InitializeComponent();
            _vm = vm;
            _docService = docService;
            _scannerService = scannerService;
            _storageService = storageService;
            _locService = locService;

            DataContext = _vm;
            Closing += MainWindow_Closing;

            _vm.RequestOpenDocumentDialog += OpenDocumentDialog;
            _vm.RequestOpenScannerDialog += OpenScannerDialog;
            _vm.RequestOpenVaultSettingsDialog += OpenVaultSettingsDialog;
            _vm.RequestOpenPrintDialog += OpenPrintDialog;
            _vm.RequestOpenUserLoginDialog += OpenUserLoginDialog;
            _vm.RequestOpenCalendarDialog += OpenCalendarDialog;
            _vm.RequestOpenAboutDialog += OpenAboutDialog;
            _vm.RequestOpenAddCategoryDialog += OpenAddCategoryDialog;
            _vm.RequestOpenEditCategoryDialog += OpenEditCategoryDialog;
            _vm.RequestOpenDeleteCategoryWizard += OpenDeleteCategoryWizard;
            _vm.RequestImportCategoryBackup += ImportCategoryBackupAsync;
            _vm.RequestOpenAddEntityDialog += OpenAddEntityDialog;
            _vm.RequestOpenEditEntityDialog += OpenEditEntityDialog;
            _vm.RequestOpenMergeEntitiesDialog += OpenMergeEntitiesDialog;
            _vm.FocusSearchAction = () =>
            {
                TxtSearchBox.Focus();
                TxtSearchBox.SelectAll();
            };
        }

        private void OpenDocumentDialog(Document? doc, DocumentType type)
        {
            var entryVm = new DocumentEntryViewModel(_docService, _scannerService, _storageService, _locService, doc, type);
            var dlg = new DocumentEntryDialog(entryVm)
            {
                Owner = this
            };

            if (dlg.ShowDialog() == true)
            {
                _ = _vm.LoadDataAsync();
            }
        }

        private async void OpenScannerDialog()
        {
            var dlg = new ScannerDialog(_scannerService)
            {
                Owner = this
            };

            if (dlg.ShowDialog() == true && dlg.ScannedResultBytes != null)
            {
                // Open new document entry with this scanned page (or multi-page PDF) already attached
                bool isPdf = dlg.ScannedResultBytes.Length >= 4 &&
                             dlg.ScannedResultBytes[0] == 0x25 &&
                             dlg.ScannedResultBytes[1] == 0x50 &&
                             dlg.ScannedResultBytes[2] == 0x44 &&
                             dlg.ScannedResultBytes[3] == 0x46;
                string ext = isPdf ? ".pdf" : ".png";
                string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ScannedDoc_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{ext}");
                await System.IO.File.WriteAllBytesAsync(tempFile, dlg.ScannedResultBytes);

                var entryVm = new DocumentEntryViewModel(_docService, _scannerService, _storageService, _locService);
                entryVm.TrackTempScanFile(tempFile);
                entryVm.PendingFilePaths.Add(tempFile);

                var entryDlg = new DocumentEntryDialog(entryVm)
                {
                    Owner = this
                };

                if (entryDlg.ShowDialog() == true)
                {
                    await _vm.LoadDataAsync();
                }
            }
        }

        private async void OpenVaultSettingsDialog()
        {
            var dlg = new VaultSettingsDialog(_storageService)
            {
                Owner = this
            };
            dlg.ShowDialog();
            await _vm.LoadDataAsync();
        }

        private async void OpenPrintDialog(Document? selectedDoc, int initialReportIndex)
        {
            var allDocs = await _docService.GetAllDocumentsAsync();
            var dlg = new PrintReportDialog(allDocs, selectedDoc, initialReportIndex)
            {
                Owner = this
            };
            dlg.ShowDialog();
        }

        private async void OpenUserLoginDialog()
        {
            var dlg = new UserLoginDialog(SecurityService.Instance)
            {
                Owner = this
            };
            dlg.ShowDialog();
            await _vm.LoadDataAsync();
        }

        private void OpenCalendarDialog()
        {
            var dlg = new DateTimeCalendarDialog
            {
                Owner = this
            };
            dlg.ShowDialog();
        }

        private void OpenAboutDialog()
        {
            var dlg = new AboutDialog
            {
                Owner = this
            };
            dlg.ShowDialog();
        }

        private async void OpenAddCategoryDialog()
        {
            var dlg = new AddCategoryDialog(_docService)
            {
                Owner = this
            };
            if (dlg.ShowDialog() == true)
            {
                await _vm.LoadDataAsync();
            }
        }

        private async void OpenEditCategoryDialog(Category category)
        {
            var dlg = new AddCategoryDialog(_docService, category)
            {
                Owner = this
            };
            if (dlg.ShowDialog() == true)
            {
                await _vm.LoadDataAsync();
            }
        }

        private async void OpenDeleteCategoryWizard(Category category)
        {
            var dlg = new DeleteCategoryWizardDialog(_docService, category)
            {
                Owner = this
            };
            if (dlg.ShowDialog() == true)
            {
                await _vm.LoadDataAsync();
            }
        }

        private async void OpenAddEntityDialog()
        {
            var dlg = new AddEntityDialog(_docService)
            {
                Owner = this
            };
            if (dlg.ShowDialog() == true)
            {
                await _vm.LoadDataAsync();
            }
        }

        private async void OpenEditEntityDialog(CorrespondentEntity entity)
        {
            var dlg = new AddEntityDialog(_docService, entity)
            {
                Owner = this
            };
            if (dlg.ShowDialog() == true)
            {
                await _vm.LoadDataAsync();
            }
        }

        private async void OpenMergeEntitiesDialog()
        {
            var dlg = new MergeEntitiesDialog(_docService, _vm.CorrespondentEntities)
            {
                Owner = this
            };
            if (dlg.ShowDialog() == true)
            {
                await _vm.LoadDataAsync();
            }
        }

        private async void ImportCategoryBackupAsync()
        {
            bool isAr = _locService.IsArabic;
            var folderDlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = isAr ? "اختر مجلد النسخة الاحتياطية للإضبارة لاستيرادها" : "Select Dossier Backup Folder to Import"
            };

            if (folderDlg.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                var result = await _docService.ImportCategoryBackupAsync(folderDlg.FolderName);
                await _vm.LoadDataAsync();

                string docsNum = DateTimeHelper.FormatNumber(result.ImportedDocumentsCount, isAr);
                string filesNum = DateTimeHelper.FormatNumber(result.ImportedAttachmentsCount, isAr);
                string msg = isAr
                    ? $"تم استيراد الإضبارة الاحتياطية ({result.RestoredCategory.DisplayName}) بنجاح!\n\nالمعاملات المستردة: {docsNum}\nالمرفقات المشفرة والمستردة: {filesNum}"
                    : $"Dossier backup ({result.RestoredCategory.DisplayName}) imported successfully!\n\nRestored Documents: {docsNum}\nRestored Encrypted Attachments: {filesNum}";

                MessageBox.Show(msg, _locService["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    isAr ? $"فشل استيراد الإضبارة الاحتياطية:\n{ex.Message}" : $"Failed to import dossier backup:\n{ex.Message}",
                    _locService["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_vm.SelectedDocument != null)
            {
                if (_vm.SelectedDocument.Attachments != null && _vm.SelectedDocument.Attachments.Count > 0)
                {
                    var allAtts = _vm.SelectedDocument.Attachments.OrderBy(a => a.Id).ToList();
                    _storageService.OpenAttachmentsInApp(allAtts, 0);
                }
                else if (!_vm.IsRecycleBinTab)
                {
                    _vm.EditDocumentCommand.Execute(null);
                }
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Delta > 0)
                    _vm.UiScale += 0.05;
                else if (e.Delta < 0)
                    _vm.UiScale -= 0.05;

                e.Handled = true;
            }
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_autoBackupCompletedOnClose) return;
            if (_isRunningCloseBackup)
            {
                e.Cancel = true;
                return;
            }

            try
            {
                var settings = await SecurityService.Instance.GetSettingsAsync();
                if (settings.AutoBackupEnabled &&
                    !string.IsNullOrWhiteSpace(settings.AutoBackupDirectory) &&
                    settings.LastFullBackupAt?.Date != DateTime.Today)
                {
                    e.Cancel = true;
                    _isRunningCloseBackup = true;
                    Cursor = Cursors.Wait;
                    Title = _locService.IsArabic
                        ? "⏳ جارٍ حفظ النسخة الاحتياطية التلقائية اليومية للنظام قبل الإغلاق..."
                        : "⏳ Saving daily automatic system backup before closing...";

                    await _docService.CreateFullSystemBackupZipAsync(settings.AutoBackupDirectory);
                    int retention = settings.AutoBackupRetentionCount > 0 ? settings.AutoBackupRetentionCount : 7;
                    await _docService.PruneOldBackupsAsync(settings.AutoBackupDirectory, retention);

                    _autoBackupCompletedOnClose = true;
                    _isRunningCloseBackup = false;
                    Close();
                }
            }
            catch (Exception ex)
            {
                try
                {
                    string logDir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "DocuArchivePro",
                        "Logs");
                    if (!System.IO.Directory.Exists(logDir)) System.IO.Directory.CreateDirectory(logDir);
                    string logFile = System.IO.Path.Combine(logDir, "AutoBackupErrors.log");
                    System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
                }
                catch { }

                _autoBackupCompletedOnClose = true;
                _isRunningCloseBackup = false;
                Close();
            }
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var user = SecurityService.Instance.CurrentUser;
                if (!user.IsAdmin && !user.CanAdd)
                {
                    bool isAr = _locService.IsArabic;
                    MessageBox.Show(
                        isAr ? "عذراً، المستخدم الحالي لا يملك صلاحية إضافة وأرشفة الملفات." : "Sorry, the current user does not have permission to add and archive files.",
                        isAr ? "صلاحيات المستخدم" : "User Permissions",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    var entryVm = new DocumentEntryViewModel(_docService, _scannerService, _storageService, _locService);
                    entryVm.AddDroppedPaths(files);

                    var dlg = new DocumentEntryDialog(entryVm)
                    {
                        Owner = this
                    };

                    if (dlg.ShowDialog() == true)
                    {
                        await _vm.LoadDataAsync();
                    }
                }
            }
        }
    }
}
