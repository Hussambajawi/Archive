using System.Collections.Generic;
using System.Threading.Tasks;
using DocuArchivePro.Models;

namespace DocuArchivePro.Services
{
    public interface IFileStorageService
    {
        string StorageRootDirectory { get; }
        Task<DocumentAttachment> SaveFileAttachmentAsync(string sourceFilePath, int documentId, bool isScanned = false);
        Task<DocumentAttachment> SaveByteArrayAttachmentAsync(byte[] fileBytes, string originalFileName, int documentId, bool isScanned = true);
        Task<byte[]> GetDecryptedBytesAsync(string storagePath);
        void OpenFileInDefaultViewer(string storagePath);
        void OpenAttachmentInApp(DocumentAttachment attachment);
        void OpenAttachmentsInApp(IReadOnlyList<DocumentAttachment> attachments, int initialIndex = 0);
        void CleanupTempSessionDirectory();
        void InvalidateCache();
        Task<int> ChangeVaultDirectoryAsync(string newDirectoryPath, bool migrateExistingFiles = true);
        Task<int> EncryptExistingUnencryptedFilesAsync();
        bool DeleteFile(string storagePath);
        long GetTotalStorageSizeBytes();
    }
}
