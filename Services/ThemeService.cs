using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using DocuArchivePro.Data;
using Microsoft.EntityFrameworkCore;

namespace DocuArchivePro.Services
{
    /// <summary>
    /// Manages global Light (Day) and Dark (Night) themes across all windows, controls,
    /// DatePickers, Calendar popups, ComboBox dropdowns, ContextMenus, and ToolTips.
    /// Preserves all {DynamicResource} and {TemplateBinding} expressions across round-trip toggles.
    /// </summary>
    public class ThemeService : INotifyPropertyChanged
    {
        private static ThemeService? _instance;
        public static ThemeService Instance => _instance ??= new ThemeService();

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _isDarkMode;
        public bool IsDarkMode => _isDarkMode;

        public string ThemeIcon => _isDarkMode ? "☀️" : "🌙";

        public string ThemeButtonLabel
        {
            get
            {
                bool isAr = LocalizationService.Instance.IsArabic;
                if (_isDarkMode)
                {
                    return isAr ? "☀️ الوضع النهاري" : "☀️ Day Mode";
                }
                return isAr ? "🌙 الوضع الليلي" : "🌙 Night Mode";
            }
        }

        public string CurrentThemeStatusText
        {
            get
            {
                bool isAr = LocalizationService.Instance.IsArabic;
                if (_isDarkMode)
                {
                    return isAr ? "الوضع الحالي: ليلي 🌙" : "Current Theme: Night Mode 🌙";
                }
                return isAr ? "الوضع الحالي: نهاري ☀️" : "Current Theme: Day Mode ☀️";
            }
        }

        static ThemeService()
        {
            try
            {
                EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnPopupMenuOpened));
                EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(OnPopupMenuOpened));
            }
            catch
            {
                // Safe guard in non-standard hosts
            }
        }

        private static void OnDatePickerCalendarOpened(object? sender, RoutedEventArgs e)
        {
            if (sender is DatePicker dp)
            {
                var popup = dp.Template?.FindName("PART_Popup", dp) as Popup;
                if (popup?.Child != null)
                {
                    Instance.ApplyThemeToSubtree(popup.Child);
                }
            }
        }

        private static void OnComboBoxDropDownOpened(object? sender, EventArgs e)
        {
            if (sender is ComboBox cmb)
            {
                var popup = cmb.Template?.FindName("PART_Popup", cmb) as Popup;
                if (popup?.Child != null)
                {
                    Instance.ApplyThemeToSubtree(popup.Child);
                }
            }
        }

        private static void OnPopupMenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is DependencyObject dep)
            {
                Instance.ApplyThemeToSubtree(dep);
            }
        }

        private static readonly DependencyProperty OriginalBackgroundProperty =
            DependencyProperty.RegisterAttached("OriginalBackground", typeof(SolidColorBrush), typeof(ThemeService), new PropertyMetadata(null));
        private static readonly DependencyProperty HasOriginalBackgroundProperty =
            DependencyProperty.RegisterAttached("HasOriginalBackground", typeof(bool), typeof(ThemeService), new PropertyMetadata(false));

        private static readonly DependencyProperty OriginalForegroundProperty =
            DependencyProperty.RegisterAttached("OriginalForeground", typeof(SolidColorBrush), typeof(ThemeService), new PropertyMetadata(null));
        private static readonly DependencyProperty HasOriginalForegroundProperty =
            DependencyProperty.RegisterAttached("HasOriginalForeground", typeof(bool), typeof(ThemeService), new PropertyMetadata(false));

        private static readonly DependencyProperty OriginalBorderBrushProperty =
            DependencyProperty.RegisterAttached("OriginalBorderBrush", typeof(SolidColorBrush), typeof(ThemeService), new PropertyMetadata(null));
        private static readonly DependencyProperty HasOriginalBorderBrushProperty =
            DependencyProperty.RegisterAttached("HasOriginalBorderBrush", typeof(bool), typeof(ThemeService), new PropertyMetadata(false));

        private static readonly Dictionary<string, string> DarkBackgroundMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["#FFFFFF"] = "#1E293B",
            ["#FFFFFFFF"] = "#1E293B",
            ["#F8FAFC"] = "#0F172A",
            ["#FFF8FAFC"] = "#0F172A",
            ["#F1F5F9"] = "#162032",
            ["#FFF1F5F9"] = "#162032",
            ["#E2E8F0"] = "#334155",
            ["#CBD5E1"] = "#334155",
            ["#EFF6FF"] = "#172554",
            ["#DBEAFE"] = "#1E3A8A",
            ["#E0F2FE"] = "#0C4A6E",
            ["#F0F9FF"] = "#0C4A6E",
            ["#ECFDF5"] = "#064E3B",
            ["#F0FDF4"] = "#064E3B",
            ["#DCFCE7"] = "#065F46",
            ["#D1FAE5"] = "#065F46",
            ["#FEF2F2"] = "#450A0A",
            ["#FEE2E2"] = "#450A0A",
            ["#FFFBEB"] = "#451A03",
            ["#FEF3C7"] = "#451A03",
            ["#FFF7ED"] = "#431407",
            ["#FAF5FF"] = "#2E1065",
            ["#F5F3FF"] = "#2E1065",
            ["#EDE9FE"] = "#3B0764",
            ["#FCE7F3"] = "#500724"
        };

        private static readonly Dictionary<string, string> DarkForegroundMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["#000000"] = "#F8FAFC",
            ["#FF000000"] = "#F8FAFC",
            ["#0F172A"] = "#F8FAFC",
            ["#FF0F172A"] = "#F8FAFC",
            ["#1E293B"] = "#F1F5F9",
            ["#FF1E293B"] = "#F1F5F9",
            ["#334155"] = "#E2E8F0",
            ["#475569"] = "#CBD5E1",
            ["#64748B"] = "#94A3B8",
            ["#1A56DB"] = "#60A5FA",
            ["#1E3A8A"] = "#93C5FD",
            ["#1E40AF"] = "#93C5FD",
            ["#1D4ED8"] = "#93C5FD",
            ["#065F46"] = "#6EE7B7",
            ["#064E3B"] = "#A7F3D0",
            ["#047857"] = "#6EE7B7",
            ["#059669"] = "#34D399",
            ["#166534"] = "#86EFAC",
            ["#14532D"] = "#BBF7D0",
            ["#15803D"] = "#4ADE80",
            ["#075985"] = "#7DD3FC",
            ["#0C4A6E"] = "#BAE6FD",
            ["#0369A1"] = "#7DD3FC",
            ["#0284C7"] = "#38BDF8",
            ["#991B1B"] = "#FCA5A5",
            ["#7F1D1D"] = "#FECACA",
            ["#B91C1C"] = "#FCA5A5",
            ["#DC2626"] = "#F87171",
            ["#EF4444"] = "#F87171",
            ["#FFFF0000"] = "#F87171",
            ["#5B21B6"] = "#C4B5FD",
            ["#4C1D95"] = "#DDD6FE",
            ["#6D28D9"] = "#C4B5FD",
            ["#92400E"] = "#FDE68A",
            ["#78350F"] = "#FDE68A",
            ["#B45309"] = "#FCD34D",
            ["#D97706"] = "#FBBF24",
            ["#9A3412"] = "#FDBA74",
            ["#C2410C"] = "#FB923C",
            ["#BE185D"] = "#F472B6"
        };

        private static readonly Dictionary<string, string> DarkBorderMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["#E2E8F0"] = "#334155",
            ["#CBD5E1"] = "#475569",
            ["#94A3B8"] = "#475569",
            ["#BFDBFE"] = "#1E40AF",
            ["#93C5FD"] = "#2563EB",
            ["#BAE6FD"] = "#0369A1",
            ["#A7F3D0"] = "#047857",
            ["#BBF7D0"] = "#15803D",
            ["#86EFAC"] = "#166534",
            ["#34D399"] = "#059669",
            ["#FECACA"] = "#991B1B",
            ["#FCA5A5"] = "#B91C1C",
            ["#F87171"] = "#DC2626",
            ["#FDE68A"] = "#92400E",
            ["#FCD34D"] = "#B45309",
            ["#FED7AA"] = "#9A3412",
            ["#E9D5FF"] = "#6D28D9",
            ["#DDD6FE"] = "#5B21B6",
            ["#A78BFA"] = "#7C3AED"
        };

        public async Task LoadSettingsAsync()
        {
            bool isDark = false;
            try
            {
                using var db = new ArchiveDbContext();
                var settings = await db.SystemSettings.FirstOrDefaultAsync();
                if (settings != null && string.Equals(settings.SelectedTheme, "Dark", StringComparison.OrdinalIgnoreCase))
                {
                    isDark = true;
                }
            }
            catch
            {
            }

            SetTheme(isDark, saveToDb: false);
        }

        public void ToggleTheme()
        {
            SetTheme(!_isDarkMode, saveToDb: true);
        }

        public void SetTheme(bool isDark, bool saveToDb = true)
        {
            _isDarkMode = isDark;
            UpdateResourcePalette(isDark);
            NotifyThemeBindings();

            if (Application.Current?.Windows != null)
            {
                foreach (Window window in Application.Current.Windows)
                {
                    ApplyThemeToWindow(window);
                }
            }

            if (saveToDb)
            {
                _ = SaveThemePreferenceAsync(isDark ? "Dark" : "Light");
            }
        }

        public void NotifyThemeBindings()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDarkMode)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThemeIcon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThemeButtonLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentThemeStatusText)));
        }

        private static async Task SaveThemePreferenceAsync(string themeName)
        {
            try
            {
                using var db = new ArchiveDbContext();
                var settings = await db.SystemSettings.FirstOrDefaultAsync();
                if (settings != null)
                {
                    settings.SelectedTheme = themeName;
                    settings.UpdatedAt = DateTime.Now;
                    await db.SaveChangesAsync();
                }
            }
            catch
            {
            }
        }

        private static void UpdateResourcePalette(bool isDark)
        {
            if (Application.Current == null) return;
            var res = Application.Current.Resources;

            if (isDark)
            {
                SetColorAndBrush(res, "PrimaryColor", "PrimaryBrush", "#3B82F6");
                SetColorAndBrush(res, "PrimaryHoverColor", "PrimaryHoverBrush", "#2563EB");
                SetColorAndBrush(res, "PrimaryLightColor", "PrimaryLightBrush", "#1E3A8A");
                SetColorAndBrush(res, "AccentColor", "AccentBrush", "#14B8A6");

                SetColorAndBrush(res, "BgDarkSidebar", "SidebarBrush", "#090E1A");
                SetColorAndBrush(res, "BgSidebarHover", "SidebarHoverBrush", "#1E293B");
                SetBrush(res, "SidebarCardBrush", "#111827");
                SetBrush(res, "SidebarNavBtnBgBrush", "#111827");
                SetBrush(res, "SidebarNavBtnFgBrush", "#F1F5F9");
                SetBrush(res, "SidebarNavBtnBorderBrush", "#334155");
                SetBrush(res, "SidebarNavBtnHoverBrush", "#1E293B");
                SetBrush(res, "HeaderBannerBgBrush", "#111827");

                SetColorAndBrush(res, "BgApp", "AppBackgroundBrush", "#0B1120");
                SetColorAndBrush(res, "BgCard", "CardBrush", "#1E293B");
                SetColorAndBrush(res, "BorderLight", "BorderBrushLight", "#334155");
                SetColorAndBrush(res, "TextPrimary", "TextPrimaryBrush", "#F8FAFC");
                SetColorAndBrush(res, "TextSecondary", "TextSecondaryBrush", "#94A3B8");

                SetBrush(res, "SurfaceSubtleBrush", "#0F172A");
                SetBrush(res, "InputBackgroundBrush", "#0F172A");
                SetBrush(res, "InputForegroundBrush", "#F8FAFC");
                SetBrush(res, "SecondaryButtonBgBrush", "#1E293B");
                SetBrush(res, "SecondaryButtonFgBrush", "#F1F5F9");
                SetBrush(res, "SecondaryButtonHoverBrush", "#334155");

                SetBrush(res, "PopupBgBrush", "#1E293B");
                SetBrush(res, "PopupHoverBrush", "#334155");
                SetBrush(res, "CalendarHeaderBgBrush", "#0F172A");
                SetBrush(res, "DatePickerBtnBgBrush", "#1E293B");
                SetBrush(res, "DatePickerBtnHoverBrush", "#334155");

                SetBrush(res, "DataGridHeaderBgBrush", "#0F172A");
                SetBrush(res, "DataGridRowHoverBrush", "#283548");
                SetBrush(res, "DataGridGridLinesBrush", "#334155");
            }
            else
            {
                SetColorAndBrush(res, "PrimaryColor", "PrimaryBrush", "#1A56DB");
                SetColorAndBrush(res, "PrimaryHoverColor", "PrimaryHoverBrush", "#1E429F");
                SetColorAndBrush(res, "PrimaryLightColor", "PrimaryLightBrush", "#E1EFFE");
                SetColorAndBrush(res, "AccentColor", "AccentBrush", "#0D9488");

                SetColorAndBrush(res, "BgDarkSidebar", "SidebarBrush", "#0F172A");
                SetColorAndBrush(res, "BgSidebarHover", "SidebarHoverBrush", "#1E293B");
                SetBrush(res, "SidebarCardBrush", "#1E293B");
                SetBrush(res, "SidebarNavBtnBgBrush", "#1E293B");
                SetBrush(res, "SidebarNavBtnFgBrush", "#FFFFFF");
                SetBrush(res, "SidebarNavBtnBorderBrush", "#334155");
                SetBrush(res, "SidebarNavBtnHoverBrush", "#334155");
                SetBrush(res, "HeaderBannerBgBrush", "#1E293B");

                SetColorAndBrush(res, "BgApp", "AppBackgroundBrush", "#F8FAFC");
                SetColorAndBrush(res, "BgCard", "CardBrush", "#FFFFFF");
                SetColorAndBrush(res, "BorderLight", "BorderBrushLight", "#E2E8F0");
                SetColorAndBrush(res, "TextPrimary", "TextPrimaryBrush", "#0F172A");
                SetColorAndBrush(res, "TextSecondary", "TextSecondaryBrush", "#64748B");

                SetBrush(res, "SurfaceSubtleBrush", "#F8FAFC");
                SetBrush(res, "InputBackgroundBrush", "#FFFFFF");
                SetBrush(res, "InputForegroundBrush", "#0F172A");
                SetBrush(res, "SecondaryButtonBgBrush", "#FFFFFF");
                SetBrush(res, "SecondaryButtonFgBrush", "#0F172A");
                SetBrush(res, "SecondaryButtonHoverBrush", "#F1F5F9");

                SetBrush(res, "PopupBgBrush", "#FFFFFF");
                SetBrush(res, "PopupHoverBrush", "#E1EFFE");
                SetBrush(res, "CalendarHeaderBgBrush", "#F1F5F9");
                SetBrush(res, "DatePickerBtnBgBrush", "#F1F5F9");
                SetBrush(res, "DatePickerBtnHoverBrush", "#E2E8F0");

                SetBrush(res, "DataGridHeaderBgBrush", "#F8FAFC");
                SetBrush(res, "DataGridRowHoverBrush", "#F8FAFC");
                SetBrush(res, "DataGridGridLinesBrush", "#F1F5F9");
            }
        }

        private static void SetColorAndBrush(ResourceDictionary res, string colorKey, string brushKey, string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            res[colorKey] = c;
            var b = new SolidColorBrush(c);
            b.Freeze();
            res[brushKey] = b;
        }

        private static void SetBrush(ResourceDictionary res, string brushKey, string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(c);
            b.Freeze();
            res[brushKey] = b;
        }

        public void ApplyThemeToWindow(Window? window)
        {
            if (window == null) return;

            ApplyThemeRecursive(window, _isDarkMode, new HashSet<DependencyObject>());

            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                ApplyThemeRecursive(window, _isDarkMode, new HashSet<DependencyObject>());
            }));
        }

        public void ApplyThemeToSubtree(DependencyObject root)
        {
            if (root == null) return;
            ApplyThemeRecursive(root, _isDarkMode, new HashSet<DependencyObject>());
        }

        private static bool IsTemplateLeafControl(DependencyObject node)
        {
            return node is ButtonBase ||
                   node is ComboBox ||
                   node is ComboBoxItem ||
                   node is DatePicker ||
                   node is DatePickerTextBox ||
                   node is Calendar ||
                   node is CalendarItem ||
                   node is CalendarDayButton ||
                   node is CalendarButton ||
                   node is TextBox ||
                   node is PasswordBox ||
                   node is ScrollBar ||
                   node is FlowDocumentScrollViewer;
        }

        private void ApplyThemeRecursive(DependencyObject node, bool isDark, HashSet<DependencyObject> visited)
        {
            if (node == null || !visited.Add(node)) return;

            // Keep WYSIWYG white print paper preview untouched
            if (node is FlowDocumentScrollViewer)
            {
                return;
            }

            if (node is FrameworkElement fe)
            {
                ThemeElement(fe, isDark);
            }

            // Always walk logical children (covers Button content StackPanels, ListBoxItems, etc.)
            foreach (var child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is DependencyObject depChild)
                {
                    ApplyThemeRecursive(depChild, isDark, visited);
                }
            }

            // Do NOT walk into visual template internals of leaf/templated controls;
            // their ControlTemplates use {DynamicResource} and {TemplateBinding} directly.
            if (IsTemplateLeafControl(node))
            {
                return;
            }

            int visualCount = (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
                ? VisualTreeHelper.GetChildrenCount(node)
                : 0;
            for (int i = 0; i < visualCount; i++)
            {
                var vChild = VisualTreeHelper.GetChild(node, i);
                ApplyThemeRecursive(vChild, isDark, visited);
            }
        }

        private static void ThemeElement(FrameworkElement fe, bool isDark)
        {
            if (fe is Window win)
            {
                ThemeProperty(win, Control.BackgroundProperty, HasOriginalBackgroundProperty, OriginalBackgroundProperty, DarkBackgroundMap, isDark);
                return;
            }

            if (fe is Border border)
            {
                ThemeProperty(border, Border.BackgroundProperty, HasOriginalBackgroundProperty, OriginalBackgroundProperty, DarkBackgroundMap, isDark);
                ThemeProperty(border, Border.BorderBrushProperty, HasOriginalBorderBrushProperty, OriginalBorderBrushProperty, DarkBorderMap, isDark);
                return;
            }

            if (fe is TextBlock tb)
            {
                ThemeProperty(tb, TextBlock.ForegroundProperty, HasOriginalForegroundProperty, OriginalForegroundProperty, DarkForegroundMap, isDark);
                return;
            }

            if (fe is TextBox txt)
            {
                ThemeLocalInputOverrides(txt, isDark);
                txt.CaretBrush = GetDynamicOrFallbackBrush("InputForegroundBrush", isDark ? "#F8FAFC" : "#0F172A");
                return;
            }

            if (fe is PasswordBox pwd)
            {
                ThemeLocalInputOverrides(pwd, isDark);
                pwd.CaretBrush = GetDynamicOrFallbackBrush("InputForegroundBrush", isDark ? "#F8FAFC" : "#0F172A");
                return;
            }

            if (fe is DatePicker dp)
            {
                dp.CalendarOpened -= OnDatePickerCalendarOpened;
                dp.CalendarOpened += OnDatePickerCalendarOpened;
                ThemeLocalInputOverrides(dp, isDark);
                return;
            }

            if (fe is ComboBox cmb)
            {
                cmb.DropDownOpened -= OnComboBoxDropDownOpened;
                cmb.DropDownOpened += OnComboBoxDropDownOpened;
                ThemeLocalInputOverrides(cmb, isDark);
                return;
            }

            if (fe is ListBox)
            {
                ThemeLocalInputOverrides((Control)fe, isDark);
                return;
            }

            if (fe is Control ctrl)
            {
                ThemeProperty(ctrl, Control.BackgroundProperty, HasOriginalBackgroundProperty, OriginalBackgroundProperty, DarkBackgroundMap, isDark);
                ThemeProperty(ctrl, Control.ForegroundProperty, HasOriginalForegroundProperty, OriginalForegroundProperty, DarkForegroundMap, isDark);
                ThemeProperty(ctrl, Control.BorderBrushProperty, HasOriginalBorderBrushProperty, OriginalBorderBrushProperty, DarkBorderMap, isDark);
            }
        }

        /// <summary>
        /// Only snapshots and overrides explicit local SolidColorBrush values.
        /// Never overwrites {DynamicResource}, {TemplateBinding}, or Style setters.
        /// </summary>
        private static void ThemeLocalInputOverrides(Control ctrl, bool isDark)
        {
            ThemeProperty(ctrl, Control.BackgroundProperty, HasOriginalBackgroundProperty, OriginalBackgroundProperty, DarkBackgroundMap, isDark);
            ThemeProperty(ctrl, Control.ForegroundProperty, HasOriginalForegroundProperty, OriginalForegroundProperty, DarkForegroundMap, isDark);
            ThemeProperty(ctrl, Control.BorderBrushProperty, HasOriginalBorderBrushProperty, OriginalBorderBrushProperty, DarkBorderMap, isDark);
        }

        private static void ThemeProperty(
            DependencyObject el,
            DependencyProperty dp,
            DependencyProperty hasOrigProp,
            DependencyProperty origValProp,
            Dictionary<string, string> darkMap,
            bool isDark)
        {
            SolidColorBrush? origBrush;

            if (!(bool)el.GetValue(hasOrigProp))
            {
                object rawLocal = el.ReadLocalValue(dp);
                if (rawLocal is not SolidColorBrush localBrush)
                {
                    // Value comes from Style, {DynamicResource}, or {TemplateBinding}: do NOT overwrite it!
                    return;
                }

                origBrush = localBrush.IsFrozen ? localBrush : (SolidColorBrush)localBrush.CloneCurrentValue();
                if (!origBrush.IsFrozen) origBrush.Freeze();
                el.SetValue(origValProp, origBrush);
                el.SetValue(hasOrigProp, true);
            }
            else
            {
                origBrush = el.GetValue(origValProp) as SolidColorBrush;
            }

            if (origBrush == null) return;

            if (isDark)
            {
                if (origBrush.Color.A == 0)
                {
                    el.SetValue(dp, origBrush);
                    return;
                }

                string origHex = ColorToHex(origBrush.Color);
                if (darkMap.TryGetValue(origHex, out string? mappedHex))
                {
                    el.SetValue(dp, CreateFrozenBrush(mappedHex));
                }
                else
                {
                    el.SetValue(dp, origBrush);
                }
            }
            else
            {
                // Restore original Day Mode SolidColorBrush faithfully
                el.SetValue(dp, origBrush);
            }
        }

        private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static SolidColorBrush CreateFrozenBrush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        private static Brush GetDynamicOrFallbackBrush(string key, string fallbackHex)
        {
            if (Application.Current?.Resources[key] is Brush resBrush)
            {
                return resBrush;
            }
            return CreateFrozenBrush(fallbackHex);
        }
    }
}
