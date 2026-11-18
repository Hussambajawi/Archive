using System;
using System.ComponentModel.DataAnnotations;

namespace DocuArchivePro.Models
{
    public class SystemSettings
    {
        [Key]
        public int Id { get; set; } = 1;

        [Required]
        [MaxLength(100)]
        public string ActionSecurityCode { get; set; } = "1234"; // الرمز السري لتأكيد الإجراءات (حذف / تعديل / إضافة إضبارة)

        [Required]
        [MaxLength(100)]
        public string EntitySecurityCode { get; set; } = "5678"; // كلمة السر الخاصة بإضافة جهة صادر أو وارد

        [MaxLength(500)]
        public string VaultStoragePath { get; set; } = string.Empty; // مسار حفظ الملفات المؤرشفة المشفرة

        [MaxLength(200)]
        public string DefaultScannerDeviceId { get; set; } = "WINDOWS_INTERACTIVE_WIA_DIALOG";

        [MaxLength(200)]
        public string DefaultScannerName { get; set; } = "نافذة اختيار السكانر القياسية في الويندوز (Windows WIA / TWAIN Dialog)";

        public int DefaultScanDpi { get; set; } = 300;

        public int DefaultScanColorMode { get; set; } = 1; // 1 = Color, 2 = Grayscale, 4 = B&W

        public bool UseScannerFeeder { get; set; } = false;

        [MaxLength(100)]
        public string SelectedArabicFont { get; set; } = "Alexandria";

        [MaxLength(50)]
        public string SelectedTheme { get; set; } = "Light"; // "Light" or "Dark"

        public bool SampleDataSeeded { get; set; } = false;

        public bool AutoBackupEnabled { get; set; } = false;

        [MaxLength(500)]
        public string AutoBackupDirectory { get; set; } = string.Empty;

        public DateTime? LastFullBackupAt { get; set; }

        public int AutoBackupRetentionCount { get; set; } = 7;

        [MaxLength(50)]
        public string AutoBackupLastStatus { get; set; } = string.Empty;

        [MaxLength(500)]
        public string AutoBackupLastErrorMessage { get; set; } = string.Empty;

        [MaxLength(200)]
        public string OrganizationName { get; set; } = string.Empty;

        [MaxLength(200)]
        public string OrganizationSubtitle { get; set; } = string.Empty;

        [MaxLength(500)]
        public string OrganizationLogoPath { get; set; } = string.Empty;

        [MaxLength(150)]
        public string ApprovalManagerName { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
