using System;
using System.Windows;
using DocuArchivePro.Data;
using DocuArchivePro.Services;
using DocuArchivePro.ViewModels;
using DocuArchivePro.Views;

namespace DocuArchivePro
{
    public partial class App : Application
    {
        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                // Prevent automatic app shutdown when the login dialog closes
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                // 1. Initialize SQLite Database & seed initial tables/categories/users/settings
                await DatabaseInitializer.InitializeAsync();

                // 2. Initialize Font, Theme & Language Services
                var locService = LocalizationService.Instance;
                var fontService = FontService.Instance;
                await fontService.LoadSettingsAsync();
                await ThemeService.Instance.LoadSettingsAsync();
                fontService.ApplyLanguage(locService.CurrentLanguage.Code);

                // Register global handler so all windows automatically apply correct font & number substitution
                EventManager.RegisterClassHandler(typeof(Window), Window.LoadedEvent, new RoutedEventHandler((s, ev) =>
                {
                    if (s is Window w)
                    {
                        fontService.ApplyToWindow(w);
                    }
                }));

                // 3. Resolve Core Services
                var storageService = new FileStorageService();
                var scannerService = new WiaScannerService();
                var docService = new DocumentService(storageService);
                var securityService = SecurityService.Instance;

                // Ensure any legacy attachments on disk are encrypted (.denc)
                await storageService.EncryptExistingUnencryptedFilesAsync();

                // 3. Show Welcome Screen first (with Date/Time Hijri & Gregorian, System Tasks Labels, Archive Stat Labels, Login, User Management & Exit)
                var welcomeWindow = new WelcomeWindow(docService, storageService, securityService);
                if (welcomeWindow.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }

                // 4. Initialize Main ViewModel
                var mainVm = new MainViewModel(docService, scannerService, storageService, locService);
                await mainVm.LoadDataAsync();

                // 5. Launch Main Window
                var mainWindow = new MainWindow(mainVm, docService, scannerService, storageService, locService);
                MainWindow = mainWindow;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"فشل بدء تشغيل النظام:\n{ex.Message}", "خطأ في التهيئة / Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                var storage = new FileStorageService();
                storage.CleanupTempSessionDirectory();
            }
            catch
            {
            }
            base.OnExit(e);
        }
    }
}
