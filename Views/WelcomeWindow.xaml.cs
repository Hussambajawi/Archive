using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DocuArchivePro.Services;

namespace DocuArchivePro.Views
{
    public partial class WelcomeWindow : Window
    {
        private readonly IDocumentService _documentService;
        private readonly IFileStorageService _fileStorageService;
        private readonly ISecurityService _securityService;
        private readonly DispatcherTimer _timer;

        public ILocalizationService Loc => LocalizationService.Instance;

        public WelcomeWindow(
            IDocumentService documentService,
            IFileStorageService fileStorageService,
            ISecurityService securityService)
        {
            InitializeComponent();
            DataContext = this;
            _documentService = documentService;
            _fileStorageService = fileStorageService;
            _securityService = securityService;

            UpdateClockAndDates();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => UpdateClockAndDates();
            _timer.Start();

            Loc.PropertyChanged += Loc_PropertyChanged;

            Loaded += async (s, e) => await LoadStatisticsLabelsAsync();
            Closed += (s, e) =>
            {
                _timer.Stop();
                Loc.PropertyChanged -= Loc_PropertyChanged;
            };
        }

        private async void Loc_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ILocalizationService.CurrentLanguage))
            {
                UpdateClockAndDates();
                await LoadStatisticsLabelsAsync();
            }
        }

        private void UpdateClockAndDates()
        {
            var now = DateTime.Now;
            bool isAr = Loc.IsArabic;

            if (isAr)
            {
                LblDayName.Text = $"يوم {DateTimeHelper.GetDayNameArabic(now)}";
                LblLiveTime.Text = DateTimeHelper.GetTimeArabic(now);
                LblHijriDate.Text = DateTimeHelper.GetHijriDateArabic(now);
                LblGregorianDate.Text = DateTimeHelper.GetGregorianDateArabic(now);
            }
            else
            {
                LblDayName.Text = DateTimeHelper.GetDayNameEnglish(now);
                LblLiveTime.Text = DateTimeHelper.GetTimeEnglish(now);
                LblHijriDate.Text = DateTimeHelper.GetHijriDateEnglish(now);
                LblGregorianDate.Text = DateTimeHelper.GetGregorianDateEnglish(now);
            }
        }

        private async Task LoadStatisticsLabelsAsync()
        {
            try
            {
                var stats = await _documentService.GetDashboardStatisticsAsync();
                var cats = await _documentService.GetCategoriesAsync();
                var users = await _securityService.GetAllUsersAsync();
                bool isAr = Loc.IsArabic;
                double mb = stats.TotalStorageBytes / (1024.0 * 1024.0);
                string mbStr = mb.ToString("F2", CultureInfo.InvariantCulture);

                if (isAr)
                {
                    LblStatTotalDocs.Text = $"{stats.TotalDocuments} معاملة مؤرشفة".ToArabicDigits();
                    LblStatTotalFiles.Text = $"{stats.TotalAttachmentsCount} ملف وصورة مشفرة".ToArabicDigits();
                    LblStatInOut.Text = $"الوارد: {stats.InboundCount}   |   الصادر: {stats.OutboundCount}".ToArabicDigits();
                    LblStatUrgentToday.Text = $"عاجل: {stats.UrgentCount}   |   مسجل اليوم: {stats.TodayCount}".ToArabicDigits();
                    LblStatStorage.Text = $"{mbStr} م.ب   |   {cats.Count} أضابير وتصنيفات".ToArabicDigits();
                    LblStatUsers.Text = $"{users.Count} مستخدمين (الافتراضي: SuperUser)".ToArabicDigits();
                }
                else
                {
                    LblStatTotalDocs.Text = $"{stats.TotalDocuments} Archived Records";
                    LblStatTotalFiles.Text = $"{stats.TotalAttachmentsCount} Encrypted Files";
                    LblStatInOut.Text = $"Inbound: {stats.InboundCount}   |   Outbound: {stats.OutboundCount}";
                    LblStatUrgentToday.Text = $"Urgent: {stats.UrgentCount}   |   Today: {stats.TodayCount}";
                    LblStatStorage.Text = $"{mbStr} MB   |   {cats.Count} Dossiers";
                    LblStatUsers.Text = $"{users.Count} Users (Default: SuperUser)";
                }
            }
            catch
            {
                // Ignore if initial load fails
            }
        }

        private void BtnToggleTheme_Click(object sender, RoutedEventArgs e) => Loc.ToggleTheme();
        private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => Loc.UiScale += 0.08;
        private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => Loc.UiScale -= 0.08;
        private void BtnZoomReset_Click(object sender, RoutedEventArgs e) => Loc.UiScale = 1.0;

        private void BtnLoginToSystem_Click(object sender, RoutedEventArgs e)
        {
            var loginPrompt = new LoginPromptDialog(_securityService)
            {
                Owner = this
            };

            if (loginPrompt.ShowDialog() == true)
            {
                DialogResult = true;
                Close();
            }
        }

        private async void BtnOpenUserManagement_Click(object sender, RoutedEventArgs e)
        {
            // Require login verification before opening User & Password Management from the Welcome Screen
            var loginPrompt = new LoginPromptDialog(_securityService)
            {
                Owner = this,
                Title = Loc.IsArabic
                    ? "تأكيد دخول مدير النظام لإدارة المستخدمين وكلمات السر"
                    : "Admin Sign-In Required for User & Password Management"
            };

            if (loginPrompt.ShowDialog() != true)
            {
                return;
            }

            if (!_securityService.CurrentUser.IsAdmin)
            {
                MessageBox.Show(
                    Loc.IsArabic
                        ? "عذراً، إدارة المستخدمين وكلمات السر متاحة لمدير النظام (SuperUser) فقط."
                        : "Sorry, User & Password Management is restricted to System Administrator (SuperUser).",
                    Loc.IsArabic ? "صلاحيات النظام" : "System Permissions",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var userMgmtDlg = new UserLoginDialog(_securityService)
            {
                Owner = this
            };
            userMgmtDlg.ShowDialog();
            await LoadStatisticsLabelsAsync();
        }

        private void BtnOpenCalendar_Click(object sender, RoutedEventArgs e)
        {
            var calDlg = new DateTimeCalendarDialog
            {
                Owner = this
            };
            calDlg.ShowDialog();
        }

        private void BtnOpenAbout_Click(object sender, RoutedEventArgs e)
        {
            var aboutDlg = new AboutDialog
            {
                Owner = this
            };
            aboutDlg.ShowDialog();
        }

        private async void BtnRestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            var loginPrompt = new LoginPromptDialog(_securityService)
            {
                Owner = this,
                Title = Loc.IsArabic
                    ? "تأكيد دخول مدير النظام لاستعادة نسخة احتياطية"
                    : "Admin Sign-In Required for Backup Restore"
            };

            if (loginPrompt.ShowDialog() != true) return;

            if (!_securityService.CurrentUser.IsAdmin)
            {
                MessageBox.Show(
                    Loc.IsArabic
                        ? "عذراً، استعادة النسخة الاحتياطية الشاملة متاحة لمدير النظام (SuperUser) حصراً."
                        : "Sorry, Full System Backup restore is restricted to System Administrator (SuperUser).",
                    Loc.IsArabic ? "صلاحيات النظام" : "System Permissions",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.IsArabic ? "اختر ملف النسخة الاحتياطية الشاملة (.zip)" : "Select Full System Backup (.zip)",
                Filter = "Backup Archive (*.zip)|*.zip"
            };
            if (dlg.ShowDialog() != true) return;

            bool isAr = Loc.IsArabic;
            FullBackupInspectionResult inspection;
            string? password = null;

            try
            {
                var initialInspection = await _documentService.InspectFullSystemBackupZipAsync(dlg.FileName);
                if (initialInspection.Manifest.IsPasswordProtected)
                {
                    var (pwdConfirmed, pwd) = BackupPasswordDialog.PromptEnterPassword(
                        this,
                        isAr ? "هذه النسخة مشفرة بكلمة مرور. أدخل كلمة المرور للاستعادة:" : "This backup is password protected. Enter password to restore:");
                    if (!pwdConfirmed) return;
                    password = pwd;
                }

                inspection = await _documentService.InspectFullSystemBackupZipAsync(dlg.FileName, password);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    isAr ? $"ملف النسخة الاحتياطية غير صالح أو تالف:\n{ex.Message}" : $"Invalid or corrupted backup archive:\n{ex.Message}",
                    Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!inspection.ChecksumsValid)
            {
                string badList = string.Join("\n", inspection.ChecksumErrors.Take(5));
                MessageBox.Show(
                    isAr
                        ? $"فشل التحقق من سلامة النسخة الاحتياطية أو كلمة المرور!\n\n{badList}"
                        : $"Backup integrity verification or password failed!\n\n{badList}",
                    Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var m = inspection.Manifest;
            string dateStr = DateTimeHelper.FormatDigits(m.ExportedAt.ToString("yyyy-MM-dd HH:mm"), isAr);
            string sourcePc = string.IsNullOrWhiteSpace(m.SourceMachineName) ? "-" : m.SourceMachineName;
            string title = isAr ? "استعادة نسخة احتياطية شاملة للنظام" : "Restore Full System Backup";
            string msg = isAr
                ? $"تفاصيل النسخة الاحتياطية المختارة:\n" +
                  $"• الجهاز المصدر: {sourcePc}  |  التاريخ: {dateStr}\n" +
                  $"• المعاملات: {DateTimeHelper.FormatNumber(m.ActiveDocumentsCount, true)}  |  المرفقات: {DateTimeHelper.FormatNumber(m.AttachmentsCount, true)}  |  المستخدمون: {DateTimeHelper.FormatNumber(m.UsersCount, true)}\n" +
                  $"• فحص السلامة (SHA-256): سليم ومطابق ✅\n\n" +
                  $"تحذير: ستقوم هذه العملية باستبدال قاعدة البيانات الحالية ببيانات هذه النسخة وإعادة ربط المسارات تلقائياً على هذا الجهاز.\nهل أنت متأكد من المتابعة؟"
                : $"Selected Backup Summary:\n" +
                  $"• Source Machine: {sourcePc}  |  Date: {dateStr}\n" +
                  $"• Documents: {m.ActiveDocumentsCount}  |  Attachments: {m.AttachmentsCount}  |  Users: {m.UsersCount}\n" +
                  $"• Integrity Check (SHA-256): Verified ✅\n\n" +
                  $"Warning: This will replace the current database with the backup contents and automatically re-bind all paths for this PC.\nContinue?";

            if (!ActionPinDialog.ConfirmWarningAndVerifyPin(title, msg)) return;

            try
            {
                var (docsCount, filesCount) = await _documentService.RestoreFullSystemBackupZipAsync(dlg.FileName, password);
                await LoadStatisticsLabelsAsync();
                MessageBox.Show(
                    isAr
                        ? $"تمت استعادة النظام وإعادة ربط جميع المسارات بنجاح!\nالمعاملات المستعادة: {DateTimeHelper.FormatNumber(docsCount, true)}\nالملفات المشفرة المستعادة: {DateTimeHelper.FormatNumber(filesCount, true)}"
                        : $"Full backup restored and all paths re-bound successfully!\nRestored Documents: {docsCount}\nRestored Vault Files: {filesCount}",
                    Loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{Loc["Error"]}: {ex.Message}", Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExitSystem_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }
    }
}