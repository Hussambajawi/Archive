using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using DocuArchivePro.Data;
using DocuArchivePro.Models;

namespace DocuArchivePro.Services
{
    public class DocumentService : IDocumentService
    {
        private readonly IFileStorageService _fileStorageService;

        public DocumentService(IFileStorageService fileStorageService)
        {
            _fileStorageService = fileStorageService;
        }

        public async Task<List<Document>> GetAllDocumentsAsync()
        {
            using var db = new ArchiveDbContext();
            return await db.Documents
                .Include(d => d.Category)
                .Include(d => d.Attachments)
                .Where(d => !d.IsDeleted)
                .OrderByDescending(d => d.RegistrationDate)
                .ThenByDescending(d => d.Id)
                .ToListAsync();
        }

        public async Task<List<Document>> SearchDocumentsAsync(DocumentFilterCriteria criteria)
        {
            using var db = new ArchiveDbContext();
            var query = db.Documents
                .Include(d => d.Category)
                .Include(d => d.Attachments)
                .AsQueryable();

            query = criteria.IncludeDeletedOnly
                ? query.Where(d => d.IsDeleted)
                : query.Where(d => !d.IsDeleted);

            if (criteria.OnlyFollowUpDue)
            {
                var today = DateTime.Today;
                query = query.Where(d =>
                    d.FollowUpDate.HasValue &&
                    d.FollowUpDate.Value.Date <= today &&
                    d.Status != DocumentStatus.Completed &&
                    d.Status != DocumentStatus.Archived &&
                    d.Status != DocumentStatus.Cancelled);
            }

            if (criteria.Type.HasValue)
            {
                query = query.Where(d => d.Type == criteria.Type.Value);
            }

            if (criteria.FromDate.HasValue)
            {
                query = query.Where(d => d.RegistrationDate >= criteria.FromDate.Value.Date);
            }

            if (criteria.ToDate.HasValue)
            {
                var endOfDay = criteria.ToDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(d => d.RegistrationDate <= endOfDay);
            }

            if (criteria.Priority.HasValue)
            {
                query = query.Where(d => d.Priority == criteria.Priority.Value);
            }

            if (criteria.Status.HasValue)
            {
                query = query.Where(d => d.Status == criteria.Status.Value);
            }

            if (criteria.CategoryId.HasValue && criteria.CategoryId.Value > 0)
            {
                query = query.Where(d => d.CategoryId == criteria.CategoryId.Value);
            }

            var results = await query
                .OrderByDescending(d => d.RegistrationDate)
                .ThenByDescending(d => d.Id)
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(criteria.SearchQuery))
            {
                string q = NormalizeArabicForSearch(criteria.SearchQuery);
                results = results.Where(d =>
                    NormalizeArabicForSearch(d.RegistrationNumber).Contains(q, StringComparison.Ordinal) ||
                    NormalizeArabicForSearch(d.RelatedRegistrationNumber).Contains(q, StringComparison.Ordinal) ||
                    NormalizeArabicForSearch(d.Subject).Contains(q, StringComparison.Ordinal) ||
                    NormalizeArabicForSearch(d.Sender).Contains(q, StringComparison.Ordinal) ||
                    NormalizeArabicForSearch(d.Recipient).Contains(q, StringComparison.Ordinal) ||
                    NormalizeArabicForSearch(d.Notes).Contains(q, StringComparison.Ordinal) ||
                    NormalizeArabicForSearch(d.ExternalReferenceNumber).Contains(q, StringComparison.Ordinal) ||
                    NormalizeArabicForSearch(d.Department).Contains(q, StringComparison.Ordinal) ||
                    NormalizeArabicForSearch(d.Tags).Contains(q, StringComparison.Ordinal))
                    .ToList();
            }

            return results;
        }

        public static string NormalizeArabicForSearch(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            string europeanDigits = DateTimeHelper.ToEuropeanDigits(input);
            var sb = new StringBuilder(europeanDigits.Length);

            foreach (char ch in europeanDigits)
            {
                // Strip Arabic diacritics (tashkeel) and tatweel
                if ((ch >= '\u064B' && ch <= '\u065F') || ch == '\u0670' || ch == '\u0640')
                {
                    continue;
                }

                char normalized = ch switch
                {
                    'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                    'ة' => 'ه',
                    'ى' => 'ي',
                    'ؤ' => 'و',
                    'ئ' => 'ي',
                    _ => char.ToLowerInvariant(ch)
                };
                sb.Append(normalized);
            }

            return sb.ToString().Trim();
        }

        public async Task<Document?> GetDocumentByIdAsync(int id)
        {
            using var db = new ArchiveDbContext();
            return await db.Documents
                .Include(d => d.Category)
                .Include(d => d.Attachments)
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        public async Task<Document?> GetDocumentByRegistrationNumberAsync(string registrationNumber)
        {
            if (string.IsNullOrWhiteSpace(registrationNumber)) return null;
            string normalized = registrationNumber.Trim();
            using var db = new ArchiveDbContext();
            return await db.Documents
                .Include(d => d.Category)
                .Include(d => d.Attachments)
                .FirstOrDefaultAsync(d => d.RegistrationNumber == normalized);
        }

        public async Task<bool> IsRegistrationNumberTakenAsync(string regNumber, int excludeDocumentId = 0)
        {
            if (string.IsNullOrWhiteSpace(regNumber)) return false;
            string normalized = regNumber.Trim();
            using var db = new ArchiveDbContext();
            return await db.Documents.AnyAsync(d => d.RegistrationNumber == normalized && d.Id != excludeDocumentId).ConfigureAwait(false);
        }

        public async Task<string> GenerateNextRegistrationNumberAsync(DocumentType type)
        {
            using var db = new ArchiveDbContext();
            string prefix = type == DocumentType.Inbound ? "IN" : "OUT";
            int currentYear = DateTime.Now.Year;
            string prefixWithYear = $"{prefix}-{currentYear}-";

            var existingNumbers = await db.Documents
                .Where(d => d.RegistrationNumber.StartsWith(prefixWithYear))
                .Select(d => d.RegistrationNumber)
                .ToListAsync();

            int maxSeq = 0;
            foreach (var regNo in existingNumbers)
            {
                string seqPart = regNo.Substring(prefixWithYear.Length);
                if (int.TryParse(seqPart, out int seq) && seq > maxSeq)
                {
                    maxSeq = seq;
                }
            }

            int nextSeq = maxSeq + 1;
            return $"{prefixWithYear}{nextSeq:D4}";
        }

        public async Task<Document> CreateDocumentAsync(Document document, List<string>? filePathsToAttach = null)
        {
            using var db = new ArchiveDbContext();

            if (string.IsNullOrWhiteSpace(document.RegistrationNumber))
            {
                document.RegistrationNumber = await GenerateNextRegistrationNumberAsync(document.Type);
            }

            document.CreatedAt = DateTime.Now;
            document.UpdatedAt = DateTime.Now;

            await db.Documents.AddAsync(document);
            await db.SaveChangesAsync();

            if (filePathsToAttach != null && filePathsToAttach.Count > 0)
            {
                foreach (var path in filePathsToAttach)
                {
                    if (File.Exists(path))
                    {
                        var attachment = await _fileStorageService.SaveFileAttachmentAsync(path, document.Id, false);
                        await db.Attachments.AddAsync(attachment);
                    }
                }
                await db.SaveChangesAsync();
            }

            await LogAuditAsync("إضافة معاملة / Create Document", $"{document.RegistrationNumber} - {document.Subject}");
            return document;
        }

        public async Task<Document> UpdateDocumentAsync(Document document)
        {
            using var db = new ArchiveDbContext();
            var existing = await db.Documents.FindAsync(document.Id);
            if (existing == null)
            {
                throw new KeyNotFoundException($"Document #{document.Id} not found");
            }

            string oldRegNumber = existing.RegistrationNumber;
            string newRegNumber = document.RegistrationNumber?.Trim() ?? string.Empty;

            existing.RegistrationNumber = newRegNumber;
            existing.Type = document.Type;
            existing.RegistrationDate = document.RegistrationDate;
            existing.DocumentDate = document.DocumentDate;
            existing.Subject = document.Subject;
            existing.Sender = document.Sender;
            existing.Recipient = document.Recipient;
            existing.CategoryId = document.CategoryId;
            existing.Priority = document.Priority;
            existing.Status = document.Status;
            existing.DeliveryMethod = document.DeliveryMethod;
            existing.ExternalReferenceNumber = document.ExternalReferenceNumber;
            existing.FollowUpDate = document.FollowUpDate;
            existing.Notes = document.Notes;
            existing.Department = document.Department;
            existing.Tags = document.Tags;
            existing.RelatedRegistrationNumber = document.RelatedRegistrationNumber;
            existing.UpdatedAt = DateTime.Now;

            if (!string.IsNullOrWhiteSpace(oldRegNumber) &&
                !string.IsNullOrWhiteSpace(newRegNumber) &&
                !string.Equals(oldRegNumber, newRegNumber, StringComparison.OrdinalIgnoreCase))
            {
                var referencingDocs = await db.Documents
                    .Where(d => d.Id != existing.Id && d.RelatedRegistrationNumber == oldRegNumber)
                    .ToListAsync();
                foreach (var refDoc in referencingDocs)
                {
                    refDoc.RelatedRegistrationNumber = newRegNumber;
                    refDoc.UpdatedAt = DateTime.Now;
                }
            }

            await db.SaveChangesAsync();
            await LogAuditAsync("تعديل معاملة / Update Document", $"{existing.RegistrationNumber} - {existing.Subject}");
            return existing;
        }

        public async Task<bool> UpdateDocumentStatusAsync(int id, DocumentStatus status)
        {
            using var db = new ArchiveDbContext();
            var doc = await db.Documents.FindAsync(id);
            if (doc == null) return false;

            doc.Status = status;
            doc.UpdatedAt = DateTime.Now;
            await db.SaveChangesAsync();
            await LogAuditAsync("تحديث حالة المعاملة / Update Document Status", $"{doc.RegistrationNumber} -> {status}");
            return true;
        }

        public async Task<bool> DeleteDocumentAsync(int id)
        {
            using var db = new ArchiveDbContext();
            var doc = await db.Documents
                .Include(d => d.Attachments)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (doc == null) return false;

            string regNo = doc.RegistrationNumber;
            string subj = doc.Subject;

            // Delete physical attachment files
            foreach (var att in doc.Attachments)
            {
                _fileStorageService.DeleteFile(att.StoragePath);
            }

            db.Documents.Remove(doc);
            await db.SaveChangesAsync();
            await LogAuditAsync("حذف نهائي لمعاملة / Permanent Delete Document", $"{regNo} - {subj}");
            return true;
        }

        public async Task<List<Category>> GetCategoriesAsync()
        {
            using var db = new ArchiveDbContext();
            return await db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        }

        public async Task<bool> IsCategoryNameTakenAsync(string name, int excludeCategoryId = 0)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string normalized = name.Trim().ToLower();
            using var db = new ArchiveDbContext();
            return await db.Categories.AnyAsync(c => c.Id != excludeCategoryId && c.IsActive && c.Name.ToLower() == normalized);
        }

        public async Task<Category> AddCategoryAsync(Category category)
        {
            if (category == null || string.IsNullOrWhiteSpace(category.Name))
                throw new InvalidOperationException("Category name cannot be empty.");

            category.Name = category.Name.Trim();
            using var db = new ArchiveDbContext();
            string normalized = category.Name.ToLower();
            var existing = await db.Categories.FirstOrDefaultAsync(c => c.Name.ToLower() == normalized);
            if (existing != null)
            {
                if (existing.IsActive)
                {
                    throw new InvalidOperationException("توجد إضبارة أخرى مسجلة بنفس هذا الاسم بالفعل! يرجى اختيار اسم فريد للإضبارة / A dossier with this name already exists.");
                }
                existing.IsActive = true;
                existing.Description = category.Description;
                await db.SaveChangesAsync();
                await LogAuditAsync("إعادة تفعيل إضبارة / Reactivate Dossier", existing.Name);
                return existing;
            }

            await db.Categories.AddAsync(category);
            await db.SaveChangesAsync();
            await LogAuditAsync("إضافة إضبارة / Add Dossier", category.Name);
            return category;
        }

        public async Task<bool> DeleteCategoryAsync(int categoryId)
        {
            return await DeleteCategoryPermanentlyWithDocumentsAsync(categoryId);
        }

        public async Task<Category> UpdateCategoryAsync(Category category)
        {
            using var db = new ArchiveDbContext();
            var existing = await db.Categories.FindAsync(category.Id);
            if (existing == null) throw new KeyNotFoundException($"Category #{category.Id} not found");

            string newName = category.Name.Trim();
            string normalized = newName.ToLower();
            bool duplicate = await db.Categories.AnyAsync(c => c.Id != category.Id && c.IsActive && c.Name.ToLower() == normalized);
            if (duplicate)
            {
                throw new InvalidOperationException("توجد إضبارة أخرى مسجلة بنفس هذا الاسم بالفعل! يرجى اختيار اسم فريد للإضبارة / A dossier with this name already exists.");
            }

            existing.Name = newName;
            existing.Description = category.Description;
            existing.IsActive = category.IsActive;

            await db.SaveChangesAsync();
            await LogAuditAsync("تعديل إضبارة / Update Dossier", existing.Name);
            return existing;
        }

        public async Task<List<CorrespondentEntity>> GetCorrespondentEntitiesAsync()
        {
            using var db = new ArchiveDbContext();
            return await db.CorrespondentEntities
                .Where(e => e.IsActive)
                .OrderBy(e => e.Name)
                .ToListAsync();
        }

        public async Task<CorrespondentEntity> AddCorrespondentEntityAsync(CorrespondentEntity entity)
        {
            using var db = new ArchiveDbContext();
            var existing = await db.CorrespondentEntities
                .FirstOrDefaultAsync(e => e.Name.ToLower() == entity.Name.Trim().ToLower());
            if (existing != null)
            {
                existing.EntityType = entity.EntityType;
                existing.Notes = entity.Notes;
                existing.IsActive = true;
                await db.SaveChangesAsync();
                return existing;
            }

            entity.Name = entity.Name.Trim();
            entity.CreatedAt = DateTime.Now;
            await db.CorrespondentEntities.AddAsync(entity);
            await db.SaveChangesAsync();
            return entity;
        }

        public async Task<CorrespondentEntity> UpdateCorrespondentEntityAsync(CorrespondentEntity entity)
        {
            using var db = new ArchiveDbContext();
            var existing = await db.CorrespondentEntities.FindAsync(entity.Id);
            if (existing == null) throw new KeyNotFoundException($"Entity #{entity.Id} not found");

            string oldName = existing.Name.Trim();
            string newName = entity.Name.Trim();

            bool duplicateExists = await db.CorrespondentEntities.AnyAsync(
                e => e.Id != entity.Id && e.IsActive && e.Name.ToLower() == newName.ToLower());
            if (duplicateExists)
            {
                throw new InvalidOperationException("توجد جهة أخرى مسجلة بنفس هذا الاسم! يمكنك استخدام أداة (دمج الجهات المكررة) لتوحيدهما / Another entity with this name already exists.");
            }

            existing.Name = newName;
            existing.EntityType = entity.EntityType;
            existing.Notes = entity.Notes;
            existing.IsActive = entity.IsActive;

            int cascadedDocsCount = 0;
            if (!string.Equals(oldName, newName, StringComparison.Ordinal))
            {
                var matchingDocs = await db.Documents
                    .Where(d => d.Sender == oldName || d.Recipient == oldName)
                    .ToListAsync();

                foreach (var doc in matchingDocs)
                {
                    bool changed = false;
                    if (doc.Sender == oldName)
                    {
                        doc.Sender = newName;
                        changed = true;
                    }
                    if (doc.Recipient == oldName)
                    {
                        doc.Recipient = newName;
                        changed = true;
                    }
                    if (changed)
                    {
                        doc.UpdatedAt = DateTime.Now;
                        cascadedDocsCount++;
                    }
                }
            }

            await db.SaveChangesAsync();
            await LogAuditAsync("تعديل جهة محفوظة / Update Entity", $"{oldName} -> {newName} (Updated {cascadedDocsCount} docs)");
            return existing;
        }

        public async Task<bool> DeleteCorrespondentEntityAsync(int entityId)
        {
            using var db = new ArchiveDbContext();
            var entity = await db.CorrespondentEntities.FindAsync(entityId);
            if (entity == null) return false;

            db.CorrespondentEntities.Remove(entity);
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<int> MergeCorrespondentEntitiesAsync(int sourceEntityId, int targetEntityId)
        {
            if (sourceEntityId <= 0 || targetEntityId <= 0 || sourceEntityId == targetEntityId)
                return 0;

            using var db = new ArchiveDbContext();
            var source = await db.CorrespondentEntities.FindAsync(sourceEntityId);
            var target = await db.CorrespondentEntities.FindAsync(targetEntityId);

            if (source == null || target == null)
                return 0;

            string sourceName = source.Name.Trim();
            string targetName = target.Name.Trim();

            // Find all documents referencing the source entity
            var matchingDocs = await db.Documents
                .Where(d => d.Sender == sourceName || d.Recipient == sourceName)
                .ToListAsync();

            int updatedCount = 0;
            foreach (var doc in matchingDocs)
            {
                bool modified = false;
                if (doc.Sender == sourceName)
                {
                    doc.Sender = targetName;
                    modified = true;
                }
                if (doc.Recipient == sourceName)
                {
                    doc.Recipient = targetName;
                    modified = true;
                }
                if (modified)
                {
                    updatedCount++;
                }
            }

            // Remove the duplicate source entity
            db.CorrespondentEntities.Remove(source);
            await db.SaveChangesAsync();

            await LogAuditAsync("MERGE_ENTITIES",
                $"دمج الجهة '{sourceName}' (معرف: {source.Id}) في الجهة '{targetName}' (معرف: {target.Id}). تم تحديث {updatedCount} معاملة.");

            return updatedCount;
        }

        public async Task<CategoryDeletionSummary> GetCategoryDeletionSummaryAsync(int categoryId)
        {
            using var db = new ArchiveDbContext();
            var cat = await db.Categories.FindAsync(categoryId);
            if (cat == null) throw new KeyNotFoundException($"Category #{categoryId} not found");

            int inboundCount = await db.Documents.CountAsync(d => d.CategoryId == categoryId && !d.IsDeleted && d.Type == DocumentType.Inbound);
            int outboundCount = await db.Documents.CountAsync(d => d.CategoryId == categoryId && !d.IsDeleted && d.Type == DocumentType.Outbound);
            int recycleBinCount = await db.Documents.CountAsync(d => d.CategoryId == categoryId && d.IsDeleted);
            var attachSizes = await db.Attachments
                .Where(a => a.Document != null && a.Document.CategoryId == categoryId)
                .Select(a => a.FileSizeBytes)
                .ToListAsync();

            return new CategoryDeletionSummary
            {
                CategoryId = cat.Id,
                CategoryName = cat.DisplayName,
                InboundCount = inboundCount,
                OutboundCount = outboundCount,
                RecycleBinCount = recycleBinCount,
                TotalDocumentsCount = inboundCount + outboundCount + recycleBinCount,
                TotalAttachmentsCount = attachSizes.Count,
                TotalAttachmentsSizeBytes = attachSizes.Sum()
            };
        }

        public async Task<CategoryBackupExportResult> ExportCategoryBackupAsync(int categoryId, string targetRootFolder)
        {
            using var db = new ArchiveDbContext();
            var cat = await db.Categories.FindAsync(categoryId);
            if (cat == null) throw new KeyNotFoundException($"Category #{categoryId} not found");

            var docs = await db.Documents
                .Include(d => d.Attachments)
                .Where(d => d.CategoryId == categoryId)
                .OrderBy(d => d.RegistrationDate)
                .ToListAsync();

            if (!Directory.Exists(targetRootFolder))
            {
                Directory.CreateDirectory(targetRootFolder);
            }

            string safeCatName = SanitizeFileName(string.IsNullOrWhiteSpace(cat.DisplayName) ? cat.Name : cat.DisplayName);
            string folderName = $"DossierBackup_{safeCatName}_{DateTime.Now:yyyyMMdd_HHmmss}";
            string backupFolder = Path.Combine(targetRootFolder, folderName);
            string attachmentsFolder = Path.Combine(backupFolder, "Attachments");
            Directory.CreateDirectory(backupFolder);
            Directory.CreateDirectory(attachmentsFolder);

            var manifest = new CategoryBackupManifest
            {
                FormatVersion = "1.0",
                ExportedAt = DateTime.Now,
                CategoryName = cat.Name,
                CategoryDescription = cat.Description
            };

            int exportedAttachments = 0;

            foreach (var doc in docs)
            {
                var docDto = new CategoryBackupDocumentDto
                {
                    RegistrationNumber = doc.RegistrationNumber,
                    Type = doc.Type,
                    RegistrationDate = doc.RegistrationDate,
                    DocumentDate = doc.DocumentDate,
                    Subject = doc.Subject,
                    Sender = doc.Sender,
                    Recipient = doc.Recipient,
                    Priority = doc.Priority,
                    Status = doc.Status,
                    DeliveryMethod = doc.DeliveryMethod,
                    ExternalReferenceNumber = doc.ExternalReferenceNumber,
                    FollowUpDate = doc.FollowUpDate,
                    Notes = doc.Notes,
                    Department = doc.Department,
                    Tags = doc.Tags,
                    RelatedRegistrationNumber = doc.RelatedRegistrationNumber,
                    CreatedAt = doc.CreatedAt
                };

                foreach (var att in doc.Attachments)
                {
                    if (!File.Exists(att.StoragePath)) continue;

                    byte[] plainBytes = await _fileStorageService.GetDecryptedBytesAsync(att.StoragePath);
                    string safeReg = SanitizeFileName(doc.RegistrationNumber);
                    string safeFile = SanitizeFileName(att.FileName);
                    string exportFileName = $"{safeReg}_{att.Id}_{safeFile}";
                    string relPath = Path.Combine("Attachments", exportFileName);
                    string fullDestPath = Path.Combine(backupFolder, relPath);

                    await File.WriteAllBytesAsync(fullDestPath, plainBytes);
                    exportedAttachments++;

                    docDto.Attachments.Add(new CategoryBackupAttachmentDto
                    {
                        OriginalFileName = att.FileName,
                        RelativePath = relPath,
                        FileExtension = att.FileExtension,
                        FileSizeBytes = plainBytes.Length,
                        IsScanned = att.IsScanned,
                        UploadedAt = att.UploadedAt
                    });
                }

                manifest.Documents.Add(docDto);
            }

            string manifestPath = Path.Combine(backupFolder, "dossier-manifest.json");
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string json = JsonSerializer.Serialize(manifest, jsonOptions);
            await File.WriteAllTextAsync(manifestPath, json, Encoding.UTF8);

            // Human-readable summary file alongside the JSON manifest
            int inCount = docs.Count(d => d.Type == DocumentType.Inbound);
            int outCount = docs.Count(d => d.Type == DocumentType.Outbound);
            var sb = new StringBuilder();
            sb.AppendLine("DocuArchive Pro - Unencrypted Dossier Backup / نسخة احتياطية غير مشفرة للإضبارة");
            sb.AppendLine($"Dossier / الإضبارة: {cat.Name}");
            sb.AppendLine($"Description / الوصف: {cat.Description}");
            sb.AppendLine($"Exported At / تاريخ النسخ الاحتياطي: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Inbound Documents / معاملات الوارد: {inCount}");
            sb.AppendLine($"Outbound Documents / معاملات الصادر: {outCount}");
            sb.AppendLine($"Archived Attachments (Unencrypted) / المرفقات المستخرجة بدون تشفير: {exportedAttachments}");
            sb.AppendLine("Note: Select this folder from Settings -> Import Dossier Backup to restore it anytime.");
            await File.WriteAllTextAsync(Path.Combine(backupFolder, "ReadMe_Dossier_Summary.txt"), sb.ToString(), Encoding.UTF8);

            return new CategoryBackupExportResult
            {
                BackupFolderPath = backupFolder,
                ManifestFilePath = manifestPath,
                ExportedDocumentsCount = docs.Count,
                ExportedAttachmentsCount = exportedAttachments
            };
        }

        public async Task<bool> DeleteCategoryPermanentlyWithDocumentsAsync(int categoryId)
        {
            using var db = new ArchiveDbContext();
            var cat = await db.Categories.FindAsync(categoryId);
            if (cat == null) return false;

            var linkedDocs = await db.Documents
                .Include(d => d.Attachments)
                .Where(d => d.CategoryId == categoryId)
                .ToListAsync();

            foreach (var doc in linkedDocs)
            {
                foreach (var att in doc.Attachments)
                {
                    _fileStorageService.DeleteFile(att.StoragePath);
                }
                db.Attachments.RemoveRange(doc.Attachments);
            }

            db.Documents.RemoveRange(linkedDocs);
            db.Categories.Remove(cat);
            await db.SaveChangesAsync();
            await LogAuditAsync("حذف إضبارة نهائياً / Permanent Delete Dossier", $"{cat.Name} ({linkedDocs.Count} docs)");
            return true;
        }

        public async Task<int> TransferCategoryDocumentsAndDeleteAsync(int sourceCategoryId, int? targetCategoryId)
        {
            if (targetCategoryId.HasValue && targetCategoryId.Value == sourceCategoryId)
            {
                throw new InvalidOperationException("Cannot transfer documents to the same dossier being deleted.");
            }

            using var db = new ArchiveDbContext();
            var sourceCat = await db.Categories.FindAsync(sourceCategoryId);
            if (sourceCat == null) return 0;

            int? effectiveTargetId = (targetCategoryId.HasValue && targetCategoryId.Value > 0)
                ? targetCategoryId.Value
                : null;

            var linkedDocs = await db.Documents
                .Where(d => d.CategoryId == sourceCategoryId)
                .ToListAsync();

            foreach (var doc in linkedDocs)
            {
                doc.CategoryId = effectiveTargetId;
                doc.UpdatedAt = DateTime.Now;
            }

            db.Categories.Remove(sourceCat);
            await db.SaveChangesAsync();

            await LogAuditAsync("نقل معاملات إضبارة وحذفها / Transfer & Delete Dossier",
                $"Moved {linkedDocs.Count} docs from '{sourceCat.Name}' to CategoryId={effectiveTargetId?.ToString() ?? "None"}");
            return linkedDocs.Count;
        }

        public async Task<CategoryBackupImportResult> ImportCategoryBackupAsync(string backupFolderPath)
        {
            if (string.IsNullOrWhiteSpace(backupFolderPath) || !Directory.Exists(backupFolderPath))
            {
                throw new DirectoryNotFoundException("Backup folder not found.");
            }

            string manifestPath = Path.Combine(backupFolderPath, "dossier-manifest.json");
            string resolvedRoot = backupFolderPath;

            if (!File.Exists(manifestPath))
            {
                // Allow picking a single nested backup folder directly
                string? nestedManifest = Directory.GetFiles(backupFolderPath, "dossier-manifest.json", SearchOption.AllDirectories).FirstOrDefault();
                if (nestedManifest == null)
                {
                    throw new FileNotFoundException("dossier-manifest.json was not found in the selected folder.");
                }
                manifestPath = nestedManifest;
                resolvedRoot = Path.GetDirectoryName(manifestPath)!;
            }

            string json = await File.ReadAllTextAsync(manifestPath, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<CategoryBackupManifest>(json);
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.CategoryName))
            {
                throw new InvalidDataException("Invalid dossier backup manifest file.");
            }

            using var db = new ArchiveDbContext();

            var category = await db.Categories.FirstOrDefaultAsync(c => c.Name == manifest.CategoryName);
            if (category == null)
            {
                category = new Category
                {
                    Name = manifest.CategoryName,
                    Description = manifest.CategoryDescription ?? string.Empty,
                    IsActive = true
                };
                await db.Categories.AddAsync(category);
                await db.SaveChangesAsync();
            }
            else
            {
                category.IsActive = true;
                if (string.IsNullOrWhiteSpace(category.Description) && !string.IsNullOrWhiteSpace(manifest.CategoryDescription))
                {
                    category.Description = manifest.CategoryDescription;
                }
                await db.SaveChangesAsync();
            }

            int importedDocs = 0;
            int importedAttachments = 0;
            string rootFull = Path.GetFullPath(resolvedRoot);

            foreach (var docDto in manifest.Documents)
            {
                // Ensure unique RegistrationNumber
                string targetRegNum = docDto.RegistrationNumber;
                var existingSame = await db.Documents
                    .Include(d => d.Attachments)
                    .FirstOrDefaultAsync(d => d.RegistrationNumber == targetRegNum);

                Document targetDoc;
                if (existingSame != null && existingSame.Subject == docDto.Subject)
                {
                    existingSame.CategoryId = category.Id;
                    existingSame.UpdatedAt = DateTime.Now;
                    targetDoc = existingSame;
                    await db.SaveChangesAsync();
                }
                else
                {
                    if (existingSame != null)
                    {
                        int suffix = 1;
                        while (await db.Documents.AnyAsync(d => d.RegistrationNumber == $"{docDto.RegistrationNumber}-IMP{suffix}"))
                        {
                            suffix++;
                        }
                        targetRegNum = $"{docDto.RegistrationNumber}-IMP{suffix}";
                    }

                    targetDoc = new Document
                    {
                        RegistrationNumber = targetRegNum,
                        Type = docDto.Type,
                        RegistrationDate = docDto.RegistrationDate,
                        DocumentDate = docDto.DocumentDate,
                        Subject = docDto.Subject,
                        Sender = docDto.Sender,
                        Recipient = docDto.Recipient,
                        CategoryId = category.Id,
                        Priority = docDto.Priority,
                        Status = docDto.Status,
                        DeliveryMethod = docDto.DeliveryMethod ?? string.Empty,
                        ExternalReferenceNumber = docDto.ExternalReferenceNumber ?? string.Empty,
                        FollowUpDate = docDto.FollowUpDate,
                        Notes = docDto.Notes ?? string.Empty,
                        Department = docDto.Department ?? string.Empty,
                        Tags = docDto.Tags ?? string.Empty,
                        RelatedRegistrationNumber = docDto.RelatedRegistrationNumber ?? string.Empty,
                        CreatedAt = docDto.CreatedAt == default ? DateTime.Now : docDto.CreatedAt,
                        UpdatedAt = DateTime.Now
                    };

                    await db.Documents.AddAsync(targetDoc);
                    await db.SaveChangesAsync();
                }

                importedDocs++;

                foreach (var attDto in docDto.Attachments)
                {
                    if (string.IsNullOrWhiteSpace(attDto.RelativePath)) continue;

                    string combined = Path.GetFullPath(Path.Combine(resolvedRoot, attDto.RelativePath));
                    if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(combined))
                    {
                        continue;
                    }

                    bool alreadyAttached = targetDoc.Attachments.Any(a => a.FileName == attDto.OriginalFileName && a.FileSizeBytes == attDto.FileSizeBytes);
                    if (alreadyAttached) continue;

                    var attachment = await _fileStorageService.SaveFileAttachmentAsync(combined, targetDoc.Id, attDto.IsScanned);
                    if (!string.IsNullOrWhiteSpace(attDto.OriginalFileName))
                    {
                        attachment.FileName = attDto.OriginalFileName;
                    }
                    if (attDto.UploadedAt != default)
                    {
                        attachment.UploadedAt = attDto.UploadedAt;
                    }

                    await db.Attachments.AddAsync(attachment);
                    await db.SaveChangesAsync();
                    importedAttachments++;
                }
            }

            return new CategoryBackupImportResult
            {
                RestoredCategory = category,
                ImportedDocumentsCount = importedDocs,
                ImportedAttachmentsCount = importedAttachments
            };
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Item";
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(ch => invalid.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Item" : cleaned.Length > 60 ? cleaned.Substring(0, 60) : cleaned;
        }

        public async Task<DashboardStatistics> GetDashboardStatisticsAsync()
        {
            using var db = new ArchiveDbContext();
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);

            int total = await db.Documents.CountAsync(d => !d.IsDeleted);
            int inbound = await db.Documents.CountAsync(d => !d.IsDeleted && d.Type == DocumentType.Inbound);
            int outbound = await db.Documents.CountAsync(d => !d.IsDeleted && d.Type == DocumentType.Outbound);
            int todayCount = await db.Documents.CountAsync(d => !d.IsDeleted && d.RegistrationDate >= today && d.RegistrationDate < tomorrow);
            int urgent = await db.Documents.CountAsync(d => !d.IsDeleted && (d.Priority == DocumentPriority.Urgent || d.Priority == DocumentPriority.VeryUrgent));
            int followUpDue = await db.Documents.CountAsync(d =>
                !d.IsDeleted &&
                d.FollowUpDate.HasValue &&
                d.FollowUpDate.Value.Date <= today &&
                d.Status != DocumentStatus.Completed &&
                d.Status != DocumentStatus.Archived &&
                d.Status != DocumentStatus.Cancelled);
            int attachments = await db.Attachments.CountAsync(a => a.Document == null || !a.Document.IsDeleted);
            int recycleCount = await db.Documents.CountAsync(d => d.IsDeleted);
            long totalStorage = _fileStorageService.GetTotalStorageSizeBytes();

            return new DashboardStatistics
            {
                TotalDocuments = total,
                InboundCount = inbound,
                OutboundCount = outbound,
                TodayCount = todayCount,
                UrgentCount = urgent,
                FollowUpDueCount = followUpDue,
                TotalAttachmentsCount = attachments,
                RecycleBinCount = recycleCount,
                TotalStorageBytes = totalStorage
            };
        }

        public async Task<DocumentAttachment> AddAttachmentAsync(int documentId, string filePath, bool isScanned = false)
        {
            using var db = new ArchiveDbContext();
            var attachment = await _fileStorageService.SaveFileAttachmentAsync(filePath, documentId, isScanned);
            await db.Attachments.AddAsync(attachment);
            await db.SaveChangesAsync();
            return attachment;
        }

        public async Task<bool> DeleteAttachmentAsync(int attachmentId)
        {
            using var db = new ArchiveDbContext();
            var att = await db.Attachments.FindAsync(attachmentId);
            if (att == null) return false;

            _fileStorageService.DeleteFile(att.StoragePath);
            db.Attachments.Remove(att);
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> MoveDocumentToRecycleBinAsync(int id)
        {
            using var db = new ArchiveDbContext();
            var doc = await db.Documents.FindAsync(id);
            if (doc == null) return false;

            doc.IsDeleted = true;
            doc.DeletedAt = DateTime.Now;
            doc.UpdatedAt = DateTime.Now;
            await db.SaveChangesAsync();
            await LogAuditAsync("نقل لسلة المحذوفات / Move to Recycle Bin", $"{doc.RegistrationNumber} - {doc.Subject}");
            return true;
        }

        public async Task<bool> RestoreDocumentFromRecycleBinAsync(int id)
        {
            using var db = new ArchiveDbContext();
            var doc = await db.Documents.FindAsync(id);
            if (doc == null) return false;

            doc.IsDeleted = false;
            doc.DeletedAt = null;
            doc.UpdatedAt = DateTime.Now;
            await db.SaveChangesAsync();
            await LogAuditAsync("استعادة من سلة المحذوفات / Restore from Recycle Bin", $"{doc.RegistrationNumber} - {doc.Subject}");
            return true;
        }

        public async Task<int> EmptyRecycleBinAsync()
        {
            using var db = new ArchiveDbContext();
            var trashed = await db.Documents
                .Include(d => d.Attachments)
                .Where(d => d.IsDeleted)
                .ToListAsync();

            if (trashed.Count == 0) return 0;

            foreach (var doc in trashed)
            {
                foreach (var att in doc.Attachments)
                {
                    _fileStorageService.DeleteFile(att.StoragePath);
                }
            }

            int count = trashed.Count;
            db.Documents.RemoveRange(trashed);
            await db.SaveChangesAsync();
            await LogAuditAsync("إفراغ سلة المحذوفات / Empty Recycle Bin", $"Deleted permanently: {count}");
            return count;
        }

        public async Task LogAuditAsync(string actionType, string details)
        {
            try
            {
                using var db = new ArchiveDbContext();
                string username = SecurityService.Instance.CurrentUser?.Username ?? "System";
                await db.AuditLogs.AddAsync(new AuditLogEntry
                {
                    Timestamp = DateTime.Now,
                    Username = username,
                    ActionType = actionType,
                    Details = details ?? string.Empty
                });
                await db.SaveChangesAsync();
            }
            catch
            {
                // Audit logging failures must not interrupt primary workflow
            }
        }

        public async Task<List<AuditLogEntry>> GetRecentAuditLogsAsync(int maxCount = 200)
        {
            using var db = new ArchiveDbContext();
            return await db.AuditLogs
                .OrderByDescending(a => a.Timestamp)
                .ThenByDescending(a => a.Id)
                .Take(maxCount)
                .ToListAsync();
        }

        public async Task ClearAuditLogsAsync()
        {
            using var db = new ArchiveDbContext();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM AuditLogs;");
            await LogAuditAsync("مسح سجل التدقيق / Clear Audit Logs", "Cleared by administrator");
        }

        public async Task<string> CreateFullSystemBackupZipAsync(
            string destinationFolder,
            string? password = null,
            IProgress<(double Progress, string Status)>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(destinationFolder))
            {
                throw new ArgumentException("Destination folder is required.", nameof(destinationFolder));
            }

            if (!Directory.Exists(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }

            DateTime exportTime = DateTime.Now;
            progress?.Report((0.05, "فحص المساحة المتوفرة وتجهيز بيئة العمل..."));

            var settings = await SecurityService.Instance.GetSettingsAsync().ConfigureAwait(false);
            string vaultDir = _fileStorageService.StorageRootDirectory;

            // Compute approximate size required
            long vaultTotalBytes = 0;
            var filesToInclude = new List<(string SourcePath, string EntryRelativePath)>();
            var addedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(vaultDir))
            {
                foreach (string f in Directory.EnumerateFiles(vaultDir, "*.*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(vaultDir, f).Replace('\\', '/');
                    filesToInclude.Add((f, $"Vault/{rel}"));
                    addedSources.Add(Path.GetFullPath(f));
                    vaultTotalBytes += new FileInfo(f).Length;
                }
            }

            // Include any legacy attachment outside the active Vault directory
            using (var db = new ArchiveDbContext())
            {
                var unmigrated = await db.Attachments
                    .Where(a => !string.IsNullOrEmpty(a.StoragePath))
                    .ToListAsync().ConfigureAwait(false);

                foreach (var att in unmigrated)
                {
                    if (File.Exists(att.StoragePath))
                    {
                        string fullSrc = Path.GetFullPath(att.StoragePath);
                        if (!addedSources.Contains(fullSrc))
                        {
                            string fileName = Path.GetFileName(fullSrc);
                            string entryPath = $"Vault/{att.UploadedAt:yyyy}/{att.UploadedAt:MM}/{fileName}";
                            filesToInclude.Add((fullSrc, entryPath));
                            addedSources.Add(fullSrc);
                            vaultTotalBytes += new FileInfo(fullSrc).Length;
                        }
                    }
                }
            }

            // Disk space verification on destination drive
            EnsureSufficientDiskSpace(destinationFolder, vaultTotalBytes);

            string stageDir = Path.Combine(Path.GetTempPath(), $"DocuArchive_Stage_{Guid.NewGuid():N}");
            Directory.CreateDirectory(stageDir);

            try
            {
                progress?.Report((0.15, "أخذ لقطة آمنة لقاعدة البيانات (SQLite Snapshot)..."));
                string stageDbPath = Path.Combine(stageDir, "DocuArchive.db");
                bool vacuumSucceeded = false;
                try
                {
                    await using var snapConn = new SqliteConnection($"Data Source={ArchiveDbContext.DatabasePath}");
                    await snapConn.OpenAsync().ConfigureAwait(false);
                    await using var snapCmd = snapConn.CreateCommand();
                    snapCmd.CommandText = "VACUUM INTO @destPath;";
                    snapCmd.Parameters.AddWithValue("@destPath", stageDbPath);
                    await snapCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    vacuumSucceeded = File.Exists(stageDbPath) && new FileInfo(stageDbPath).Length > 0;
                }
                catch
                {
                    vacuumSucceeded = false;
                }

                if (!vacuumSucceeded)
                {
                    using (var db = new ArchiveDbContext())
                    {
                        await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(FULL);").ConfigureAwait(false);
                    }
                    SqliteConnection.ClearAllPools();

                    string dbPath = ArchiveDbContext.DatabasePath;
                    if (File.Exists(dbPath))
                    {
                        File.Copy(dbPath, stageDbPath, overwrite: true);
                    }
                }

                progress?.Report((0.25, "حساب إحصائيات النظام من لقطة القاعدة المجمّدة..."));
                var manifest = new FullBackupManifest
                {
                    FormatVersion = "2.0",
                    ApplicationName = "DocuArchive Pro V1.5",
                    ExportedAt = exportTime,
                    SourceMachineName = Environment.MachineName,
                    SourceWindowsUser = Environment.UserName,
                    CreatedByAppUser = SecurityService.Instance.CurrentUser?.Username ?? "System",
                    AppLanguage = LocalizationService.Instance.CurrentLanguage.Code,
                    AppTheme = settings.SelectedTheme,
                    SelectedArabicFont = settings.SelectedArabicFont,
                    OriginalVaultPath = vaultDir,
                    OrganizationName = settings.OrganizationName ?? string.Empty,
                    TotalVaultSizeBytes = vaultTotalBytes
                };

                // Read exact consistent counts from the snapshotted database file
                using (var snapConn = new SqliteConnection($"Data Source={stageDbPath};Mode=ReadOnly"))
                {
                    await snapConn.OpenAsync().ConfigureAwait(false);
                    using var cmd = snapConn.CreateCommand();

                    cmd.CommandText = "SELECT COUNT(*) FROM Documents WHERE IsDeleted = 0;";
                    manifest.ActiveDocumentsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false));

                    cmd.CommandText = "SELECT COUNT(*) FROM Documents WHERE IsDeleted = 1;";
                    manifest.RecycleBinDocumentsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false));

                    cmd.CommandText = "SELECT COUNT(*) FROM Documents WHERE IsDeleted = 0 AND Type = 1;"; // Inbound
                    manifest.InboundCount = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false));

                    cmd.CommandText = "SELECT COUNT(*) FROM Documents WHERE IsDeleted = 0 AND Type = 2;"; // Outbound
                    manifest.OutboundCount = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false));

                    cmd.CommandText = "SELECT COUNT(*) FROM Categories WHERE IsActive = 1;";
                    manifest.CategoriesCount = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false));

                    cmd.CommandText = "SELECT COUNT(*) FROM CorrespondentEntities WHERE IsActive = 1;";
                    manifest.CorrespondentEntitiesCount = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false));

                    cmd.CommandText = "SELECT COUNT(*) FROM Users;";
                    manifest.UsersCount = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false));

                    cmd.CommandText = "SELECT COUNT(*) FROM AuditLogs;";
                    manifest.AuditLogsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false));

                    cmd.CommandText = "SELECT COUNT(*) FROM Attachments;";
                    manifest.AttachmentsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false));
                }

                // Encrypt SQLite database inside the backup archive with AES-256
                progress?.Report((0.35, "تشفير وحماية قاعدة البيانات بتقنية AES-256..."));
                byte[] dbBytes = await File.ReadAllBytesAsync(stageDbPath).ConfigureAwait(false);
                byte[] encDbBytes;

                if (!string.IsNullOrWhiteSpace(password))
                {
                    byte[] salt = FileStorageService.GenerateRandomSalt(16);
                    byte[] key = FileStorageService.DeriveKeyFromPassword(password.Trim(), salt);
                    string verifier = FileStorageService.ComputeKeyVerifier(key, salt);
                    encDbBytes = FileStorageService.EncryptBytesWithKey(dbBytes, key);
                    manifest.IsEncryptedDatabase = true;
                    manifest.IsPasswordProtected = true;
                    manifest.PasswordSalt = Convert.ToHexString(salt);
                    manifest.PasswordVerifier = verifier;
                }
                else
                {
                    encDbBytes = FileStorageService.EncryptBytes(dbBytes);
                    manifest.IsEncryptedDatabase = true;
                    manifest.IsPasswordProtected = false;
                    manifest.PasswordSalt = null;
                    manifest.PasswordVerifier = null;
                }

                await File.WriteAllBytesAsync(stageDbPath, encDbBytes).ConfigureAwait(false);
                manifest.DatabaseSha256 = ComputeFileSha256(stageDbPath);

                // Bundle custom report organization logo if configured
                string? stageLogoPath = null;
                if (!string.IsNullOrWhiteSpace(settings.OrganizationLogoPath) && File.Exists(settings.OrganizationLogoPath))
                {
                    string stageBrandingDir = Path.Combine(stageDir, "Branding");
                    Directory.CreateDirectory(stageBrandingDir);
                    string ext = Path.GetExtension(settings.OrganizationLogoPath);
                    if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
                    stageLogoPath = Path.Combine(stageBrandingDir, $"org_logo{ext}");
                    File.Copy(settings.OrganizationLogoPath, stageLogoPath, overwrite: true);
                    manifest.LogoRelativePath = $"Branding/org_logo{ext}";
                    manifest.FileChecksums[manifest.LogoRelativePath] = ComputeFileSha256(stageLogoPath);
                }

                // Compute SHA-256 Checksums for all vault attachments
                progress?.Report((0.45, "حساب بصمات السلامة الرقمية SHA-256 للمرفقات..."));
                for (int i = 0; i < filesToInclude.Count; i++)
                {
                    var item = filesToInclude[i];
                    manifest.FileChecksums[item.EntryRelativePath] = ComputeFileSha256(item.SourcePath);
                    if (i % 25 == 0 || i == filesToInclude.Count - 1)
                    {
                        double p = 0.45 + (0.25 * ((double)(i + 1) / Math.Max(1, filesToInclude.Count)));
                        progress?.Report((p, $"حساب بصمات المرفقات ({i + 1}/{filesToInclude.Count})..."));
                    }
                }

                // Write manifest JSON & README info file
                progress?.Report((0.72, "تجهيز ملفات الفهرس ودليل الاستعادة..."));
                string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                string manifestPath = Path.Combine(stageDir, "backup-manifest.json");
                await File.WriteAllTextAsync(manifestPath, manifestJson, Encoding.UTF8).ConfigureAwait(false);

                string readmePath = Path.Combine(stageDir, "Backup_Info_ReadMe.txt");
                await File.WriteAllTextAsync(readmePath, GenerateReadme(manifest), Encoding.UTF8).ConfigureAwait(false);

                // Stream directly to final ZIP file avoiding double-copy of vault files
                progress?.Report((0.78, "ضغط وتجميع حزمة النسخة الاحتياطية الشاملة (.zip)..."));
                string zipName = $"DocuArchivePro_FullBackup_{exportTime:yyyyMMdd_HHmmss}.zip";
                string zipPath = Path.Combine(destinationFolder, zipName);
                if (File.Exists(zipPath)) File.Delete(zipPath);

                using (var zipFs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                using (var archive = new ZipArchive(zipFs, ZipArchiveMode.Create))
                {
                    // Add encrypted SQLite database, manifest, and readme
                    archive.CreateEntryFromFile(stageDbPath, "DocuArchive.db", CompressionLevel.Optimal);
                    archive.CreateEntryFromFile(manifestPath, "backup-manifest.json", CompressionLevel.Optimal);
                    archive.CreateEntryFromFile(readmePath, "Backup_Info_ReadMe.txt", CompressionLevel.Optimal);

                    if (!string.IsNullOrEmpty(stageLogoPath) && File.Exists(stageLogoPath))
                    {
                        archive.CreateEntryFromFile(stageLogoPath, manifest.LogoRelativePath, CompressionLevel.Optimal);
                    }

                    // Add vault files with optimized compression (.denc is already encrypted, use Fastest)
                    for (int i = 0; i < filesToInclude.Count; i++)
                    {
                        var item = filesToInclude[i];
                        var compLevel = item.SourcePath.EndsWith(".denc", StringComparison.OrdinalIgnoreCase)
                            ? CompressionLevel.Fastest
                            : CompressionLevel.Optimal;

                        archive.CreateEntryFromFile(item.SourcePath, item.EntryRelativePath, compLevel);

                        if (i % 25 == 0 || i == filesToInclude.Count - 1)
                        {
                            double p = 0.80 + (0.18 * ((double)(i + 1) / Math.Max(1, filesToInclude.Count)));
                            progress?.Report((p, $"ضغط وحفظ المرفقات في الأرشيف ({i + 1}/{filesToInclude.Count})..."));
                        }
                    }
                }

                // Update settings ONLY AFTER the ZIP file has been created and closed successfully
                var liveSettings = await SecurityService.Instance.GetSettingsAsync().ConfigureAwait(false);
                liveSettings.LastFullBackupAt = exportTime;
                liveSettings.AutoBackupLastStatus = "Success";
                liveSettings.AutoBackupLastErrorMessage = string.Empty;
                await SecurityService.Instance.SaveSettingsAsync(liveSettings).ConfigureAwait(false);

                // Auto-prune older backups according to retention policy
                int retention = liveSettings.AutoBackupRetentionCount > 0 ? liveSettings.AutoBackupRetentionCount : 7;
                await PruneOldBackupsAsync(destinationFolder, retention).ConfigureAwait(false);

                await LogAuditAsync("نسخ احتياطي شامل / Full System Backup", $"{zipPath} (Docs: {manifest.ActiveDocumentsCount}, Files: {manifest.AttachmentsCount})").ConfigureAwait(false);
                progress?.Report((1.0, "تم إنشاء النسخة الاحتياطية بنجاح!"));
                return zipPath;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(stageDir))
                    {
                        Directory.Delete(stageDir, recursive: true);
                    }
                }
                catch { }
            }
        }

        public async Task<FullBackupInspectionResult> InspectFullSystemBackupZipAsync(string zipFilePath, string? password = null)
        {
            if (string.IsNullOrWhiteSpace(zipFilePath) || !File.Exists(zipFilePath))
            {
                throw new FileNotFoundException("Backup zip file not found.", zipFilePath);
            }

            var fi = new FileInfo(zipFilePath);
            var result = new FullBackupInspectionResult
            {
                ZipFilePath = zipFilePath,
                ZipSizeBytes = fi.Length,
                ChecksumsValid = true
            };

            using var archive = ZipFile.OpenRead(zipFilePath);
            var dbEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals("DocuArchive.db", StringComparison.OrdinalIgnoreCase));
            if (dbEntry == null)
            {
                throw new InvalidDataException("Invalid full backup archive: DocuArchive.db not found.");
            }

            var manifestEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals("backup-manifest.json", StringComparison.OrdinalIgnoreCase));
            if (manifestEntry != null)
            {
                result.HasManifest = true;
                using var ms = manifestEntry.Open();
                using var sr = new StreamReader(ms, Encoding.UTF8);
                string json = await sr.ReadToEndAsync().ConfigureAwait(false);
                var manifest = JsonSerializer.Deserialize<FullBackupManifest>(json);
                if (manifest != null)
                {
                    result.Manifest = manifest;

                    if (manifest.IsPasswordProtected)
                    {
                        result.RequiresPassword = true;
                        if (string.IsNullOrWhiteSpace(password))
                        {
                            result.PasswordVerified = false;
                        }
                        else
                        {
                            try
                            {
                                byte[] salt = Convert.FromHexString(manifest.PasswordSalt ?? string.Empty);
                                byte[] key = FileStorageService.DeriveKeyFromPassword(password.Trim(), salt);
                                string verifier = FileStorageService.ComputeKeyVerifier(key, salt);
                                result.PasswordVerified = string.Equals(verifier, manifest.PasswordVerifier, StringComparison.OrdinalIgnoreCase);
                                if (!result.PasswordVerified)
                                {
                                    result.ChecksumErrors.Add("كلمة مرور النسخة الاحتياطية غير صحيحة / Incorrect backup password");
                                    result.ChecksumsValid = false;
                                }
                            }
                            catch
                            {
                                result.PasswordVerified = false;
                                result.ChecksumsValid = false;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(manifest.DatabaseSha256))
                    {
                        using var dbs = dbEntry.Open();
                        string actualDbHash = ComputeStreamSha256(dbs);
                        if (!string.Equals(actualDbHash, manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            result.ChecksumErrors.Add("DocuArchive.db");
                        }
                    }

                    foreach (var kv in manifest.FileChecksums)
                    {
                        string expectedRel = kv.Key.Replace('\\', '/');
                        var targetEntry = archive.Entries.FirstOrDefault(e =>
                            e.FullName.Replace('\\', '/').Equals(expectedRel, StringComparison.OrdinalIgnoreCase));
                        if (targetEntry == null)
                        {
                            result.ChecksumErrors.Add($"{expectedRel} (missing)");
                            continue;
                        }

                        using var es = targetEntry.Open();
                        string actualHash = ComputeStreamSha256(es);
                        if (!string.Equals(actualHash, kv.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            result.ChecksumErrors.Add(expectedRel);
                        }
                    }

                    result.ChecksumsValid = result.ChecksumErrors.Count == 0;
                }
            }
            else
            {
                // Legacy v1.0 ZIP backup inspection fallback
                string tempDb = Path.Combine(Path.GetTempPath(), $"inspect_{Guid.NewGuid():N}.db");
                try
                {
                    dbEntry.ExtractToFile(tempDb, overwrite: true);
                    byte[] dbRaw = await File.ReadAllBytesAsync(tempDb).ConfigureAwait(false);
                    if (FileStorageService.IsEncryptedPayload(dbRaw))
                    {
                        byte[] decrypted = FileStorageService.DecryptBytes(dbRaw);
                        await File.WriteAllBytesAsync(tempDb, decrypted).ConfigureAwait(false);
                    }

                    using var conn = new SqliteConnection($"Data Source={tempDb};Mode=ReadOnly");
                    await conn.OpenAsync().ConfigureAwait(false);

                    result.Manifest.FormatVersion = "1.0";
                    result.Manifest.ExportedAt = fi.LastWriteTime;

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM Documents WHERE IsDeleted = 0;";
                    try { result.Manifest.ActiveDocumentsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false)); } catch { }

                    cmd.CommandText = "SELECT COUNT(*) FROM Attachments;";
                    try { result.Manifest.AttachmentsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false)); } catch { }

                    cmd.CommandText = "SELECT COUNT(*) FROM Users;";
                    try { result.Manifest.UsersCount = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false)); } catch { }

                    cmd.CommandText = "SELECT COUNT(*) FROM Categories WHERE IsActive = 1;";
                    try { result.Manifest.CategoriesCount = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false)); } catch { }
                }
                finally
                {
                    SqliteConnection.ClearAllPools();
                    try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
                }
            }

            return result;
        }

        public async Task<(int RestoredDocs, int RestoredFiles)> RestoreFullSystemBackupZipAsync(
            string zipFilePath,
            string? password = null,
            IProgress<(double Progress, string Status)>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(zipFilePath) || !File.Exists(zipFilePath))
            {
                throw new FileNotFoundException("Backup zip file not found.", zipFilePath);
            }

            // Security check: Only System Administrator is authorized to restore a full database backup
            var currentUser = SecurityService.Instance.CurrentUser;
            if (currentUser != null && !currentUser.IsAdmin)
            {
                throw new UnauthorizedAccessException("استعادة النسخة الاحتياطية الشاملة متاحة لمدير النظام (SuperUser) فقط لأسباب أمنية.");
            }

            progress?.Report((0.05, "فحص ملف النسخة الاحتياطية والتحقق من سلامة الأرشيف..."));
            string extractDir = Path.Combine(Path.GetTempPath(), $"DocuArchive_Restore_{Guid.NewGuid():N}");
            Directory.CreateDirectory(extractDir);
            string fullExtractDir = Path.GetFullPath(extractDir);

            string targetDb = ArchiveDbContext.DatabasePath;
            string safetyBakPath = targetDb + ".pre_restore.bak";
            bool liveDbBackedUp = false;

            try
            {
                // Safe extraction with strict Zip Slip protection
                using (var archive = ZipFile.OpenRead(zipFilePath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        string destinationPath = Path.GetFullPath(Path.Combine(extractDir, entry.FullName));
                        if (!destinationPath.StartsWith(fullExtractDir, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException($"Entry is outside target directory (Zip Slip detected): {entry.FullName}");
                        }

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destinationPath);
                            continue;
                        }

                        string? dir = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        entry.ExtractToFile(destinationPath, overwrite: true);
                    }
                }

                string extractedDb = Path.Combine(extractDir, "DocuArchive.db");
                if (!File.Exists(extractedDb))
                {
                    string? foundDb = Directory.GetFiles(extractDir, "DocuArchive.db", SearchOption.AllDirectories).FirstOrDefault();
                    if (foundDb == null)
                    {
                        throw new InvalidDataException("Invalid full backup archive: DocuArchive.db not found.");
                    }
                    extractedDb = foundDb;
                }

                string extractedRoot = Path.GetDirectoryName(extractedDb)!;

                // 1. Verify SHA-256 checksums BEFORE modifying live database or vault
                progress?.Report((0.20, "التحقق من بصمات السلامة الرقمية SHA-256 لجميع الملفات..."));
                FullBackupManifest? manifest = null;
                string manifestPath = Path.Combine(extractedRoot, "backup-manifest.json");
                if (File.Exists(manifestPath))
                {
                    string mJson = await File.ReadAllTextAsync(manifestPath, Encoding.UTF8).ConfigureAwait(false);
                    manifest = JsonSerializer.Deserialize<FullBackupManifest>(mJson);
                    if (manifest != null)
                    {
                        if (!string.IsNullOrEmpty(manifest.DatabaseSha256))
                        {
                            string actualDbHash = ComputeFileSha256(extractedDb);
                            if (!string.Equals(actualDbHash, manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidDataException("فشل فحص السلامة: بصمة قاعدة البيانات SHA-256 غير متطابقة.");
                            }
                        }

                        foreach (var kv in manifest.FileChecksums)
                        {
                            string relNative = kv.Key.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                            string filePath = Path.GetFullPath(Path.Combine(extractedRoot, relNative));
                            if (!filePath.StartsWith(Path.GetFullPath(extractedRoot), StringComparison.OrdinalIgnoreCase) || !File.Exists(filePath))
                            {
                                throw new InvalidDataException($"فشل فحص السلامة: الملف '{kv.Key}' مفقود من حزمة النسخة الاحتياطية.");
                            }
                            string actualHash = ComputeFileSha256(filePath);
                            if (!string.Equals(actualHash, kv.Value, StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidDataException($"فشل فحص السلامة: تطابق بصمة SHA-256 للملف '{kv.Key}' غير سليم أو تالف.");
                            }
                        }
                    }
                }

                // 2. Decrypt database if protected
                progress?.Report((0.35, "فك تشفير وفحص سلامة قاعدة البيانات..."));
                byte[] dbRawBytes = await File.ReadAllBytesAsync(extractedDb).ConfigureAwait(false);
                byte[] plainDbBytes;

                if (manifest != null && manifest.IsPasswordProtected)
                {
                    if (string.IsNullOrWhiteSpace(password))
                    {
                        throw new InvalidOperationException("هذه النسخة الاحتياطية محمية بكلمة مرور. يرجى إدخال كلمة المرور للاستعادة.");
                    }

                    byte[] salt = Convert.FromHexString(manifest.PasswordSalt ?? string.Empty);
                    byte[] key = FileStorageService.DeriveKeyFromPassword(password.Trim(), salt);
                    string verifier = FileStorageService.ComputeKeyVerifier(key, salt);
                    if (!string.Equals(verifier, manifest.PasswordVerifier, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new UnauthorizedAccessException("كلمة مرور النسخة الاحتياطية غير صحيحة.");
                    }

                    plainDbBytes = FileStorageService.DecryptBytesWithKey(dbRawBytes, key);
                    await File.WriteAllBytesAsync(extractedDb, plainDbBytes).ConfigureAwait(false);
                }
                else if (FileStorageService.IsEncryptedPayload(dbRawBytes))
                {
                    plainDbBytes = FileStorageService.DecryptBytes(dbRawBytes);
                    await File.WriteAllBytesAsync(extractedDb, plainDbBytes).ConfigureAwait(false);
                }

                // 3. Verify SQLite database structural integrity BEFORE touching live database
                try
                {
                    using var verifyConn = new SqliteConnection($"Data Source={extractedDb};Mode=ReadOnly");
                    await verifyConn.OpenAsync().ConfigureAwait(false);
                    using var checkCmd = verifyConn.CreateCommand();
                    checkCmd.CommandText = "PRAGMA quick_check;";
                    string? checkRes = Convert.ToString(await checkCmd.ExecuteScalarAsync().ConfigureAwait(false));
                    if (!string.Equals(checkRes, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"SQLite integrity check failed: {checkRes}");
                    }
                }
                finally
                {
                    SqliteConnection.ClearAllPools();
                }

                // 4. Resolve safe writable target Vault on the current machine
                string targetVault;
                try
                {
                    targetVault = _fileStorageService.StorageRootDirectory;
                }
                catch
                {
                    targetVault = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "DocuArchivePro",
                        "Vault");
                }
                if (!Directory.Exists(targetVault))
                {
                    Directory.CreateDirectory(targetVault);
                }

                // Check free disk space in target vault drive
                EnsureSufficientDiskSpace(targetVault, manifest?.TotalVaultSizeBytes ?? (new FileInfo(zipFilePath).Length * 2));

                // 5. Create a pre-restore safety snapshot of current live DB for automatic rollback
                if (File.Exists(targetDb))
                {
                    try
                    {
                        using (var liveDb = new ArchiveDbContext())
                        {
                            await liveDb.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(FULL);").ConfigureAwait(false);
                        }
                        SqliteConnection.ClearAllPools();
                        File.Copy(targetDb, safetyBakPath, overwrite: true);
                        liveDbBackedUp = true;
                    }
                    catch { }
                }

                // 6. Copy encrypted vault files to target machine's Vault folder
                progress?.Report((0.55, "استعادة ونقل المرفقات المشفرة إلى المستودع..."));
                string extractedVault = Path.Combine(extractedRoot, "Vault");
                int copiedFiles = 0;
                if (Directory.Exists(extractedVault))
                {
                    var filesToCopy = Directory.GetFiles(extractedVault, "*.*", SearchOption.AllDirectories);
                    for (int i = 0; i < filesToCopy.Length; i++)
                    {
                        string srcFile = filesToCopy[i];
                        string rel = Path.GetRelativePath(extractedVault, srcFile);
                        string destFile = Path.Combine(targetVault, rel);
                        string? destSub = Path.GetDirectoryName(destFile);
                        if (!string.IsNullOrEmpty(destSub) && !Directory.Exists(destSub))
                        {
                            Directory.CreateDirectory(destSub);
                        }
                        File.Copy(srcFile, destFile, overwrite: true);
                        copiedFiles++;

                        if (i % 25 == 0 || i == filesToCopy.Length - 1)
                        {
                            double p = 0.55 + (0.25 * ((double)(i + 1) / Math.Max(1, filesToCopy.Length)));
                            progress?.Report((p, $"نسخ ملفات المستودع ({i + 1}/{filesToCopy.Length})..."));
                        }
                    }
                }

                // 7. Restore custom organization branding logo to local AppData folder on target machine
                string restoredLogoPath = string.Empty;
                string extractedBrandingDir = Path.Combine(extractedRoot, "Branding");
                if (Directory.Exists(extractedBrandingDir))
                {
                    string? logoFile = Directory.GetFiles(extractedBrandingDir, "*.*").FirstOrDefault();
                    if (logoFile != null)
                    {
                        string targetBrandingDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "DocuArchivePro",
                            "Branding");
                        if (!Directory.Exists(targetBrandingDir)) Directory.CreateDirectory(targetBrandingDir);
                        restoredLogoPath = Path.Combine(targetBrandingDir, Path.GetFileName(logoFile));
                        File.Copy(logoFile, restoredLogoPath, overwrite: true);
                    }
                }

                // 8. Replace live database cleanly with automatic rollback on error
                progress?.Report((0.85, "تطبيق قاعدة البيانات المستعادة وتحديث الهيكل..."));
                try
                {
                    SqliteConnection.ClearAllPools();
                    foreach (string walExt in new[] { "-wal", "-shm" })
                    {
                        string sideFile = targetDb + walExt;
                        if (File.Exists(sideFile))
                        {
                            try { File.Delete(sideFile); } catch { }
                        }
                    }

                    File.Copy(extractedDb, targetDb, overwrite: true);
                    await DatabaseInitializer.InitializeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Automatic Rollback to safety snapshot
                    if (liveDbBackedUp && File.Exists(safetyBakPath))
                    {
                        try
                        {
                            SqliteConnection.ClearAllPools();
                            File.Copy(safetyBakPath, targetDb, overwrite: true);
                        }
                        catch { }
                    }
                    throw new InvalidOperationException($"فشل تطبيق قاعدة البيانات المستعادة. تم التراجع التلقائي إلى الحالة السابقة: {ex.Message}", ex);
                }

                // 9. Re-bind machine-specific paths and scanner hardware inside restored DB
                progress?.Report((0.92, "إعادة ربط المسارات وإعدادات النظام..."));
                int docsCount;
                using (var db = new ArchiveDbContext())
                {
                    var restoredSettings = await db.SystemSettings.FirstOrDefaultAsync().ConfigureAwait(false);
                    if (restoredSettings != null)
                    {
                        restoredSettings.VaultStoragePath = targetVault;

                        if (!string.IsNullOrEmpty(restoredLogoPath))
                        {
                            restoredSettings.OrganizationLogoPath = restoredLogoPath;
                        }
                        else if (!string.IsNullOrWhiteSpace(restoredSettings.OrganizationLogoPath) &&
                                 !File.Exists(restoredSettings.OrganizationLogoPath))
                        {
                            restoredSettings.OrganizationLogoPath = string.Empty;
                        }

                        if (!string.IsNullOrWhiteSpace(restoredSettings.AutoBackupDirectory))
                        {
                            string? root = Path.GetPathRoot(restoredSettings.AutoBackupDirectory);
                            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                            {
                                restoredSettings.AutoBackupDirectory = string.Empty;
                                restoredSettings.AutoBackupEnabled = false;
                            }
                        }

                        // Reset scanner device if restoring on a different machine to prevent hardware faults
                        if (manifest != null && !string.Equals(manifest.SourceMachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                        {
                            restoredSettings.DefaultScannerDeviceId = "WINDOWS_INTERACTIVE_WIA_DIALOG";
                            restoredSettings.DefaultScannerName = "نافذة اختيار السكانر القياسية في الويندوز (Windows WIA / TWAIN Dialog)";
                        }

                        restoredSettings.UpdatedAt = DateTime.Now;
                    }

                    // Fast O(1) in-memory index for attachments
                    var vaultFileIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (Directory.Exists(targetVault))
                    {
                        foreach (string f in Directory.EnumerateFiles(targetVault, "*.*", SearchOption.AllDirectories))
                        {
                            string fn = Path.GetFileName(f);
                            if (!vaultFileIndex.ContainsKey(fn))
                            {
                                vaultFileIndex[fn] = f;
                            }
                        }
                    }

                    var attachments = await db.Attachments.ToListAsync().ConfigureAwait(false);
                    foreach (var att in attachments)
                    {
                        string fileName = Path.GetFileName(att.StoragePath);
                        string canonicalPath = Path.Combine(targetVault, att.UploadedAt.ToString("yyyy"), att.UploadedAt.ToString("MM"), fileName);
                        if (File.Exists(canonicalPath))
                        {
                            att.StoragePath = canonicalPath;
                        }
                        else if (vaultFileIndex.TryGetValue(fileName, out var matchedPath))
                        {
                            att.StoragePath = matchedPath;
                        }
                    }

                    await db.SaveChangesAsync().ConfigureAwait(false);
                    docsCount = await db.Documents.CountAsync(d => !d.IsDeleted).ConfigureAwait(false);

                    // Sync current user session safely without unauthorized elevation
                    string currentUserName = SecurityService.Instance.CurrentUser?.Username ?? "SuperUser";
                    var matchedUser = await db.Users.FirstOrDefaultAsync(u => u.Username == currentUserName).ConfigureAwait(false);
                    if (matchedUser != null)
                    {
                        SecurityService.Instance.CurrentUser = matchedUser;
                    }
                    else
                    {
                        // Previous user does not exist in the restored database.
                        // Force clean re-authentication instead of silently elevating to admin!
                        SecurityService.Instance.CurrentUser = new AppUser
                        {
                            Username = string.Empty,
                            RoleTitle = "غير مسجل / Requires Login",
                            IsAdmin = false,
                            CanAdd = false,
                            CanEdit = false,
                            CanDelete = false,
                            CanScan = false,
                            CanPrint = false,
                            CanManageStorage = false,
                            CanManageEntities = false
                        };
                    }
                }

                _fileStorageService.InvalidateCache();

                await LogAuditAsync("استعادة نسخة احتياطية شاملة / Restore Full Backup", $"Source: {manifest?.SourceMachineName ?? "ZIP"}, Docs: {docsCount}, Files: {copiedFiles}").ConfigureAwait(false);
                progress?.Report((1.0, "تمت استعادة النظام وإعادة ربط المسارات بنجاح!"));
                return (docsCount, copiedFiles);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(extractDir))
                    {
                        Directory.Delete(extractDir, recursive: true);
                    }
                }
                catch { }
            }
        }

        public async Task<int> PruneOldBackupsAsync(string backupDirectory, int retentionCount)
        {
            if (retentionCount <= 0 || string.IsNullOrWhiteSpace(backupDirectory) || !Directory.Exists(backupDirectory))
            {
                return 0;
            }

            return await Task.Run(() =>
            {
                int deletedCount = 0;
                try
                {
                    var dir = new DirectoryInfo(backupDirectory);
                    var backupFiles = dir.GetFiles("DocuArchivePro_FullBackup_*.zip")
                                         .OrderByDescending(f => f.CreationTime)
                                         .ToList();

                    if (backupFiles.Count > retentionCount)
                    {
                        var filesToDelete = backupFiles.Skip(retentionCount);
                        foreach (var file in filesToDelete)
                        {
                            try
                            {
                                file.Delete();
                                deletedCount++;
                            }
                            catch { }
                        }
                    }
                }
                catch { }
                return deletedCount;
            }).ConfigureAwait(false);
        }

        private static void EnsureSufficientDiskSpace(string folderPath, long requiredBytes)
        {
            try
            {
                string? root = Path.GetPathRoot(Path.GetFullPath(folderPath));
                if (string.IsNullOrEmpty(root)) return;
                var drive = new DriveInfo(root);
                long requiredWithMargin = (long)(requiredBytes * 1.25) + (50 * 1024 * 1024);
                if (drive.AvailableFreeSpace < requiredWithMargin)
                {
                    double freeMb = drive.AvailableFreeSpace / (1024.0 * 1024.0);
                    double reqMb = requiredWithMargin / (1024.0 * 1024.0);
                    throw new InvalidOperationException(
                        $"المساحة المتوفرة على القرص ({drive.Name}) غير كافية.\nالمساحة المتوفرة: {freeMb:F1} م.ب، المساحة المطلوبة: {reqMb:F1} م.ب.");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                // Ignore if drive info is inaccessible
            }
        }

        private static string GenerateReadme(FullBackupManifest manifest)
        {
            var readme = new StringBuilder();
            readme.AppendLine("================================================================================");
            readme.AppendLine("DocuArchive Pro V1.5 - Portable Full System Backup / نسخة احتياطية شاملة محمولة");
            readme.AppendLine("================================================================================");
            readme.AppendLine($"Format Version / إصدار النسخة: {manifest.FormatVersion}");
            readme.AppendLine($"Created At / تاريخ الإنشاء: {manifest.ExportedAt:yyyy-MM-dd HH:mm:ss}");
            readme.AppendLine($"Source Machine / اسم الجهاز المصدر: {manifest.SourceMachineName} ({manifest.SourceWindowsUser})");
            readme.AppendLine($"Created By / تم الإنشاء بواسطة: {manifest.CreatedByAppUser}");
            readme.AppendLine($"Database Encryption / تشفير القاعدة: {(manifest.IsPasswordProtected ? "Protected with Custom Password (AES-256)" : "Protected with Master Key (AES-256)")}");
            readme.AppendLine($"Active Documents / المعاملات الفعالة: {manifest.ActiveDocumentsCount} (Inbound: {manifest.InboundCount}, Outbound: {manifest.OutboundCount})");
            readme.AppendLine($"Recycle Bin / سلة المحذوفات: {manifest.RecycleBinDocumentsCount}");
            readme.AppendLine($"Dossiers / الأضابير: {manifest.CategoriesCount}");
            readme.AppendLine($"Entities / الجهات المحفوظة: {manifest.CorrespondentEntitiesCount}");
            readme.AppendLine($"Users / المستخدمون: {manifest.UsersCount}");
            readme.AppendLine($"Encrypted Attachments / المرفقات المشفرة: {manifest.AttachmentsCount}");
            readme.AppendLine($"Database SHA-256: {manifest.DatabaseSha256}");
            readme.AppendLine();
            readme.AppendLine("How to Restore on Another PC / كيفية الاستعادة على جهاز آخر:");
            readme.AppendLine("1. Install and launch DocuArchive Pro on the target computer.");
            readme.AppendLine("2. On Welcome Screen click 'Restore Backup', or sign in as Admin -> Settings -> Full System Backup & Restore.");
            readme.AppendLine("3. Click 'Restore Full Backup (.zip)' and choose this ZIP file.");
            readme.AppendLine("4. If a custom password was set, enter it when prompted.");
            readme.AppendLine("5. All database records, users, encrypted files, and branding will be re-bound automatically.");
            return readme.ToString();
        }

        private static string ComputeFileSha256(string filePath)
        {
            using var fs = File.OpenRead(filePath);
            return ComputeStreamSha256(fs);
        }

        private static string ComputeStreamSha256(Stream stream)
        {
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
