using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using DocuArchivePro.Models;
using DocuArchivePro.Services;

namespace DocuArchivePro.Views
{
    public partial class DeleteCategoryWizardDialog : Window
    {
        private readonly IDocumentService _documentService;
        private readonly Category _category;
        private readonly PropertyChangedEventHandler _locHandler;

        private CategoryDeletionSummary? _summary;
        private CategoryBackupExportResult? _backupResult;
        private int _currentStep = 1;

        public ILocalizationService Loc => LocalizationService.Instance;

        public DeleteCategoryWizardDialog(IDocumentService documentService, Category category)
        {
            InitializeComponent();
            _documentService = documentService;
            _category = category;
            DataContext = this;

            string defaultRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "DocuArchive_Backups");
            TxtBackupFolder.Text = defaultRoot;

            _locHandler = (s, e) =>
            {
                if (e.PropertyName == nameof(ILocalizationService.CurrentLanguage))
                {
                    RefreshDynamicTexts();
                }
            };
            Loc.PropertyChanged += _locHandler;
            Closed += (s, e) => Loc.PropertyChanged -= _locHandler;

            Loaded += async (s, e) => await LoadSummaryAsync();
        }

        private async Task LoadSummaryAsync()
        {
            try
            {
                _summary = await _documentService.GetCategoryDeletionSummaryAsync(_category.Id);

                var allCats = await _documentService.GetCategoriesAsync();
                var targetOptions = new System.Collections.Generic.List<Category>
                {
                    new Category { Id = 0, Name = "عام (بدون تصنيف محدد) / General (Unassigned)" }
                };
                foreach (var c in allCats)
                {
                    if (c.Id != _category.Id)
                    {
                        targetOptions.Add(c);
                    }
                }
                CmbTransferTargetCategory.ItemsSource = targetOptions;
                CmbTransferTargetCategory.SelectedIndex = targetOptions.Count > 1 ? 1 : 0;

                RefreshDynamicTexts();
                SetStep(1);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Loc.IsArabic ? $"تعذر قراءة بيانات الإضبارة:\n{ex.Message}" : $"Failed to load dossier summary:\n{ex.Message}",
                    Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        private void RefreshDynamicTexts()
        {
            if (_summary == null) return;

            bool isAr = Loc.IsArabic;
            string catName = _category.DisplayName;
            string inStr = DateTimeHelper.FormatNumber(_summary.InboundCount, isAr);
            string outStr = DateTimeHelper.FormatNumber(_summary.OutboundCount, isAr);
            string recStr = DateTimeHelper.FormatNumber(_summary.RecycleBinCount, isAr);
            string attStr = DateTimeHelper.FormatNumber(_summary.TotalAttachmentsCount, isAr);

            TxtInboundCountVal.Text = inStr;
            TxtOutboundCountVal.Text = outStr;
            TxtAttachmentsCountVal.Text = attStr;

            TxtStep1ImpactMessage.Text = isAr
                ? $"أنت الآن على وشك حذف الإضبارة: ({catName})\nوالمرتبط بها: معاملات وارد ({inStr})، صادر ({outStr})، سلة المحذوفات ({recStr})، وملفات مرفقة ({attStr})."
                : $"You are about to delete the dossier: ({catName})\nLinked records: Inbound ({inStr}), Outbound ({outStr}), Recycle Bin ({recStr}), Attachments ({attStr}).";

            TxtStep3FinalImpact.Text = isAr
                ? $"سيتم حذف الإضبارة ({catName}) مع ({inStr}) معاملة وارد و({outStr}) صادر و({recStr}) في سلة المحذوفات و({attStr}) مرفق مؤرشف نهائياً."
                : $"Dossier ({catName}) will be permanently deleted with ({inStr}) Inbound, ({outStr}) Outbound, ({recStr}) Recycle Bin documents, and ({attStr}) archived files.";

            if (_backupResult != null)
            {
                BackupStatusBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCFCE7"));
                BackupStatusBanner.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#86EFAC"));
                TxtBackupStatusBanner.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#166534"));

                string docsExported = DateTimeHelper.FormatNumber(_backupResult.ExportedDocumentsCount, isAr);
                string filesExported = DateTimeHelper.FormatNumber(_backupResult.ExportedAttachmentsCount, isAr);
                TxtBackupStatusBanner.Text = isAr
                    ? $"✅ تم إنشاء نسخة احتياطية غير مشفرة بنجاح ({docsExported} معاملة، {filesExported} ملف) في المسار:\n{_backupResult.BackupFolderPath}"
                    : $"✅ Unencrypted backup created successfully ({docsExported} documents, {filesExported} files) at:\n{_backupResult.BackupFolderPath}";
            }
            else
            {
                BackupStatusBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF3C7"));
                BackupStatusBanner.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCD34D"));
                TxtBackupStatusBanner.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#92400E"));

                TxtBackupStatusBanner.Text = isAr
                    ? "⚠️ تنبيه: لم يتم إنشاء نسخة احتياطية لهذه الإضبارة. في حال المتابعة لن تتمكن من استرجاع هذه الملفات لاحقاً."
                    : "⚠️ Notice: No backup was created for this dossier. If you proceed, these files cannot be recovered later.";
            }
        }

        private void SetStep(int step)
        {
            _currentStep = step;
            PanelStep1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            PanelStep2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            PanelStep3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;

            UpdateBadge(StepBadge1, TxtBadge1, step == 1, step > 1);
            UpdateBadge(StepBadge2, TxtBadge2, step == 2, step > 2);
            UpdateBadge(StepBadge3, TxtBadge3, step == 3, false);

            RefreshDynamicTexts();
            ThemeService.Instance.ApplyThemeToWindow(this);
        }

        private static void UpdateBadge(System.Windows.Controls.Border badge, System.Windows.Controls.TextBlock text, bool isActive, bool isDone)
        {
            if (isDone)
            {
                badge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCFCE7"));
                badge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));
                text.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15803D"));
                text.FontWeight = FontWeights.Bold;
            }
            else if (isActive)
            {
                badge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFF6FF"));
                badge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
                text.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E40AF"));
                text.FontWeight = FontWeights.Bold;
            }
            else
            {
                badge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                badge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
                text.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                text.FontWeight = FontWeights.SemiBold;
            }
        }

        private void BtnStep1Next_Click(object sender, RoutedEventArgs e)
        {
            SetStep(2);
        }

        private void BtnBrowseBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = Loc.IsArabic ? "اختر مجلد حفظ النسخة الاحتياطية غير المشفرة للإضبارة" : "Select Folder for Unencrypted Dossier Backup",
                InitialDirectory = Directory.Exists(TxtBackupFolder.Text)
                    ? TxtBackupFolder.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };

            if (dlg.ShowDialog(this) == true)
            {
                TxtBackupFolder.Text = dlg.FolderName;
            }
        }

        private async void BtnYesCreateBackup_Click(object sender, RoutedEventArgs e)
        {
            string targetRoot = TxtBackupFolder.Text.Trim();
            if (string.IsNullOrWhiteSpace(targetRoot))
            {
                MessageBox.Show(
                    Loc.IsArabic ? "يرجى اختيار مجلد صالح لحفظ النسخة الاحتياطية." : "Please select a valid target folder for the backup.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                BtnCreateBackup.IsEnabled = false;
                _backupResult = await _documentService.ExportCategoryBackupAsync(_category.Id, targetRoot);
                SetStep(3);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Loc.IsArabic ? $"فشل إنشاء النسخة الاحتياطية:\n{ex.Message}" : $"Failed to create backup:\n{ex.Message}",
                    Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnCreateBackup.IsEnabled = true;
            }
        }

        private void BtnSkipBackup_Click(object sender, RoutedEventArgs e)
        {
            _backupResult = null;
            SetStep(3);
        }

        private void BtnBackToStep1_Click(object sender, RoutedEventArgs e)
        {
            SetStep(1);
        }

        private void BtnBackToStep2_Click(object sender, RoutedEventArgs e)
        {
            SetStep(2);
        }

        private async void BtnTransferAndDelete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnTransferAndDelete.IsEnabled = false;
                BtnFinalDelete.IsEnabled = false;

                var selectedCat = CmbTransferTargetCategory.SelectedItem as Category;
                int? targetCatId = (selectedCat != null && selectedCat.Id > 0) ? selectedCat.Id : null;

                int movedCount = await _documentService.TransferCategoryDocumentsAndDeleteAsync(_category.Id, targetCatId);
                bool isAr = Loc.IsArabic;
                string targetDisplay = selectedCat?.DisplayName ?? (isAr ? "عام" : "General");
                string countStr = DateTimeHelper.FormatNumber(movedCount, isAr);

                string msg = isAr
                    ? $"تم نقل ({countStr}) معاملة ومرفقاتها بأمان إلى ({targetDisplay})، وحذف الإضبارة القديمة ({_category.DisplayName}) بنجاح!"
                    : $"Successfully transferred ({countStr}) documents to ({targetDisplay}) and deleted the old dossier ({_category.DisplayName})!";

                MessageBox.Show(msg, Loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Loc.IsArabic ? $"حدث خطأ أثناء نقل المعاملات:\n{ex.Message}" : $"Error transferring documents:\n{ex.Message}",
                    Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnTransferAndDelete.IsEnabled = true;
                BtnFinalDelete.IsEnabled = true;
            }
        }

        private async void BtnDeletePermanently_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnFinalDelete.IsEnabled = false;
                await _documentService.DeleteCategoryPermanentlyWithDocumentsAsync(_category.Id);

                bool isAr = Loc.IsArabic;
                string msg = isAr
                    ? $"تم حذف الإضبارة ({_category.DisplayName}) وجميع معاملاتها ومرفقاتها المؤرشفة نهائياً بنجاح."
                    : $"Dossier ({_category.DisplayName}) and all linked transactions and archived files were permanently deleted.";

                if (_backupResult != null)
                {
                    msg += isAr
                        ? $"\n\nتم الاحتفاظ بنسختك الاحتياطية غير المشفرة في:\n{_backupResult.BackupFolderPath}\nويمكنك استيرادها مستقبلاً من الإعدادات -> استيراد إضبارة احتياطية."
                        : $"\n\nYour unencrypted backup is saved at:\n{_backupResult.BackupFolderPath}\nYou can re-import it anytime from Settings -> Import Dossier Backup.";
                }

                MessageBox.Show(msg, Loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Loc.IsArabic ? $"حدث خطأ أثناء الحذف النهائي:\n{ex.Message}" : $"Error during permanent deletion:\n{ex.Message}",
                    Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnFinalDelete.IsEnabled = true;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
