using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using DocuArchivePro.Services;

namespace DocuArchivePro.Views
{
    public partial class ActionPinDialog : Window
    {
        private readonly bool _isEntityPinMode;
        public ILocalizationService Loc => LocalizationService.Instance;

        public ActionPinDialog(string actionDescription, bool isEntityPinMode = false)
        {
            InitializeComponent();
            _isEntityPinMode = isEntityPinMode;
            DataContext = this;
            if (!string.IsNullOrWhiteSpace(actionDescription))
            {
                TxtActionDescription.Text = actionDescription;
            }
            if (_isEntityPinMode)
            {
                TxtHeader.Text = Loc["EntityPinDialogHeader"];
                TxtPrompt.Text = Loc["EntityPinDialogPrompt"];
            }
            Loaded += (s, e) => PwdActionCode.Focus();
        }

        private async void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            await VerifyAndCloseAsync();
        }

        private async void PwdActionCode_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await VerifyAndCloseAsync();
            }
        }

        private async Task VerifyAndCloseAsync()
        {
            string entered = PwdActionCode.Password;
            bool isValid = _isEntityPinMode
                ? await SecurityService.Instance.VerifyEntityCodeAsync(entered)
                : await SecurityService.Instance.VerifyActionCodeAsync(entered);
            if (isValid)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                TxtError.Visibility = Visibility.Visible;
                PwdActionCode.Clear();
                PwdActionCode.Focus();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Prompts for the dedicated pre-configured Entity Addition Security Password when a user lacks entity permission.
        /// </summary>
        public static bool VerifyEntityAdditionPin(string actionTitle, Window? owner = null)
        {
            var pinDlg = new ActionPinDialog(actionTitle, isEntityPinMode: true)
            {
                Owner = owner ?? Application.Current.MainWindow
            };
            return pinDlg.ShowDialog() == true;
        }

        /// <summary>
        /// Shows the warning message first; if the user selects Yes, prompts for the pre-configured Action Security Code.
        /// </summary>
        public static bool ConfirmWarningAndVerifyPin(string actionTitle, string warningMessage, Window? owner = null)
        {
            bool isAr = LocalizationService.Instance.IsArabic;
            string caption = isAr ? $"تنبيه تحذيري - {actionTitle}" : $"Security Warning - {actionTitle}";

            var warningResult = MessageBox.Show(
                warningMessage,
                caption,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (warningResult != MessageBoxResult.Yes)
            {
                return false;
            }

            var pinDlg = new ActionPinDialog(actionTitle)
            {
                Owner = owner ?? Application.Current.MainWindow
            };

            return pinDlg.ShowDialog() == true;
        }
    }
}