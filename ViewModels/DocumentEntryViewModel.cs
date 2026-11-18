using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using DocuArchivePro.Data;
using DocuArchivePro.Models;
using DocuArchivePro.Services;
using DocuArchivePro.Views;

namespace DocuArchivePro.ViewModels
{
    public class DocumentEntryViewModel : ViewModelBase
    {
        private readonly IDocumentService _documentService;
        private readonly IScannerService _scannerService;
        private readonly ILocalizationService _loc;
        private readonly IFileStorageService _fileStorageService;

        public ILocalizationService Loc => _loc;

        private int _id;
        private string _registrationNumber = string.Empty;
        private DocumentType _type = DocumentType.Inbound;
        private DateTime _registrationDate = DateTime.Now;
        private DateTime _documentDate = DateTime.Now;
        private string _subject = string.Empty;
        private string _sender = string.Empty;
        private string _recipient = string.Empty;
        private int? _categoryId;
        private DocumentPriority _priority = DocumentPriority.Normal;
        private DocumentStatus _status = DocumentStatus.New;
        private string _deliveryMethod = LocalizationService.Instance.IsArabic ? "تسليم يدوي" : "Hand Delivery";
        private string _externalReferenceNumber = string.Empty;
        private DateTime? _followUpDate;
        private string _notes = string.Empty;
        private string _department = string.Empty;
        private string _tags = string.Empty;
        private string _relatedRegistrationNumber = string.Empty;
        private readonly string _initialRelatedRegistrationNumber = string.Empty;
        private readonly string _initialRegistrationNumber = string.Empty;

        public bool IsEditMode => _id > 0;
        public string DialogTitle => IsEditMode 
            ? $"{_loc["Edit"]} - {_registrationNumber}" 
            : _loc["NewRegistration"];

        public int Id { get => _id; set => SetField(ref _id, value); }
        public string RegistrationNumber { get => _registrationNumber; set => SetField(ref _registrationNumber, value); }
        public DocumentType Type
        {
            get => _type;
            set
            {
                if (SetField(ref _type, value))
                {
                    OnPropertyChanged(nameof(IsInbound));
                    OnPropertyChanged(nameof(IsOutbound));
                    UpdateAutoRegNumber();
                }
            }
        }

        public bool IsInbound
        {
            get => Type == DocumentType.Inbound;
            set { if (value) Type = DocumentType.Inbound; }
        }

        public bool IsOutbound
        {
            get => Type == DocumentType.Outbound;
            set { if (value) Type = DocumentType.Outbound; }
        }

        public DateTime RegistrationDate { get => _registrationDate; set => SetField(ref _registrationDate, value); }
        public DateTime DocumentDate { get => _documentDate; set => SetField(ref _documentDate, value); }
        public string Subject { get => _subject; set => SetField(ref _subject, value); }
        public string Sender
        {
            get => _sender;
            set
            {
                if (SetField(ref _sender, value))
                {
                    OnPropertyChanged(nameof(SelectedSenderEntityName));
                }
            }
        }
        public string Recipient
        {
            get => _recipient;
            set
            {
                if (SetField(ref _recipient, value))
                {
                    OnPropertyChanged(nameof(SelectedRecipientEntityName));
                }
            }
        }

        public bool CanManualEntityEntry => SecurityService.Instance.CurrentUser.IsAdmin || SecurityService.Instance.CurrentUser.CanManageEntities;
        public Visibility EntityLockIconVisibility => CanManualEntityEntry ? Visibility.Collapsed : Visibility.Visible;

        public string? SelectedSenderEntityName
        {
            get => string.IsNullOrWhiteSpace(_sender) ? null : SavedEntityNames.FirstOrDefault(n => string.Equals(n, _sender, StringComparison.Ordinal));
            set
            {
                if (value != null && !string.Equals(_sender, value, StringComparison.Ordinal))
                {
                    Sender = value;
                }
            }
        }

        public string? SelectedRecipientEntityName
        {
            get => string.IsNullOrWhiteSpace(_recipient) ? null : SavedEntityNames.FirstOrDefault(n => string.Equals(n, _recipient, StringComparison.Ordinal));
            set
            {
                if (value != null && !string.Equals(_recipient, value, StringComparison.Ordinal))
                {
                    Recipient = value;
                }
            }
        }
        public int? CategoryId { get => _categoryId; set => SetField(ref _categoryId, value); }
        public DocumentPriority Priority
        {
            get => _priority;
            set
            {
                if (SetField(ref _priority, value))
                {
                    OnPropertyChanged(nameof(PriorityIndex));
                }
            }
        }

        public int PriorityIndex
        {
            get => Math.Max(0, (int)Priority - 1);
            set => Priority = (DocumentPriority)(value + 1);
        }

        public DocumentStatus Status
        {
            get => _status;
            set
            {
                if (SetField(ref _status, value))
                {
                    OnPropertyChanged(nameof(StatusIndex));
                }
            }
        }

        public int StatusIndex
        {
            get => Math.Max(0, (int)Status - 1);
            set => Status = (DocumentStatus)(value + 1);
        }
        public string DeliveryMethod { get => _deliveryMethod; set => SetField(ref _deliveryMethod, value); }
        public string ExternalReferenceNumber { get => _externalReferenceNumber; set => SetField(ref _externalReferenceNumber, value); }
        public DateTime? FollowUpDate { get => _followUpDate; set => SetField(ref _followUpDate, value); }
        public string Notes { get => _notes; set => SetField(ref _notes, value); }
        public string Department { get => _department; set => SetField(ref _department, value); }
        public string Tags { get => _tags; set => SetField(ref _tags, value); }
        public string RelatedRegistrationNumber { get => _relatedRegistrationNumber; set => SetField(ref _relatedRegistrationNumber, value); }

        public IDocumentService DocumentService => _documentService;

        public ObservableCollection<Category> Categories { get; } = new();
        public ObservableCollection<CorrespondentEntity> CorrespondentEntities { get; } = new();
        public ObservableCollection<string> SavedEntityNames { get; } = new();
        public ObservableCollection<string> ExistingRegistrationNumbers { get; } = new();
        public ObservableCollection<DocumentAttachment> ExistingAttachments { get; } = new();
        public ObservableCollection<string> PendingFilePaths { get; } = new();
        private readonly HashSet<string> _tempScanFiles = new(StringComparer.OrdinalIgnoreCase);

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand BrowseFileCommand { get; }
        public ICommand ScanFileCommand { get; }
        public ICommand PreviewPendingFileCommand { get; }
        public ICommand RemovePendingFileCommand { get; }
        public ICommand DeleteExistingAttachmentCommand { get; }
        public ICommand OpenAttachmentCommand { get; }
        public ICommand AddNewCategoryCommand { get; }
        public ICommand EditSelectedCategoryCommand { get; }
        public ICommand AddSenderEntityCommand { get; }
        public ICommand AddRecipientEntityCommand { get; }

        public event Action<bool>? RequestClose;
        public event Action? RequestOpenAddCategoryDialog;
        public event Action<Category>? RequestOpenEditCategoryDialog;
        public event Action<string, string>? RequestOpenAddEntityDialog;

        public DocumentEntryViewModel(
            IDocumentService documentService, 
            IScannerService scannerService, 
            IFileStorageService fileStorageService,
            ILocalizationService localizationService,
            Document? existingDocument = null,
            DocumentType initialType = DocumentType.Inbound)
        {
            _documentService = documentService;
            _scannerService = scannerService;
            _fileStorageService = fileStorageService;
            _loc = localizationService;

            SaveCommand = new RelayCommand(async () => await SaveAsync());
            CancelCommand = new RelayCommand(() =>
            {
                CleanupTempScanFiles();
                RequestClose?.Invoke(false);
            });
            BrowseFileCommand = new RelayCommand(BrowseFile);
            ScanFileCommand = new RelayCommand(async () => await ScanDocumentAsync());
            PreviewPendingFileCommand = new RelayCommand(p => PreviewPendingFile(p as string));
            RemovePendingFileCommand = new RelayCommand(p =>
            {
                if (p is string s)
                {
                    PendingFilePaths.Remove(s);
                    if (_tempScanFiles.Remove(s))
                    {
                        try { if (File.Exists(s)) File.Delete(s); } catch { }
                    }
                }
            });
            DeleteExistingAttachmentCommand = new RelayCommand(async p => { if (p is DocumentAttachment att) await DeleteExistingAttachmentAsync(att); });
            OpenAttachmentCommand = new RelayCommand(p =>
            {
                if (p is DocumentAttachment att)
                {
                    if (ExistingAttachments.Count > 1)
                    {
                        var list = ExistingAttachments.ToList();
                        int idx = list.IndexOf(att);
                        _fileStorageService.OpenAttachmentsInApp(list, idx >= 0 ? idx : 0);
                    }
                    else
                    {
                        _fileStorageService.OpenAttachmentInApp(att);
                    }
                }
            });
            AddNewCategoryCommand = new RelayCommand(() =>
            {
                var user = SecurityService.Instance.CurrentUser;
                if (!user.IsAdmin && !user.CanAdd && !user.CanEdit)
                {
                    MessageBox.Show(
                        _loc.IsArabic
                            ? "عذراً، المستخدم الحالي لا يملك صلاحية إضافة الأضابير والتصنيفات."
                            : "Sorry, the current user does not have permission to add dossiers.",
                        _loc.IsArabic ? "صلاحيات المستخدم" : "User Permissions",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                RequestOpenAddCategoryDialog?.Invoke();
            });
            AddSenderEntityCommand = new RelayCommand(() => RequestOpenAddEntityDialog?.Invoke("Sender", Sender));
            AddRecipientEntityCommand = new RelayCommand(() => RequestOpenAddEntityDialog?.Invoke("Recipient", Recipient));
            EditSelectedCategoryCommand = new RelayCommand(() =>
            {
                var user = SecurityService.Instance.CurrentUser;
                if (!user.IsAdmin && !user.CanEdit)
                {
                    MessageBox.Show(
                        _loc.IsArabic
                            ? "عذراً، المستخدم الحالي لا يملك صلاحية تعديل الأضابير والتصنيفات."
                            : "Sorry, the current user does not have permission to edit dossiers.",
                        _loc.IsArabic ? "صلاحيات المستخدم" : "User Permissions",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var selectedCat = Categories.FirstOrDefault(c => c.Id == CategoryId);
                if (selectedCat == null)
                {
                    MessageBox.Show(
                        _loc.IsArabic ? "يرجى اختيار إضبارة من القائمة لتعديلها." : "Please select a dossier from the list to edit.",
                        _loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                RequestOpenEditCategoryDialog?.Invoke(selectedCat);
            });

            LoadCategories();
            LoadEntities();
            LoadExistingRegistrationNumbers(existingDocument?.RegistrationNumber);

            if (existingDocument != null)
            {
                Id = existingDocument.Id;
                RegistrationNumber = existingDocument.RegistrationNumber;
                Type = existingDocument.Type;
                RegistrationDate = existingDocument.RegistrationDate;
                DocumentDate = existingDocument.DocumentDate;
                Subject = existingDocument.Subject;
                Sender = existingDocument.Sender;
                Recipient = existingDocument.Recipient;
                CategoryId = existingDocument.CategoryId;
                Priority = existingDocument.Priority;
                Status = existingDocument.Status;
                DeliveryMethod = existingDocument.DeliveryMethod;
                ExternalReferenceNumber = existingDocument.ExternalReferenceNumber;
                FollowUpDate = existingDocument.FollowUpDate;
                Notes = existingDocument.Notes;
                Department = existingDocument.Department;
                Tags = existingDocument.Tags;
                RelatedRegistrationNumber = existingDocument.RelatedRegistrationNumber;
                _initialRelatedRegistrationNumber = existingDocument.RelatedRegistrationNumber?.Trim() ?? string.Empty;
                _initialRegistrationNumber = existingDocument.RegistrationNumber?.Trim() ?? string.Empty;

                if (existingDocument.Attachments != null)
                {
                    foreach (var a in existingDocument.Attachments) ExistingAttachments.Add(a);
                }
            }
            else
            {
                Type = initialType;
                UpdateAutoRegNumber();
            }
        }

        private async void LoadCategories()
        {
            var cats = await _documentService.GetCategoriesAsync();
            Categories.Clear();
            foreach (var c in cats) Categories.Add(c);
            if (!CategoryId.HasValue && Categories.Count > 0)
            {
                CategoryId = Categories[0].Id;
            }
        }

        public async Task AddAndSelectCategoryAsync(Category newCategory)
        {
            var cats = await _documentService.GetCategoriesAsync();
            Categories.Clear();
            foreach (var c in cats) Categories.Add(c);
            CategoryId = newCategory.Id;
        }

        private async void LoadEntities()
        {
            await RefreshSavedEntitiesAsync();
        }

        private async void LoadExistingRegistrationNumbers(string? excludeRegNumber)
        {
            try
            {
                var allDocs = await _documentService.GetAllDocumentsAsync();
                ExistingRegistrationNumbers.Clear();
                foreach (var d in allDocs)
                {
                    if (!string.IsNullOrWhiteSpace(d.RegistrationNumber) &&
                        !string.Equals(d.RegistrationNumber, excludeRegNumber, StringComparison.OrdinalIgnoreCase) &&
                        !ExistingRegistrationNumbers.Contains(d.RegistrationNumber))
                    {
                        ExistingRegistrationNumbers.Add(d.RegistrationNumber);
                    }
                }
            }
            catch { }
        }

        public async Task RefreshSavedEntitiesAsync()
        {
            string preservedSender = _sender;
            string preservedRecipient = _recipient;

            var entities = await _documentService.GetCorrespondentEntitiesAsync();
            CorrespondentEntities.Clear();
            SavedEntityNames.Clear();

            foreach (var e in entities)
            {
                CorrespondentEntities.Add(e);
                string display = e.DisplayName;
                if (!string.IsNullOrWhiteSpace(display) && !SavedEntityNames.Contains(display))
                {
                    SavedEntityNames.Add(display);
                }
            }

            if (!string.IsNullOrWhiteSpace(preservedSender) && !SavedEntityNames.Contains(preservedSender))
            {
                SavedEntityNames.Add(preservedSender);
            }
            if (!string.IsNullOrWhiteSpace(preservedRecipient) && !SavedEntityNames.Contains(preservedRecipient))
            {
                SavedEntityNames.Add(preservedRecipient);
            }

            _sender = preservedSender;
            _recipient = preservedRecipient;
            OnPropertyChanged(nameof(Sender));
            OnPropertyChanged(nameof(Recipient));
            OnPropertyChanged(nameof(SelectedSenderEntityName));
            OnPropertyChanged(nameof(SelectedRecipientEntityName));
        }

        public async Task AddAndSelectEntityAsync(CorrespondentEntity newEntity, string targetField)
        {
            string selectedName = newEntity.DisplayName;
            if (string.Equals(targetField, "Recipient", StringComparison.OrdinalIgnoreCase))
            {
                Recipient = selectedName;
            }
            else
            {
                Sender = selectedName;
            }

            await RefreshSavedEntitiesAsync();
        }

        private async void UpdateAutoRegNumber()
        {
            if (!IsEditMode)
            {
                RegistrationNumber = await _documentService.GenerateNextRegistrationNumberAsync(Type);
            }
        }

        private static readonly HashSet<string> SupportedFolderDropExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".docx", ".doc", ".xlsx", ".xls", ".rtf", ".txt", ".csv"
        };

        public int AddDroppedPaths(IEnumerable<string>? paths)
        {
            if (paths == null) return 0;
            int added = 0;

            foreach (var rawPath in paths)
            {
                if (string.IsNullOrWhiteSpace(rawPath)) continue;

                if (Directory.Exists(rawPath))
                {
                    try
                    {
                        foreach (var nestedFile in Directory.EnumerateFiles(rawPath, "*.*", SearchOption.AllDirectories))
                        {
                            string ext = Path.GetExtension(nestedFile);
                            if (SupportedFolderDropExtensions.Contains(ext))
                            {
                                var fi = new FileInfo(nestedFile);
                                if (fi.Exists && fi.Length > 0 && !PendingFilePaths.Contains(nestedFile))
                                {
                                    PendingFilePaths.Add(nestedFile);
                                    added++;
                                }
                            }
                        }
                    }
                    catch { }
                }
                else if (File.Exists(rawPath))
                {
                    try
                    {
                        var fi = new FileInfo(rawPath);
                        if (fi.Exists && fi.Length > 0 && !PendingFilePaths.Contains(rawPath))
                        {
                            PendingFilePaths.Add(rawPath);
                            added++;
                        }
                    }
                    catch { }
                }
            }

            return added;
        }

        private void BrowseFile()
        {
            var dlg = new OpenFileDialog
            {
                Title = _loc["SelectFile"],
                Filter = "All Supported Files (*.pdf;*.jpg;*.png;*.docx;*.xlsx)|*.pdf;*.jpg;*.jpeg;*.png;*.docx;*.xlsx;*.tiff|PDF Documents (*.pdf)|*.pdf|Images (*.jpg;*.png)|*.jpg;*.jpeg;*.png|Office Docs (*.docx;*.xlsx)|*.docx;*.xlsx|All Files (*.*)|*.*",
                Multiselect = true
            };

            if (dlg.ShowDialog() == true)
            {
                AddDroppedPaths(dlg.FileNames);
            }
        }

        public void TrackTempScanFile(string filePath)
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                _tempScanFiles.Add(filePath);
            }
        }

        public void CleanupTempScanFiles()
        {
            foreach (string path in _tempScanFiles.ToList())
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch
                {
                }
            }
            _tempScanFiles.Clear();
        }

        private async Task ScanDocumentAsync()
        {
            var user = SecurityService.Instance.CurrentUser;
            if (!user.IsAdmin && !user.CanScan)
            {
                MessageBox.Show(
                    _loc.IsArabic ? "عذراً، المستخدم الحالي لا يملك صلاحية استخدام الماسح الضوئي." : "Sorry, the current user does not have scanner permission.",
                    _loc.IsArabic ? "صلاحيات المستخدم" : "User Permissions",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var dlg = new ScannerDialog(_scannerService)
                {
                    Owner = Application.Current.MainWindow
                };

                if (dlg.ShowDialog() == true && dlg.ScannedResultBytes != null)
                {
                    bool isPdf = dlg.ScannedResultBytes.Length >= 4 &&
                                 dlg.ScannedResultBytes[0] == 0x25 &&
                                 dlg.ScannedResultBytes[1] == 0x50 &&
                                 dlg.ScannedResultBytes[2] == 0x44 &&
                                 dlg.ScannedResultBytes[3] == 0x46;
                    string ext = isPdf ? ".pdf" : ".png";
                    string tempPath = Path.Combine(Path.GetTempPath(), $"Scan_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{ext}");
                    await File.WriteAllBytesAsync(tempPath, dlg.ScannedResultBytes);

                    TrackTempScanFile(tempPath);
                    PendingFilePaths.Add(tempPath);
                    MessageBox.Show(_loc["ScanComplete"], _loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{_loc["Error"]}: {ex.Message}", _loc["Error"], MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void PreviewPendingFile(string? filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    MessageBox.Show(
                        _loc.IsArabic ? "الملف المحدد غير موجود على الجهاز." : "The selected file could not be found.",
                        _loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                byte[] rawBytes = File.ReadAllBytes(filePath);
                byte[] decrypted = FileStorageService.DecryptBytes(rawBytes);

                var viewer = new AttachmentViewerDialog(decrypted, Path.GetFileName(filePath))
                {
                    Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow
                };
                viewer.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    _loc.IsArabic ? $"تعذر استعراض الملف:\n{ex.Message}" : $"Failed to preview file:\n{ex.Message}",
                    _loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteExistingAttachmentAsync(DocumentAttachment att)
        {
            var user = SecurityService.Instance.CurrentUser;
            if (!user.IsAdmin && !user.CanDelete)
            {
                MessageBox.Show(
                    _loc.IsArabic ? "عذراً، المستخدم الحالي لا يملك صلاحية حذف المرفقات المؤرشفة." : "Sorry, the current user does not have permission to delete archived attachments.",
                    _loc.IsArabic ? "صلاحيات المستخدم" : "User Permissions",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string pinTitle = _loc.IsArabic ? "حذف مرفق مؤرشف ومشفر" : "Delete Encrypted Attachment";
            string pinMsg = _loc.IsArabic
                ? $"هل أنت متأكد من رغبتك في حذف هذا الملف المؤرشف نهائياً؟\n\nاسم الملف: {att.FileName}"
                : $"Are you sure you want to permanently delete this archived file?\n\nFilename: {att.FileName}";

            if (ActionPinDialog.ConfirmWarningAndVerifyPin(pinTitle, pinMsg))
            {
                await _documentService.DeleteAttachmentAsync(att.Id);
                ExistingAttachments.Remove(att);
            }
        }

        private async Task SaveAsync()
        {
            string regNum = RegistrationNumber?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(regNum))
            {
                MessageBox.Show(
                    _loc.IsArabic ? "يرجى إدخال أو توليد رقم القيد للمعاملة." : "Please enter or generate a registration number.",
                    _loc["Error"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (await _documentService.IsRegistrationNumberTakenAsync(regNum, Id))
            {
                MessageBox.Show(
                    _loc.IsArabic
                        ? $"رقم القيد ({regNum}) مسجل مسبقاً لمعاملة أخرى في النظام! يرجى اختيار رقم قيد فريد غير مكرر."
                        : $"Registration number ({regNum}) already exists! Please choose a unique registration number.",
                    _loc["Error"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(Subject))
            {
                MessageBox.Show(
                    _loc.IsArabic ? "يرجى إدخال موضوع المعاملة." : "Please enter the document subject.",
                    _loc["Error"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!string.IsNullOrWhiteSpace(RelatedRegistrationNumber) &&
                string.Equals(RelatedRegistrationNumber.Trim(), regNum, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    _loc.IsArabic ? "لا يمكن ربط المعاملة بنفس رقم قيدها." : "A document cannot reference its own registration number.",
                    _loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var doc = new Document
            {
                Id = Id,
                RegistrationNumber = regNum,
                Type = Type,
                RegistrationDate = RegistrationDate,
                DocumentDate = DocumentDate,
                Subject = Subject.Trim(),
                Sender = Sender?.Trim() ?? string.Empty,
                Recipient = Recipient?.Trim() ?? string.Empty,
                CategoryId = CategoryId,
                Priority = Priority,
                Status = Status,
                DeliveryMethod = DeliveryMethod?.Trim() ?? string.Empty,
                ExternalReferenceNumber = ExternalReferenceNumber?.Trim() ?? string.Empty,
                FollowUpDate = FollowUpDate,
                Notes = Notes?.Trim() ?? string.Empty,
                Department = Department?.Trim() ?? string.Empty,
                Tags = Tags?.Trim() ?? string.Empty,
                RelatedRegistrationNumber = RelatedRegistrationNumber?.Trim() ?? string.Empty
            };

            try
            {
                if (IsEditMode)
                {
                    await _documentService.UpdateDocumentAsync(doc);
                    // Add any newly attached files in edit mode
                    foreach (var path in PendingFilePaths)
                    {
                        await _documentService.AddAttachmentAsync(doc.Id, path, _tempScanFiles.Contains(path));
                    }
                }
                else
                {
                    await _documentService.CreateDocumentAsync(doc, PendingFilePaths.ToList());
                }

                // Bidirectional linking: Update or clear previous reverse link if changed in EditMode, and set new reverse link
                try
                {
                    using var db = new ArchiveDbContext();
                    if (IsEditMode &&
                        !string.IsNullOrWhiteSpace(_initialRelatedRegistrationNumber) &&
                        !string.Equals(_initialRelatedRegistrationNumber, doc.RelatedRegistrationNumber, StringComparison.OrdinalIgnoreCase))
                    {
                        var oldTarget = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                            db.Documents, d => d.RegistrationNumber == _initialRelatedRegistrationNumber);
                        if (oldTarget != null &&
                            (string.Equals(oldTarget.RelatedRegistrationNumber, _initialRegistrationNumber, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(oldTarget.RelatedRegistrationNumber, doc.RegistrationNumber, StringComparison.OrdinalIgnoreCase)))
                        {
                            oldTarget.RelatedRegistrationNumber = string.Empty;
                            oldTarget.UpdatedAt = DateTime.Now;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(doc.RelatedRegistrationNumber))
                    {
                        var targetDoc = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                            db.Documents, d => d.RegistrationNumber == doc.RelatedRegistrationNumber);
                        if (targetDoc != null && string.IsNullOrWhiteSpace(targetDoc.RelatedRegistrationNumber))
                        {
                            targetDoc.RelatedRegistrationNumber = doc.RegistrationNumber;
                            targetDoc.UpdatedAt = DateTime.Now;
                        }
                    }
                    await db.SaveChangesAsync();
                }
                catch { }

                CleanupTempScanFiles();
                MessageBox.Show(_loc["SavedSuccessfully"], _loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
                RequestClose?.Invoke(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{_loc["Error"]}: {ex.Message}", _loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
