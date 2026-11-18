using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using DocuArchivePro.Services;

namespace DocuArchivePro.Views
{
    public partial class ScannerDialog : Window, INotifyPropertyChanged
    {
        private readonly IScannerService _scannerService;
        public ILocalizationService Loc => LocalizationService.Instance;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ScannerDeviceInfo> AvailableScanners { get; } = new();
        private ScannerDeviceInfo? _selectedScanner;
        public ScannerDeviceInfo? SelectedScanner
        {
            get => _selectedScanner;
            set
            {
                if (_selectedScanner != value)
                {
                    _selectedScanner = value;
                    OnPropertyChanged(nameof(SelectedScanner));
                }
            }
        }
        public byte[]? ScannedResultBytes { get; private set; }

        public ScannerDialog(IScannerService scannerService)
        {
            InitializeComponent();
            DataContext = this;
            _scannerService = scannerService;
            Loaded += async (s, e) => await LoadScannersAndDefaultsAsync();
        }

        private async Task LoadScannersAndDefaultsAsync()
        {
            var scanners = await _scannerService.GetAvailableScannersAsync();
            AvailableScanners.Clear();
            foreach (var s in scanners) AvailableScanners.Add(s);

            var settings = await SecurityService.Instance.GetSettingsAsync();
            UpdateDefaultScannerBanner(settings.DefaultScannerName, settings.DefaultScanDpi);

            // Restore saved DPI
            if (settings.DefaultScanDpi == 150) CmbDpi.SelectedIndex = 0;
            else if (settings.DefaultScanDpi == 600) CmbDpi.SelectedIndex = 2;
            else CmbDpi.SelectedIndex = 1;

            // Restore saved ColorMode
            if (settings.DefaultScanColorMode == 2) CmbColorMode.SelectedIndex = 1;
            else if (settings.DefaultScanColorMode == 4) CmbColorMode.SelectedIndex = 2;
            else CmbColorMode.SelectedIndex = 0;

            ChkUseFeeder.IsChecked = settings.UseScannerFeeder;

            var matched = (!string.Equals(settings.DefaultScannerDeviceId, "VIRTUAL_TEST_SCANNER_01", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(settings.DefaultScannerDeviceId, WiaScannerService.InteractiveDialogDeviceId, StringComparison.OrdinalIgnoreCase))
                ? AvailableScanners.FirstOrDefault(s => s.DeviceId == settings.DefaultScannerDeviceId)
                : null;

            matched ??= AvailableScanners.FirstOrDefault(s => s.DeviceId != WiaScannerService.InteractiveDialogDeviceId)
                        ?? AvailableScanners.FirstOrDefault();

            if (matched != null)
            {
                SelectedScanner = matched;
                UpdateDefaultScannerBanner(matched.Name, settings.DefaultScanDpi);
            }
        }

        private void UpdateDefaultScannerBanner(string scannerName, int dpi)
        {
            bool isAr = Loc.IsArabic;
            string text = isAr
                ? $"السكانر المعتمد حالياً: {scannerName} ({dpi} DPI)"
                : $"Active Default Scanner: {scannerName} ({dpi} DPI)";
            TxtDefaultScannerInfo.Text = DateTimeHelper.FormatDigits(text, isAr);
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private async void BtnRefreshScanners_Click(object sender, RoutedEventArgs e)
        {
            await LoadScannersAndDefaultsAsync();
            bool isAr = Loc.IsArabic;
            string msg = isAr
                ? $"تم فحص وتحديث قائمة أجهزة السكانر المعرفة على الجهاز (عدد الخيارات المتاحة: {DateTimeHelper.FormatNumber(AvailableScanners.Count, true)})."
                : $"Scanner devices list refreshed (available devices: {AvailableScanners.Count}).";
            MessageBox.Show(msg, Loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnSaveDefaultScanner_Click(object sender, RoutedEventArgs e)
        {
            bool isAr = Loc.IsArabic;
            if (SelectedScanner == null)
            {
                MessageBox.Show(
                    isAr ? "يرجى اختيار جهاز سكانر من القائمة أولاً." : "Please select a scanner device from the list first.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int dpi = 300;
            if (CmbDpi.SelectedIndex == 0) dpi = 150;
            else if (CmbDpi.SelectedIndex == 2) dpi = 600;

            int colorMode = 1;
            if (CmbColorMode.SelectedIndex == 1) colorMode = 2;
            else if (CmbColorMode.SelectedIndex == 2) colorMode = 4;

            var settings = await SecurityService.Instance.GetSettingsAsync();
            settings.DefaultScannerDeviceId = SelectedScanner.DeviceId;
            settings.DefaultScannerName = SelectedScanner.Name;
            settings.DefaultScanDpi = dpi;
            settings.DefaultScanColorMode = colorMode;
            settings.UseScannerFeeder = ChkUseFeeder.IsChecked == true;

            await SecurityService.Instance.SaveSettingsAsync(settings);
            UpdateDefaultScannerBanner(settings.DefaultScannerName, settings.DefaultScanDpi);

            MessageBox.Show(
                isAr
                    ? $"تم حفظ السكانر ({SelectedScanner.Name}) كماسح ضوئي افتراضي للبرنامج بنجاح!"
                    : $"Scanner ({SelectedScanner.Name}) saved as the default device!",
                Loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int dpi = 300;
                if (CmbDpi.SelectedIndex == 0) dpi = 150;
                else if (CmbDpi.SelectedIndex == 2) dpi = 600;

                ScanColorMode mode = ScanColorMode.Color;
                if (CmbColorMode.SelectedIndex == 1) mode = ScanColorMode.Grayscale;
                else if (CmbColorMode.SelectedIndex == 2) mode = ScanColorMode.BlackAndWhite;

                var parameters = new ScanParameters
                {
                    DeviceId = SelectedScanner?.DeviceId,
                    ResolutionDpi = dpi,
                    ColorMode = mode,
                    UseFeeder = ChkUseFeeder.IsChecked == true,
                    OutputFormat = "PNG"
                };

                IsEnabled = false;
                ScannedResultBytes = await _scannerService.ScanDocumentAsync(parameters);

                if (IsPdfBytes(ScannedResultBytes))
                {
                    ImgPreview.Source = null;
                    TxtPlaceholder.Text = Loc.IsArabic
                        ? "✅ تم مسح مستند متعدد الصفحات بنجاح وحفظه بصيغة PDF.\nاضغط على (اعتماد الوثيقة الممسوحة) لإرفاقه بالمعاملة."
                        : "✅ Multi-page document scanned successfully as PDF.\nClick (Accept Scanned Document) to attach it.";
                    TxtPlaceholder.Visibility = Visibility.Visible;
                    BtnAccept.IsEnabled = true;
                }
                else
                {
                    using var ms = new MemoryStream(ScannedResultBytes);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    ImgPreview.Source = bitmap;
                    TxtPlaceholder.Visibility = Visibility.Collapsed;
                    BtnAccept.IsEnabled = true;
                }
            }
            catch (OperationCanceledException)
            {
                // User cancelled interactive dialog
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private static bool IsPdfBytes(byte[]? bytes)
        {
            return bytes != null && bytes.Length >= 4 &&
                   bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46;
        }

        private void BtnAccept_Click(object sender, RoutedEventArgs e)
        {
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