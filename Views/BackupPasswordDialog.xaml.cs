using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using DocuArchivePro.Services;

namespace DocuArchivePro.Views
{
    public enum BackupPasswordMode
    {
        SetPassword,
        EnterPassword
    }

    public partial class BackupPasswordDialog : Window, INotifyPropertyChanged
    {
        private readonly BackupPasswordMode _mode;
        public ILocalizationService Loc => LocalizationService.Instance;

        public string WindowTitle => _mode == BackupPasswordMode.SetPassword
            ? (Loc.IsArabic ? "حماية النسخة الاحتياطية بكلمة مرور" : "Backup Password Protection")
            : (Loc.IsArabic ? "إدخال كلمة مرور النسخة الاحتياطية" : "Enter Backup Password");

        public string HeaderText => _mode == BackupPasswordMode.SetPassword
            ? (Loc.IsArabic ? "تشفير النسخة الاحتياطية الشاملة" : "Full Backup Encryption")
            : (Loc.IsArabic ? "النسخة الاحتياطية مشفرة بكلمة مرور" : "Password-Protected Backup");

        public string SubHeaderText => _mode == BackupPasswordMode.SetPassword
            ? (Loc.IsArabic ? "حماية متقدمة بتقنية AES-256 (اختياري)" : "Advanced AES-256 Protection (Optional)")
            : (Loc.IsArabic ? "مطلوب إدخال كلمة المرور لفك التشفير" : "Decryption Password Required");

        public string PromptText => _mode == BackupPasswordMode.SetPassword
            ? (Loc.IsArabic
                ? "يمكنك تعيين كلمة مرور مخصصة لحماية ملف النسخة الاحتياطية وقاعدة البيانات.\nإذا تركت الحقل فارغاً، سيتم تشفيرها تلقائياً بمفتاح النظام لحمايتها من الفتح الخارجي."
                : "You can set an optional password to encrypt the backup database with AES-256.\nIf left blank, it will be automatically protected using the system vault key.")
            : (Loc.IsArabic
                ? "قاعدة بيانات هذه النسخة مشفرة بكلمة مرور مخصصة. يرجى إدخال كلمة المرور للمتابعة والتحقق من سلامة البيانات:"
                : "This backup archive is encrypted with a custom password. Please enter the password to inspect or restore:");

        public string ConfirmButtonText => _mode == BackupPasswordMode.SetPassword
            ? (Loc.IsArabic ? "متابعة وإنشاء النسخة" : "Continue & Create Backup")
            : (Loc.IsArabic ? "تأكيد وفك التشفير" : "Confirm & Decrypt");

        public string? ResultPassword { get; private set; }

        public BackupPasswordDialog(BackupPasswordMode mode)
        {
            _mode = mode;
            InitializeComponent();
            DataContext = this;

            if (_mode == BackupPasswordMode.SetPassword)
            {
                PnlSetPassword.Visibility = Visibility.Visible;
                PnlEnterPassword.Visibility = Visibility.Collapsed;
                Loaded += (s, e) => PwdSetPassword.Focus();
            }
            else
            {
                PnlSetPassword.Visibility = Visibility.Collapsed;
                PnlEnterPassword.Visibility = Visibility.Visible;
                Loaded += (s, e) => PwdEnterPassword.Focus();
            }
        }

        private void ChkNoPassword_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool skip = ChkNoPassword.IsChecked == true;
            PwdSetPassword.IsEnabled = !skip;
            PwdConfirmPassword.IsEnabled = !skip;
            if (skip)
            {
                PwdSetPassword.Password = string.Empty;
                PwdConfirmPassword.Password = string.Empty;
                TxtError.Visibility = Visibility.Collapsed;
            }
        }

        private void PwdEnterPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirm_Click(sender, e);
            }
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            TxtError.Visibility = Visibility.Collapsed;

            if (_mode == BackupPasswordMode.SetPassword)
            {
                if (ChkNoPassword.IsChecked == true || string.IsNullOrWhiteSpace(PwdSetPassword.Password))
                {
                    ResultPassword = null;
                    DialogResult = true;
                    Close();
                    return;
                }

                string pwd = PwdSetPassword.Password;
                string confirm = PwdConfirmPassword.Password;

                if (pwd.Length < 4)
                {
                    ShowError(Loc.IsArabic ? "كلمة المرور يجب أن لا تقل عن 4 خانات." : "Password must be at least 4 characters.");
                    PwdSetPassword.Focus();
                    return;
                }

                if (pwd != confirm)
                {
                    ShowError(Loc.IsArabic ? "كلمتا المرور غير متطابقتين!" : "Passwords do not match!");
                    PwdConfirmPassword.Focus();
                    return;
                }

                ResultPassword = pwd;
                DialogResult = true;
                Close();
            }
            else
            {
                string entered = PwdEnterPassword.Password;
                if (string.IsNullOrWhiteSpace(entered))
                {
                    ShowError(Loc.IsArabic ? "يرجى إدخال كلمة المرور." : "Please enter the password.");
                    PwdEnterPassword.Focus();
                    return;
                }

                ResultPassword = entered;
                DialogResult = true;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowError(string msg)
        {
            TxtError.Text = msg;
            TxtError.Visibility = Visibility.Visible;
        }

        public static (bool Confirmed, string? Password) PromptSetPassword(Window? owner = null)
        {
            var dlg = new BackupPasswordDialog(BackupPasswordMode.SetPassword);
            if (owner != null) dlg.Owner = owner;
            bool? res = dlg.ShowDialog();
            return (res == true, dlg.ResultPassword);
        }

        public static (bool Confirmed, string? Password) PromptEnterPassword(Window? owner = null, string? errorPrompt = null)
        {
            var dlg = new BackupPasswordDialog(BackupPasswordMode.EnterPassword);
            if (owner != null) dlg.Owner = owner;
            if (!string.IsNullOrEmpty(errorPrompt))
            {
                dlg.ShowError(errorPrompt);
            }
            bool? res = dlg.ShowDialog();
            return (res == true, dlg.ResultPassword);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
