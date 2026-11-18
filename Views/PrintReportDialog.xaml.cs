using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using DocuArchivePro.Data;
using DocuArchivePro.Models;
using DocuArchivePro.Services;

namespace DocuArchivePro.Views
{
    public partial class PrintReportDialog : Window
    {
        private readonly List<Document> _allDocuments;
        private readonly Document? _selectedDocument;
        private readonly SystemSettings? _reportSettings;
        private List<Document> _filteredDocs = new();
        private bool _isInitialized = false;
        private bool _isShowingBarcodeSticker = false;

        public ILocalizationService Loc => LocalizationService.Instance;

        public PrintReportDialog(List<Document> documents, Document? selectedDocument = null, int initialReportIndex = 0)
        {
            InitializeComponent();
            DataContext = this;
            _allDocuments = documents ?? new List<Document>();
            _selectedDocument = selectedDocument ?? _allDocuments.FirstOrDefault();

            try
            {
                using var db = new ArchiveDbContext();
                _reportSettings = db.SystemSettings.FirstOrDefault();

                var cats = new List<Category>
                {
                    new Category { Id = 0, Name = "كافة الأضابير / All Dossiers" }
                };
                cats.AddRange(db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToList());
                CmbFilterCategory.ItemsSource = cats;
                CmbFilterCategory.SelectedIndex = 0;
            }
            catch { }

            CmbReportType.SelectedIndex = initialReportIndex;
            _isInitialized = true;
            BuildReportPreview();
        }

        private void FilterOption_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitialized) BuildReportPreview();
        }

        private void TxtReportSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitialized) BuildReportPreview();
        }

        private void BtnRefreshPreview_Click(object sender, RoutedEventArgs e)
        {
            BuildReportPreview();
        }

        private void BuildReportPreview()
        {
            _isShowingBarcodeSticker = false;
            bool isAr = Loc.IsArabic;
            DocViewer.Document = BuildCurrentReportFlowDocument();
            string userDisplay = SecurityService.Instance.CurrentUser.LocalizedUsername;
            string summary = isAr
                ? $"إجمالي السجلات المشمولة في التقرير: {_filteredDocs.Count} معاملة  |  المستخدم: {userDisplay}"
                : $"Total Records Included: {_filteredDocs.Count} documents  |  User: {userDisplay}";
            TxtSummaryFooter.Text = DateTimeHelper.FormatDigits(summary, isAr);
        }

        private FlowDocument BuildCurrentReportFlowDocument()
        {
            bool isAr = Loc.IsArabic;
            int reportType = CmbReportType.SelectedIndex;
            var query = _allDocuments.AsEnumerable();

            if (DpFrom.SelectedDate is DateTime from)
            {
                query = query.Where(d => d.RegistrationDate.Date >= from.Date);
            }
            if (DpTo.SelectedDate is DateTime to)
            {
                query = query.Where(d => d.RegistrationDate.Date <= to.Date);
            }
            if (CmbFilterCategory?.SelectedItem is Category selectedCat && selectedCat.Id > 0)
            {
                query = query.Where(d => d.CategoryId == selectedCat.Id);
            }
            if (!string.IsNullOrWhiteSpace(TxtReportSearch?.Text))
            {
                string q = DocumentService.NormalizeArabicForSearch(TxtReportSearch.Text);
                query = query.Where(d =>
                    DocumentService.NormalizeArabicForSearch(d.RegistrationNumber).Contains(q, StringComparison.Ordinal) ||
                    DocumentService.NormalizeArabicForSearch(d.RelatedRegistrationNumber).Contains(q, StringComparison.Ordinal) ||
                    DocumentService.NormalizeArabicForSearch(d.Subject).Contains(q, StringComparison.Ordinal) ||
                    DocumentService.NormalizeArabicForSearch(d.Sender).Contains(q, StringComparison.Ordinal) ||
                    DocumentService.NormalizeArabicForSearch(d.Recipient).Contains(q, StringComparison.Ordinal) ||
                    DocumentService.NormalizeArabicForSearch(d.Department).Contains(q, StringComparison.Ordinal) ||
                    DocumentService.NormalizeArabicForSearch(d.ExternalReferenceNumber).Contains(q, StringComparison.Ordinal) ||
                    DocumentService.NormalizeArabicForSearch(d.Notes).Contains(q, StringComparison.Ordinal));
            }

            string reportTitle = isAr
                ? "كشف عام بالمعاملات المؤرشفة (الصادر والوارد)"
                : "General Archived Documents Register (Inbound & Outbound)";

            if (reportType == 1)
            {
                query = query.Where(d => d.Type == DocumentType.Inbound);
                reportTitle = isAr ? "تقرير كشف المعاملات الواردة" : "Inbound Documents Register Report";
            }
            else if (reportType == 2)
            {
                query = query.Where(d => d.Type == DocumentType.Outbound);
                reportTitle = isAr ? "تقرير كشف المعاملات الصادرة" : "Outbound Documents Register Report";
            }
            else if (reportType == 3)
            {
                query = query.Where(d => d.Priority == DocumentPriority.Urgent || d.Priority == DocumentPriority.VeryUrgent || d.Priority == DocumentPriority.Confidential);
                reportTitle = isAr ? "تقرير المعاملات العاجلة والسرية للغاية" : "Urgent & Confidential Documents Report";
            }
            else if (reportType == 4)
            {
                reportTitle = isAr ? "بطاقة واستمارة قيد معاملة رسمية مفردة" : "Single Official Document Registration Card";
            }
            else if (reportType == 5)
            {
                reportTitle = isAr ? "التقرير الإحصائي الشامل للأرشيف والتصنيفات" : "Comprehensive Archive & Dossiers Statistics Report";
            }
            else if (reportType == 6)
            {
                var today = DateTime.Today;
                query = query.Where(d =>
                    d.FollowUpDate.HasValue &&
                    d.FollowUpDate.Value.Date <= today &&
                    d.Status != DocumentStatus.Completed &&
                    d.Status != DocumentStatus.Archived &&
                    d.Status != DocumentStatus.Cancelled);
                reportTitle = isAr
                    ? "كشف المعاملات المستحقة للمتابعة والمتأخرة"
                    : "Follow-Up Due & Overdue Documents Report";
            }

            _filteredDocs = query.OrderByDescending(d => d.RegistrationDate).ToList();

            if (reportType == 4)
            {
                return BuildSingleDocumentFlow(_selectedDocument ?? _filteredDocs.FirstOrDefault(), reportTitle);
            }
            if (reportType == 5)
            {
                return BuildStatisticalFlow(_filteredDocs, reportTitle);
            }
            return BuildTableReportFlow(_filteredDocs, reportTitle);
        }

        private FlowDocument CreateBaseFlowDocument(string reportTitle)
        {
            bool isAr = Loc.IsArabic;
            FontFamily activeFont = Application.Current.Resources["AppFontFamily"] as FontFamily ?? new FontFamily("Segoe UI, Tahoma, Arial");

            var doc = new FlowDocument
            {
                FlowDirection = Loc.CurrentFlowDirection,
                FontFamily = activeFont,
                FontSize = 12.5,
                PagePadding = new Thickness(40),
                ColumnWidth = double.PositiveInfinity
            };

            var now = DateTime.Now;

            var headerSection = new Section();

            // Custom Organization Logo if configured
            if (!string.IsNullOrWhiteSpace(_reportSettings?.OrganizationLogoPath) && File.Exists(_reportSettings.OrganizationLogoPath))
            {
                try
                {
                    byte[] logoBytes = File.ReadAllBytes(_reportSettings.OrganizationLogoPath);
                    using var lms = new MemoryStream(logoBytes);
                    var logoBmp = new BitmapImage();
                    logoBmp.BeginInit();
                    logoBmp.CacheOption = BitmapCacheOption.OnLoad;
                    logoBmp.StreamSource = lms;
                    logoBmp.EndInit();
                    logoBmp.Freeze();

                    var logoImg = new Image
                    {
                        Source = logoBmp,
                        MaxHeight = 68,
                        MaxWidth = 200,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    headerSection.Blocks.Add(new BlockUIContainer(logoImg)
                    {
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 6)
                    });
                }
                catch { }
            }

            string orgTitle = !string.IsNullOrWhiteSpace(_reportSettings?.OrganizationName)
                ? _reportSettings.OrganizationName
                : Loc["AppTitle"];

            var titlePara = new Paragraph(new Run(orgTitle))
            {
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(26, 86, 219)),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };
            headerSection.Blocks.Add(titlePara);

            if (!string.IsNullOrWhiteSpace(_reportSettings?.OrganizationSubtitle))
            {
                headerSection.Blocks.Add(new Paragraph(new Run(_reportSettings.OrganizationSubtitle))
                {
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            }

            var subTitlePara = new Paragraph(new Run(reportTitle))
            {
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            headerSection.Blocks.Add(subTitlePara);

            string dateInfoText = isAr
                ? $"اليوم: {DateTimeHelper.GetDayNameArabic(now)}   |   الوقت: {DateTimeHelper.GetTimeArabic(now)}   |   الميلادي: {DateTimeHelper.GetGregorianDateArabic(now)}   |   الهجري: {DateTimeHelper.GetHijriDateArabic(now)}"
                : $"Day: {DateTimeHelper.GetDayNameEnglish(now)}   |   Time: {DateTimeHelper.GetTimeEnglish(now)}   |   Gregorian: {DateTimeHelper.GetGregorianDateEnglish(now)}   |   Hijri: {DateTimeHelper.GetHijriDateEnglish(now)}";

            var dateInfoPara = new Paragraph(new Run(dateInfoText))
            {
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                TextAlignment = TextAlignment.Center,
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(0, 0, 0, 1.5),
                Padding = new Thickness(0, 0, 0, 10),
                Margin = new Thickness(0, 0, 0, 16)
            };
            headerSection.Blocks.Add(dateInfoPara);

            doc.Blocks.Add(headerSection);
            return doc;
        }

        private FlowDocument BuildTableReportFlow(List<Document> docs, string reportTitle)
        {
            bool isAr = Loc.IsArabic;
            var doc = CreateBaseFlowDocument(reportTitle);

            int inbound = docs.Count(d => d.Type == DocumentType.Inbound);
            int outbound = docs.Count(d => d.Type == DocumentType.Outbound);
            int urgent = docs.Count(d => d.Priority == DocumentPriority.Urgent || d.Priority == DocumentPriority.VeryUrgent);

            string summaryStr = isAr
                ? $"إجمالي المعاملات في الكشف: ({docs.Count})   |   الوارد: ({inbound})   |   الصادر: ({outbound})   |   العاجل: ({urgent})"
                : $"Total Records: ({docs.Count})   |   Inbound: ({inbound})   |   Outbound: ({outbound})   |   Urgent: ({urgent})";

            var summaryPara = new Paragraph(new Run(DateTimeHelper.FormatDigits(summaryStr, isAr)))
            {
                FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 14)
            };
            doc.Blocks.Add(summaryPara);

            var table = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
            string[] headers = isAr
                ? new[] { "م", "رقم القيد", "النوع", "تاريخ القيد", "الموضوع", "الراسل", "المستلم", "الأولوية", "المرفقات" }
                : new[] { "#", "Reg No.", "Type", "Reg Date", "Subject", "Sender", "Recipient", "Priority", "Files" };
            double[] widths = { 35, 105, 65, 85, 210, 125, 125, 80, 55 };

            foreach (var w in widths)
            {
                table.Columns.Add(new TableColumn { Width = new GridLength(w) });
            }

            var rowGroup = new TableRowGroup();

            var headerRow = new TableRow { Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)), Foreground = Brushes.White, FontWeight = FontWeights.Bold };
            foreach (var h in headers)
            {
                headerRow.Cells.Add(new TableCell(new Paragraph(new Run(h)) { Margin = new Thickness(4) })
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(4)
                });
            }
            rowGroup.Rows.Add(headerRow);

            int index = 1;
            foreach (var d in docs)
            {
                var row = new TableRow
                {
                    Background = index % 2 == 0
                        ? new SolidColorBrush(Color.FromRgb(248, 250, 252))
                        : Brushes.White
                };

                string typeText = d.Type == DocumentType.Inbound ? Loc["Inbound"] : Loc["Outbound"];
                string prioText = d.Priority switch
                {
                    DocumentPriority.Urgent => Loc["PriorityUrgent"],
                    DocumentPriority.VeryUrgent => Loc["PriorityVeryUrgent"],
                    DocumentPriority.Confidential => Loc["PriorityConfidential"],
                    _ => Loc["PriorityNormal"]
                };

                string[] cells =
                {
                    DateTimeHelper.FormatNumber(index, isAr),
                    DateTimeHelper.FormatDigits(d.RegistrationNumber, isAr),
                    typeText,
                    DateTimeHelper.FormatDigits(d.RegistrationDate.ToString("yyyy-MM-dd"), isAr),
                    DateTimeHelper.FormatDigits(d.Subject, isAr),
                    DateTimeHelper.FormatDigits(d.Sender, isAr),
                    DateTimeHelper.FormatDigits(d.Recipient, isAr),
                    prioText,
                    DateTimeHelper.FormatNumber(d.Attachments?.Count ?? 0, isAr)
                };

                foreach (var c in cells)
                {
                    row.Cells.Add(new TableCell(new Paragraph(new Run(c)) { Margin = new Thickness(3), FontSize = 11.5 })
                    {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                        BorderThickness = new Thickness(0.5),
                        Padding = new Thickness(4)
                    });
                }

                rowGroup.Rows.Add(row);
                index++;
            }

            table.RowGroups.Add(rowGroup);
            doc.Blocks.Add(table);

            AddSignatureFooter(doc);
            return doc;
        }

        private FlowDocument BuildSingleDocumentFlow(Document? d, string reportTitle)
        {
            bool isAr = Loc.IsArabic;
            var doc = CreateBaseFlowDocument(reportTitle);
            if (d == null)
            {
                doc.Blocks.Add(new Paragraph(new Run(isAr ? "لا توجد معاملة محددة للطباعة." : "No document selected for printing.")));
                return doc;
            }

            string typeText = d.Type == DocumentType.Inbound ? Loc["Inbound"] : Loc["Outbound"];
            string prioText = d.Priority switch
            {
                DocumentPriority.Urgent => Loc["PriorityUrgent"],
                DocumentPriority.VeryUrgent => Loc["PriorityVeryUrgent"],
                DocumentPriority.Confidential => Loc["PriorityConfidential"],
                _ => Loc["PriorityNormal"]
            };
            string statusText = d.Status switch
            {
                DocumentStatus.InProgress => Loc["StatusInProgress"],
                DocumentStatus.Completed => Loc["StatusCompleted"],
                DocumentStatus.Archived => Loc["StatusArchived"],
                DocumentStatus.Cancelled => Loc["StatusCancelled"],
                _ => Loc["StatusNew"]
            };

            // Embed QR & Barcode Identification Stamp at top of Single Document Card
            try
            {
                string hashDigest = d.Attachments?.FirstOrDefault()?.FileHashSha256 ?? string.Empty;
                var stampBmp = BarcodeQrGenerator.GenerateDocumentQrAndBarcodeStamp(
                    d.RegistrationNumber,
                    d.RegistrationDate,
                    d.Type == DocumentType.Inbound ? "INBOUND" : "OUTBOUND",
                    hashDigest);
                var stampImg = new Image
                {
                    Source = stampBmp,
                    Width = 380,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                doc.Blocks.Add(new BlockUIContainer(stampImg)
                {
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 14)
                });
            }
            catch { }

            var table = new Table { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
            table.Columns.Add(new TableColumn { Width = new GridLength(190) });
            table.Columns.Add(new TableColumn { Width = new GridLength(490) });

            var rg = new TableRowGroup();
            void AddFieldRow(string label, string? val)
            {
                var r = new TableRow();
                r.Cells.Add(new TableCell(new Paragraph(new Run(label)) { Margin = new Thickness(6), FontWeight = FontWeights.Bold })
                {
                    Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(0.5)
                });
                r.Cells.Add(new TableCell(new Paragraph(new Run(DateTimeHelper.FormatDigits(val ?? "-", isAr))) { Margin = new Thickness(6) })
                {
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(0.5)
                });
                rg.Rows.Add(r);
            }

            AddFieldRow(Loc["RegistrationNumber"] + ":", d.RegistrationNumber);
            AddFieldRow(Loc["RelatedRegNumberLabel"], string.IsNullOrWhiteSpace(d.RelatedRegistrationNumber) ? "-" : d.RelatedRegistrationNumber);
            AddFieldRow(Loc["DocumentType"] + ":", typeText);
            AddFieldRow(Loc["RegistrationDate"] + ":", isAr
                ? $"{d.RegistrationDate:yyyy-MM-dd} ({DateTimeHelper.GetHijriDateArabic(d.RegistrationDate)})"
                : $"{d.RegistrationDate:yyyy-MM-dd} ({DateTimeHelper.GetHijriDateEnglish(d.RegistrationDate)})");
            AddFieldRow(Loc["DocumentDate"] + ":", d.DocumentDate.ToString("yyyy-MM-dd"));
            AddFieldRow(Loc["Subject"] + ":", d.Subject);
            AddFieldRow(Loc["Sender"] + ":", d.Sender);
            AddFieldRow(Loc["Recipient"] + ":", d.Recipient);
            AddFieldRow(Loc["Category"] + ":", d.Category?.DisplayName ?? (isAr ? "عام" : "General"));
            AddFieldRow(Loc["Department"] + ":", d.Department);
            AddFieldRow(Loc["Priority"] + ":", prioText);
            AddFieldRow(Loc["Status"] + ":", statusText);
            AddFieldRow(Loc["DeliveryMethod"] + ":", d.DeliveryMethod);
            AddFieldRow(Loc["ExternalRef"] + ":", d.ExternalReferenceNumber);
            AddFieldRow(Loc["Notes"] + ":", d.Notes);
            AddFieldRow(Loc["ColAttachments"] + ":", isAr ? $"{d.Attachments?.Count ?? 0} مرفق" : $"{d.Attachments?.Count ?? 0} files");

            table.RowGroups.Add(rg);
            doc.Blocks.Add(table);

            AddSignatureFooter(doc);
            return doc;
        }

        private FlowDocument BuildStatisticalFlow(List<Document> docs, string reportTitle)
        {
            bool isAr = Loc.IsArabic;
            var doc = CreateBaseFlowDocument(reportTitle);

            int total = docs.Count;
            int inbound = docs.Count(d => d.Type == DocumentType.Inbound);
            int outbound = docs.Count(d => d.Type == DocumentType.Outbound);
            int urgent = docs.Count(d => d.Priority == DocumentPriority.Urgent || d.Priority == DocumentPriority.VeryUrgent);
            int completed = docs.Count(d => d.Status == DocumentStatus.Completed);
            int inProgress = docs.Count(d => d.Status == DocumentStatus.InProgress);
            int totalAtt = docs.Sum(d => d.Attachments?.Count ?? 0);

            var list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(20, 10, 20, 20) };
            if (isAr)
            {
                list.ListItems.Add(new ListItem(new Paragraph(new Run(DateTimeHelper.FormatDigits($"إجمالي عدد المعاملات المسجلة: {total} معاملة", true))) { FontWeight = FontWeights.Bold }));
                list.ListItems.Add(new ListItem(new Paragraph(new Run(DateTimeHelper.FormatDigits($"إجمالي المعاملات الواردة: {inbound} معاملة", true)))));
                list.ListItems.Add(new ListItem(new Paragraph(new Run(DateTimeHelper.FormatDigits($"إجمالي المعاملات الصادرة: {outbound} معاملة", true)))));
                list.ListItems.Add(new ListItem(new Paragraph(new Run(DateTimeHelper.FormatDigits($"المعاملات العاجلة والفورية: {urgent} معاملة", true)))));
                list.ListItems.Add(new ListItem(new Paragraph(new Run(DateTimeHelper.FormatDigits($"المعاملات المنجزة: {completed} معاملة", true)))));
                list.ListItems.Add(new ListItem(new Paragraph(new Run(DateTimeHelper.FormatDigits($"المعاملات قيد المتابعة: {inProgress} معاملة", true)))));
                list.ListItems.Add(new ListItem(new Paragraph(new Run(DateTimeHelper.FormatDigits($"إجمالي الملفات والصور المؤرشفة المشفرة: {totalAtt} ملف", true)))));
            }
            else
            {
                list.ListItems.Add(new ListItem(new Paragraph(new Run($"Total Registered Documents: {total}")) { FontWeight = FontWeights.Bold }));
                list.ListItems.Add(new ListItem(new Paragraph(new Run($"Total Inbound Documents: {inbound}"))));
                list.ListItems.Add(new ListItem(new Paragraph(new Run($"Total Outbound Documents: {outbound}"))));
                list.ListItems.Add(new ListItem(new Paragraph(new Run($"Urgent & Immediate Documents: {urgent}"))));
                list.ListItems.Add(new ListItem(new Paragraph(new Run($"Completed Documents: {completed}"))));
                list.ListItems.Add(new ListItem(new Paragraph(new Run($"In-Progress Documents: {inProgress}"))));
                list.ListItems.Add(new ListItem(new Paragraph(new Run($"Total Encrypted Attachments: {totalAtt} files"))));
            }

            doc.Blocks.Add(list);
            AddSignatureFooter(doc);
            return doc;
        }

        private void AddSignatureFooter(FlowDocument doc)
        {
            string managerName = !string.IsNullOrWhiteSpace(_reportSettings?.ApprovalManagerName)
                ? _reportSettings.ApprovalManagerName
                : (Loc.IsArabic ? "المبرمج حسام البعجاوي" : "Eng. Hussam Al-Bajawi");

            string text = Loc.IsArabic
                ? $"\n\nتوقيع الموظف المختص: ........................................          اعتماد إدارة الأرشيف ({managerName}): ........................................"
                : $"\n\nOfficer Signature: ........................................          Archive Management Approval ({managerName}): ........................................";

            var footerPara = new Paragraph(new Run(text))
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 24, 0, 0)
            };
            doc.Blocks.Add(footerPara);
        }

        private FlowDocument BuildBarcodeStickerFlowDocument(Document d)
        {
            bool isAr = Loc.IsArabic;
            FontFamily activeFont = Application.Current.Resources["AppFontFamily"] as FontFamily ?? new FontFamily("Segoe UI, Tahoma, Arial");
            int stickerSize = CmbStickerSize?.SelectedIndex ?? 0;

            double pageWidthDip = 794;
            double pageHeightDip = 1123;
            Thickness padding = new Thickness(32);
            double stampWidth = 420;
            double orgFontSize = 14;
            double detailFontSize = 11.5;

            if (stickerSize == 1) // 50 x 30 mm Thermal Small
            {
                pageWidthDip = 189; // 50mm @ 96 dpi
                pageHeightDip = 113; // 30mm @ 96 dpi
                padding = new Thickness(6, 4, 6, 4);
                stampWidth = 160;
                orgFontSize = 8.5;
                detailFontSize = 7.5;
            }
            else if (stickerSize == 2) // 80 x 50 mm Thermal Medium
            {
                pageWidthDip = 302; // 80mm @ 96 dpi
                pageHeightDip = 189; // 50mm @ 96 dpi
                padding = new Thickness(10, 8, 10, 8);
                stampWidth = 260;
                orgFontSize = 11;
                detailFontSize = 9.5;
            }

            var doc = new FlowDocument
            {
                FlowDirection = Loc.CurrentFlowDirection,
                FontFamily = activeFont,
                FontSize = detailFontSize,
                PagePadding = padding,
                PageWidth = pageWidthDip,
                PageHeight = pageHeightDip,
                ColumnWidth = double.PositiveInfinity,
                Background = Brushes.White,
                Foreground = Brushes.Black
            };

            string orgTitle = !string.IsNullOrWhiteSpace(_reportSettings?.OrganizationName)
                ? _reportSettings.OrganizationName
                : Loc["AppTitle"];

            doc.Blocks.Add(new Paragraph(new Run(orgTitle))
            {
                FontSize = orgFontSize,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(26, 86, 219)),
                Margin = new Thickness(0, 0, 0, stickerSize > 0 ? 3 : 8)
            });

            string hashDigest = d.Attachments?.FirstOrDefault()?.FileHashSha256 ?? string.Empty;
            var stampBmp = BarcodeQrGenerator.GenerateDocumentQrAndBarcodeStamp(
                d.RegistrationNumber,
                d.RegistrationDate,
                d.Type == DocumentType.Inbound ? "INBOUND" : "OUTBOUND",
                hashDigest);

            var stampImg = new Image
            {
                Source = stampBmp,
                Width = stampWidth,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            doc.Blocks.Add(new BlockUIContainer(stampImg)
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, stickerSize > 0 ? 4 : 12)
            });

            string summary = isAr
                ? $"الموضوع: {d.Subject}\nالراسل: {d.Sender}  |  المستلم: {d.Recipient}"
                : $"Subject: {d.Subject}\nSender: {d.Sender}  |  Recipient: {d.Recipient}";
            doc.Blocks.Add(new Paragraph(new Run(DateTimeHelper.FormatDigits(summary, isAr)))
            {
                TextAlignment = TextAlignment.Center,
                FontSize = detailFontSize,
                Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                Margin = new Thickness(0)
            });

            return doc;
        }

        private void BtnPrintBarcodeSticker_Click(object sender, RoutedEventArgs e)
        {
            var targetDoc = _selectedDocument ?? _filteredDocs.FirstOrDefault();
            if (targetDoc == null)
            {
                MessageBox.Show(
                    Loc.IsArabic ? "يرجى اختيار معاملة أولاً لطباعة ملصق الباركود." : "Please select a document first to print a barcode sticker.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isShowingBarcodeSticker = true;
            var stickerDoc = BuildBarcodeStickerFlowDocument(targetDoc);
            DocViewer.Document = stickerDoc;

            var printDlg = new PrintDialog();
            if (printDlg.ShowDialog() == true)
            {
                DocumentPrintExportService.PrintOrExportFlowDocument(
                    printDlg,
                    stickerDoc,
                    $"Sticker_{targetDoc.RegistrationNumber}",
                    this,
                    freshDocFactory: () => BuildBarcodeStickerFlowDocument(targetDoc));
            }
        }

        private void BtnExportPdf_Click(object sender, RoutedEventArgs e)
        {
            if (DocViewer.Document == null) return;
            bool isAr = Loc.IsArabic;
            var saveDlg = new SaveFileDialog
            {
                Title = isAr ? "تصدير التقرير مباشرة إلى ملف PDF" : "Export Report to PDF Document",
                Filter = "PDF Document (*.pdf)|*.pdf",
                FileName = $"Report_Archive_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
            };

            if (saveDlg.ShowDialog(this) == true)
            {
                try
                {
                    FlowDocument printDoc;
                    var targetDoc = _selectedDocument ?? _filteredDocs.FirstOrDefault();
                    if (_isShowingBarcodeSticker && targetDoc != null)
                    {
                        printDoc = BuildBarcodeStickerFlowDocument(targetDoc);
                    }
                    else
                    {
                        printDoc = BuildCurrentReportFlowDocument();
                    }

                    DocumentPrintExportService.ExportFlowDocumentToPdfFile(printDoc, saveDlg.FileName);
                    DocumentPrintExportService.VerifyAndAnnouncePdfSaved(saveDlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        isAr ? $"تعذر حفظ ملف PDF:\n{ex.Message}" : $"Failed to save PDF file:\n{ex.Message}",
                        Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnPrintNow_Click(object sender, RoutedEventArgs e)
        {
            var printDlg = new PrintDialog();
            if (printDlg.ShowDialog() == true && DocViewer.Document != null)
            {
                DocumentPrintExportService.PrintOrExportFlowDocument(
                    printDlg,
                    DocViewer.Document,
                    Loc["AppTitle"],
                    this,
                    freshDocFactory: BuildCurrentReportFlowDocument);
            }
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            bool isAr = Loc.IsArabic;
            var saveDlg = new SaveFileDialog
            {
                Title = isAr ? "تصدير كشف المعاملات إلى ملف Excel (CSV)" : "Export Document Register to Excel (CSV)",
                Filter = "Excel CSV (*.csv)|*.csv",
                FileName = $"Report_Archive_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };

            if (saveDlg.ShowDialog(this) == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine(isAr
                    ? "رقم القيد,رقم القيد المرتبط,النوع,تاريخ التسجيل,تاريخ الخطاب,الموضوع,الراسل,المستلم,التصنيف,الإدارة,الأولوية,الحالة,طريقة التسليم,الرقم المرجعي الخارجي,تاريخ المتابعة,الملاحظات,المرفقات"
                    : "Reg No.,Related Reg No.,Type,Registration Date,Document Date,Subject,Sender,Recipient,Dossier,Department,Priority,Status,Delivery Method,External Ref,Follow-Up Date,Notes,Attachments");

                foreach (var d in _filteredDocs)
                {
                    string typeText = d.Type == DocumentType.Inbound ? Loc["Inbound"] : Loc["Outbound"];
                    string prioText = d.Priority switch
                    {
                        DocumentPriority.Urgent => Loc["PriorityUrgent"],
                        DocumentPriority.VeryUrgent => Loc["PriorityVeryUrgent"],
                        DocumentPriority.Confidential => Loc["PriorityConfidential"],
                        _ => Loc["PriorityNormal"]
                    };
                    string statusText = d.Status switch
                    {
                        DocumentStatus.InProgress => Loc["StatusInProgress"],
                        DocumentStatus.Completed => Loc["StatusCompleted"],
                        DocumentStatus.Archived => Loc["StatusArchived"],
                        DocumentStatus.Cancelled => Loc["StatusCancelled"],
                        _ => Loc["StatusNew"]
                    };
                    string followUpStr = d.FollowUpDate.HasValue
                        ? DateTimeHelper.FormatDigits(d.FollowUpDate.Value.ToString("yyyy-MM-dd"), isAr)
                        : string.Empty;

                    sb.AppendLine(string.Join(",",
                        EscapeCsv(DateTimeHelper.FormatDigits(d.RegistrationNumber, isAr)),
                        EscapeCsv(DateTimeHelper.FormatDigits(d.RelatedRegistrationNumber, isAr)),
                        EscapeCsv(typeText),
                        EscapeCsv(DateTimeHelper.FormatDigits(d.RegistrationDate.ToString("yyyy-MM-dd"), isAr)),
                        EscapeCsv(DateTimeHelper.FormatDigits(d.DocumentDate.ToString("yyyy-MM-dd"), isAr)),
                        EscapeCsv(d.Subject),
                        EscapeCsv(d.Sender),
                        EscapeCsv(d.Recipient),
                        EscapeCsv(d.Category?.DisplayName),
                        EscapeCsv(d.Department),
                        EscapeCsv(prioText),
                        EscapeCsv(statusText),
                        EscapeCsv(d.DeliveryMethod),
                        EscapeCsv(d.ExternalReferenceNumber),
                        EscapeCsv(followUpStr),
                        EscapeCsv(d.Notes),
                        EscapeCsv(DateTimeHelper.FormatNumber(d.Attachments?.Count ?? 0, isAr))));
                }

                // Write UTF-8 with BOM so Arabic/Unicode opens cleanly in Microsoft Excel
                File.WriteAllBytes(saveDlg.FileName, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray());
                _ = new DocumentService(new FileStorageService()).LogAuditAsync("تصدير تقرير CSV / Export CSV Report", $"{saveDlg.FileName} ({_filteredDocs.Count} rows)");
                MessageBox.Show(
                    isAr ? $"تم تصدير التقرير بنجاح إلى:\n{saveDlg.FileName}" : $"Report exported successfully to:\n{saveDlg.FileName}",
                    Loc["Success"], MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            string cleaned = value
                .Replace("\r\n", " ")
                .Replace("\n", " ")
                .Replace("\r", " ")
                .Trim();

            // Prevent CSV Formula Injection / #NAME? errors in Excel when values start with =, +, -, or @
            if (cleaned.Length > 0 && (cleaned[0] == '=' || cleaned[0] == '+' || cleaned[0] == '-' || cleaned[0] == '@'))
            {
                cleaned = "'" + cleaned;
            }

            cleaned = cleaned.Replace("\"", "\"\"");
            return $"\"{cleaned}\"";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}