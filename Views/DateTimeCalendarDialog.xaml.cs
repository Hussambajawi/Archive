using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DocuArchivePro.Services;

namespace DocuArchivePro.Views
{
    public partial class DateTimeCalendarDialog : Window
    {
        private readonly DispatcherTimer _timer;
        public ILocalizationService Loc => LocalizationService.Instance;

        public DateTimeCalendarDialog()
        {
            InitializeComponent();
            DataContext = this;
            UpdateClockAndDates();

            DpConverter.SelectedDate = DateTime.Today;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (s, e) => UpdateClockAndDates();
            _timer.Start();

            Closed += (s, e) => _timer.Stop();
        }

        private void UpdateClockAndDates()
        {
            var now = DateTime.Now;
            if (Loc.IsArabic)
            {
                TxtDayBanner.Text = $"يوم {DateTimeHelper.GetDayNameArabic(now)}";
                TxtLiveClock.Text = DateTimeHelper.GetTimeArabic(now);
                TxtHijriFull.Text = DateTimeHelper.GetHijriDateArabic(now);
                TxtHijriNumeric.Text = "التقويم الهجري المعتمد للأرشفة";
                TxtGregorianFull.Text = DateTimeHelper.GetGregorianDateArabic(now);
                TxtGregorianNumeric.Text = DateTimeHelper.FormatDigits($"التقويم الميلادي الرسمي ({now:yyyy-MM-dd})", true);
            }
            else
            {
                TxtDayBanner.Text = DateTimeHelper.GetDayNameEnglish(now);
                TxtLiveClock.Text = DateTimeHelper.GetTimeEnglish(now);
                TxtHijriFull.Text = DateTimeHelper.GetHijriDateEnglish(now);
                TxtHijriNumeric.Text = "Official Umm Al-Qura Hijri Calendar";
                TxtGregorianFull.Text = DateTimeHelper.GetGregorianDateEnglish(now);
                TxtGregorianNumeric.Text = $"Official Gregorian Calendar ({now:yyyy-MM-dd})";
            }
        }

        private void DpConverter_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DpConverter.SelectedDate is DateTime dt)
            {
                if (Loc.IsArabic)
                {
                    string day = DateTimeHelper.GetDayNameArabic(dt);
                    string greg = DateTimeHelper.GetGregorianDateArabic(dt);
                    string hijri = DateTimeHelper.GetHijriDateArabic(dt);

                    TxtConvertedResult.Text = $"اليوم: {day}  |  الميلادي: {greg}";
                    TxtConvertedSub.Text = $"الموافق بالهجري: {hijri}";
                }
                else
                {
                    string day = DateTimeHelper.GetDayNameEnglish(dt);
                    string greg = DateTimeHelper.GetGregorianDateEnglish(dt);
                    string hijri = DateTimeHelper.GetHijriDateEnglish(dt);

                    TxtConvertedResult.Text = $"Day: {day}  |  Gregorian: {greg}";
                    TxtConvertedSub.Text = $"Corresponding Hijri: {hijri}";
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}