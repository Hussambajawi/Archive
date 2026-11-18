using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using DocuArchivePro.Services;

namespace DocuArchivePro.Models
{
    public class DocumentAttachment
    {
        [Key]
        public int Id { get; set; }

        public int DocumentId { get; set; }

        [ForeignKey("DocumentId")]
        public virtual Document? Document { get; set; }

        [Required]
        [MaxLength(260)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string StoragePath { get; set; } = string.Empty;

        [MaxLength(50)]
        public string FileExtension { get; set; } = string.Empty;

        public long FileSizeBytes { get; set; }

        [MaxLength(64)]
        public string FileHashSha256 { get; set; } = string.Empty;

        public bool IsScanned { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.Now;

        [NotMapped]
        public string FileSizeFormatted
        {
            get
            {
                bool isAr = LocalizationService.Instance.IsArabic;
                string formatted;
                if (FileSizeBytes < 1024)
                {
                    formatted = isAr ? $"{FileSizeBytes} بايت" : $"{FileSizeBytes} B";
                }
                else if (FileSizeBytes < 1024 * 1024)
                {
                    string num = (FileSizeBytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture);
                    formatted = isAr ? $"{num} ك.ب" : $"{num} KB";
                }
                else
                {
                    string num = (FileSizeBytes / (1024.0 * 1024.0)).ToString("F2", CultureInfo.InvariantCulture);
                    formatted = isAr ? $"{num} م.ب" : $"{num} MB";
                }
                return DateTimeHelper.FormatDigits(formatted, isAr);
            }
        }
    }
}