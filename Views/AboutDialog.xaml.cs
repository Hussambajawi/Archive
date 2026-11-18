using System.Windows;
using DocuArchivePro.Services;

namespace DocuArchivePro.Views
{
    public partial class AboutDialog : Window
    {
        public ILocalizationService Loc => LocalizationService.Instance;

        public AboutDialog()
        {
            InitializeComponent();
            DataContext = this;

            Loaded += (s, e) =>
            {
                FontService.Instance.ApplyToWindow(this);
                ThemeService.Instance.ApplyThemeToWindow(this);
            };
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
