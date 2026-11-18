using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DocuArchivePro.Services;

namespace DocuArchivePro.Models
{
    public class AppUser
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        public string Username { get; set; } = string.Empty;

        [MaxLength(150)]
        public string RoleTitle { get; set; } = "موظف أرشيف";

        [MaxLength(100)]
        public string LoginPassword { get; set; } = string.Empty;

        public bool IsAdmin { get; set; } = false;
        public bool CanAdd { get; set; } = true;
        public bool CanEdit { get; set; } = false;
        public bool CanDelete { get; set; } = false;
        public bool CanScan { get; set; } = true;
        public bool CanPrint { get; set; } = true;
        public bool CanManageStorage { get; set; } = false;
        public bool CanManageEntities { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [NotMapped]
        public string LocalizedUsername
        {
            get
            {
                if (LocalizationService.Instance.IsArabic) return Username;
                return Username switch
                {
                    "موظف الأرشيف والصادر والوارد" => "Archive & Dispatch Officer",
                    "مستخدم مستعرض (قراءة فقط)" => "Viewer User (Read-Only)",
                    _ => Username
                };
            }
        }

        [NotMapped]
        public string LocalizedRoleTitle
        {
            get
            {
                if (LocalizationService.Instance.IsArabic) return RoleTitle;
                return RoleTitle switch
                {
                    "مدير النظام العام - كامل الصلاحيات" => "System Administrator - Full Access",
                    "مدخل بيانات وأرشفة" => "Data Entry & Archiving",
                    "اطلاع وبحث فقط" => "Read & Search Only",
                    "موظف أرشيف" => "Archive Officer",
                    _ => RoleTitle
                };
            }
        }

        [NotMapped]
        public string PermissionsSummary
        {
            get
            {
                bool isAr = LocalizationService.Instance.IsArabic;
                if (IsAdmin)
                {
                    return isAr ? "كافة الصلاحيات (مدير النظام الشامل)" : "All Permissions (System Admin)";
                }

                var perms = new List<string>();
                if (CanAdd) perms.Add(isAr ? "إضافة" : "Add");
                if (CanEdit) perms.Add(isAr ? "تعديل" : "Edit");
                if (CanDelete) perms.Add(isAr ? "حذف" : "Delete");
                if (CanScan) perms.Add(isAr ? "سكانر" : "Scan");
                if (CanPrint) perms.Add(isAr ? "طباعة" : "Print");
                if (CanManageStorage) perms.Add(isAr ? "إدارة المستودع" : "Manage Vault");
                if (CanManageEntities) perms.Add(isAr ? "إدارة وإدخال الجهات" : "Manage/Manual Entities");

                if (perms.Count > 0)
                {
                    return string.Join(isAr ? " ، " : ", ", perms);
                }

                return isAr ? "استعراض فقط (قراءة)" : "View Only (Read)";
            }
        }

        public override string ToString() => $"{LocalizedUsername} ({LocalizedRoleTitle})";
    }
}