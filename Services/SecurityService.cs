using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using DocuArchivePro.Data;
using DocuArchivePro.Models;

namespace DocuArchivePro.Services
{
    public class SecurityService : ISecurityService
    {
        private const string HashPrefix = "SH256$";
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Pbkdf2Iterations = 10000;

        private static SecurityService? _instance;
        public static SecurityService Instance => _instance ??= new SecurityService();

        public AppUser CurrentUser { get; set; } = new AppUser
        {
            Username = "SuperUser",
            RoleTitle = "مدير النظام العام - كامل الصلاحيات",
            LoginPassword = string.Empty,
            IsAdmin = true,
            CanAdd = true,
            CanEdit = true,
            CanDelete = true,
            CanScan = true,
            CanPrint = true,
            CanManageStorage = true,
            CanManageEntities = true
        };

        public static bool IsHashedSecret(string? value)
        {
            return !string.IsNullOrEmpty(value) && value.StartsWith(HashPrefix, StringComparison.Ordinal);
        }

        public static string HashSecret(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return string.Empty;
            }
            if (IsHashedSecret(plainText))
            {
                return plainText;
            }

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(plainText),
                salt,
                Pbkdf2Iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            return $"{HashPrefix}{Convert.ToHexString(salt)}${Convert.ToHexString(hash)}";
        }

        public static bool VerifySecret(string? enteredPlain, string? storedValue)
        {
            if (string.IsNullOrEmpty(storedValue))
            {
                return true;
            }
            if (string.IsNullOrEmpty(enteredPlain))
            {
                return false;
            }
            if (!IsHashedSecret(storedValue))
            {
                return string.Equals(enteredPlain, storedValue, StringComparison.Ordinal);
            }

            try
            {
                string[] parts = storedValue.Split('$');
                if (parts.Length != 3 || !string.Equals(parts[0], "SH256", StringComparison.Ordinal))
                {
                    return false;
                }

                byte[] salt = Convert.FromHexString(parts[1]);
                byte[] expectedHash = Convert.FromHexString(parts[2]);
                byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(enteredPlain),
                    salt,
                    Pbkdf2Iterations,
                    HashAlgorithmName.SHA256,
                    expectedHash.Length);

                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch
            {
                return false;
            }
        }

        public bool VerifyUserPassword(AppUser user, string enteredPassword)
        {
            if (user == null) return false;
            if (string.IsNullOrEmpty(user.LoginPassword)) return true;
            return VerifySecret(enteredPassword, user.LoginPassword);
        }

        public async Task<List<AppUser>> GetAllUsersAsync()
        {
            using var db = new ArchiveDbContext();
            return await db.Users.OrderByDescending(u => u.IsAdmin).ThenBy(u => u.Id).ToListAsync();
        }

        public async Task<bool> IsUsernameTakenAsync(string username, int excludeUserId = 0)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;
            string normalized = username.Trim().ToLower();
            using var db = new ArchiveDbContext();
            return await db.Users.AnyAsync(u => u.Id != excludeUserId && u.Username.ToLower() == normalized);
        }

        public async Task<AppUser> SaveUserAsync(AppUser user, bool clearPassword = false)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.Username))
            {
                throw new InvalidOperationException("Username cannot be empty.");
            }

            user.Username = user.Username.Trim();

            using var db = new ArchiveDbContext();
            string normalizedUsername = user.Username.ToLower();
            bool duplicateExists = await db.Users.AnyAsync(u => u.Id != user.Id && u.Username.ToLower() == normalizedUsername);
            if (duplicateExists)
            {
                throw new InvalidOperationException("اسم المستخدم مسجل مسبقاً لمستخدم آخر في النظام / Username is already taken.");
            }

            if (user.IsAdmin)
            {
                user.CanAdd = true;
                user.CanEdit = true;
                user.CanDelete = true;
                user.CanScan = true;
                user.CanPrint = true;
                user.CanManageStorage = true;
                user.CanManageEntities = true;
            }

            string normalizedPassword = string.IsNullOrEmpty(user.LoginPassword)
                ? string.Empty
                : (IsHashedSecret(user.LoginPassword) ? user.LoginPassword : HashSecret(user.LoginPassword.Trim()));

            if (user.Id > 0)
            {
                var existing = await db.Users.FindAsync(user.Id);
                if (existing != null)
                {
                    if (existing.IsAdmin && !user.IsAdmin)
                    {
                        int otherAdminsCount = await db.Users.CountAsync(u => u.IsAdmin && u.Id != existing.Id);
                        if (otherAdminsCount == 0)
                        {
                            throw new InvalidOperationException("لا يمكن إلغاء صلاحية مدير النظام عن المدير الوحيد المتبقي في النظام لمنع قفل النظام / Cannot demote the only remaining Administrator.");
                        }
                    }

                    existing.Username = user.Username;
                    existing.RoleTitle = user.RoleTitle;
                    if (clearPassword)
                    {
                        existing.LoginPassword = string.Empty;
                    }
                    else if (!string.IsNullOrEmpty(normalizedPassword))
                    {
                        existing.LoginPassword = normalizedPassword;
                    }
                    existing.IsAdmin = user.IsAdmin;
                    existing.CanAdd = user.CanAdd;
                    existing.CanEdit = user.CanEdit;
                    existing.CanDelete = user.CanDelete;
                    existing.CanScan = user.CanScan;
                    existing.CanPrint = user.CanPrint;
                    existing.CanManageStorage = user.CanManageStorage;
                    existing.CanManageEntities = user.CanManageEntities;
                    await db.SaveChangesAsync();
                    if (CurrentUser.Id == existing.Id)
                    {
                        CurrentUser = existing;
                    }
                    return existing;
                }
            }

            user.LoginPassword = clearPassword ? string.Empty : normalizedPassword;
            user.CreatedAt = DateTime.Now;
            await db.Users.AddAsync(user);
            await db.SaveChangesAsync();
            return user;
        }

        public async Task<bool> DeleteUserAsync(int userId)
        {
            using var db = new ArchiveDbContext();
            int totalUsers = await db.Users.CountAsync();
            if (totalUsers <= 1)
            {
                return false; // Prevent deleting the last remaining user
            }

            var user = await db.Users.FindAsync(userId);
            if (user == null) return false;

            // Security guard: Prevent user from deleting their currently active logged-in account
            if (CurrentUser != null && CurrentUser.Id == user.Id)
            {
                return false;
            }

            // Security guard: Prevent deleting the only remaining Administrator
            if (user.IsAdmin)
            {
                int adminCount = await db.Users.CountAsync(u => u.IsAdmin);
                if (adminCount <= 1)
                {
                    return false;
                }
            }

            db.Users.Remove(user);
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<SystemSettings> GetSettingsAsync()
        {
            using var db = new ArchiveDbContext();
            var settings = await db.SystemSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                string defaultVault = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DocuArchivePro",
                    "Vault");

                settings = new SystemSettings
                {
                    ActionSecurityCode = HashSecret("1234"),
                    EntitySecurityCode = HashSecret("5678"),
                    VaultStoragePath = defaultVault,
                    DefaultScannerDeviceId = "WINDOWS_INTERACTIVE_WIA_DIALOG",
                    DefaultScannerName = "نافذة اختيار السكانر القياسية في الويندوز (Windows WIA / TWAIN Dialog)",
                    DefaultScanDpi = 300,
                    DefaultScanColorMode = 1,
                    UseScannerFeeder = false,
                    UpdatedAt = DateTime.Now
                };
                await db.SystemSettings.AddAsync(settings);
                await db.SaveChangesAsync();
            }
            return settings;
        }

        public async Task<SystemSettings> SaveSettingsAsync(SystemSettings settings)
        {
            using var db = new ArchiveDbContext();
            string hashedAction = string.IsNullOrEmpty(settings.ActionSecurityCode)
                ? string.Empty
                : (IsHashedSecret(settings.ActionSecurityCode) ? settings.ActionSecurityCode : HashSecret(settings.ActionSecurityCode.Trim()));
            string hashedEntity = string.IsNullOrEmpty(settings.EntitySecurityCode)
                ? string.Empty
                : (IsHashedSecret(settings.EntitySecurityCode) ? settings.EntitySecurityCode : HashSecret(settings.EntitySecurityCode.Trim()));

            var existing = await db.SystemSettings.FirstOrDefaultAsync();
            if (existing == null)
            {
                settings.ActionSecurityCode = hashedAction;
                settings.EntitySecurityCode = hashedEntity;
                settings.UpdatedAt = DateTime.Now;
                await db.SystemSettings.AddAsync(settings);
                await db.SaveChangesAsync();
                return settings;
            }

            existing.ActionSecurityCode = hashedAction;
            existing.EntitySecurityCode = hashedEntity;
            existing.VaultStoragePath = settings.VaultStoragePath;
            existing.DefaultScannerDeviceId = settings.DefaultScannerDeviceId;
            existing.DefaultScannerName = settings.DefaultScannerName;
            existing.DefaultScanDpi = settings.DefaultScanDpi;
            existing.DefaultScanColorMode = settings.DefaultScanColorMode;
            existing.UseScannerFeeder = settings.UseScannerFeeder;
            existing.AutoBackupEnabled = settings.AutoBackupEnabled;
            existing.AutoBackupDirectory = settings.AutoBackupDirectory;
            existing.LastFullBackupAt = settings.LastFullBackupAt;
            existing.AutoBackupRetentionCount = settings.AutoBackupRetentionCount > 0 ? settings.AutoBackupRetentionCount : 7;
            existing.AutoBackupLastStatus = settings.AutoBackupLastStatus ?? string.Empty;
            existing.AutoBackupLastErrorMessage = settings.AutoBackupLastErrorMessage ?? string.Empty;
            existing.OrganizationName = settings.OrganizationName;
            existing.OrganizationSubtitle = settings.OrganizationSubtitle;
            existing.OrganizationLogoPath = settings.OrganizationLogoPath;
            existing.ApprovalManagerName = settings.ApprovalManagerName;
            existing.UpdatedAt = DateTime.Now;

            await db.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> VerifyActionCodeAsync(string enteredCode)
        {
            var settings = await GetSettingsAsync();
            if (string.IsNullOrEmpty(settings.ActionSecurityCode))
            {
                return true;
            }
            return VerifySecret(enteredCode?.Trim(), settings.ActionSecurityCode);
        }

        public async Task<bool> VerifyEntityCodeAsync(string enteredCode)
        {
            var settings = await GetSettingsAsync();
            if (string.IsNullOrEmpty(settings.EntitySecurityCode))
            {
                return true;
            }
            return VerifySecret(enteredCode?.Trim(), settings.EntitySecurityCode);
        }
    }
}
