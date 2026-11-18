using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DocuArchivePro.Models;

namespace DocuArchivePro.Services
{
    public class DocumentFilterCriteria
    {
        public DocumentType? Type { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public DocumentPriority? Priority { get; set; }
        public DocumentStatus? Status { get; set; }
        public int? CategoryId { get; set; }
        public string? SearchQuery { get; set; } // Free text query across Subject, RegNumber, Sender, Recipient, Notes
        public bool OnlyFollowUpDue { get; set; }
        public bool IncludeDeletedOnly { get; set; }
    }

    public class DashboardStatistics
    {
        public int TotalDocuments { get; set; }
        public int InboundCount { get; set; }
        public int OutboundCount { get; set; }
        public int TodayCount { get; set; }
        public int UrgentCount { get; set; }
        public int FollowUpDueCount { get; set; }
        public int TotalAttachmentsCount { get; set; }
        public int RecycleBinCount { get; set; }
        public long TotalStorageBytes { get; set; }
    }

    public class CategoryDeletionSummary
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public int InboundCount { get; set; }
        public int OutboundCount { get; set; }
        public int RecycleBinCount { get; set; }
        public int TotalDocumentsCount { get; set; }
        public int TotalAttachmentsCount { get; set; }
        public long TotalAttachmentsSizeBytes { get; set; }
    }

    public class CategoryBackupAttachmentDto
    {
        public string OriginalFileName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string FileExtension { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public bool IsScanned { get; set; }
        public DateTime UploadedAt { get; set; }
    }

    public class CategoryBackupDocumentDto
    {
        public string RegistrationNumber { get; set; } = string.Empty;
        public DocumentType Type { get; set; }
        public DateTime RegistrationDate { get; set; }
        public DateTime DocumentDate { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Sender { get; set; } = string.Empty;
        public string Recipient { get; set; } = string.Empty;
        public DocumentPriority Priority { get; set; }
        public DocumentStatus Status { get; set; }
        public string DeliveryMethod { get; set; } = string.Empty;
        public string ExternalReferenceNumber { get; set; } = string.Empty;
        public DateTime? FollowUpDate { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;
        public string RelatedRegistrationNumber { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<CategoryBackupAttachmentDto> Attachments { get; set; } = new();
    }

    public class CategoryBackupManifest
    {
        public string FormatVersion { get; set; } = "1.0";
        public DateTime ExportedAt { get; set; } = DateTime.Now;
        public string CategoryName { get; set; } = string.Empty;
        public string CategoryDescription { get; set; } = string.Empty;
        public List<CategoryBackupDocumentDto> Documents { get; set; } = new();
    }

    public class CategoryBackupExportResult
    {
        public string BackupFolderPath { get; set; } = string.Empty;
        public string ManifestFilePath { get; set; } = string.Empty;
        public int ExportedDocumentsCount { get; set; }
        public int ExportedAttachmentsCount { get; set; }
    }

    public class CategoryBackupImportResult
    {
        public Category RestoredCategory { get; set; } = null!;
        public int ImportedDocumentsCount { get; set; }
        public int ImportedAttachmentsCount { get; set; }
    }

    public class FullBackupManifest
    {
        public string FormatVersion { get; set; } = "2.0";
        public string ApplicationName { get; set; } = "DocuArchive Pro V1.5";
        public DateTime ExportedAt { get; set; } = DateTime.Now;
        public string SourceMachineName { get; set; } = Environment.MachineName;
        public string SourceWindowsUser { get; set; } = Environment.UserName;
        public string CreatedByAppUser { get; set; } = string.Empty;
        public string AppLanguage { get; set; } = "ar";
        public string AppTheme { get; set; } = "Light";
        public string SelectedArabicFont { get; set; } = "Alexandria";
        public string OriginalVaultPath { get; set; } = string.Empty;
        public string OrganizationName { get; set; } = string.Empty;
        public string LogoRelativePath { get; set; } = string.Empty;

        public int ActiveDocumentsCount { get; set; }
        public int RecycleBinDocumentsCount { get; set; }
        public int InboundCount { get; set; }
        public int OutboundCount { get; set; }
        public int CategoriesCount { get; set; }
        public int CorrespondentEntitiesCount { get; set; }
        public int UsersCount { get; set; }
        public int AuditLogsCount { get; set; }
        public int AttachmentsCount { get; set; }
        public long TotalVaultSizeBytes { get; set; }

        public bool IsEncryptedDatabase { get; set; } = false;
        public bool IsPasswordProtected { get; set; } = false;
        public string? PasswordSalt { get; set; }
        public string? PasswordVerifier { get; set; }

        public string DatabaseSha256 { get; set; } = string.Empty;
        public Dictionary<string, string> FileChecksums { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class FullBackupInspectionResult
    {
        public string ZipFilePath { get; set; } = string.Empty;
        public long ZipSizeBytes { get; set; }
        public bool HasManifest { get; set; }
        public bool RequiresPassword { get; set; } = false;
        public bool PasswordVerified { get; set; } = true;
        public bool ChecksumsValid { get; set; }
        public List<string> ChecksumErrors { get; set; } = new();
        public FullBackupManifest Manifest { get; set; } = new();
    }

    public interface IDocumentService
    {
        Task<List<Document>> GetAllDocumentsAsync();
        Task<List<Document>> SearchDocumentsAsync(DocumentFilterCriteria criteria);
        Task<Document?> GetDocumentByIdAsync(int id);
        Task<Document?> GetDocumentByRegistrationNumberAsync(string registrationNumber);
        Task<bool> IsRegistrationNumberTakenAsync(string regNumber, int excludeDocumentId = 0);
        Task<string> GenerateNextRegistrationNumberAsync(DocumentType type);
        Task<Document> CreateDocumentAsync(Document document, List<string>? filePathsToAttach = null);
        Task<Document> UpdateDocumentAsync(Document document);
        Task<bool> UpdateDocumentStatusAsync(int id, DocumentStatus status);
        Task<bool> DeleteDocumentAsync(int id);
        Task<List<Category>> GetCategoriesAsync();
        Task<bool> IsCategoryNameTakenAsync(string name, int excludeCategoryId = 0);
        Task<Category> AddCategoryAsync(Category category);
        Task<bool> DeleteCategoryAsync(int categoryId);
        Task<Category> UpdateCategoryAsync(Category category);
        Task<List<CorrespondentEntity>> GetCorrespondentEntitiesAsync();
        Task<CorrespondentEntity> AddCorrespondentEntityAsync(CorrespondentEntity entity);
        Task<CorrespondentEntity> UpdateCorrespondentEntityAsync(CorrespondentEntity entity);
        Task<bool> DeleteCorrespondentEntityAsync(int entityId);
        Task<int> MergeCorrespondentEntitiesAsync(int sourceEntityId, int targetEntityId);
        Task<CategoryDeletionSummary> GetCategoryDeletionSummaryAsync(int categoryId);
        Task<CategoryBackupExportResult> ExportCategoryBackupAsync(int categoryId, string targetRootFolder);
        Task<bool> DeleteCategoryPermanentlyWithDocumentsAsync(int categoryId);
        Task<int> TransferCategoryDocumentsAndDeleteAsync(int sourceCategoryId, int? targetCategoryId);
        Task<CategoryBackupImportResult> ImportCategoryBackupAsync(string backupFolderPath);
        Task<DashboardStatistics> GetDashboardStatisticsAsync();
        Task<DocumentAttachment> AddAttachmentAsync(int documentId, string filePath, bool isScanned = false);
        Task<bool> DeleteAttachmentAsync(int attachmentId);

        // Recycle Bin (Soft Delete / Restore / Empty)
        Task<bool> MoveDocumentToRecycleBinAsync(int id);
        Task<bool> RestoreDocumentFromRecycleBinAsync(int id);
        Task<int> EmptyRecycleBinAsync();

        // Audit Trail
        Task LogAuditAsync(string actionType, string details);
        Task<List<AuditLogEntry>> GetRecentAuditLogsAsync(int maxCount = 200);
        Task ClearAuditLogsAsync();

        // Full System Backup & Restore (SQLite + Encrypted Vault + Branding + Integrity Manifest)
        Task<string> CreateFullSystemBackupZipAsync(string destinationFolder, string? password = null, IProgress<(double Progress, string Status)>? progress = null);
        Task<FullBackupInspectionResult> InspectFullSystemBackupZipAsync(string zipFilePath, string? password = null);
        Task<(int RestoredDocs, int RestoredFiles)> RestoreFullSystemBackupZipAsync(string zipFilePath, string? password = null, IProgress<(double Progress, string Status)>? progress = null);
        Task<int> PruneOldBackupsAsync(string backupDirectory, int retentionCount);
    }
}
