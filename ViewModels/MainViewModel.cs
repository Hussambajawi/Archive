using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using DocuArchivePro.Data;
using DocuArchivePro.Models;
using DocuArchivePro.Services;
using DocuArchivePro.Views;

namespace DocuArchivePro.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IDocumentService _documentService;
        private readonly IScannerService _scannerService;
        private readonly IFileStorageService _fileStorageService;
        private readonly ILocalizationService _loc;
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _searchDebounceTimer;

        public ILocalizationService Loc => _loc;

        // Live Time, Day, Gregorian & Hijri Date Properties
        private string _liveClockText = string.Empty;
        public string LiveClockText { get => _liveClockText; set => SetField(ref _liveClockText, value); }

        private string _liveDayText = string.Empty;
        public string LiveDayText { get => _liveDayText; set => SetField(ref _liveDayText, value); }

        private string _liveGregorianText = string.Empty;
        public string LiveGregorianText { get => _liveGregorianText; set => SetField(ref _liveGregorianText, value); }

        private string _liveHijriText = string.Empty;
        public string LiveHijriText { get => _liveHijriText; set => SetField(ref _liveHijriText, value); }

        private string _currentUserSummary = string.Empty;
        public string CurrentUserSummary { get => _currentUserSummary; set => SetField(ref _currentUserSummary, value); }

        private string _currentVaultPathText = string.Empty;
        public string CurrentVaultPathText { get => _currentVaultPathText; set => SetField(ref _currentVaultPathText, value); }

        // Dynamic UI & Button Scale Factor synced with LocalizationService.UiScale
        public double UiScale
        {
            get => _loc.UiScale;
            set
            {
                _loc.UiScale = value;
                OnPropertyChanged(nameof(UiScale));
                OnPropertyChanged(nameof(UiScalePercentText));
            }
        }

        public string UiScalePercentText => _loc.UiScalePercentText;

        // Active Navigation Tab: "Dashboard", "Inbound", "Outbound", "All", "RecycleBin", "Settings"
        private string _currentTab = "Dashboard";
        public string CurrentTab
        {
            get => _currentTab;
            set
            {
                if (SetField(ref _currentTab, value))
                {
                    OnPropertyChanged(nameof(IsDashboardTab));
                    OnPropertyChanged(nameof(IsInboundTab));
                    OnPropertyChanged(nameof(IsOutboundTab));
                    OnPropertyChanged(nameof(IsAllDocsTab));
                    OnPropertyChanged(nameof(IsRecycleBinTab));
                    OnPropertyChanged(nameof(IsNormalDocsTab));
                    OnPropertyChanged(nameof(IsSettingsTab));
                    OnPropertyChanged(nameof(IsDocumentsViewVisible));
                    ExecuteTabChange();
                }
            }
        }

        public bool IsDashboardTab => CurrentTab == "Dashboard";
        public bool IsInboundTab => CurrentTab == "Inbound";
        public bool IsOutboundTab => CurrentTab == "Outbound";
        public bool IsAllDocsTab => CurrentTab == "All";
        public bool IsRecycleBinTab => CurrentTab == "RecycleBin";
        public bool IsNormalDocsTab => !IsSettingsTab && !IsRecycleBinTab;
        public bool IsSettingsTab => CurrentTab == "Settings";
        public bool IsDocumentsViewVisible => !IsSettingsTab;

        public bool IsCurrentUserAdmin => SecurityService.Instance.CurrentUser.IsAdmin;

        // Statistics
        private DashboardStatistics _stats = new();
        public DashboardStatistics Stats
        {
            get => _stats;
            set
            {
                if (SetField(ref _stats, value))
                {
                    OnPropertyChanged(nameof(HasFollowUpDue));
                    OnPropertyChanged(nameof(FollowUpAlertBannerText));
                }
            }
        }

        public bool HasFollowUpDue => Stats != null && Stats.FollowUpDueCount > 0;

        public string FollowUpAlertBannerText
        {
            get
            {
                int count = Stats?.FollowUpDueCount ?? 0;
                bool isAr = _loc.IsArabic;
                string num = DateTimeHelper.FormatNumber(count, isAr);
                return isAr
                    ? $"⏰ تنبيه مواعيد المتابعة: توجد ({num}) معاملة مستحقة أو متأخرة عن موعد المتابعة المحدد لها."
                    : $"⏰ Follow-Up Alert: There are ({num}) transaction(s) due or overdue for follow-up.";
            }
        }

        private bool _onlyFollowUpDue;
        public bool OnlyFollowUpDue
        {
            get => _onlyFollowUpDue;
            set
            {
                if (SetField(ref _onlyFollowUpDue, value))
                {
                    _ = RefreshDocumentsAsync();
                }
            }
        }

        // Documents & Audit Lists
        public ObservableCollection<Document> Documents { get; } = new();
        public ObservableCollection<Category> Categories { get; } = new();
        public ObservableCollection<Category> FilterCategories { get; } = new();
        public ObservableCollection<CorrespondentEntity> CorrespondentEntities { get; } = new();
        public ObservableCollection<AuditLogEntry> AuditLogs { get; } = new();

        // Full Backup & Report Branding Settings
        private bool _isLoadingSettings;
        private bool _autoBackupEnabled;
        public bool AutoBackupEnabled
        {
            get => _autoBackupEnabled;
            set
            {
                if (_autoBackupEnabled == value) return;
                var user = SecurityService.Instance.CurrentUser;
                if (!_isLoadingSettings && user != null && !user.IsAdmin && !user.CanManageStorage)
                {
                    MessageBox.Show(
                        _loc.IsArabic ? "عذراً، تغيير إعدادات النسخ الاحتياطية متاح لمدير النظام أو مسؤول المستودع فقط." : "Backup settings can only be changed by Administrator or Vault Manager.",
                        _loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                    OnPropertyChanged(nameof(AutoBackupEnabled));
                    return;
                }

                if (SetField(ref _autoBackupEnabled, value) && !_isLoadingSettings)
                {
                    _ = SaveAutoBackupPreferenceAsync();
                }
            }
        }

        private string _autoBackupDirectory = string.Empty;
        public string AutoBackupDirectory
        {
            get => _autoBackupDirectory;
            set => SetField(ref _autoBackupDirectory, value);
        }

        private int _autoBackupRetentionCount = 7;
        public int AutoBackupRetentionCount
        {
            get => _autoBackupRetentionCount;
            set
            {
                if (SetField(ref _autoBackupRetentionCount, value) && !_isLoadingSettings)
                {
                    _ = SaveAutoBackupPreferenceAsync();
                }
            }
        }

        private double _backupProgress;
        public double BackupProgress
        {
            get => _backupProgress;
            set => SetField(ref _backupProgress, value);
        }

        private string _backupStatusText = string.Empty;
        public string BackupStatusText
        {
            get => _backupStatusText;
            set => SetField(ref _backupStatusText, value);
        }

        private bool _isBackupOperationRunning;
        public bool IsBackupOperationRunning
        {
            get => _isBackupOperationRunning;
            set
            {
                if (SetField(ref _isBackupOperationRunning, value))
                {
                    OnPropertyChanged(nameof(CanRunBackupOperations));
                }
            }
        }

        public bool CanRunBackupOperations => !_isBackupOperationRunning;

        private DateTime? _lastFullBackupAt;
        public string LastFullBackupText => _lastFullBackupAt.HasValue
            ? DateTimeHelper.FormatDigits(_lastFullBackupAt.Value.ToString("yyyy-MM-dd HH:mm"), _loc.IsArabic)
            : (_loc.IsArabic ? "لم يتم الإنشاء بعد" : "Not created yet");

        private string _organizationName = string.Empty;
        public string OrganizationName { get => _organizationName; set => SetField(ref _organizationName, value); }

        private string _organizationSubtitle = string.Empty;
        public string OrganizationSubtitle { get => _organizationSubtitle; set => SetField(ref _organizationSubtitle, value); }

        private string _organizationLogoPath = string.Empty;
        public string OrganizationLogoPath { get => _organizationLogoPath; set => SetField(ref _organizationLogoPath, value); }

        private string _approvalManagerName = string.Empty;
        public string ApprovalManagerName { get => _approvalManagerName; set => SetField(ref _approvalManagerName, value); }

        private Document? _selectedDocument;
        public Document? SelectedDocument
        {
            get => _selectedDocument;
            set => SetField(ref _selectedDocument, value);
        }

        // Filters
        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetField(ref _searchQuery, value))
                {
                    _searchDebounceTimer.Stop();
                    _searchDebounceTimer.Start();
                }
            }
        }

        private DateTime? _filterFromDate;
        public DateTime? FilterFromDate
        {
            get => _filterFromDate;
            set { if (SetField(ref _filterFromDate, value)) _ = RefreshDocumentsAsync(); }
        }

        private DateTime? _filterToDate;
        public DateTime? FilterToDate
        {
            get => _filterToDate;
            set { if (SetField(ref _filterToDate, value)) _ = RefreshDocumentsAsync(); }
        }

        private int? _filterCategoryId = 0;
        public int? FilterCategoryId
        {
            get => _filterCategoryId;
            set { if (SetField(ref _filterCategoryId, value)) _ = RefreshDocumentsAsync(); }
        }

        private DocumentPriority? _filterPriority;
        public DocumentPriority? FilterPriority
        {
            get => _filterPriority;
            set
            {
                if (SetField(ref _filterPriority, value))
                {
                    OnPropertyChanged(nameof(FilterPriorityIndex));
                    _ = RefreshDocumentsAsync();
                }
            }
        }

        public int FilterPriorityIndex
        {
            get => _filterPriority.HasValue ? (int)_filterPriority.Value : 0;
            set
            {
                DocumentPriority? next = value > 0 && Enum.IsDefined(typeof(DocumentPriority), value)
                    ? (DocumentPriority)value
                    : null;
                if (_filterPriority != next)
                {
                    _filterPriority = next;
                    OnPropertyChanged(nameof(FilterPriority));
                    OnPropertyChanged(nameof(FilterPriorityIndex));
                    _ = RefreshDocumentsAsync();
                }
            }
        }

        private DocumentStatus? _filterStatus;
        public DocumentStatus? FilterStatus
        {
            get => _filterStatus;
            set
            {
                if (SetField(ref _filterStatus, value))
                {
                    OnPropertyChanged(nameof(FilterStatusIndex));
                    _ = RefreshDocumentsAsync();
                }
            }
        }

        public int FilterStatusIndex
        {
            get => _filterStatus.HasValue ? (int)_filterStatus.Value : 0;
            set
            {
                DocumentStatus? next = value > 0 && Enum.IsDefined(typeof(DocumentStatus), value)
                    ? (DocumentStatus)value
                    : null;
                if (_filterStatus != next)
                {
                    _filterStatus = next;
                    OnPropertyChanged(nameof(FilterStatus));
                    OnPropertyChanged(nameof(FilterStatusIndex));
                    _ = RefreshDocumentsAsync();
                }
            }
        }

        // Commands
        public ICommand NavigateTabCommand { get; }
        public ICommand NewDocumentCommand { get; }
        public ICommand EditDocumentCommand { get; }
        public ICommand DeleteDocumentCommand { get; }
        public ICommand PreviewSelectedAttachmentCommand { get; }
        public ICommand PrintSelectedDocumentCommand { get; }
        public ICommand OpenPrintCenterCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ResetFiltersCommand { get; }
        public ICommand OpenAttachmentCommand { get; }
        public ICommand OpenVaultCommand { get; }
        public ICommand OpenDatabaseFolderCommand { get; }
        public ICommand ChangeLanguageCommand { get; }
        public ICommand QuickScanCommand { get; }
        public ICommand OpenUserManagementCommand { get; }
        public ICommand OpenCalendarDialogCommand { get; }
        public ICommand OpenAboutDialogCommand { get; }
        public ICommand ZoomInUiCommand { get; }
        public ICommand ZoomOutUiCommand { get; }
        public ICommand ResetUiZoomCommand { get; }
        public ICommand OpenAddCategoryCommand { get; }
        public ICommand EditCategoryCommand { get; }
        public ICommand DeleteCategoryCommand { get; }
        public ICommand ImportCategoryBackupCommand { get; }
        public ICommand OpenAddEntityCommand { get; }
        public ICommand EditEntityCommand { get; }
        public ICommand DeleteEntityCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand SetThemeCommand { get; }
        public ICommand FilterFollowUpDueCommand { get; }
        public ICommand RestoreSelectedDocumentCommand { get; }
        public ICommand EmptyRecycleBinCommand { get; }
        public ICommand ChooseAutoBackupFolderCommand { get; }
        public ICommand OpenBackupFolderCommand { get; }
        public ICommand CreateFullBackupCommand { get; }
        public ICommand InspectFullBackupCommand { get; }
        public ICommand RestoreFullBackupCommand { get; }
        public ICommand BrowseReportLogoCommand { get; }
        public ICommand ClearReportLogoCommand { get; }
        public ICommand SaveReportBrandingCommand { get; }
        public ICommand RefreshAuditLogsCommand { get; }
        public ICommand ClearAuditLogsCommand { get; }
        public ICommand OpenRelatedDocumentCommand { get; }
        public ICommand NewInboundCommand { get; }
        public ICommand NewOutboundCommand { get; }
        public ICommand FocusSearchCommand { get; }
        public Action? FocusSearchAction { get; set; }
        public ICommand MergeEntitiesCommand { get; }
        public ICommand MarkSelectedAsCompletedCommand { get; }

        public IReadOnlyList<ArabicFontItem> AvailableArabicFonts => FontService.Instance.AvailableArabicFonts;
        private ArabicFontItem? _selectedArabicFont;
        public ArabicFontItem? SelectedArabicFont
        {
            get => _selectedArabicFont ?? FontService.Instance.CurrentArabicFont;
            set
            {
                if (SetField(ref _selectedArabicFont, value) && value != null)
                {
                    _ = FontService.Instance.SaveSelectedFontAsync(value.Key);
                }
            }
        }

        public bool IsArabicLanguage => _loc.IsArabic;

        public event Action<Document?, DocumentType>? RequestOpenDocumentDialog;
        public event Action? RequestOpenScannerDialog;
        public event Action? RequestOpenVaultSettingsDialog;
        public event Action<Document?, int>? RequestOpenPrintDialog;
        public event Action? RequestOpenUserLoginDialog;
        public event Action? RequestOpenCalendarDialog;
        public event Action? RequestOpenAboutDialog;
        public event Action? RequestOpenAddCategoryDialog;
        public event Action<Category>? RequestOpenEditCategoryDialog;
        public event Action<Category>? RequestOpenDeleteCategoryWizard;
        public event Action? RequestImportCategoryBackup;
        public event Action? RequestOpenAddEntityDialog;
        public event Action<CorrespondentEntity>? RequestOpenEditEntityDialog;
        public event Action? RequestOpenMergeEntitiesDialog;

        public MainViewModel(
            IDocumentService documentService,
            IScannerService scannerService,
            IFileStorageService fileStorageService,
            ILocalizationService localizationService)
        {
            _documentService = documentService;
            _scannerService = scannerService;
            _fileStorageService = fileStorageService;
            _loc = localizationService;

            _loc.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ILocalizationService.CurrentLanguage))
                {
                    OnLanguageUpdated();
                }
                else if (e.PropertyName == nameof(ILocalizationService.UiScale) || e.PropertyName == nameof(ILocalizationService.UiScalePercentText))
                {
                    OnPropertyChanged(nameof(UiScale));
                    OnPropertyChanged(nameof(UiScalePercentText));
                }
            };

            UpdateClockAndUserBanner();
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) => UpdateClockAndUserBanner();
            _clockTimer.Start();

            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _searchDebounceTimer.Tick += async (s, e) =>
            {
                _searchDebounceTimer.Stop();
                await RefreshDocumentsAsync();
            };

            NavigateTabCommand = new RelayCommand(tab => CurrentTab = tab?.ToString() ?? "Dashboard");

            NewDocumentCommand = new RelayCommand(param =>
            {
                var user = SecurityService.Instance.CurrentUser;
                if (!user.IsAdmin && !user.CanAdd)
                {
                    MessageBox.Show(
                        _loc.IsArabic
                            ? "عذراً، المستخدم الحالي لا يملك صلاحية إضافة معاملات جديدة."
                            : "Sorry, the current user does not have permission to add new documents.",
                        _loc.IsArabic ? "صلاحيات المستخدم" : "User Permissions",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                DocumentType type = DocumentType.Inbound;
                if (param?.ToString() == "Outbound" || IsOutboundTab) type = DocumentType.Outbound;
                RequestOpenDocumentDialog?.Invoke(null, type);
            });

            NewInboundCommand = new RelayCommand(_ =>
            {
                var user = SecurityService.Instance.CurrentUser;
                if (!user.IsAdmin && !user.CanAdd)
                {
                    MessageBox.Show(
                        _loc.IsArabic
                            ? "عذراً، المستخدم الحالي لا يملك صلاحية إضافة معاملات جديدة."
                            : "Sorry, the current user does not have permission to add new documents.",
                        _loc.IsArabic ? "صلاحيات المستخدم" : "User Permissions",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                RequestOpenDocumentDialog?.Invoke(null, DocumentType.Inbound);
            });

            NewOutboundCommand = new RelayCommand(_ =>
            {
                var user = SecurityService.Instance.CurrentUser;
                if (!user.IsAdmin && !user.CanAdd)
                {
                    MessageBox.Show(
                        _loc.IsArabic
                            ? "عذراً، المستخدم الحالي لا يملك صلاحية إضافة معاملات جديدة."
                            : "Sorry, the current user does not have permission to add new documents.",
                        _loc.IsArabic ? "صلاحيات المستخدم" : "User Permissions",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                RequestOpenDocumentDialog?.Invoke(null, DocumentType.Outbound);
            });

            FocusSearchCommand = new RelayCommand(_ => FocusSearchAction?.Invoke());

            OpenRelatedDocumentCommand = new RelayCommand(async param =>
            {
                var regNum = param?.ToString()?.Trim();
                if (string.IsNullOrEmpty(regNum)) return;

                var doc = await _documentService.GetDocumentByRegistrationNumberAsync(regNum);
                if (doc == null)
                {
                    MessageBox.Show(
                        _loc.IsArabic
                            ? $"لم يتم العثور على المعاملة المرتبطة برقم قيد ({regNum})."
                            : $"Related document with registration number ({regNum}) was not found.",
                        _loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (doc.IsDeleted)
                {
                    CurrentTab = "RecycleBin";
                }
                else if (doc.Type == DocumentType.Inbound)
                {
                    CurrentTab = "Inbound";
                }
                else if (doc.Type == DocumentType.Outbound)
                {
                    CurrentTab = "Outbound";
                }
                else
                {
                    CurrentTab = "All";
                }

                var found = Documents.FirstOrDefault(d => d.Id == doc.Id);
                if (found != null)
                {
                    SelectedDocument = found;
                }
            });

            EditDocumentCommand = new RelayCommand(() =>
            {
                if (SelectedDocument == null) return;

                var user = SecurityService.Instance.CurrentUser;
                if (!user.IsAdmin && !user.CanEdit)
                {
                    MessageBox.Show(
                        _loc.IsArabic
                            ? "عذراً، المستخدم الحالي لا يملك صلاحية تعديل المعاملات المؤرشفة."
                            : "Sorry, the current user does not have permission to edit archived documents.",
                        _loc.IsArabic ? "صلاحيات المستخدم" : "User Permissions",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string regNum = DateTimeHelper.FormatDigits(SelectedDocument.RegistrationNumber, _loc.IsArabic);
                string title = _loc.IsArabic ? "تعديل معاملة مؤرشفة" : "Edit Archived Document";
                string msg = _loc.IsArabic
                    ? $"هل أنت متأكد من رغبتك في إجراء تعديل على هذه المعاملة المؤرشفة؟\n\nرقم القيد: {regNum}\nالموضوع: {SelectedDocument.Subject}"
                    : $"Are you sure you want to edit this archived document?\n\nRegistration No.: {regNum}\nSubject: {SelectedDocument.Subject}";

                if (ActionPinDialog.ConfirmWarningAndVerifyPin(title, msg))
                {
                    RequestOpenDocumentDialog?.Invoke(SelectedDocument, SelectedDocument.Type);
                }
            }, () => SelectedDocument != null);

            DeleteDocumentCommand = new RelayCommand(async () => await DeleteSelectedDocumentAsync(), () => SelectedDocument != null);

            PreviewSelectedAttachmentCommand = new RelayCommand(() =>
            {
                if (SelectedDocument == null) return;
                if (SelectedDocument.Attachments != null && SelectedDocument.Attachments.Count > 0)
                {
                    var allAtts = SelectedDocument.Attachments.OrderBy(a => a.Id).ToList();
                    _fileStorageService.OpenAttachmentsInApp(allAtts, 0);
                }
                else
                {
                    MessageBox.Show(
                        _loc.IsArabic
                            ? "لا توجد ملفات أو صور مرفقة بهذه المعاملة حالياً.\nيمكنك إرفاق ملف أو مسحه ضوئياً من خلال تعديل المعاملة."
                            : "No files or images are currently attached to this document.\nYou can attach or scan a file by editing the document.",
                        _loc.IsArabic ? "استعراض المرفقات المشفرة" : "Encrypted Attachments Viewer",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }, () => SelectedDocument != null);

            PrintSelectedDocumentCommand = new RelayCommand(() =>
            {
                var user = SecurityService.Instance.CurrentUser;
                if (!user.IsAdmin && !user.CanPrint)
                {
                    MessageBox.Show(
                        _loc.IsArabic
                            ? "عذراً، المستخدم الحالي لا يملك صلاحية الطباعة."
                            : "Sorry, the current user does not have printing permission.",
                        _loc.IsArabic ? "صلاحيات المستخدم" : "User Permissions",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (SelectedDocument != null)
                {
                    RequestOpenPrintDialog?.Invoke(SelectedDocument, 4); // Single Document Card
                }
            }, () => SelectedDocument != null);

            OpenPrintCenterCommand = new RelayCommand(() =>
            {
                var user = SecurityService.Instance.CurrentUser;
                if (!user.IsAdmin && !user.CanPrint)
                {
                    MessageBox.Show(
                        _loc.IsArabic
                            ? "عذراً، المستخدم الحالي لا يملك صلاحية الطباعة واستخراج التقارير."
                            : "Sorry, the current user does not have permission to print or export reports.",
                        _loc.IsArabic ? "صلاحيات المستخدم" : "User Permissions",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                RequestOpenPrintDialog?.Invoke(SelectedDocument, 0);
            });

            RefreshCommand = new RelayCommand(async () => await LoadDataAsync());

            ResetFiltersCommand = new RelayCommand(() =>
            {
                _searchDebounceTimer.Stop();
                _searchQuery = string.Empty;
                _filterFromDate = null;
                _filterToDate = null;
                _filterCategoryId = 0;
                _filterPriority = null;
                _filterStatus = null;
                _onlyFollowUpDue = false;
                OnPropertyChanged(nameof(SearchQuery));
                OnPropertyChanged(nameof(FilterFromDate));
                OnPropertyChanged(nameof(FilterToDate));
                OnPropertyChanged(nameof(FilterCategoryId));
                OnPropertyChanged(nameof(FilterPriority));
                OnPropertyChanged(nameof(FilterPriorityIndex));
                OnPropertyChanged(nameof(FilterStatus));
                OnPropertyChanged(nameof(FilterStatusIndex));
                OnPropertyChanged(nameof(OnlyFollowUpDue));
                _ = RefreshDocumentsAsync();
            });

            OpenAttachmentCommand = new RelayCommand(param =>
            {
                if (param is DocumentAttachment att)
                {
                    _fileStorageService.OpenAttachmentInApp(att);
                }
            });

            OpenVaultCommand = new RelayCommand(() =>
            {
                var user = SecurityService.Instance.CurrentUser;
                if (!user.IsAdmin && !user.CanManageStorage)
                {
                    MessageBox.Show(
                        _loc.IsArabic
                            ? "عذراً، المستخدم الحالي لا يملك صلاحية إدارة مكان حفظ وتشفير المستودع."
                            : "Sorry, the current user does not have permission to manage the encrypted vault location.",
                        _loc.IsArabic ? "صلاحيات المستخدم" : "User Permissions",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                RequestOpenVaultSettingsDialog?.Invoke();
            });

            OpenDatabaseFolderCommand = new RelayCommand(() =>
            {
                var user = SecurityService.Instance.CurrentUser;
                if (!user.IsAdmin && !user.CanManageStorage)
                {
                    MessageBox.Show(
                        _loc.IsArabic
                            ? "عذراً، الوصول إلى مجلد قاعدة البيانات متاح فقط لمدير النظام أو من يملك صلاحية إدارة المستودع."
                            : "Sorry, opening the database folder requires Vault Management or Administrator permission.",
                        _loc.IsArabic ? "صلاحيات المستخدم" : "User Permissions",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                string dir = Path.GetDirectoryName(ArchiveDbContext.DatabasePath)!;
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            });

            ChangeLanguageCommand = new RelayCommand(langCode =>
            {
                if (langCode is string code)
                {
                    _loc.SetLanguage(code);
                }
            });

            OpenAddCategoryCommand = new RelayCommand(() =>
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

            EditCategoryCommand = new RelayCommand(param =>
            {
                if (param is Category cat)
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
                    RequestOpenEditCategoryDialog?.Invoke(cat);
                }
            });

            ImportCategoryBackupCommand = new RelayCommand(() => RequestImportCategoryBackup?.Invoke());

            OpenAddEntityCommand = new RelayCommand(() =>
            {
                var user = SecurityService.Instance.CurrentUser;
                if (!user.IsAdmin && !user.CanManageEntities)
                {
                    string actionTitle = _loc.IsArabic
                        ? "إضافة جهة صادر / وارد للمحفوظات (كلمة سر الجهات الخاصة)"
                        : "Add Saved Inbound / Outbound Entity (Entity Security Password)";
                    if (!ActionPinDialog.VerifyEntityAdditionPin(actionTitle))
                    {
                        return;
                    }
                }
                RequestOpenAddEntityDialog?.Invoke();
            });

            EditEntityCommand = new RelayCommand(param =>
            {
                if (param is CorrespondentEntity entity)
                {
                    var user = SecurityService.Instance.CurrentUser;
                    if (!user.IsAdmin && !user.CanManageEntities)
                    {
                        string actionTitle = _loc.IsArabic
                            ? "تعديل جهة صادر / وارد محفوظة (كلمة سر الجهات الخاصة)"
                            : "Edit Saved Correspondent Entity (Entity Security Password)";
                        if (!ActionPinDialog.VerifyEntityAdditionPin(actionTitle))
                        {
                            return;
                        }
                    }
                    RequestOpenEditEntityDialog?.Invoke(entity);
                }
            });

            DeleteEntityCommand = new RelayCommand(async param =>
            {
                if (param is CorrespondentEntity entity)
                {
                    var user = SecurityService.Instance.CurrentUser;
                    if (!user.IsAdmin && !user.CanManageEntities)
                    {
                        string actionTitle = _loc.IsArabic
                            ? "حذف جهة صادر / وارد محفوظة (كلمة سر الجهات الخاصة)"
                            : "Delete Saved Correspondent Entity (Entity Security Password)";
                        if (!ActionPinDialog.VerifyEntityAdditionPin(actionTitle))
                        {
                            return;
                        }
                    }
                    else
                    {
                        var confirm = MessageBox.Show(
                            _loc.IsArabic
                                ? $"هل أنت متأكد من حذف الجهة المحفوظة ({entity.DisplayName}) من القائمة؟"
                                : $"Are you sure you want to remove the saved entity ({entity.DisplayName})?",
                            _loc["ConfirmDelete"],
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
                        if (confirm != MessageBoxResult.Yes) return;
                    }

                    await _documentService.DeleteCorrespondentEntityAsync(entity.Id);
                    await LoadDataAsync();
                }
            });

            MergeEntitiesCommand = new RelayCommand(() =>
            {
                var user = SecurityService.Instance.CurrentUser;
                if (!user.IsAdmin && !user.CanManageEntities)
                {
                    string actionTitle = _loc.IsArabic
                        ? "دمج وتوحيد الجهات المكررة (صلاحية خاصة)"
                        : "Merge Duplicate Entities (Special Permission)";
                    if (!ActionPinDialog.VerifyEntityAdditionPin(actionTitle))
                    {
                        return;
                    }
                }
                RequestOpenMergeEntitiesDialog?.Invoke();
            });

            MarkSelectedAsCompletedCommand = new RelayCommand(async () =>
            {
                if (SelectedDocument == null) return;
                var user = SecurityService.Instance.CurrentUser;
                if (!user.IsAdmin && !user.CanEdit && !user.CanAdd)
                {
                    MessageBox.Show(
                        _loc.IsArabic
                            ? "عذراً، المستخدم الحالي لا يملك صلاحية تحديث حالة المعاملات."
                            : "Sorry, the current user does not have permission to update document status.",
                        _loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await _documentService.UpdateDocumentStatusAsync(SelectedDocument.Id, DocumentStatus.Completed);
                await LoadDataAsync();
            }, () => SelectedDocument != null && !IsRecycleBinTab);

            DeleteCategoryCommand = new RelayCommand(param =>
            {
                if (param is Category cat)
                {
                    var user = SecurityService.Instance.CurrentUser;
                    if (!user.IsAdmin && !user.CanDelete)
                    {
                        MessageBox.Show(
                            _loc.IsArabic
                                ? "عذراً، المستخدم الحالي لا يملك صلاحية حذف الأضابير والمعاملات."
                                : "Sorry, the current user does not have permission to delete dossiers or documents.",
                            _loc.IsArabic ? "صلاحيات المستخدم" : "User Permissions",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    string prompt = _loc.IsArabic
                        ? $"هل أنت متأكد من رغبتك في البدء بإجراءات حذف الإضبارة / التصنيف:\n\n{cat.DisplayName}؟\n\nيتطلب المتابعة إدخال كلمة سر الحذف المعدة مسبقاً."
                        : $"Are you sure you want to start deletion procedures for this dossier / category:\n\n{cat.DisplayName}?\n\nContinuing requires the pre-configured deletion security password.";
                    string title = _loc.IsArabic ? "حذف إضبارة / تصنيف" : "Delete Dossier / Category";

                    if (ActionPinDialog.ConfirmWarningAndVerifyPin(title, prompt))
                    {
                        RequestOpenDeleteCategoryWizard?.Invoke(cat);
                    }
                }
            });

            QuickScanCommand = new RelayCommand(() =>
            {
                var user = SecurityService.Instance.CurrentUser;
                if (!user.IsAdmin && !user.CanScan)
                {
                    MessageBox.Show(
                        _loc.IsArabic
                            ? "عذراً، المستخدم الحالي لا يملك صلاحية استخدام السكانر."
                            : "Sorry, the current user does not have scanner permission.",
                        _loc.IsArabic ? "صلاحيات المستخدم" : "User Permissions",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                RequestOpenScannerDialog?.Invoke();
            });

            OpenUserManagementCommand = new RelayCommand(() => RequestOpenUserLoginDialog?.Invoke());
            OpenCalendarDialogCommand = new RelayCommand(() => RequestOpenCalendarDialog?.Invoke());
            OpenAboutDialogCommand = new RelayCommand(() => RequestOpenAboutDialog?.Invoke());
            ZoomInUiCommand = new RelayCommand(() => UiScale += 0.08);
            ZoomOutUiCommand = new RelayCommand(() => UiScale -= 0.08);
            ResetUiZoomCommand = new RelayCommand(() => UiScale = 1.0);
            ToggleThemeCommand = new RelayCommand(() => _loc.ToggleTheme());
            SetThemeCommand = new RelayCommand(param =>
            {
                bool isDark = string.Equals(param?.ToString(), "Dark", StringComparison.OrdinalIgnoreCase);
                _loc.SetTheme(isDark);
            });

            FilterFollowUpDueCommand = new RelayCommand(param =>
            {
                bool clear = string.Equals(param?.ToString(), "Clear", StringComparison.OrdinalIgnoreCase);
                OnlyFollowUpDue = !clear;
            });

            RestoreSelectedDocumentCommand = new RelayCommand(async () => await RestoreSelectedDocumentAsync(), () => SelectedDocument != null && IsRecycleBinTab);
            EmptyRecycleBinCommand = new RelayCommand(async () => await EmptyRecycleBinAsync());
            ChooseAutoBackupFolderCommand = new RelayCommand(async () => await ChooseAutoBackupFolderAsync());
            OpenBackupFolderCommand = new RelayCommand(OpenBackupFolder);
            CreateFullBackupCommand = new RelayCommand(async () => await CreateFullBackupAsync());
            InspectFullBackupCommand = new RelayCommand(async () => await InspectFullBackupAsync());
            RestoreFullBackupCommand = new RelayCommand(async () => await RestoreFullBackupAsync());
            BrowseReportLogoCommand = new RelayCommand(BrowseReportLogo);
            ClearReportLogoCommand = new RelayCommand(() => OrganizationLogoPath = string.Empty);
            SaveReportBrandingCommand = new RelayCommand(async () => await SaveReportBrandingAsync());
            RefreshAuditLogsCommand = new RelayCommand(async () => await RefreshAuditLogsAsync());
            ClearAuditLogsCommand = new RelayCommand(async () => await ClearAuditLogsAsync());
        }

        private void OnLanguageUpdated()
        {
            UpdateClockAndUserBanner();
            OnPropertyChanged(nameof(IsArabicLanguage));
            OnPropertyChanged(nameof(UiScalePercentText));
            OnPropertyChanged(nameof(Stats));
            OnPropertyChanged(nameof(FollowUpAlertBannerText));
            OnPropertyChanged(nameof(LastFullBackupText));
            OnPropertyChanged(nameof(AvailableArabicFonts));
            CollectionViewSource.GetDefaultView(Documents)?.Refresh();
            CollectionViewSource.GetDefaultView(Categories)?.Refresh();
            CollectionViewSource.GetDefaultView(FilterCategories)?.Refresh();
            CollectionViewSource.GetDefaultView(CorrespondentEntities)?.Refresh();
            CollectionViewSource.GetDefaultView(AuditLogs)?.Refresh();
        }

        public void UpdateClockAndUserBanner()
        {
            var now = DateTime.Now;
            bool isAr = _loc.IsArabic;

            if (isAr)
            {
                LiveClockText = DateTimeHelper.GetTimeArabic(now);
                LiveDayText = DateTimeHelper.GetDayNameArabic(now);
                LiveGregorianText = DateTimeHelper.GetGregorianDateArabic(now);
                LiveHijriText = DateTimeHelper.GetHijriDateArabic(now);
            }
            else
            {
                LiveClockText = DateTimeHelper.GetTimeEnglish(now);
                LiveDayText = DateTimeHelper.GetDayNameEnglish(now);
                LiveGregorianText = DateTimeHelper.GetGregorianDateEnglish(now);
                LiveHijriText = DateTimeHelper.GetHijriDateEnglish(now);
            }

            var u = SecurityService.Instance.CurrentUser;
            CurrentUserSummary = $"{u.LocalizedUsername} ({u.LocalizedRoleTitle})";
            CurrentVaultPathText = _fileStorageService.StorageRootDirectory;
            OnPropertyChanged(nameof(IsCurrentUserAdmin));
        }

        public async Task LoadDataAsync()
        {
            UpdateClockAndUserBanner();
            Stats = await _documentService.GetDashboardStatisticsAsync();
            var cats = await _documentService.GetCategoriesAsync();
            Categories.Clear();
            foreach (var c in cats) Categories.Add(c);

            int? previousCatFilter = _filterCategoryId;
            FilterCategories.Clear();
            FilterCategories.Add(new Category { Id = 0, Name = "كافة الأضابير والتصنيفات / All Dossiers & Categories" });
            foreach (var c in cats) FilterCategories.Add(c);
            _filterCategoryId = (previousCatFilter.HasValue && FilterCategories.Any(c => c.Id == previousCatFilter.Value))
                ? previousCatFilter
                : 0;
            OnPropertyChanged(nameof(FilterCategoryId));

            var entities = await _documentService.GetCorrespondentEntitiesAsync();
            CorrespondentEntities.Clear();
            foreach (var ent in entities) CorrespondentEntities.Add(ent);

            await LoadSettingsFieldsAsync();
            await RefreshAuditLogsAsync();
            await RefreshDocumentsAsync();
        }

        private async void ExecuteTabChange()
        {
            if (IsSettingsTab)
            {
                await LoadSettingsFieldsAsync();
                await RefreshAuditLogsAsync();
            }
            await RefreshDocumentsAsync();
            ThemeService.Instance.ApplyThemeToWindow(Application.Current?.MainWindow);
        }

        public async Task RefreshDocumentsAsync()
        {
            DocumentType? docType = null;
            if (IsInboundTab) docType = DocumentType.Inbound;
            else if (IsOutboundTab) docType = DocumentType.Outbound;

            var criteria = new DocumentFilterCriteria
            {
                Type = docType,
                FromDate = FilterFromDate,
                ToDate = FilterToDate,
                CategoryId = (FilterCategoryId.HasValue && FilterCategoryId.Value > 0) ? FilterCategoryId : null,
                Priority = FilterPriority,
                Status = FilterStatus,
                SearchQuery = SearchQuery,
                OnlyFollowUpDue = !IsRecycleBinTab && OnlyFollowUpDue,
                IncludeDeletedOnly = IsRecycleBinTab
            };

            var docs = await _documentService.SearchDocumentsAsync(criteria);
            Documents.Clear();
            foreach (var d in docs) Documents.Add(d);

            Stats = await _documentService.GetDashboardStatisticsAsync();
        }

        private async Task DeleteSelectedDocumentAsync()
        {
            if (SelectedDocument == null) return;

            var user = SecurityService.Instance.CurrentUser;
            if (!user.IsAdmin && !user.CanDelete)
            {
                MessageBox.Show(
                    _loc.IsArabic
                        ? "عذراً، المستخدم الحالي لا يملك صلاحية حذف المعاملات المؤرشفة."
                        : "Sorry, the current user does not have permission to delete archived documents.",
                    _loc.IsArabic ? "صلاحيات المستخدم" : "User Permissions",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string regNum = DateTimeHelper.FormatDigits(SelectedDocument.RegistrationNumber, _loc.IsArabic);
            if (IsRecycleBinTab)
            {
                string warningMsg = $"{_loc["DeleteMessage"]}\n\n{_loc["RegistrationNumber"]}: {regNum}\n{_loc["Subject"]}: {SelectedDocument.Subject}";
                string pinTitle = _loc.IsArabic ? "حذف معاملة مؤرشفة نهائياً" : "Permanently Delete Archived Document";

                if (ActionPinDialog.ConfirmWarningAndVerifyPin(pinTitle, warningMsg))
                {
                    await _documentService.DeleteDocumentAsync(SelectedDocument.Id);
                    await LoadDataAsync();
                }
            }
            else
            {
                string warningMsg = _loc.IsArabic
                    ? $"هل ترغب في نقل هذه المعاملة إلى سلة المحذوفات؟\n(يمكنك استعادتها لاحقاً أو حذفها نهائياً من سلة المحذوفات)\n\nرقم القيد: {regNum}\nالموضوع: {SelectedDocument.Subject}"
                    : $"Move this document to the Recycle Bin?\n(You can restore it later or permanently delete it from the Recycle Bin)\n\nRegistration No.: {regNum}\nSubject: {SelectedDocument.Subject}";
                string pinTitle = _loc.IsArabic ? "نقل المعاملة إلى سلة المحذوفات" : "Move Document to Recycle Bin";

                if (ActionPinDialog.ConfirmWarningAndVerifyPin(pinTitle, warningMsg))
                {
                    await _documentService.MoveDocumentToRecycleBinAsync(SelectedDocument.Id);
                    await LoadDataAsync();
                }
            }
        }

        private async Task RestoreSelectedDocumentAsync()
        {
            if (SelectedDocument == null) return;

            var user = SecurityService.Instance.CurrentUser;
            if (!user.IsAdmin && !user.CanDelete && !user.CanEdit)
            {
                MessageBox.Show(
                    _loc.IsArabic ? "عذراً، لا تملك صلاحية استعادة المعاملات." : "Sorry, you do not have permission to restore documents.",
                    _loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await _documentService.RestoreDocumentFromRecycleBinAsync(SelectedDocument.Id);
            await LoadDataAsync();
            MessageBox.Show(
                _loc.IsArabic ? "تمت استعادة المعاملة إلى السجل الرئيسي بنجاح." : "Document restored to the main archive successfully.",
                _loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task EmptyRecycleBinAsync()
        {
            var user = SecurityService.Instance.CurrentUser;
            if (!user.IsAdmin && !user.CanDelete)
            {
                MessageBox.Show(
                    _loc.IsArabic ? "عذراً، لا تملك صلاحية إفراغ سلة المحذوفات." : "Sorry, you do not have permission to empty the Recycle Bin.",
                    _loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string title = _loc.IsArabic ? "إفراغ سلة المحذوفات نهائياً" : "Empty Recycle Bin Permanently";
            string msg = _loc.IsArabic
                ? "تحذير: سيتم حذف جميع المعاملات والمرفقات الموجودة داخل سلة المحذوفات نهائياً ولا يمكن استرجاعها!\nهل تريد المتابعة؟"
                : "Warning: All documents and encrypted files in the Recycle Bin will be permanently deleted!\nContinue?";
            if (!ActionPinDialog.ConfirmWarningAndVerifyPin(title, msg)) return;

            int removed = await _documentService.EmptyRecycleBinAsync();
            await LoadDataAsync();
            MessageBox.Show(
                _loc.IsArabic
                    ? $"تم إفراغ سلة المحذوفات بنجاح (عدد المعاملات المحذوفة نهائياً: {DateTimeHelper.FormatNumber(removed, true)})."
                    : $"Recycle Bin emptied successfully (permanently deleted: {removed}).",
                _loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task LoadSettingsFieldsAsync()
        {
            try
            {
                _isLoadingSettings = true;
                var settings = await SecurityService.Instance.GetSettingsAsync();
                AutoBackupEnabled = settings.AutoBackupEnabled;
                AutoBackupDirectory = settings.AutoBackupDirectory;
                AutoBackupRetentionCount = settings.AutoBackupRetentionCount > 0 ? settings.AutoBackupRetentionCount : 7;
                _lastFullBackupAt = settings.LastFullBackupAt;
                OrganizationName = settings.OrganizationName;
                OrganizationSubtitle = settings.OrganizationSubtitle;
                OrganizationLogoPath = settings.OrganizationLogoPath;
                ApprovalManagerName = settings.ApprovalManagerName;
                OnPropertyChanged(nameof(LastFullBackupText));
            }
            catch { }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        private async Task SaveAutoBackupPreferenceAsync()
        {
            try
            {
                var user = SecurityService.Instance.CurrentUser;
                if (user != null && !user.IsAdmin && !user.CanManageStorage) return;

                var settings = await SecurityService.Instance.GetSettingsAsync();
                settings.AutoBackupEnabled = AutoBackupEnabled;
                settings.AutoBackupDirectory = AutoBackupDirectory;
                settings.AutoBackupRetentionCount = AutoBackupRetentionCount > 0 ? AutoBackupRetentionCount : 7;
                await SecurityService.Instance.SaveSettingsAsync(settings);
            }
            catch { }
        }

        private async Task ChooseAutoBackupFolderAsync()
        {
            var user = SecurityService.Instance.CurrentUser;
            if (user != null && !user.IsAdmin && !user.CanManageStorage)
            {
                MessageBox.Show(
                    _loc.IsArabic ? "هذه الميزة متاحة لمدير النظام أو مسؤول المستودع فقط." : "This option requires Vault Management or Administrator permission.",
                    _loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = _loc.IsArabic ? "اختر مجلد حفظ النسخ الاحتياطية التلقائية" : "Select Automatic Backup Target Folder"
            };
            if (dlg.ShowDialog() == true)
            {
                AutoBackupDirectory = dlg.FolderName;
                var settings = await SecurityService.Instance.GetSettingsAsync();
                settings.AutoBackupDirectory = AutoBackupDirectory;
                settings.AutoBackupEnabled = AutoBackupEnabled;
                settings.AutoBackupRetentionCount = AutoBackupRetentionCount > 0 ? AutoBackupRetentionCount : 7;
                await SecurityService.Instance.SaveSettingsAsync(settings);
            }
        }

        private void OpenBackupFolder()
        {
            try
            {
                string folder = !string.IsNullOrWhiteSpace(AutoBackupDirectory) && Directory.Exists(AutoBackupDirectory)
                    ? AutoBackupDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{_loc["Error"]}: {ex.Message}", _loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CreateFullBackupAsync()
        {
            var user = SecurityService.Instance.CurrentUser;
            if (user != null && !user.IsAdmin && !user.CanManageStorage)
            {
                MessageBox.Show(
                    _loc.IsArabic ? "عذراً، النسخ الاحتياطي الشامل متاح لمدير النظام أو مسؤول المستودع فقط." : "Full system backup requires Vault Management or Administrator permission.",
                    _loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = _loc.IsArabic ? "اختر مجلد حفظ النسخة الاحتياطية الشاملة (.zip)" : "Select Folder to Save Full Backup (.zip)",
                InitialDirectory = Directory.Exists(AutoBackupDirectory)
                    ? AutoBackupDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };

            if (dlg.ShowDialog() != true) return;

            // Prompt for optional password protection
            var (pwdConfirmed, password) = BackupPasswordDialog.PromptSetPassword(Application.Current?.MainWindow);
            if (!pwdConfirmed) return;

            try
            {
                IsBackupOperationRunning = true;
                BackupProgress = 0;
                var progress = new Progress<(double Progress, string Status)>(p =>
                {
                    BackupProgress = p.Progress;
                    BackupStatusText = p.Status;
                });

                string zipPath = await _documentService.CreateFullSystemBackupZipAsync(dlg.FolderName, password, progress);
                var info = await _documentService.InspectFullSystemBackupZipAsync(zipPath, password);
                await LoadSettingsFieldsAsync();
                await RefreshAuditLogsAsync();

                bool isAr = _loc.IsArabic;
                double mb = info.ZipSizeBytes / (1024.0 * 1024.0);
                string sizeStr = mb >= 1.0
                    ? $"{mb:F2} {(isAr ? "م.ب" : "MB")}"
                    : $"{(info.ZipSizeBytes / 1024.0):F1} {(isAr ? "ك.ب" : "KB")}";
                var m = info.Manifest;
                string encNote = m.IsPasswordProtected
                    ? (isAr ? "🔒 مشفرة بكلمة مرور مخصصة (AES-256)" : "🔒 Protected with Custom Password (AES-256)")
                    : (isAr ? "🛡️ مشفرة بمفتاح النظام القياسي (AES-256)" : "🛡️ Protected with Master Key (AES-256)");

                string okMsg = isAr
                    ? $"تم إنشاء النسخة الاحتياطية الشاملة بنجاح!\n\n" +
                      $"📦 الملف: {zipPath}\n" +
                      $"📏 الحجم: {DateTimeHelper.FormatDigits(sizeStr, true)}\n" +
                      $"🔐 الأمان والتشفير: {encNote}\n" +
                      $"📄 المعاملات المؤرشفة: {DateTimeHelper.FormatNumber(m.ActiveDocumentsCount, true)}  |  سلة المحذوفات: {DateTimeHelper.FormatNumber(m.RecycleBinDocumentsCount, true)}\n" +
                      $"🔒 المرفقات المشفرة: {DateTimeHelper.FormatNumber(m.AttachmentsCount, true)}  |  الأضابير: {DateTimeHelper.FormatNumber(m.CategoriesCount, true)}\n" +
                      $"👥 المستخدمون: {DateTimeHelper.FormatNumber(m.UsersCount, true)}  |  الجهات: {DateTimeHelper.FormatNumber(m.CorrespondentEntitiesCount, true)}\n" +
                      $"✅ تم تضمين بصمات التحقق (SHA-256) والشعار والإعدادات لنقلها واستعادتها على أي جهاز آخر بسهولة."
                    : $"Full system backup created successfully!\n\n" +
                      $"📦 File: {zipPath}\n" +
                      $"📏 Size: {sizeStr}\n" +
                      $"🔐 Security: {encNote}\n" +
                      $"📄 Active Documents: {m.ActiveDocumentsCount}  |  Recycle Bin: {m.RecycleBinDocumentsCount}\n" +
                      $"🔒 Encrypted Attachments: {m.AttachmentsCount}  |  Dossiers: {m.CategoriesCount}\n" +
                      $"👥 Users: {m.UsersCount}  |  Entities: {m.CorrespondentEntitiesCount}\n" +
                      $"✅ Includes SHA-256 integrity manifest, branding logo, and settings for seamless restore on any PC.";

                MessageBox.Show(okMsg, _loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{_loc["Error"]}: {ex.Message}", _loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBackupOperationRunning = false;
                BackupStatusText = string.Empty;
            }
        }

        private async Task InspectFullBackupAsync()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = _loc.IsArabic ? "اختر ملف النسخة الاحتياطية لفحص سلامتها (.zip)" : "Select Backup Archive to Inspect (.zip)",
                Filter = "Backup Archive (*.zip)|*.zip"
            };
            if (dlg.ShowDialog() != true) return;

            bool isAr = _loc.IsArabic;
            try
            {
                var initialInspection = await _documentService.InspectFullSystemBackupZipAsync(dlg.FileName);
                string? password = null;

                if (initialInspection.Manifest.IsPasswordProtected)
                {
                    var (pwdConfirmed, pwd) = BackupPasswordDialog.PromptEnterPassword(
                        Application.Current?.MainWindow,
                        isAr ? "النسخة مشفرة بكلمة مرور مخصصة. أدخل كلمة المرور لفحص سلامتها:" : "This backup is password protected. Enter password to inspect:");
                    if (!pwdConfirmed) return;
                    password = pwd;
                }

                var inspection = await _documentService.InspectFullSystemBackupZipAsync(dlg.FileName, password);
                var m = inspection.Manifest;
                string dateStr = DateTimeHelper.FormatDigits(m.ExportedAt.ToString("yyyy-MM-dd HH:mm"), isAr);
                string sourcePc = string.IsNullOrWhiteSpace(m.SourceMachineName) ? "-" : m.SourceMachineName;
                double mb = inspection.ZipSizeBytes / (1024.0 * 1024.0);
                string sizeStr = mb >= 1.0 ? $"{mb:F2} MB" : $"{(inspection.ZipSizeBytes / 1024.0):F1} KB";

                if (!inspection.ChecksumsValid)
                {
                    string badList = string.Join("\n", inspection.ChecksumErrors.Take(6));
                    MessageBox.Show(
                        isAr
                            ? $"⚠️ نتيجة فحص النسخة الاحتياطية: تم اكتشاف أخطاء في السلامة أو كلمة المرور!\n\nالملفات المتأثرة:\n{badList}"
                            : $"⚠️ Backup inspection failed! Corrupted files or incorrect password:\n\n{badList}",
                        _loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string encType = m.IsPasswordProtected
                    ? (isAr ? "محمية بكلمة مرور مخصصة (AES-256) ✅" : "Protected with Custom Password (AES-256) ✅")
                    : (isAr ? "محمية بمفتاح النظام القياسي (AES-256) ✅" : "Protected with Master Key (AES-256) ✅");

                string reportMsg = isAr
                    ? $"تقرير فحص سلامة النسخة الاحتياطية:\n\n" +
                      $"• ملف النسخة: {Path.GetFileName(dlg.FileName)}\n" +
                      $"• فحص السلامة والبصمات (SHA-256): سليم ومطابق بنسبة 100% ✅\n" +
                      $"• نوع التشفير: {encType}\n" +
                      $"• الجهاز المصدر: {sourcePc}  |  المستخدم: {m.SourceWindowsUser}\n" +
                      $"• تاريخ الإنشاء: {dateStr}  |  الحجم: {DateTimeHelper.FormatDigits(sizeStr, true)}\n" +
                      $"• المعاملات: {DateTimeHelper.FormatNumber(m.ActiveDocumentsCount, true)} (وارد: {DateTimeHelper.FormatNumber(m.InboundCount, true)}، صادر: {DateTimeHelper.FormatNumber(m.OutboundCount, true)})\n" +
                      $"• سلة المحذوفات: {DateTimeHelper.FormatNumber(m.RecycleBinDocumentsCount, true)}  |  المرفقات: {DateTimeHelper.FormatNumber(m.AttachmentsCount, true)}\n" +
                      $"• الأضابير: {DateTimeHelper.FormatNumber(m.CategoriesCount, true)}  |  المستخدمون: {DateTimeHelper.FormatNumber(m.UsersCount, true)}\n\n" +
                      $"جاهزة للاستعادة الآمنة على أي جهاز بنجاح."
                    : $"Backup Archive Verification Report:\n\n" +
                      $"• Archive: {Path.GetFileName(dlg.FileName)}\n" +
                      $"• Integrity Check (SHA-256): 100% Verified ✅\n" +
                      $"• Encryption: {encType}\n" +
                      $"• Source Machine: {sourcePc}  |  User: {m.SourceWindowsUser}\n" +
                      $"• Created: {dateStr}  |  Size: {sizeStr}\n" +
                      $"• Documents: {m.ActiveDocumentsCount} (Inbound: {m.InboundCount}, Outbound: {m.OutboundCount})\n" +
                      $"• Recycle Bin: {m.RecycleBinDocumentsCount}  |  Attachments: {m.AttachmentsCount}\n" +
                      $"• Dossiers: {m.CategoriesCount}  |  Users: {m.UsersCount}\n\n" +
                      $"Ready for safe restore on any system.";

                MessageBox.Show(reportMsg, isAr ? "تقرير فحص النسخة الاحتياطية" : "Backup Verification Report", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{_loc["Error"]}: {ex.Message}", _loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RestoreFullBackupAsync()
        {
            var user = SecurityService.Instance.CurrentUser;
            if (user == null || !user.IsAdmin)
            {
                MessageBox.Show(
                    _loc.IsArabic
                        ? "عذراً، استعادة النسخة الاحتياطية الشاملة للنظام متاحة لمدير النظام (SuperUser) حصراً لأسباب أمنية وحماية لحسابات المستخدمين."
                        : "Sorry, Full System Backup restore is restricted to System Administrator (SuperUser) only for security reasons.",
                    _loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = _loc.IsArabic ? "اختر ملف النسخة الاحتياطية الشاملة (.zip)" : "Select Full System Backup (.zip)",
                Filter = "Backup Archive (*.zip)|*.zip"
            };
            if (dlg.ShowDialog() != true) return;

            bool isAr = _loc.IsArabic;
            FullBackupInspectionResult inspection;
            string? password = null;

            try
            {
                var initialInspection = await _documentService.InspectFullSystemBackupZipAsync(dlg.FileName);
                if (initialInspection.Manifest.IsPasswordProtected)
                {
                    var (pwdConfirmed, pwd) = BackupPasswordDialog.PromptEnterPassword(
                        Application.Current?.MainWindow,
                        isAr ? "هذه النسخة مشفرة بكلمة مرور. أدخل كلمة المرور للاستعادة:" : "This backup is password protected. Enter password to restore:");
                    if (!pwdConfirmed) return;
                    password = pwd;
                }

                inspection = await _documentService.InspectFullSystemBackupZipAsync(dlg.FileName, password);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    isAr ? $"ملف النسخة الاحتياطية غير صالح أو تالف:\n{ex.Message}" : $"Invalid or corrupted backup archive:\n{ex.Message}",
                    _loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!inspection.ChecksumsValid)
            {
                string badList = string.Join("\n", inspection.ChecksumErrors.Take(5));
                MessageBox.Show(
                    isAr
                        ? $"فشل التحقق من سلامة النسخة الاحتياطية أو كلمة المرور!\n\n{badList}"
                        : $"Backup integrity verification or password failed!\n\n{badList}",
                    _loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var m = inspection.Manifest;
            string dateStr = DateTimeHelper.FormatDigits(m.ExportedAt.ToString("yyyy-MM-dd HH:mm"), isAr);
            string sourcePc = string.IsNullOrWhiteSpace(m.SourceMachineName) ? "-" : m.SourceMachineName;
            string title = isAr ? "استعادة نسخة احتياطية شاملة للنظام" : "Restore Full System Backup";
            string msg = isAr
                ? $"تفاصيل النسخة الاحتياطية المختارة:\n" +
                  $"• الجهاز المصدر: {sourcePc}  |  التاريخ: {dateStr}\n" +
                  $"• المعاملات: {DateTimeHelper.FormatNumber(m.ActiveDocumentsCount, true)}  |  المرفقات: {DateTimeHelper.FormatNumber(m.AttachmentsCount, true)}  |  المستخدمون: {DateTimeHelper.FormatNumber(m.UsersCount, true)}\n" +
                  $"• فحص السلامة (SHA-256): سليم ومطابق ✅\n\n" +
                  $"تحذير: ستقوم هذه العملية باستبدال قاعدة البيانات الحالية ببيانات هذه النسخة وإعادة ربط المسارات تلقائياً على هذا الجهاز.\nهل أنت متأكد من المتابعة؟"
                : $"Selected Backup Summary:\n" +
                  $"• Source Machine: {sourcePc}  |  Date: {dateStr}\n" +
                  $"• Documents: {m.ActiveDocumentsCount}  |  Attachments: {m.AttachmentsCount}  |  Users: {m.UsersCount}\n" +
                  $"• Integrity Check (SHA-256): Verified ✅\n\n" +
                  $"Warning: This will replace the current database with the backup contents and automatically re-bind all paths for this PC.\nContinue?";

            if (!ActionPinDialog.ConfirmWarningAndVerifyPin(title, msg)) return;

            try
            {
                IsBackupOperationRunning = true;
                BackupProgress = 0;
                var progress = new Progress<(double Progress, string Status)>(p =>
                {
                    BackupProgress = p.Progress;
                    BackupStatusText = p.Status;
                });

                var (docsCount, filesCount) = await _documentService.RestoreFullSystemBackupZipAsync(dlg.FileName, password, progress);
                await FontService.Instance.LoadSettingsAsync();
                await ThemeService.Instance.LoadSettingsAsync();
                FontService.Instance.ApplyLanguage(_loc.CurrentLanguage.Code);
                await LoadDataAsync();

                bool requiresReLogin = string.IsNullOrEmpty(SecurityService.Instance.CurrentUser?.Username);
                if (requiresReLogin)
                {
                    MessageBox.Show(
                        isAr
                            ? "تمت استعادة النسخة الاحتياطية بنجاح!\nنظراً لأن حساب المستخدم السابق غير مسجل في النسخة المستعادة، يرجى تسجيل الدخول بحساب من النسخة المستعادة."
                            : "Backup restored successfully!\nSince your previous user account is not present in the restored database, please log in with an account from the restored system.",
                        _loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);

                    var loginPrompt = new LoginPromptDialog(SecurityService.Instance) { Owner = Application.Current?.MainWindow };
                    if (loginPrompt.ShowDialog() != true)
                    {
                        var welcome = new WelcomeWindow(_documentService, _fileStorageService, SecurityService.Instance);
                        welcome.Show();
                        Application.Current?.MainWindow?.Close();
                        return;
                    }
                    await LoadDataAsync();
                }
                else
                {
                    MessageBox.Show(
                        isAr
                            ? $"تمت استعادة النظام وإعادة ربط جميع المسارات بنجاح!\nالمعاملات المستعادة: {DateTimeHelper.FormatNumber(docsCount, true)}\nالملفات المشفرة المستعادة: {DateTimeHelper.FormatNumber(filesCount, true)}"
                            : $"Full backup restored and all paths re-bound successfully!\nRestored Documents: {docsCount}\nRestored Vault Files: {filesCount}",
                        _loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{_loc["Error"]}: {ex.Message}", _loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBackupOperationRunning = false;
                BackupStatusText = string.Empty;
            }
        }

        private void BrowseReportLogo()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = _loc.IsArabic ? "اختر صورة شعار المؤسسة" : "Select Organization Logo Image",
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
            };
            if (dlg.ShowDialog() == true)
            {
                OrganizationLogoPath = dlg.FileName;
            }
        }

        private async Task SaveReportBrandingAsync()
        {
            var user = SecurityService.Instance.CurrentUser;
            if (!user.IsAdmin)
            {
                MessageBox.Show(
                    _loc.IsArabic ? "تخصيص ترويسة وشعار التقارير متاح لمدير النظام فقط." : "Customizing report branding is restricted to System Administrators.",
                    _loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var settings = await SecurityService.Instance.GetSettingsAsync();
            settings.OrganizationName = OrganizationName?.Trim() ?? string.Empty;
            settings.OrganizationSubtitle = OrganizationSubtitle?.Trim() ?? string.Empty;
            settings.OrganizationLogoPath = OrganizationLogoPath?.Trim() ?? string.Empty;
            settings.ApprovalManagerName = ApprovalManagerName?.Trim() ?? string.Empty;
            await SecurityService.Instance.SaveSettingsAsync(settings);
            await _documentService.LogAuditAsync("تحديث ترويسة التقارير / Update Report Branding", settings.OrganizationName);
            await RefreshAuditLogsAsync();

            MessageBox.Show(
                _loc.IsArabic ? "تم حفظ إعدادات شعار وترويسة التقارير بنجاح." : "Report branding & logo settings saved successfully.",
                _loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public async Task RefreshAuditLogsAsync()
        {
            try
            {
                var logs = await _documentService.GetRecentAuditLogsAsync(200);
                AuditLogs.Clear();
                foreach (var entry in logs) AuditLogs.Add(entry);
            }
            catch { }
        }

        private async Task ClearAuditLogsAsync()
        {
            if (!SecurityService.Instance.CurrentUser.IsAdmin)
            {
                MessageBox.Show(
                    _loc.IsArabic ? "مسح سجل التدقيق متاح لمدير النظام فقط." : "Clearing the audit log is restricted to System Administrators.",
                    _loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string title = _loc.IsArabic ? "مسح سجل تدقيق العمليات" : "Clear Security Audit Log";
            string msg = _loc.IsArabic ? "هل أنت متأكد من مسح كافة سجلات التدقيق السابقة؟" : "Are you sure you want to clear all previous audit log entries?";
            if (!ActionPinDialog.ConfirmWarningAndVerifyPin(title, msg)) return;

            await _documentService.ClearAuditLogsAsync();
            await RefreshAuditLogsAsync();
        }
    }
}