using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using DocuArchivePro.Models;
using DocuArchivePro.Services;

namespace DocuArchivePro.Data
{
    public static class DatabaseInitializer
    {
        public static async Task InitializeAsync()
        {
            using var context = new ArchiveDbContext();
            
            // Ensure database and schema are created
            await context.Database.EnsureCreatedAsync();

            // Ensure new tables exist even if the database file was created in an earlier version
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS Users (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL,
                    RoleTitle TEXT NOT NULL DEFAULT 'موظف أرشيف',
                    LoginPassword TEXT NOT NULL DEFAULT '',
                    IsAdmin INTEGER NOT NULL DEFAULT 0,
                    CanAdd INTEGER NOT NULL DEFAULT 1,
                    CanEdit INTEGER NOT NULL DEFAULT 0,
                    CanDelete INTEGER NOT NULL DEFAULT 0,
                    CanScan INTEGER NOT NULL DEFAULT 1,
                    CanPrint INTEGER NOT NULL DEFAULT 1,
                    CanManageStorage INTEGER NOT NULL DEFAULT 0,
                    CanManageEntities INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL
                );
            ");

            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS CorrespondentEntities (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    EntityType TEXT NOT NULL DEFAULT 'Both',
                    Notes TEXT NOT NULL DEFAULT '',
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL
                );
            ");

            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS SystemSettings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ActionSecurityCode TEXT NOT NULL DEFAULT '1234',
                    EntitySecurityCode TEXT NOT NULL DEFAULT '5678',
                    VaultStoragePath TEXT NOT NULL DEFAULT '',
                    DefaultScannerDeviceId TEXT NOT NULL DEFAULT 'WINDOWS_INTERACTIVE_WIA_DIALOG',
                    DefaultScannerName TEXT NOT NULL DEFAULT 'نافذة اختيار السكانر القياسية في الويندوز (Windows WIA / TWAIN Dialog)',
                    DefaultScanDpi INTEGER NOT NULL DEFAULT 300,
                    DefaultScanColorMode INTEGER NOT NULL DEFAULT 1,
                    UseScannerFeeder INTEGER NOT NULL DEFAULT 0,
                    SelectedArabicFont TEXT NOT NULL DEFAULT 'Alexandria',
                    SelectedTheme TEXT NOT NULL DEFAULT 'Light',
                    SampleDataSeeded INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT NOT NULL
                );
            ");

            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS AuditLogs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    Username TEXT NOT NULL DEFAULT '',
                    ActionType TEXT NOT NULL DEFAULT '',
                    Details TEXT NOT NULL DEFAULT ''
                );
            ");

            // Add new columns to existing databases safely
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Users ADD COLUMN CanManageEntities INTEGER NOT NULL DEFAULT 0;"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("UPDATE Users SET CanManageEntities = 1 WHERE IsAdmin = 1;"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SystemSettings ADD COLUMN EntitySecurityCode TEXT NOT NULL DEFAULT '5678';"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SystemSettings ADD COLUMN SelectedArabicFont TEXT NOT NULL DEFAULT 'Alexandria';"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SystemSettings ADD COLUMN SelectedTheme TEXT NOT NULL DEFAULT 'Light';"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SystemSettings ADD COLUMN SampleDataSeeded INTEGER NOT NULL DEFAULT 0;"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SystemSettings ADD COLUMN AutoBackupEnabled INTEGER NOT NULL DEFAULT 0;"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SystemSettings ADD COLUMN AutoBackupDirectory TEXT NOT NULL DEFAULT '';"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SystemSettings ADD COLUMN LastFullBackupAt TEXT NULL;"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SystemSettings ADD COLUMN AutoBackupRetentionCount INTEGER NOT NULL DEFAULT 7;"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SystemSettings ADD COLUMN AutoBackupLastStatus TEXT NOT NULL DEFAULT '';"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SystemSettings ADD COLUMN AutoBackupLastErrorMessage TEXT NOT NULL DEFAULT '';"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SystemSettings ADD COLUMN OrganizationName TEXT NOT NULL DEFAULT '';"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SystemSettings ADD COLUMN OrganizationSubtitle TEXT NOT NULL DEFAULT '';"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SystemSettings ADD COLUMN OrganizationLogoPath TEXT NOT NULL DEFAULT '';"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SystemSettings ADD COLUMN ApprovalManagerName TEXT NOT NULL DEFAULT '';"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Documents ADD COLUMN RelatedRegistrationNumber TEXT NOT NULL DEFAULT '';"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Documents ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0;"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Documents ADD COLUMN DeletedAt TEXT NULL;"); } catch { }

            // High-speed indexes for large archives
            try { await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_Documents_IsDeleted_Type_RegDate ON Documents (IsDeleted, Type, RegistrationDate);"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_Documents_FollowUpDate ON Documents (FollowUpDate);"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_Documents_RelatedRegistrationNumber ON Documents (RelatedRegistrationNumber);"); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_AuditLogs_Timestamp ON AuditLogs (Timestamp);"); } catch { }

            // Seed default SystemSettings if empty
            if (!await context.SystemSettings.AnyAsync())
            {
                string defaultVault = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DocuArchivePro",
                    "Vault");

                if (!Directory.Exists(defaultVault))
                {
                    Directory.CreateDirectory(defaultVault);
                }

                await context.SystemSettings.AddAsync(new SystemSettings
                {
                    ActionSecurityCode = SecurityService.HashSecret("1234"),
                    EntitySecurityCode = SecurityService.HashSecret("5678"),
                    VaultStoragePath = defaultVault,
                    DefaultScannerDeviceId = "WINDOWS_INTERACTIVE_WIA_DIALOG",
                    DefaultScannerName = "نافذة اختيار السكانر القياسية في الويندوز (Windows WIA / TWAIN Dialog)",
                    DefaultScanDpi = 300,
                    DefaultScanColorMode = 1,
                    UseScannerFeeder = false,
                    SelectedArabicFont = "Alexandria",
                    SelectedTheme = "Light",
                    SampleDataSeeded = false,
                    UpdatedAt = DateTime.Now
                });
                await context.SaveChangesAsync();
            }
            else
            {
                var existingSettings = await context.SystemSettings.FirstOrDefaultAsync();
                if (existingSettings != null)
                {
                    bool changed = false;
                    if (string.IsNullOrWhiteSpace(existingSettings.EntitySecurityCode) ||
                        (!SecurityService.IsHashedSecret(existingSettings.EntitySecurityCode) &&
                         SecurityService.VerifySecret(existingSettings.EntitySecurityCode.Trim(), existingSettings.ActionSecurityCode)))
                    {
                        string fallbackCode = SecurityService.VerifySecret("5678", existingSettings.ActionSecurityCode)
                            ? "2468"
                            : "5678";
                        existingSettings.EntitySecurityCode = SecurityService.HashSecret(fallbackCode);
                        changed = true;
                    }
                    if (!string.IsNullOrEmpty(existingSettings.ActionSecurityCode) && !SecurityService.IsHashedSecret(existingSettings.ActionSecurityCode))
                    {
                        existingSettings.ActionSecurityCode = SecurityService.HashSecret(existingSettings.ActionSecurityCode.Trim());
                        changed = true;
                    }
                    if (!string.IsNullOrEmpty(existingSettings.EntitySecurityCode) && !SecurityService.IsHashedSecret(existingSettings.EntitySecurityCode))
                    {
                        existingSettings.EntitySecurityCode = SecurityService.HashSecret(existingSettings.EntitySecurityCode.Trim());
                        changed = true;
                    }
                    if (string.Equals(existingSettings.DefaultScannerDeviceId, "VIRTUAL_TEST_SCANNER_01", StringComparison.OrdinalIgnoreCase))
                    {
                        existingSettings.DefaultScannerDeviceId = "WINDOWS_INTERACTIVE_WIA_DIALOG";
                        existingSettings.DefaultScannerName = "نافذة اختيار السكانر القياسية في الويندوز";
                        changed = true;
                    }
                    if (changed)
                    {
                        await context.SaveChangesAsync();
                    }
                }
            }

            // Seed default Users if empty, and ensure at least one Admin account exists (without resetting renamed admins)
            if (!await context.Users.AnyAsync())
            {
                var defaultUsers = new List<AppUser>
                {
                    new AppUser
                    {
                        Username = "SuperUser",
                        RoleTitle = "مدير النظام العام - كامل الصلاحيات",
                        LoginPassword = SecurityService.HashSecret("Admin"),
                        IsAdmin = true,
                        CanAdd = true,
                        CanEdit = true,
                        CanDelete = true,
                        CanScan = true,
                        CanPrint = true,
                        CanManageStorage = true,
                        CanManageEntities = true,
                        CreatedAt = DateTime.Now
                    },
                    new AppUser
                    {
                        Username = "موظف الأرشيف والصادر والوارد",
                        RoleTitle = "مدخل بيانات وأرشفة",
                        LoginPassword = SecurityService.HashSecret("123"),
                        IsAdmin = false,
                        CanAdd = true,
                        CanEdit = true,
                        CanDelete = false,
                        CanScan = true,
                        CanPrint = true,
                        CanManageStorage = false,
                        CreatedAt = DateTime.Now
                    },
                    new AppUser
                    {
                        Username = "مستخدم مستعرض (قراءة فقط)",
                        RoleTitle = "اطلاع وبحث فقط",
                        LoginPassword = "",
                        IsAdmin = false,
                        CanAdd = false,
                        CanEdit = false,
                        CanDelete = false,
                        CanScan = false,
                        CanPrint = true,
                        CanManageStorage = false,
                        CreatedAt = DateTime.Now
                    }
                };

                await context.Users.AddRangeAsync(defaultUsers);
                await context.SaveChangesAsync();
            }
            else
            {
                bool usersChanged = false;
                var allUsers = await context.Users.ToListAsync();
                foreach (var u in allUsers)
                {
                    if (!string.IsNullOrEmpty(u.LoginPassword) && !SecurityService.IsHashedSecret(u.LoginPassword))
                    {
                        u.LoginPassword = SecurityService.HashSecret(u.LoginPassword);
                        usersChanged = true;
                    }
                }

                bool anyAdmin = allUsers.Any(u => u.IsAdmin);
                if (!anyAdmin)
                {
                    await context.Users.AddAsync(new AppUser
                    {
                        Username = "SuperUser",
                        RoleTitle = "مدير النظام العام - كامل الصلاحيات",
                        LoginPassword = SecurityService.HashSecret("Admin"),
                        IsAdmin = true,
                        CanAdd = true,
                        CanEdit = true,
                        CanDelete = true,
                        CanScan = true,
                        CanPrint = true,
                        CanManageStorage = true,
                        CanManageEntities = true,
                        CreatedAt = DateTime.Now
                    });
                    usersChanged = true;
                }

                if (usersChanged)
                {
                    await context.SaveChangesAsync();
                }
            }

            // Seed default categories and sample documents only ONCE during initial database setup
            var settings = await context.SystemSettings.FirstOrDefaultAsync();
            if (settings != null && !settings.SampleDataSeeded)
            {
                // Seed default categories if empty on first run
                if (!await context.Categories.AnyAsync())
                {
                    var defaultCategories = new List<Category>
                    {
                        new Category { Name = "مراسلات رسمية / Official Letters", Description = "خطابات ومراسلات إدارية عامة" },
                        new Category { Name = "قرارات وتعاميم / Decisions & Circulars", Description = "قرارات مجلس الإدارة والتعاميم الداخلية" },
                        new Category { Name = "فواتير ومعاملات مالية / Financial", Description = "مطالبات، فواتير، مستندات صرف وقبض" },
                        new Category { Name = "عقود واتفاقيات / Contracts & Legal", Description = "عقود الشراكة، التوريد، والاتفاقيات القانونية" },
                        new Category { Name = "تقارير ودراسات فنية / Technical Reports", Description = "تقارير المشاريع والدراسات الفنية" },
                        new Category { Name = "شؤون الموظفين / Human Resources", Description = "ملفات التوظيف والإجازات والشكاوى" }
                    };

                    await context.Categories.AddRangeAsync(defaultCategories);
                    await context.SaveChangesAsync();
                }

                if (!await context.Documents.AnyAsync())
                {
                    var generalCat = await context.Categories.FirstOrDefaultAsync();
                    int catId = generalCat?.Id ?? 1;

                    var sampleDocs = new List<Document>
                    {
                        new Document
                        {
                            RegistrationNumber = "IN-2026-0001",
                            Type = DocumentType.Inbound,
                            RegistrationDate = DateTime.Now.AddDays(-2),
                            DocumentDate = DateTime.Now.AddDays(-3),
                            Subject = "طلب توريد أجهزة حاسوب وتجهيزات تقنية للفرع الرئيسي",
                            Sender = "شركة التقنية المتقدمة للحلول البرمجية",
                            Recipient = "مدير إدارة تكنولوجيا المعلومات",
                            CategoryId = catId,
                            Priority = DocumentPriority.Urgent,
                            Status = DocumentStatus.InProgress,
                            DeliveryMethod = "البريد الإلكتروني الرسمي",
                            ExternalReferenceNumber = "TECH-REF-8842",
                            FollowUpDate = DateTime.Now.AddDays(5),
                            Notes = "يرجى دراسة عروض الأسعار ومقارنتها بالميزانية السنوية المعتمدة والتنسيق مع الإدارة المالية.",
                            Department = "إدارة تكنولوجيا المعلومات",
                            Tags = "توريد, تقنية, عروض أسعار"
                        },
                        new Document
                        {
                            RegistrationNumber = "OUT-2026-0001",
                            Type = DocumentType.Outbound,
                            RegistrationDate = DateTime.Now.AddDays(-1),
                            DocumentDate = DateTime.Now.AddDays(-1),
                            Subject = "إشعار اعتماد الميزانية الربعية وتحديث خطة العمل",
                            Sender = "المدير العام التنفيذي",
                            Recipient = "جميع مدراء الإدارات ورؤساء الأقسام",
                            CategoryId = catId,
                            Priority = DocumentPriority.Normal,
                            Status = DocumentStatus.Completed,
                            DeliveryMethod = "تسليم يدوي وتعميم داخلي",
                            ExternalReferenceNumber = "DIR-EXEC-102",
                            FollowUpDate = null,
                            Notes = "تم اعتماد خطة الربع الأول من قبل مجلس الإدارة مع التشديد على الالتزام بسقوف الإنفاق.",
                            Department = "الإدارة العليا",
                            Tags = "ميزانية, تعميم, الربع الأول",
                            RelatedRegistrationNumber = "IN-2026-0001"
                        }
                    };

                    await context.Documents.AddRangeAsync(sampleDocs);
                }

                // Mark sample data seeded as true permanently so default transactions never recreate after deletion
                settings.SampleDataSeeded = true;
                await context.SaveChangesAsync();
            }

            // Seed default correspondent entities once if table is empty
            if (!await context.CorrespondentEntities.AnyAsync())
            {
                var defaultEntities = new List<CorrespondentEntity>
                {
                    new CorrespondentEntity { Name = "المدير العام التنفيذي / Chief Executive Officer", EntityType = "Both", Notes = "الجهة الإدارية العليا" },
                    new CorrespondentEntity { Name = "إدارة تكنولوجيا المعلومات / Information Technology Dept", EntityType = "Both", Notes = "الدعم الفني والأنظمة" },
                    new CorrespondentEntity { Name = "الإدارة المالية والمحاسبة / Finance & Accounting Dept", EntityType = "Both", Notes = "المعاملات والفواتير المالية" },
                    new CorrespondentEntity { Name = "إدارة الموارد البشرية / Human Resources Dept", EntityType = "Both", Notes = "شؤون الموظفين والتعيينات" },
                    new CorrespondentEntity { Name = "إدارة الشؤون القانونية والعقود / Legal & Contracts Dept", EntityType = "Both", Notes = "العقود والاتفاقيات الرسمية" },
                    new CorrespondentEntity { Name = "جميع مدراء الإدارات ورؤساء الأقسام / All Department Heads", EntityType = "Outbound", Notes = "التعاميم الداخلية" },
                    new CorrespondentEntity { Name = "شركة التقنية المتقدمة للحلول البرمجية / Advanced Tech Solutions Co.", EntityType = "Inbound", Notes = "مورد وتقنية معلومات" },
                    new CorrespondentEntity { Name = "مؤسسة الصيانة والخدمات الحديثة / Modern Maintenance Est.", EntityType = "Inbound", Notes = "خدمات الصيانة العامة" }
                };

                await context.CorrespondentEntities.AddRangeAsync(defaultEntities);
                await context.SaveChangesAsync();
            }
        }
    }
}
