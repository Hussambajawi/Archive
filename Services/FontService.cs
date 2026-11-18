using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocuArchivePro.Data;
using Microsoft.EntityFrameworkCore;

namespace DocuArchivePro.Services
{
    public class ArabicFontItem : INotifyPropertyChanged
    {
        public string Key { get; set; } = string.Empty;
        public string ArabicDisplayName { get; set; } = string.Empty;
        public string EnglishDisplayName { get; set; } = string.Empty;
        public string FontFamilyName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public FontFamily FontFamily { get; set; } = null!;

        public string DisplayName => LocalizationService.Instance.IsArabic ? ArabicDisplayName : EnglishDisplayName;

        public event PropertyChangedEventHandler? PropertyChanged;
        public void RefreshDisplayName() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));

        public override string ToString() => DisplayName;
    }

    public class FontService
    {
        private static FontService? _instance;
        public static FontService Instance => _instance ??= new FontService();

        private readonly List<ArabicFontItem> _fonts = new();
        public IReadOnlyList<ArabicFontItem> AvailableArabicFonts => _fonts;

        private ArabicFontItem? _currentArabicFont;
        public ArabicFontItem? CurrentArabicFont
        {
            get => _currentArabicFont;
            set
            {
                if (value != null && _currentArabicFont != value)
                {
                    _currentArabicFont = value;
                }
            }
        }

        public static readonly FontFamily DefaultEnglishFont = new FontFamily("Segoe UI, Tahoma, Arial");

        public FontService()
        {
            DiscoverFonts();
        }

        public void DiscoverFonts()
        {
            _fonts.Clear();

            string? fontsRoot = ResolveFontsDirectory();

            // 1. Alexandria
            string alexDir = fontsRoot != null && Directory.Exists(Path.Combine(fontsRoot, "Alexandria"))
                ? Path.Combine(fontsRoot, "Alexandria")
                : (fontsRoot != null && Directory.Exists(Path.Combine(fontsRoot, "Alexandria,Amiri,Amiri_Quran", "Alexandria", "static"))
                    ? Path.Combine(fontsRoot, "Alexandria,Amiri,Amiri_Quran", "Alexandria", "static")
                    : string.Empty);

            FontFamily alexFam = CreateFontFamily(alexDir, "Alexandria");
            _fonts.Add(new ArabicFontItem
            {
                Key = "Alexandria",
                ArabicDisplayName = "خط الإسكندرية الحديث (Alexandria)",
                EnglishDisplayName = "Alexandria Modern Arabic Font",
                FontFamilyName = "Alexandria",
                FolderPath = alexDir,
                FontFamily = alexFam
            });

            // 2. Amiri
            string amiriDir = fontsRoot != null && Directory.Exists(Path.Combine(fontsRoot, "Amiri"))
                ? Path.Combine(fontsRoot, "Amiri")
                : (fontsRoot != null && Directory.Exists(Path.Combine(fontsRoot, "Alexandria,Amiri,Amiri_Quran", "Amiri"))
                    ? Path.Combine(fontsRoot, "Alexandria,Amiri,Amiri_Quran", "Amiri")
                    : string.Empty);

            FontFamily amiriFam = CreateFontFamily(amiriDir, "Amiri");
            _fonts.Add(new ArabicFontItem
            {
                Key = "Amiri",
                ArabicDisplayName = "خط الأميري الكلاسيكي (Amiri)",
                EnglishDisplayName = "Amiri Classic Arabic Font",
                FontFamilyName = "Amiri",
                FolderPath = amiriDir,
                FontFamily = amiriFam
            });

            // 3. Amiri Quran
            string quranDir = fontsRoot != null && Directory.Exists(Path.Combine(fontsRoot, "Amiri_Quran"))
                ? Path.Combine(fontsRoot, "Amiri_Quran")
                : (fontsRoot != null && Directory.Exists(Path.Combine(fontsRoot, "Alexandria,Amiri,Amiri_Quran", "Amiri_Quran"))
                    ? Path.Combine(fontsRoot, "Alexandria,Amiri,Amiri_Quran", "Amiri_Quran")
                    : string.Empty);

            FontFamily quranFam = CreateFontFamily(quranDir, "Amiri Quran");
            _fonts.Add(new ArabicFontItem
            {
                Key = "Amiri_Quran",
                ArabicDisplayName = "خط أميري قرآن العثماني (Amiri Quran)",
                EnglishDisplayName = "Amiri Quran Calligraphic Font",
                FontFamilyName = "Amiri Quran",
                FolderPath = quranDir,
                FontFamily = quranFam
            });

            _currentArabicFont = _fonts[0]; // Default to Alexandria
        }

        private static string? ResolveFontsDirectory()
        {
            string candidate1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArabicFonts");
            if (Directory.Exists(candidate1)) return candidate1;

            string candidate2 = @"C:\Users\Pc\Desktop\DocuArchivePro\ArabicFonts";
            if (Directory.Exists(candidate2)) return candidate2;

            string? cur = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(cur))
            {
                string c = Path.Combine(cur, "ArabicFonts");
                if (Directory.Exists(c)) return c;
                var parent = Directory.GetParent(cur);
                cur = parent?.FullName;
            }

            return null;
        }

        private static FontFamily CreateFontFamily(string dirPath, string familyName)
        {
            if (!string.IsNullOrEmpty(dirPath) && Directory.Exists(dirPath))
            {
                try
                {
                    string uriString = "file:///" + dirPath.Replace('\\', '/').TrimEnd('/') + "/";
                    var baseUri = new Uri(uriString);
                    return new FontFamily(baseUri, $"./#{familyName}, Segoe UI, Tahoma, Cairo, Arial");
                }
                catch
                {
                }
            }
            return new FontFamily($"{familyName}, Segoe UI, Tahoma, Cairo, Arial");
        }

        public async Task LoadSettingsAsync()
        {
            try
            {
                using var db = new ArchiveDbContext();
                var settings = await db.SystemSettings.FirstOrDefaultAsync();
                if (settings != null && !string.IsNullOrWhiteSpace(settings.SelectedArabicFont))
                {
                    var match = _fonts.FirstOrDefault(f => f.Key.Equals(settings.SelectedArabicFont, StringComparison.OrdinalIgnoreCase) ||
                                                           f.FontFamilyName.Equals(settings.SelectedArabicFont, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        _currentArabicFont = match;
                    }
                }
            }
            catch
            {
            }
        }

        public async Task SaveSelectedFontAsync(string fontKey)
        {
            var match = _fonts.FirstOrDefault(f => f.Key.Equals(fontKey, StringComparison.OrdinalIgnoreCase) ||
                                                   f.FontFamilyName.Equals(fontKey, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                _currentArabicFont = match;
                try
                {
                    using var db = new ArchiveDbContext();
                    var settings = await db.SystemSettings.FirstOrDefaultAsync();
                    if (settings != null)
                    {
                        settings.SelectedArabicFont = match.Key;
                        settings.UpdatedAt = DateTime.Now;
                        await db.SaveChangesAsync();
                    }
                }
                catch
                {
                }

                var loc = LocalizationService.Instance;
                if (loc.IsArabic)
                {
                    ApplyLanguage("ar");
                }
            }
        }

        public void ApplyLanguage(string languageCode)
        {
            bool isArabic = languageCode.Equals("ar", StringComparison.OrdinalIgnoreCase);

            // 1. Update dynamic AppFontFamily in Application Resources
            FontFamily targetFamily = isArabic
                ? (_currentArabicFont?.FontFamily ?? _fonts[0].FontFamily)
                : DefaultEnglishFont;

            if (Application.Current != null)
            {
                Application.Current.Resources["AppFontFamily"] = targetFamily;
            }

            // Refresh localized font item names
            foreach (var f in _fonts)
            {
                f.RefreshDisplayName();
            }

            // 2. Apply NumberSubstitution, Language & FlowDirection to all open windows
            if (Application.Current != null && Application.Current.Windows != null)
            {
                foreach (Window window in Application.Current.Windows)
                {
                    ApplyToWindow(window, isArabic);
                }
            }
        }

        public void ApplyToWindow(Window window, bool? isArabicParam = null)
        {
            if (window == null) return;

            bool isArabic = isArabicParam ?? LocalizationService.Instance.IsArabic;

            if (isArabic)
            {
                NumberSubstitution.SetCultureSource(window, NumberCultureSource.Override);
                NumberSubstitution.SetCultureOverride(window, new CultureInfo("ar-SA"));
                NumberSubstitution.SetSubstitution(window, NumberSubstitutionMethod.Traditional);
                window.Language = XmlLanguage.GetLanguage("ar-SA");
                window.FlowDirection = FlowDirection.RightToLeft;
            }
            else
            {
                NumberSubstitution.SetCultureSource(window, NumberCultureSource.Override);
                NumberSubstitution.SetCultureOverride(window, new CultureInfo("en-US"));
                NumberSubstitution.SetSubstitution(window, NumberSubstitutionMethod.European);
                window.Language = XmlLanguage.GetLanguage("en-US");
                window.FlowDirection = FlowDirection.LeftToRight;
            }

            window.UseLayoutRounding = true;
            window.SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(window, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(window, TextRenderingMode.ClearType);

            try
            {
                bool isSecurityWindow =
                    window is Views.ActionPinDialog ||
                    window is Views.LoginPromptDialog ||
                    window is Views.UserLoginDialog ||
                    window is Views.DeleteCategoryWizardDialog ||
                    window is Views.VaultSettingsDialog ||
                    window is Views.AddCategoryDialog ||
                    (window is Views.AddEntityDialog && !(SecurityService.Instance.CurrentUser.IsAdmin || SecurityService.Instance.CurrentUser.CanManageEntities));

                string iconUri = isSecurityWindow
                    ? "pack://application:,,,/Resources/Icons/SecurityLockIcon.png"
                    : "pack://application:,,,/Resources/Icons/AppIcon.png";
                window.Icon = BitmapFrame.Create(new Uri(iconUri, UriKind.Absolute));
            }
            catch
            {
            }

            // Bind root grid LayoutTransform to global UiScale so user-selected button/window size applies across all screens
            if (window.Content is FrameworkElement rootElement && rootElement.LayoutTransform is Transform existing && existing is not ScaleTransform)
            {
                // Leave custom non-scale transforms untouched
            }
            else if (window.Content is FrameworkElement rootEl && rootEl.LayoutTransform is Transform t && t == Transform.Identity)
            {
                var scale = new ScaleTransform();
                var loc = LocalizationService.Instance;
                BindingOperations.SetBinding(scale, ScaleTransform.ScaleXProperty, new Binding(nameof(LocalizationService.UiScale)) { Source = loc, Mode = BindingMode.OneWay });
                BindingOperations.SetBinding(scale, ScaleTransform.ScaleYProperty, new Binding(nameof(LocalizationService.UiScale)) { Source = loc, Mode = BindingMode.OneWay });
                rootEl.LayoutTransform = scale;
            }

            if (!HasWheelZoomHandler(window))
            {
                window.SetValue(HasWheelZoomAttachedProperty, true);
                window.PreviewMouseWheel += (s, e) =>
                {
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        LocalizationService.Instance.UiScale += e.Delta > 0 ? 0.05 : -0.05;
                        e.Handled = true;
                    }
                };
            }

            ThemeService.Instance.ApplyThemeToWindow(window);
        }

        private static readonly DependencyProperty HasWheelZoomAttachedProperty =
            DependencyProperty.RegisterAttached("HasWheelZoomAttached", typeof(bool), typeof(FontService), new PropertyMetadata(false));

        private static bool HasWheelZoomHandler(Window w) => (bool)w.GetValue(HasWheelZoomAttachedProperty);
    }
}