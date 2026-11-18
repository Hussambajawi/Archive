using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DocuArchivePro.Services;

namespace DocuArchivePro.Models
{
    public class AuditLogEntry
    {
        [Key]
        public int Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        [MaxLength(150)]
        public string Username { get; set; } = string.Empty;

        [MaxLength(120)]
        public string ActionType { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string Details { get; set; } = string.Empty;

        [NotMapped]
        public string LocalizedActionType
        {
            get
            {
                bool isAr = LocalizationService.Instance.IsArabic;
                int sep = ActionType.IndexOf(" / ", StringComparison.Ordinal);
                if (sep > 0)
                {
                    string arPart = ActionType.Substring(0, sep).Trim();
                    string enPart = ActionType.Substring(sep + 3).Trim();
                    return isAr ? arPart : enPart;
                }
                return ActionType;
            }
        }

        [NotMapped]
        public string FormattedTimestamp =>
            DateTimeHelper.FormatDigits(Timestamp.ToString("yyyy-MM-dd HH:mm:ss"), LocalizationService.Instance.IsArabic);
    }
}
