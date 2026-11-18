using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DocuArchivePro.Services;

namespace DocuArchivePro.Models
{
    public class Category
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(250)]
        public string Description { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

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
        public string DisplayDescription
        {
            get
            {
                bool isAr = LocalizationService.Instance.IsArabic;
                if (!isAr)
                {
                    return Description switch
                    {
                        "خطابات ومراسلات إدارية عامة" => "General official letters & correspondence",
                        "قرارات مجلس الإدارة والتعاميم الداخلية" => "Board decisions & internal circulars",
                        "مطالبات، فواتير، مستندات صرف وقبض" => "Invoices, claims & payment vouchers",
                        "عقود الشراكة، التوريد، والاتفاقيات القانونية" => "Partnership, supply & legal agreements",
                        "تقارير المشاريع والدراسات الفنية" => "Project reports & technical studies",
                        "ملفات التوظيف والإجازات والشكاوى" => "Recruitment, leave & HR records",
                        _ => DateTimeHelper.FormatDigits(Description, false)
                    };
                }
                return DateTimeHelper.FormatDigits(Description, true);
            }
        }
    }
}