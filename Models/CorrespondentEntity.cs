using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DocuArchivePro.Services;

namespace DocuArchivePro.Models
{
    public class CorrespondentEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(250)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50)]
        public string EntityType { get; set; } = "Both"; // "Both", "Inbound", "Outbound"

        [MaxLength(500)]
        public string Notes { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [NotMapped]
        public string DisplayName
        {
            get
            {
                bool isAr = LocalizationService.Instance.IsArabic;
                int sep = Name.IndexOf(" / ", StringComparison.Ordinal);
                if (sep > 0)
                {
                    string part1 = Name.Substring(0, sep).Trim();
                    string part2 = Name.Substring(sep + 3).Trim();
                    string chosen = isAr ? part1 : part2;
                    return DateTimeHelper.FormatDigits(chosen, isAr);
                }
                return DateTimeHelper.FormatDigits(Name, isAr);
            }
        }

        [NotMapped]
        public string LocalizedEntityType
        {
            get
            {
                bool isAr = LocalizationService.Instance.IsArabic;
                return EntityType switch
                {
                    "Inbound" => isAr ? "جهة وارد" : "Inbound Entity",
                    "Outbound" => isAr ? "جهة صادر" : "Outbound Entity",
                    _ => isAr ? "وارد وصادر معاً" : "Inbound & Outbound"
                };
            }
        }

        [NotMapped]
        public string DisplayNotes => DateTimeHelper.FormatDigits(Notes, LocalizationService.Instance.IsArabic);
    }
}
