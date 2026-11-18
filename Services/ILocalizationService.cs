using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using DocuArchivePro.Models;

namespace DocuArchivePro.Services
{
    public interface ILocalizationService : INotifyPropertyChanged
    {
        AppLanguage CurrentLanguage { get; set; }
        IReadOnlyList<AppLanguage> SupportedLanguages { get; }
        FlowDirection CurrentFlowDirection { get; }
        bool IsArabic { get; }
        double UiScale { get; set; }
        string UiScalePercentText { get; }
        bool IsDarkMode { get; }
        string ThemeButtonLabel { get; }
        string CurrentThemeStatusText { get; }
        string this[string key] { get; }

        void SetLanguage(string languageCode);
        void ToggleTheme();
        void SetTheme(bool isDark);
        string GetString(string key);
    }
}