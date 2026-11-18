using System.Globalization;

namespace DocuArchivePro.Models
{
    public class AppLanguage
    {
        public string Code { get; set; } = "ar";
        public string NativeName { get; set; } = "العربية";
        public string EnglishName { get; set; } = "Arabic";
        public bool IsRightToLeft { get; set; } = true;
        public string FlagIcon { get; set; } = "🇸🇦";

        public override string ToString() => $"{FlagIcon} {NativeName} ({EnglishName})";
    }
}
