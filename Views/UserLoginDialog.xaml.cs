using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DocuArchivePro.Models;
using DocuArchivePro.Services;

namespace DocuArchivePro.Views
{
    public partial class UserLoginDialog : Window
    {
        private readonly ISecurityService _securityService;
        private readonly DispatcherTimer _clockTimer;
        public ILocalizationService Loc => LocalizationService.Instance;
        public ObservableCollection<AppUser> UsersList { get; } = new();
        public AppUser? SelectedUser { get; private set; }

        public UserLoginDialog(ISecurityService securityService)
        {
            InitializeComponent();
            DataContext = this;
            _securityService = securityService;
            DgUsers.ItemsSource = UsersList;
            CmbLoginUser.ItemsSource = UsersList;

            UpdateHeaderClock();
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) => UpdateHeaderClock();
            _clockTimer.Start();

            Loaded += async (s, e) => await LoadUsersAndSettingsAsync();
            Closed += (s, e) => _clockTimer.Stop();
        }

        private void UpdateHeaderClock()
        {
            var now = DateTime.Now;
            if (Loc.IsArabic)
            {
                TxtHeaderDayAndTime.Text = $"{DateTimeHelper.GetDayNameArabic(now)} | {DateTimeHelper.GetTimeArabic(now)}";
                TxtHeaderGregorian.Text = $"الميلادي: {DateTimeHelper.GetGregorianDateArabic(now)}";
                TxtHeaderHijri.Text = $"الهجري: {DateTimeHelper.GetHijriDateArabic(now)}";
            }
            else
            {
                TxtHeaderDayAndTime.Text = $"{DateTimeHelper.GetDayNameEnglish(now)} | {DateTimeHelper.GetTimeEnglish(now)}";
                TxtHeaderGregorian.Text = $"Gregorian: {DateTimeHelper.GetGregorianDateEnglish(now)}";
                TxtHeaderHijri.Text = $"Hijri: {DateTimeHelper.GetHijriDateEnglish(now)}";
            }
        }

        private async Task LoadUsersAndSettingsAsync()
        {
            ApplyAdminAccessState();

            var users = await _securityService.GetAllUsersAsync();
            UsersList.Clear();
            foreach (var u in users) UsersList.Add(u);

            if (UsersList.Count > 0)
            {
                DgUsers.SelectedIndex = 0;
                CmbLoginUser.SelectedIndex = 0;
            }

            // Keep secret input fields blank so stored hashes are never exposed or overwritten unintentionally
            PwdActionSecurityCode.Clear();
            PwdEntitySecurityCode.Clear();
        }

        private void ApplyAdminAccessState()
        {
            bool isAdmin = _securityService.CurrentUser.IsAdmin;
            AdminManagementPanel.IsEnabled = isAdmin;
            AdminManagementPanel.Opacity = isAdmin ? 1.0 : 0.65;
            AdminOnlyBanner.Visibility = isAdmin ? Visibility.Collapsed : Visibility.Visible;
        }

        private bool EnsureAdminAccess()
        {
            if (_securityService.CurrentUser.IsAdmin) return true;

            MessageBox.Show(
                Loc.IsArabic
                    ? "عذراً، إدارة المستخدمين وكلمات السر متاحة لمدير النظام العام فقط."
                    : "Sorry, user and security password management is restricted to System Administrators.",
                Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private void DgUsers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DgUsers.SelectedItem is AppUser user)
            {
                CmbLoginUser.SelectedItem = user;
                PopulateEditForm(user);
            }
        }

        private void CmbLoginUser_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLoginUser.SelectedItem is AppUser user)
            {
                DgUsers.SelectedItem = user;
                PopulateEditForm(user);
                TxtSelectedUserPermissions.Text = Loc.IsArabic
                    ? $"الصلاحيات الممنوحة: {user.PermissionsSummary}"
                    : $"Assigned Permissions: {user.PermissionsSummary}";
            }
        }

        private void PopulateEditForm(AppUser user)
        {
            TxtEditUsername.Text = user.Username;
            TxtEditRole.Text = user.RoleTitle;
            PwdEditLoginPassword.Clear();
            ChkClearLoginPassword.IsChecked = false;
            ChkIsAdmin.IsChecked = user.IsAdmin;
            ChkCanAdd.IsChecked = user.CanAdd;
            ChkCanEdit.IsChecked = user.CanEdit;
            ChkCanDelete.IsChecked = user.CanDelete;
            ChkCanScan.IsChecked = user.CanScan;
            ChkCanPrint.IsChecked = user.CanPrint;
            ChkCanManageStorage.IsChecked = user.CanManageStorage;
            ChkCanManageEntities.IsChecked = user.CanManageEntities;
        }

        private void ChkIsAdmin_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkIsAdmin.IsChecked == true)
            {
                ChkCanAdd.IsChecked = true;
                ChkCanEdit.IsChecked = true;
                ChkCanDelete.IsChecked = true;
                ChkCanScan.IsChecked = true;
                ChkCanPrint.IsChecked = true;
                ChkCanManageStorage.IsChecked = true;
                ChkCanManageEntities.IsChecked = true;
            }
        }

        private async void BtnAddNewUser_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAdminAccess()) return;

            string enteredUsername = TxtEditUsername.Text.Trim();
            if (string.IsNullOrWhiteSpace(enteredUsername))
            {
                MessageBox.Show(
                    Loc.IsArabic ? "يرجى إدخال اسم المستخدم الجديد." : "Please enter the new username.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (await _securityService.IsUsernameTakenAsync(enteredUsername))
            {
                MessageBox.Show(
                    Loc.IsArabic
                        ? $"اسم المستخدم ({enteredUsername}) مسجل مسبقاً لمستخدم آخر في النظام! يرجى اختيار اسم فريد."
                        : $"Username ({enteredUsername}) is already registered! Please choose a unique username.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newUser = new AppUser
            {
                Username = enteredUsername,
                RoleTitle = string.IsNullOrWhiteSpace(TxtEditRole.Text)
                    ? (Loc.IsArabic ? "موظف أرشيف" : "Archive Officer")
                    : TxtEditRole.Text.Trim(),
                LoginPassword = PwdEditLoginPassword.Password,
                IsAdmin = ChkIsAdmin.IsChecked == true,
                CanAdd = ChkCanAdd.IsChecked == true,
                CanEdit = ChkCanEdit.IsChecked == true,
                CanDelete = ChkCanDelete.IsChecked == true,
                CanScan = ChkCanScan.IsChecked == true,
                CanPrint = ChkCanPrint.IsChecked == true,
                CanManageStorage = ChkCanManageStorage.IsChecked == true,
                CanManageEntities = ChkCanManageEntities.IsChecked == true
            };

            try
            {
                await _securityService.SaveUserAsync(newUser, clearPassword: ChkClearLoginPassword.IsChecked == true);
                await new DocumentService(new FileStorageService()).LogAuditAsync("إضافة مستخدم / Add User", newUser.Username);
                await LoadUsersAndSettingsAsync();
                CmbLoginUser.SelectedItem = newUser;
                MessageBox.Show(
                    Loc.IsArabic
                        ? $"تمت إضافة المستخدم ({newUser.Username}) وتحديد صلاحياته بنجاح."
                        : $"User ({newUser.Username}) added with assigned permissions successfully.",
                    Loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnUpdateSelectedUser_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAdminAccess()) return;

            if (DgUsers.SelectedItem is not AppUser selected)
            {
                MessageBox.Show(
                    Loc.IsArabic ? "يرجى تحديد مستخدم من القائمة لتعديل بياناته وصلاحياته." : "Please select a user from the list to edit.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string enteredUsername = TxtEditUsername.Text.Trim();
            if (string.IsNullOrWhiteSpace(enteredUsername))
            {
                MessageBox.Show(
                    Loc.IsArabic ? "اسم المستخدم لا يمكن أن يكون فارغاً." : "Username cannot be empty.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (await _securityService.IsUsernameTakenAsync(enteredUsername, selected.Id))
            {
                MessageBox.Show(
                    Loc.IsArabic
                        ? $"اسم المستخدم ({enteredUsername}) مسجل مسبقاً لمستخدم آخر في النظام! يرجى اختيار اسم فريد."
                        : $"Username ({enteredUsername}) is already registered to another user!",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool wantsAdmin = ChkIsAdmin.IsChecked == true;
            if (selected.IsAdmin && !wantsAdmin)
            {
                var allUsers = await _securityService.GetAllUsersAsync();
                if (allUsers.Count(u => u.IsAdmin && u.Id != selected.Id) == 0)
                {
                    ChkIsAdmin.IsChecked = true;
                    MessageBox.Show(
                        Loc.IsArabic
                            ? "عذراً، لا يمكن إلغاء صلاحية (مدير النظام) عن هذا الحساب لأنه المدير الوحيد المتبقي في النظام (لمنع قفل النظام)."
                            : "Cannot remove Administrator privilege from this user because it is the only remaining Administrator in the system.",
                        Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            selected.Username = enteredUsername;
            selected.RoleTitle = TxtEditRole.Text.Trim();
            bool clearPwd = ChkClearLoginPassword.IsChecked == true;
            if (clearPwd)
            {
                selected.LoginPassword = string.Empty;
            }
            else if (!string.IsNullOrEmpty(PwdEditLoginPassword.Password))
            {
                selected.LoginPassword = PwdEditLoginPassword.Password;
            }
            selected.IsAdmin = wantsAdmin;
            selected.CanAdd = ChkCanAdd.IsChecked == true;
            selected.CanEdit = ChkCanEdit.IsChecked == true;
            selected.CanDelete = ChkCanDelete.IsChecked == true;
            selected.CanScan = ChkCanScan.IsChecked == true;
            selected.CanPrint = ChkCanPrint.IsChecked == true;
            selected.CanManageStorage = ChkCanManageStorage.IsChecked == true;
            selected.CanManageEntities = ChkCanManageEntities.IsChecked == true;

            try
            {
                await _securityService.SaveUserAsync(selected, clearPassword: clearPwd);
                await new DocumentService(new FileStorageService()).LogAuditAsync("تعديل مستخدم / Update User", selected.Username);
                await LoadUsersAndSettingsAsync();
                MessageBox.Show(
                    Loc.IsArabic
                        ? $"تم تحديث بيانات وصلاحيات المستخدم ({selected.Username}) بنجاح."
                        : $"User ({selected.Username}) updated successfully.",
                    Loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnDeleteSelectedUser_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAdminAccess()) return;

            if (DgUsers.SelectedItem is not AppUser selected)
            {
                return;
            }

            if (_securityService.CurrentUser != null && _securityService.CurrentUser.Id == selected.Id)
            {
                MessageBox.Show(
                    Loc.IsArabic
                        ? "عذراً، لا يمكن للمستخدم حذف الحساب الذي قام بتسجيل الدخول به حالياً."
                        : "Cannot delete the account you are currently logged in with.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var allUsers = await _securityService.GetAllUsersAsync();
            if (selected.IsAdmin && allUsers.Count(u => u.IsAdmin) <= 1)
            {
                MessageBox.Show(
                    Loc.IsArabic
                        ? "عذراً، لا يمكن حذف هذا المستخدم لأنه المدير الوحيد المتبقي في النظام (يجب توفر مدير واحد على الأقل لمنع قفل النظام)."
                        : "Cannot delete this user because it is the only remaining Administrator in the system.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string title = Loc.IsArabic ? "حذف مستخدم من النظام" : "Delete User Account";
            string msg = Loc.IsArabic
                ? $"هل أنت متأكد من رغبتك في حذف المستخدم ({selected.Username})؟"
                : $"Are you sure you want to delete user ({selected.Username})?";

            if (!ActionPinDialog.ConfirmWarningAndVerifyPin(title, msg, this))
            {
                return;
            }

            bool deleted = await _securityService.DeleteUserAsync(selected.Id);
            if (!deleted)
            {
                MessageBox.Show(
                    Loc.IsArabic ? "تعذر حذف المستخدم المطلوب لضمان أمان النظام." : "Cannot delete the specified user for system security reasons.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await new DocumentService(new FileStorageService()).LogAuditAsync("حذف مستخدم / Delete User", selected.Username);
            await LoadUsersAndSettingsAsync();
            MessageBox.Show(
                Loc.IsArabic ? "تم حذف المستخدم بنجاح." : "User deleted successfully.",
                Loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnSaveActionCode_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAdminAccess()) return;

            string code = PwdActionSecurityCode.Password.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                MessageBox.Show(
                    Loc.IsArabic ? "يرجى إدخال رمز تأكيد الإجراءات السري." : "Please enter a security action PIN.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var settings = await _securityService.GetSettingsAsync();
            if (await _securityService.VerifyEntityCodeAsync(code))
            {
                MessageBox.Show(
                    Loc.IsArabic
                        ? "يجب أن يكون رمز تأكيد الإجراءات (حذف/تعديل/إضافة إضبارة) مختلفاً عن كلمة سر إضافة جهات الصادر والوارد!"
                        : "The Dossier Action PIN (Edit/Delete/Add Dossier) must be different from the Correspondent Entity Addition password!",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            settings.ActionSecurityCode = SecurityService.HashSecret(code);
            await _securityService.SaveSettingsAsync(settings);
            await new DocumentService(new FileStorageService()).LogAuditAsync("تغيير رمز الأمان / Change Action PIN", "ActionSecurityCode updated");
            PwdActionSecurityCode.Clear();

            MessageBox.Show(
                Loc.IsArabic
                    ? "تم حفظ واعتماد رمز تأكيد الإجراءات الحساسة (الحذف / التعديل / إضافة إضبارة) بنجاح."
                    : "Security action confirmation PIN (Edit / Delete / Add Dossier) saved successfully.",
                Loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnSaveEntityCode_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureAdminAccess()) return;

            string entityCode = PwdEntitySecurityCode.Password.Trim();
            if (string.IsNullOrWhiteSpace(entityCode))
            {
                MessageBox.Show(
                    Loc.IsArabic ? "يرجى إدخال كلمة السر الخاصة بإضافة جهات الصادر والوارد." : "Please enter the password for adding Inbound/Outbound entities.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var settings = await _securityService.GetSettingsAsync();
            if (await _securityService.VerifyActionCodeAsync(entityCode))
            {
                MessageBox.Show(
                    Loc.IsArabic
                        ? "يجب أن تكون كلمة سر إضافة جهات الصادر والوارد مختلفة عن كلمة سر حذف وتعديل وإضافة الأضابير!"
                        : "The Correspondent Entity Addition password must be different from the Dossier Edit/Delete security password!",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            settings.EntitySecurityCode = SecurityService.HashSecret(entityCode);
            await _securityService.SaveSettingsAsync(settings);
            await new DocumentService(new FileStorageService()).LogAuditAsync("تغيير كلمة سر الجهات / Change Entity PIN", "EntitySecurityCode updated");
            PwdEntitySecurityCode.Clear();

            MessageBox.Show(
                Loc.IsArabic
                    ? "تم حفظ واعتماد كلمة السر الخاصة بإضافة جهات الصادر والوارد بنجاح."
                    : "Correspondent Entity Addition password saved successfully.",
                Loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            if (CmbLoginUser.SelectedItem is not AppUser chosenUser)
            {
                MessageBox.Show(
                    Loc.IsArabic ? "يرجى اختيار مستخدم للدخول." : "Please select a user to sign in.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!string.IsNullOrEmpty(chosenUser.LoginPassword))
            {
                if (!_securityService.VerifyUserPassword(chosenUser, PwdLoginPassword.Password))
                {
                    MessageBox.Show(
                        Loc.IsArabic ? "كلمة مرور الدخول لهذا المستخدم غير صحيحة!" : "Incorrect login password for this user!",
                        Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                    PwdLoginPassword.Focus();
                    return;
                }
            }

            SelectedUser = chosenUser;
            _securityService.CurrentUser = chosenUser;
            await new DocumentService(new FileStorageService()).LogAuditAsync("تبديل المستخدم / Switch User", chosenUser.Username);
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}