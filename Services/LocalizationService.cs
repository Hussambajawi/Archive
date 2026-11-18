using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using DocuArchivePro.Models;

namespace DocuArchivePro.Services
{
    public class LocalizationService : ILocalizationService
    {
        private static LocalizationService? _instance;
        public static LocalizationService Instance => _instance ??= new LocalizationService();

        private readonly Dictionary<string, Dictionary<string, string>> _translations = new();
        private AppLanguage _currentLanguage;
        private double _uiScale = 1.0;

        public event PropertyChangedEventHandler? PropertyChanged;

        public AppLanguage CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (value != null && _currentLanguage != value)
                {
                    _currentLanguage = value;
                    OnPropertyChanged(nameof(CurrentLanguage));
                    OnPropertyChanged(nameof(IsArabic));
                    OnPropertyChanged(nameof(CurrentFlowDirection));
                    OnPropertyChanged(nameof(UiScalePercentText));
                    OnPropertyChanged(nameof(ThemeButtonLabel));
                    OnPropertyChanged(nameof(CurrentThemeStatusText));
                    OnPropertyChanged("Item[]"); // Notifies WPF bindings of dictionary changes

                    // Dynamically apply corresponding font family and numerals (Arabic vs Default English)
                    try
                    {
                        FontService.Instance.ApplyLanguage(value.Code);
                        ThemeService.Instance.NotifyThemeBindings();
                    }
                    catch
                    {
                    }
                }
            }
        }

        public bool IsArabic =>
            _currentLanguage.Code.Equals("ar", StringComparison.OrdinalIgnoreCase);

        public FlowDirection CurrentFlowDirection =>
            _currentLanguage.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        public double UiScale
        {
            get => _uiScale;
            set
            {
                double clamped = Math.Round(Math.Clamp(value, 0.75, 1.50), 2);
                if (Math.Abs(_uiScale - clamped) > 0.001)
                {
                    _uiScale = clamped;
                    OnPropertyChanged(nameof(UiScale));
                    OnPropertyChanged(nameof(UiScalePercentText));
                }
            }
        }

        public string UiScalePercentText =>
            DateTimeHelper.FormatDigits($"{(int)Math.Round(UiScale * 100)}%", IsArabic);

        public bool IsDarkMode => ThemeService.Instance.IsDarkMode;
        public string ThemeButtonLabel => ThemeService.Instance.ThemeButtonLabel;
        public string CurrentThemeStatusText => ThemeService.Instance.CurrentThemeStatusText;

        public void ToggleTheme()
        {
            ThemeService.Instance.ToggleTheme();
        }

        public void SetTheme(bool isDark)
        {
            ThemeService.Instance.SetTheme(isDark);
        }

        public IReadOnlyList<AppLanguage> SupportedLanguages { get; } = new List<AppLanguage>
        {
            new AppLanguage { Code = "ar", NativeName = "العربية", EnglishName = "Arabic", IsRightToLeft = true, FlagIcon = "🇸🇦" },
            new AppLanguage { Code = "en", NativeName = "English (الإنجليزية)", EnglishName = "English", IsRightToLeft = false, FlagIcon = "🇺🇸" }
        };

        public string this[string key] => GetString(key);

        public LocalizationService()
        {
            _currentLanguage = SupportedLanguages[0]; // Arabic default as requested
            LoadLanguageFiles();
            ThemeService.Instance.PropertyChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(IsDarkMode));
                OnPropertyChanged(nameof(ThemeButtonLabel));
                OnPropertyChanged(nameof(CurrentThemeStatusText));
            };
        }

        private void LoadLanguageFiles()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string langFolder = Path.Combine(appDir, "Resources", "Languages");

            foreach (var lang in SupportedLanguages)
            {
                string filePath = Path.Combine(langFolder, $"Strings.{lang.Code}.json");
                if (File.Exists(filePath))
                {
                    try
                    {
                        string json = File.ReadAllText(filePath);
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (dict != null)
                        {
                            _translations[lang.Code] = dict;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to load language {lang.Code}: {ex.Message}");
                    }
                }
            }
        }

        public void SetLanguage(string languageCode)
        {
            var target = SupportedLanguages.FirstOrDefault(l => l.Code.Equals(languageCode, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                CurrentLanguage = target;
            }
        }

        public string GetString(string key)
        {
            if (_translations.TryGetValue(_currentLanguage.Code, out var dict) && dict.TryGetValue(key, out var val))
            {
                return val;
            }

            // Fallback to Arabic or English
            if (_translations.TryGetValue("ar", out var arDict) && arDict.TryGetValue(key, out var arVal))
            {
                return arVal;
            }

            if (_translations.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var enVal))
            {
                return enVal;
            }

            return key; // return key itself if not translated
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}