using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using DocuArchivePro.Data;
using DocuArchivePro.Models;
using DocuArchivePro.Views;

namespace DocuArchivePro.Services
{
    public class FileStorageService : IFileStorageService
    {
        // 16-byte magic header identifying encrypted archive files
        private static readonly byte[] MagicHeader = Encoding.ASCII.GetBytes("DOCUARCH_ENC_V1!");

        // 256-bit internal encryption key derived from application passphrase
        private static readonly byte[] AesKey = SHA256.HashData(
            Encoding.UTF8.GetBytes("HussamBajawi_DocuArchivePro_2026_MasterVaultSecretKey!@#"));

        private string? _cachedRootDirectory;

        public void InvalidateCache()
        {
            _cachedRootDirectory = null;
        }

        public string StorageRootDirectory
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_cachedRootDirectory))
                {
                    try
                    {
                        using var db = new ArchiveDbContext();
                        var settings = db.SystemSettings.FirstOrDefault();
                        if (settings != null && !string.IsNullOrWhiteSpace(settings.VaultStoragePath))
                        {
                            _cachedRootDirectory = settings.VaultStoragePath;
                        }
                    }
                    catch
                    {
                        // Fallback if DB not yet initialized
                    }
                }

                if (string.IsNullOrWhiteSpace(_cachedRootDirectory))
                {
                    _cachedRootDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "DocuArchivePro",
                        "Vault");
                }

                if (!Directory.Exists(_cachedRootDirectory))
                {
                    Directory.CreateDirectory(_cachedRootDirectory);
                }

                return _cachedRootDirectory;
            }
        }

        public async Task<DocumentAttachment> SaveFileAttachmentAsync(string sourceFilePath, int documentId, bool isScanned = false)
        {
            if (!File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException("الملف المصدر غير موجود", sourceFilePath);
            }

            byte[] bytes = await File.ReadAllBytesAsync(sourceFilePath);
            string originalFileName = Path.GetFileName(sourceFilePath);
            return await SaveByteArrayAttachmentAsync(bytes, originalFileName, documentId, isScanned);
        }

        public async Task<DocumentAttachment> SaveByteArrayAttachmentAsync(byte[] fileBytes, string originalFileName, int documentId, bool isScanned = true)
        {
            string ext = Path.GetExtension(originalFileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) ext = ".png";

            // Compute SHA256 Hash of original unencrypted content
            byte[] hashBytes = SHA256.HashData(fileBytes);
            string hashStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            // Store in Year/Month structured subfolder inside the chosen Vault directory
            string yearMonthFolder = Path.Combine(StorageRootDirectory, DateTime.Now.ToString("yyyy"), DateTime.Now.ToString("MM"));
            if (!Directory.Exists(yearMonthFolder))
            {
                Directory.CreateDirectory(yearMonthFolder);
            }

            // Save with encrypted .denc extension so external apps cannot recognize or open it
            string uniqueFileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.denc";
            string destinationPath = Path.Combine(yearMonthFolder, uniqueFileName);

            byte[] encryptedPayload = EncryptBytes(fileBytes);
            await File.WriteAllBytesAsync(destinationPath, encryptedPayload);

            return new DocumentAttachment
            {
                DocumentId = documentId,
                FileName = originalFileName,
                StoragePath = destinationPath,
                FileExtension = ext,
                FileSizeBytes = fileBytes.Length,
                FileHashSha256 = hashStr,
                IsScanned = isScanned,
                UploadedAt = DateTime.Now
            };
        }

        public static byte[] EncryptBytes(byte[] plainBytes)
        {
            return EncryptBytesWithKey(plainBytes, AesKey);
        }

        public static byte[] EncryptBytesWithKey(byte[] plainBytes, byte[] key)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var ms = new MemoryStream();
            // 1. Write 16-byte Magic Header
            ms.Write(MagicHeader, 0, MagicHeader.Length);
            // 2. Write 16-byte Random IV
            ms.Write(aes.IV, 0, aes.IV.Length);

            // 3. Write AES-256 Encrypted Payload
            using (var encryptor = aes.CreateEncryptor())
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            {
                cs.Write(plainBytes, 0, plainBytes.Length);
                cs.FlushFinalBlock();
            }

            return ms.ToArray();
        }

        public static byte[] DecryptBytes(byte[] fileOnDisk)
        {
            return DecryptBytesWithKey(fileOnDisk, AesKey);
        }

        public static byte[] DecryptBytesWithKey(byte[] fileOnDisk, byte[] key)
        {
            if (!IsEncryptedPayload(fileOnDisk))
            {
                return fileOnDisk; // Legacy unencrypted file fallback
            }

            byte[] iv = new byte[16];
            Buffer.BlockCopy(fileOnDisk, MagicHeader.Length, iv, 0, 16);

            int cipherOffset = MagicHeader.Length + 16;
            int cipherLength = fileOnDisk.Length - cipherOffset;

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var inputMs = new MemoryStream(fileOnDisk, cipherOffset, cipherLength);
            using var decryptor = aes.CreateDecryptor();
            using var cs = new CryptoStream(inputMs, decryptor, CryptoStreamMode.Read);
            using var outputMs = new MemoryStream();
            cs.CopyTo(outputMs);
            return outputMs.ToArray();
        }

        public static byte[] DeriveKeyFromPassword(string password, byte[] salt, int iterations = 100_000)
        {
            return Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        }

        public static byte[] GenerateRandomSalt(int length = 16)
        {
            return RandomNumberGenerator.GetBytes(length);
        }

        public static string ComputeKeyVerifier(byte[] key, byte[] salt)
        {
            byte[] combined = new byte[key.Length + salt.Length];
            Buffer.BlockCopy(key, 0, combined, 0, key.Length);
            Buffer.BlockCopy(salt, 0, combined, key.Length, salt.Length);
            return Convert.ToHexString(SHA256.HashData(combined)).ToLowerInvariant();
        }

        public static bool IsEncryptedPayload(byte[] data)
        {
            if (data == null || data.Length <= MagicHeader.Length + 16) return false;
            for (int i = 0; i < MagicHeader.Length; i++)
            {
                if (data[i] != MagicHeader[i]) return false;
            }
            return true;
        }

        public async Task<byte[]> GetDecryptedBytesAsync(string storagePath)
        {
            if (!File.Exists(storagePath))
            {
                throw new FileNotFoundException("لم يتم العثور على الملف المؤرشف في المستودع", storagePath);
            }

            byte[] diskBytes = await File.ReadAllBytesAsync(storagePath);
            return DecryptBytes(diskBytes);
        }

        public void OpenAttachmentInApp(DocumentAttachment attachment)
        {
            if (attachment == null) return;

            try
            {
                if (attachment.DocumentId > 0)
                {
                    using var db = new ArchiveDbContext();
                    var allDocAttachments = db.Attachments
                        .Where(a => a.DocumentId == attachment.DocumentId)
                        .OrderBy(a => a.Id)
                        .ToList();

                    if (allDocAttachments.Count > 0)
                    {
                        int index = allDocAttachments.FindIndex(a => a.Id == attachment.Id);
                        if (index < 0) index = 0;
                        OpenAttachmentsInApp(allDocAttachments, index);
                        return;
                    }
                }

                OpenAttachmentsInApp(new[] { attachment }, 0);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر فك تشفير أو استعراض الملف:\n{ex.Message}", "خطأ أمني", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OpenAttachmentsInApp(IReadOnlyList<DocumentAttachment> attachments, int initialIndex = 0)
        {
            if (attachments == null || attachments.Count == 0) return;

            try
            {
                int safeIndex = Math.Clamp(initialIndex, 0, attachments.Count - 1);
                var initial = attachments[safeIndex];
                if (!File.Exists(initial.StoragePath))
                {
                    MessageBox.Show($"الملف المؤرشف غير موجود في المسار:\n{initial.StoragePath}", "خطأ في فتح المرفق", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var viewer = new AttachmentViewerDialog(attachments, safeIndex)
                {
                    Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow
                };
                viewer.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر فك تشفير أو استعراض الملف:\n{ex.Message}", "خطأ أمني", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void CleanupTempSessionDirectory()
        {
            string[] tempDirs =
            {
                Path.Combine(Path.GetTempPath(), "DocuArchive_SecureSession"),
                Path.Combine(Path.GetTempPath(), "DocuArchive_WordPdfExport")
            };

            foreach (string dir in tempDirs)
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        foreach (string file in Directory.GetFiles(dir))
                        {
                            try { File.Delete(file); } catch { }
                        }
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        {
                            Directory.Delete(dir);
                        }
                    }
                }
                catch { }
            }

            try
            {
                string tempRoot = Path.GetTempPath();
                foreach (string pattern in new[] { "Scan_*.png", "Scan_*.pdf", "ScannedDoc_*.png", "ScannedDoc_*.pdf" })
                {
                    foreach (string file in Directory.GetFiles(tempRoot, pattern))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        public void OpenFileInDefaultViewer(string storagePath)
        {
            try
            {
                using var db = new ArchiveDbContext();
                var att = db.Attachments.FirstOrDefault(a => a.StoragePath == storagePath);
                if (att != null)
                {
                    OpenAttachmentInApp(att);
                    return;
                }

                if (!File.Exists(storagePath))
                {
                    MessageBox.Show("لم يتم العثور على الملف في الأرشيف", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                byte[] diskBytes = File.ReadAllBytes(storagePath);
                byte[] decrypted = DecryptBytes(diskBytes);
                string fileName = Path.GetFileName(storagePath);

                var viewer = new AttachmentViewerDialog(decrypted, fileName)
                {
                    Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow
                };
                viewer.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر فتح الملف المؤرشف:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task<int> ChangeVaultDirectoryAsync(string newDirectoryPath, bool migrateExistingFiles = true)
        {
            if (string.IsNullOrWhiteSpace(newDirectoryPath))
                throw new ArgumentException("يرجى تحديد مسار صالح للمجلد");

            if (!Directory.Exists(newDirectoryPath))
            {
                Directory.CreateDirectory(newDirectoryPath);
            }

            string oldRoot = StorageRootDirectory;
            int migratedCount = 0;

            using var db = new ArchiveDbContext();

            if (migrateExistingFiles && !string.Equals(oldRoot, newDirectoryPath, StringComparison.OrdinalIgnoreCase))
            {
                var attachments = await db.Attachments.ToListAsync();
                foreach (var att in attachments)
                {
                    if (File.Exists(att.StoragePath))
                    {
                        byte[] rawOrEnc = await File.ReadAllBytesAsync(att.StoragePath);
                        byte[] encrypted = IsEncryptedPayload(rawOrEnc) ? rawOrEnc : EncryptBytes(rawOrEnc);

                        string yearMonth = Path.Combine(newDirectoryPath, att.UploadedAt.ToString("yyyy"), att.UploadedAt.ToString("MM"));
                        if (!Directory.Exists(yearMonth)) Directory.CreateDirectory(yearMonth);

                        string newFileName = Path.GetFileNameWithoutExtension(att.StoragePath) + ".denc";
                        string newFilePath = Path.Combine(yearMonth, newFileName);

                        await File.WriteAllBytesAsync(newFilePath, encrypted);
                        try { File.Delete(att.StoragePath); } catch { }

                        att.StoragePath = newFilePath;
                        migratedCount++;
                    }
                }
                await db.SaveChangesAsync();
            }

            var settings = await SecurityService.Instance.GetSettingsAsync();
            settings.VaultStoragePath = newDirectoryPath;
            await SecurityService.Instance.SaveSettingsAsync(settings);

            _cachedRootDirectory = newDirectoryPath;
            return migratedCount;
        }

        public async Task<int> EncryptExistingUnencryptedFilesAsync()
        {
            int encryptedCount = 0;
            using var db = new ArchiveDbContext();
            var attachments = await db.Attachments.ToListAsync();

            foreach (var att in attachments)
            {
                if (File.Exists(att.StoragePath))
                {
                    byte[] data = await File.ReadAllBytesAsync(att.StoragePath);
                    if (!IsEncryptedPayload(data))
                    {
                        byte[] encrypted = EncryptBytes(data);
                        string newPath = Path.ChangeExtension(att.StoragePath, ".denc");
                        await File.WriteAllBytesAsync(newPath, encrypted);
                        if (!string.Equals(att.StoragePath, newPath, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(att.StoragePath); } catch { }
                        }
                        att.StoragePath = newPath;
                        encryptedCount++;
                    }
                }
            }

            if (encryptedCount > 0)
            {
                await db.SaveChangesAsync();
            }

            return encryptedCount;
        }

        public bool DeleteFile(string storagePath)
        {
            try
            {
                if (File.Exists(storagePath))
                {
                    File.Delete(storagePath);
                    return true;
                }
            }
            catch (Exception)
            {
                // File locked or permission error
            }
            return false;
        }

        public long GetTotalStorageSizeBytes()
        {
            if (!Directory.Exists(StorageRootDirectory)) return 0;
            long total = 0;
            var dir = new DirectoryInfo(StorageRootDirectory);
            foreach (var file in dir.GetFiles("*.*", SearchOption.AllDirectories))
            {
                total += file.Length;
            }
            return total;
        }
    }
}
