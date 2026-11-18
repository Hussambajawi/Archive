using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DocuArchivePro.Models;
using DocuArchivePro.Services;

namespace DocuArchivePro.Views
{
    public partial class LoginPromptDialog : Window
    {
        private readonly ISecurityService _securityService;
        public ILocalizationService Loc => LocalizationService.Instance;
        public ObservableCollection<AppUser> Users { get; } = new();

        public LoginPromptDialog(ISecurityService securityService)
        {
            InitializeComponent();
            DataContext = this;
            _securityService = securityService;
            CmbUsers.ItemsSource = Users;

            Loaded += async (s, e) => await LoadUsersAsync();
        }

        private async Task LoadUsersAsync()
        {
            var list = await _securityService.GetAllUsersAsync();
            Users.Clear();
            foreach (var u in list) Users.Add(u);

            var defaultSuper = Users.FirstOrDefault(u => u.Username.Equals("SuperUser", System.StringComparison.OrdinalIgnoreCase))
                               ?? Users.FirstOrDefault();
            if (defaultSuper != null)
            {
                CmbUsers.SelectedItem = defaultSuper;
            }
            PwdBox.Focus();
        }

        private void CmbUsers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbUsers.SelectedItem is AppUser user)
            {
                TxtRoleInfo.Text = Loc.IsArabic
                    ? $"الصفة: {user.LocalizedRoleTitle}  |  الصلاحيات: {user.PermissionsSummary}"
                    : $"Role: {user.LocalizedRoleTitle}  |  Permissions: {user.PermissionsSummary}";
                TxtError.Visibility = Visibility.Collapsed;
            }
        }

        private async void PwdBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await AttemptLoginAsync();
            }
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            await AttemptLoginAsync();
        }

        private async Task AttemptLoginAsync()
        {
            if (CmbUsers.SelectedItem is not AppUser selectedUser)
            {
                MessageBox.Show(
                    Loc.IsArabic ? "يرجى اختيار المستخدم أولاً." : "Please select a user first.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string enteredPassword = PwdBox.Password;
            if (!string.IsNullOrEmpty(selectedUser.LoginPassword))
            {
                if (!_securityService.VerifyUserPassword(selectedUser, enteredPassword))
                {
                    TxtError.Visibility = Visibility.Visible;
                    PwdBox.Clear();
                    PwdBox.Focus();
                    return;
                }
            }

            _securityService.CurrentUser = selectedUser;
            await new DocumentService(new FileStorageService()).LogAuditAsync("تسجيل دخول / Sign In", selectedUser.Username);
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }
    }
}