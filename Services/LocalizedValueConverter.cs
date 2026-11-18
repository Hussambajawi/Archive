using System;
using System.Globalization;
using System.Windows.Data;
using DocuArchivePro.Models;

namespace DocuArchivePro.Services
{
    /// <summary>
    /// Converts enums (DocumentType, DocumentPriority, DocumentStatus), dates, numbers, and strings
    /// to the active UI language and numeral format (Arabic-Indic vs Western digits).
    /// </summary>
    public class LocalizedValueConverter : IValueConverter
    {
        public static LocalizedValueConverter Instance { get; } = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;

            var loc = LocalizationService.Instance;
            bool isAr = loc.IsArabic;

            if (value is DocumentType docType)
            {
                return docType == DocumentType.Inbound ? loc["Inbound"] : loc["Outbound"];
            }

            if (value is DocumentPriority prio)
            {
                return prio switch
                {
                    DocumentPriority.Urgent => loc["PriorityUrgent"],
                    DocumentPriority.VeryUrgent => loc["PriorityVeryUrgent"],
                    DocumentPriority.Confidential => loc["PriorityConfidential"],
                    _ => loc["PriorityNormal"]
                };
            }

            if (value is DocumentStatus status)
            {
                return status switch
                {
                    DocumentStatus.InProgress => loc["StatusInProgress"],
                    DocumentStatus.Completed => loc["StatusCompleted"],
                    DocumentStatus.Archived => loc["StatusArchived"],
                    DocumentStatus.Cancelled => loc["StatusCancelled"],
                    _ => loc["StatusNew"]
                };
            }

            if (value is DateTime dt)
            {
                string fmt = parameter as string ?? "yyyy-MM-dd";
                return DateTimeHelper.FormatDigits(dt.ToString(fmt, CultureInfo.InvariantCulture), isAr);
            }

            if (value is Category cat)
            {
                return cat.DisplayName;
            }

            string raw = value is IFormattable formattable
                ? formattable.ToString(parameter as string, CultureInfo.InvariantCulture)
                : value.ToString() ?? string.Empty;

            return DateTimeHelper.FormatDigits(raw, isAr);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}