using System;
using System.Globalization;

namespace DocuArchivePro.Services
{
    public static class DateTimeHelper
    {
        private static readonly string[] HijriMonthsAr =
        {
            "محرم", "صفر", "ربيع الأول", "ربيع الآخر",
            "جمادى الأولى", "جمادى الآخرة", "رجب", "شعبان",
            "رمضان", "شوال", "ذو القعدة", "ذو الحجة"
        };

        private static readonly string[] HijriMonthsEn =
        {
            "Muharram", "Safar", "Rabi' al-Awwal", "Rabi' al-Thani",
            "Jumada al-Awwal", "Jumada al-Thani", "Rajab", "Sha'ban",
            "Ramadan", "Shawwal", "Dhu al-Qi'dah", "Dhu al-Hijjah"
        };

        private static readonly string[] GregorianMonthsAr =
        {
            "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
            "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"
        };

        private static readonly string[] ArabicDays =
        {
            "الأحد", "الإثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"
        };

        public static string ToArabicDigits(this string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return input
                .Replace('0', '٠')
                .Replace('1', '١')
                .Replace('2', '٢')
                .Replace('3', '٣')
                .Replace('4', '٤')
                .Replace('5', '٥')
                .Replace('6', '٦')
                .Replace('7', '٧')
                .Replace('8', '٨')
                .Replace('9', '٩');
        }

        public static string ToEuropeanDigits(this string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return input
                .Replace('٠', '0')
                .Replace('١', '1')
                .Replace('٢', '2')
                .Replace('٣', '3')
                .Replace('٤', '4')
                .Replace('٥', '5')
                .Replace('٦', '6')
                .Replace('٧', '7')
                .Replace('٨', '8')
                .Replace('٩', '9');
        }

        /// <summary>
        /// Converts digits in a string to Arabic-Indic (٠-٩) when Arabic is active,
        /// or European digits (0-9) when English is active.
        /// </summary>
        public static string FormatDigits(string? input, bool? isArabic = null)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            bool ar = isArabic ?? LocalizationService.Instance.IsArabic;
            return ar ? input.ToArabicDigits() : input.ToEuropeanDigits();
        }

        /// <summary>
        /// Formats any numeric or textual value with the active language's digit system.
        /// </summary>
        public static string FormatNumber(object? value, bool? isArabic = null)
        {
            if (value == null) return string.Empty;
            string str = value switch
            {
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
            return FormatDigits(str, isArabic);
        }

        public static string GetDayNameArabic(DateTime dt)
        {
            return ArabicDays[(int)dt.DayOfWeek];
        }

        public static string GetTimeArabic(DateTime dt)
        {
            int hour12 = dt.Hour % 12;
            if (hour12 == 0) hour12 = 12;
            string amPm = dt.Hour >= 12 ? "مساءً" : "صباحاً";
            string raw = $"{hour12:D2}:{dt.Minute:D2}:{dt.Second:D2} {amPm}";
            return raw.ToArabicDigits();
        }

        public static string GetGregorianDateArabic(DateTime dt)
        {
            string monthName = GregorianMonthsAr[dt.Month - 1];
            string raw = $"{dt.Day:D2} {monthName} {dt.Year} م ({dt:yyyy/MM/dd})";
            return raw.ToArabicDigits();
        }

        public static string GetHijriDateArabic(DateTime dt)
        {
            int hYear, hMonth, hDay;
            try
            {
                Calendar cal = new UmAlQuraCalendar();
                hYear = cal.GetYear(dt);
                hMonth = cal.GetMonth(dt);
                hDay = cal.GetDayOfMonth(dt);
            }
            catch
            {
                Calendar cal = new HijriCalendar();
                hYear = cal.GetYear(dt);
                hMonth = cal.GetMonth(dt);
                hDay = cal.GetDayOfMonth(dt);
            }
            string monthName = HijriMonthsAr[Math.Clamp(hMonth - 1, 0, 11)];
            string raw = $"{hDay:D2} {monthName} {hYear} هـ ({hYear:D4}/{hMonth:D2}/{hDay:D2} هـ)";
            return raw.ToArabicDigits();
        }

        // English format methods (using default European digits and Latin words)
        public static string GetDayNameEnglish(DateTime dt)
        {
            return dt.ToString("dddd", CultureInfo.InvariantCulture);
        }

        public static string GetTimeEnglish(DateTime dt)
        {
            return dt.ToString("hh:mm:ss tt", CultureInfo.InvariantCulture);
        }

        public static string GetGregorianDateEnglish(DateTime dt)
        {
            return dt.ToString("dd MMMM yyyy (yyyy/MM/dd)", CultureInfo.InvariantCulture);
        }

        public static string GetHijriDateEnglish(DateTime dt)
        {
            int hYear, hMonth, hDay;
            try
            {
                Calendar cal = new UmAlQuraCalendar();
                hYear = cal.GetYear(dt);
                hMonth = cal.GetMonth(dt);
                hDay = cal.GetDayOfMonth(dt);
            }
            catch
            {
                Calendar cal = new HijriCalendar();
                hYear = cal.GetYear(dt);
                hMonth = cal.GetMonth(dt);
                hDay = cal.GetDayOfMonth(dt);
            }
            string monthName = HijriMonthsEn[Math.Clamp(hMonth - 1, 0, 11)];
            return $"{hDay:D2} {monthName} {hYear} AH ({hYear:D4}/{hMonth:D2}/{hDay:D2})";
        }
    }
}
