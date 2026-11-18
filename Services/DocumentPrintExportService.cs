using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Printing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Microsoft.Win32;

namespace DocuArchivePro.Services
{
    /// <summary>
    /// Provides reliable printing, Word/RTF rendering, and direct multi-page PDF export
    /// without suffering from Windows "Microsoft Print to PDF" silent aborts or empty pages.
    /// </summary>
    public static class DocumentPrintExportService
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private static readonly XNamespace V = "urn:schemas-microsoft-com:vml";

        static DocumentPrintExportService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public static bool IsWordExtension(string ext)
        {
            return ext == ".docx" || ext == ".doc" || ext == ".rtf" || ext == ".odt" || ext == ".html" || ext == ".htm";
        }

        public static string SanitizePrintJobTitle(string? rawTitle)
        {
            if (string.IsNullOrWhiteSpace(rawTitle))
                return "DocuArchivePro Document";

            var sb = new StringBuilder(rawTitle.Length);
            foreach (char c in rawTitle)
            {
                if (c == ',' || c == ':' || c == ';' || c == '/' || c == '\\' ||
                    c == '"' || c == '\'' || c == '|' || c == '*' || c == '?' ||
                    c == '<' || c == '>' || char.IsControl(c))
                {
                    sb.Append(' ');
                }
                else
                {
                    sb.Append(c);
                }
            }

            string cleaned = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            if (string.IsNullOrEmpty(cleaned))
                return "DocuArchivePro Document";

            return cleaned.Length > 80 ? cleaned.Substring(0, 80).Trim() : cleaned;
        }

        public static string SanitizeFileName(string? rawName, string fallback = "Exported_Document")
        {
            string baseName = Path.GetFileNameWithoutExtension(rawName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = fallback;

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(invalid, '_');
            }
            baseName = baseName.Replace(',', '_').Trim(' ', '.');
            return string.IsNullOrWhiteSpace(baseName) ? fallback : baseName;
        }

        public static bool IsVirtualPdfPrinter(PrintQueue? queue)
        {
            if (queue == null) return false;
            try
            {
                string combined = $"{queue.Name} {queue.FullName} {queue.QueueDriver?.Name} {queue.QueuePort?.Name}";
                return combined.Contains("PDF", StringComparison.OrdinalIgnoreCase) ||
                       combined.Contains("PORTPROMPT", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        #region Word / RTF / HTML / ODT Loader into FlowDocument

        public static FlowDocument LoadWordFlowDocument(byte[] bytes, string extension, string originalFileName)
        {
            bool isAr = LocalizationService.Instance.IsArabic;
            var doc = CreatePrintableDocumentShell(isAr);

            if (bytes == null || bytes.Length == 0)
            {
                doc.Blocks.Add(new Paragraph(new Run(isAr ? "الملف المرفق فارغ." : "Attached document is empty.")));
                return doc;
            }

            string ext = (extension ?? Path.GetExtension(originalFileName)).ToLowerInvariant();

            try
            {
                // 1. RTF detection
                if (ext == ".rtf" || StartsWithAscii(bytes, "{\\rtf"))
                {
                    LoadRtfIntoFlowDocument(doc, bytes);
                    if (doc.Blocks.Count > 0) return doc;
                }

                // 2. ZIP container (.docx or .odt)
                if (bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B)
                {
                    if (ext == ".odt")
                    {
                        LoadOdtIntoFlowDocument(doc, bytes);
                    }
                    else
                    {
                        LoadDocxIntoFlowDocument(doc, bytes);
                    }
                    if (doc.Blocks.Count > 0) return doc;
                }

                // 3. HTML / MHTML / Word XML saved with .doc extension
                string head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 512)).TrimStart();
                if (ext == ".html" || ext == ".htm" ||
                    head.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
                    head.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                    head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
                {
                    LoadHtmlOrXmlTextIntoFlowDocument(doc, Encoding.UTF8.GetString(bytes));
                    if (doc.Blocks.Count > 0) return doc;
                }

                // 4. Legacy binary Word (.doc) or fallback text extraction
                LoadLegacyDocTextIntoFlowDocument(doc, bytes, isAr);
            }
            catch (Exception ex)
            {
                doc.Blocks.Clear();
                doc.Blocks.Add(new Paragraph(new Run(isAr
                    ? $"ملخص المستند: {originalFileName}\nتعذر تحليل التنسيق بالكامل: {ex.Message}"
                    : $"Document Summary: {originalFileName}\nPartial parsing fallback: {ex.Message}"))
                {
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 12)
                });
                LoadLegacyDocTextIntoFlowDocument(doc, bytes, isAr);
            }

            if (doc.Blocks.Count == 0)
            {
                doc.Blocks.Add(new Paragraph(new Run(isAr
                    ? $"المستند: {originalFileName} (لا يحتوي على نص قابل للاستخراج، يمكنك استخدام زر فتح خارجي لعرضه في Microsoft Word)"
                    : $"Document: {originalFileName} (No extractable text found; use external open to view in Microsoft Word)")));
            }

            return doc;
        }

        private static FlowDocument CreatePrintableDocumentShell(bool isAr)
        {
            FontFamily activeFont = Application.Current?.Resources["AppFontFamily"] as FontFamily
                ?? new FontFamily("Segoe UI, Tahoma, Arial");

            return new FlowDocument
            {
                Background = Brushes.White,
                Foreground = Brushes.Black,
                FlowDirection = isAr ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                FontFamily = activeFont,
                FontSize = 13,
                PagePadding = new Thickness(48),
                ColumnWidth = double.PositiveInfinity
            };
        }

        private static bool StartsWithAscii(byte[] bytes, string prefix)
        {
            if (bytes.Length < prefix.Length) return false;
            string head = Encoding.ASCII.GetString(bytes, 0, prefix.Length);
            return head.Equals(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static void LoadRtfIntoFlowDocument(FlowDocument doc, byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            range.Load(ms, DataFormats.Rtf);
            doc.Background = Brushes.White;
            doc.Foreground = Brushes.Black;
        }

        private static void LoadDocxIntoFlowDocument(FlowDocument doc, byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

            // Load relationships to resolve embedded images
            var relMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var relsEntry = zip.GetEntry("word/_rels/document.xml.rels");
            if (relsEntry != null)
            {
                using var rs = relsEntry.Open();
                var relDoc = XDocument.Load(rs);
                foreach (var rel in relDoc.Descendants().Where(e => e.Name.LocalName == "Relationship"))
                {
                    string? id = rel.Attribute("Id")?.Value;
                    string? target = rel.Attribute("Target")?.Value;
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                    {
                        string normalized = target.StartsWith("/") ? target.TrimStart('/') : "word/" + target.TrimStart('/');
                        relMap[id] = normalized;
                    }
                }
            }

            // Load headers first if present
            foreach (var headerName in new[] { "word/header1.xml", "word/header2.xml" })
            {
                var hEntry = zip.GetEntry(headerName);
                if (hEntry != null)
                {
                    using var hs = hEntry.Open();
                    var hDoc = XDocument.Load(hs);
                    ParseWordBodyElements(hDoc.Root, doc, zip, relMap, isHeaderOrFooter: true);
                }
            }

            var docEntry = zip.GetEntry("word/document.xml");
            if (docEntry != null)
            {
                using var ds = docEntry.Open();
                var mainDoc = XDocument.Load(ds);
                var body = mainDoc.Root?.Element(W + "body") ?? mainDoc.Root;
                if (body != null)
                {
                    ParseWordBodyElements(body, doc, zip, relMap, isHeaderOrFooter: false);
                }
            }

            // Load footers if present
            foreach (var footerName in new[] { "word/footer1.xml", "word/footer2.xml" })
            {
                var fEntry = zip.GetEntry(footerName);
                if (fEntry != null)
                {
                    using var fs = fEntry.Open();
                    var fDoc = XDocument.Load(fs);
                    ParseWordBodyElements(fDoc.Root, doc, zip, relMap, isHeaderOrFooter: true);
                }
            }
        }

        private static void ParseWordBodyElements(XElement? container, FlowDocument doc, ZipArchive zip, Dictionary<string, string> relMap, bool isHeaderOrFooter)
        {
            if (container == null) return;

            foreach (var el in container.Elements())
            {
                if (el.Name == W + "p")
                {
                    var p = ParseWordParagraph(el, zip, relMap, isHeaderOrFooter);
                    doc.Blocks.Add(p);
                }
                else if (el.Name == W + "tbl")
                {
                    var tbl = ParseWordTable(el, zip, relMap);
                    doc.Blocks.Add(tbl);
                }
                else if (el.Name == W + "sdt")
                {
                    var sdtContent = el.Element(W + "sdtContent");
                    if (sdtContent != null)
                    {
                        ParseWordBodyElements(sdtContent, doc, zip, relMap, isHeaderOrFooter);
                    }
                }
            }
        }

        private static Paragraph ParseWordParagraph(XElement pEl, ZipArchive zip, Dictionary<string, string> relMap, bool isHeaderOrFooter)
        {
            var p = new Paragraph
            {
                Margin = isHeaderOrFooter ? new Thickness(0, 2, 0, 4) : new Thickness(0, 0, 0, 6),
                Foreground = isHeaderOrFooter ? new SolidColorBrush(Color.FromRgb(71, 85, 105)) : Brushes.Black
            };

            var pPr = pEl.Element(W + "pPr");
            bool isHeading = false;
            int headingLevel = 0;

            if (pPr != null)
            {
                var bidi = pPr.Element(W + "bidi");
                if (bidi != null && bidi.Attribute(W + "val")?.Value != "0" && bidi.Attribute(W + "val")?.Value != "false")
                {
                    p.FlowDirection = FlowDirection.RightToLeft;
                }

                string? jc = pPr.Element(W + "jc")?.Attribute(W + "val")?.Value;
                p.TextAlignment = jc switch
                {
                    "center" => TextAlignment.Center,
                    "right" => TextAlignment.Right,
                    "left" => TextAlignment.Left,
                    "both" or "distribute" => TextAlignment.Justify,
                    _ => p.TextAlignment
                };

                string? pStyle = pPr.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
                if (!string.IsNullOrEmpty(pStyle))
                {
                    if (pStyle.Contains("Title", StringComparison.OrdinalIgnoreCase))
                    {
                        isHeading = true;
                        headingLevel = 1;
                    }
                    else if (pStyle.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) &&
                             int.TryParse(Regex.Match(pStyle, @"\d+").Value, out int lvl))
                    {
                        isHeading = true;
                        headingLevel = lvl;
                    }
                }

                if (pPr.Element(W + "numPr") != null)
                {
                    p.Inlines.Add(new Run("•  ") { FontWeight = FontWeights.Bold });
                }
            }

            if (isHeading)
            {
                p.FontWeight = FontWeights.Bold;
                p.FontSize = headingLevel switch
                {
                    1 => 20,
                    2 => 16.5,
                    3 => 14.5,
                    _ => 13.5
                };
                p.Margin = new Thickness(0, 10, 0, 6);
            }

            foreach (var child in pEl.Elements())
            {
                if (child.Name == W + "r")
                {
                    AppendWordRun(child, p, zip, relMap, isHeading);
                }
                else if (child.Name == W + "hyperlink")
                {
                    foreach (var rChild in child.Elements(W + "r"))
                    {
                        AppendWordRun(rChild, p, zip, relMap, isHeading, forceLinkStyle: true);
                    }
                }
                else if (child.Name == W + "sdt")
                {
                    var content = child.Element(W + "sdtContent");
                    if (content != null)
                    {
                        foreach (var rChild in content.Descendants(W + "r"))
                        {
                            AppendWordRun(rChild, p, zip, relMap, isHeading);
                        }
                    }
                }
            }

            // Detect Arabic characters to auto-align RTL when bidi tag wasn't explicit
            string combinedText = string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text));
            if (pPr?.Element(W + "bidi") == null && ContainsArabic(combinedText))
            {
                p.FlowDirection = FlowDirection.RightToLeft;
            }

            return p;
        }

        private static void AppendWordRun(XElement rEl, Paragraph p, ZipArchive zip, Dictionary<string, string> relMap, bool isHeading, bool forceLinkStyle = false)
        {
            var rPr = rEl.Element(W + "rPr");
            bool bold = isHeading || IsWordPropEnabled(rPr, W + "b");
            bool italic = IsWordPropEnabled(rPr, W + "i");
            bool underline = forceLinkStyle || (rPr?.Element(W + "u") != null && rPr.Element(W + "u")?.Attribute(W + "val")?.Value != "none");

            double? fontSizePt = null;
            string? szVal = rPr?.Element(W + "sz")?.Attribute(W + "val")?.Value ?? rPr?.Element(W + "szCs")?.Attribute(W + "val")?.Value;
            if (double.TryParse(szVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double halfPts) && halfPts > 0)
            {
                fontSizePt = (halfPts / 2.0) * (96.0 / 72.0);
            }

            Brush? fgBrush = forceLinkStyle ? new SolidColorBrush(Color.FromRgb(29, 78, 216)) : null;
            string? colorHex = rPr?.Element(W + "color")?.Attribute(W + "val")?.Value;
            if (!string.IsNullOrEmpty(colorHex) && colorHex.Length == 6 && !colorHex.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    byte cr = Convert.ToByte(colorHex.Substring(0, 2), 16);
                    byte cg = Convert.ToByte(colorHex.Substring(2, 2), 16);
                    byte cb = Convert.ToByte(colorHex.Substring(4, 2), 16);
                    fgBrush = new SolidColorBrush(Color.FromRgb(cr, cg, cb));
                }
                catch { }
            }

            // Check for embedded images inside drawings/pict
            foreach (var blip in rEl.Descendants(A + "blip"))
            {
                string? relId = blip.Attribute(R + "embed")?.Value ?? blip.Attribute(R + "link")?.Value;
                if (!string.IsNullOrEmpty(relId) && relMap.TryGetValue(relId, out string? imgTarget))
                {
                    AddEmbeddedZipImage(p, zip, imgTarget);
                }
            }
            foreach (var vImg in rEl.Descendants(V + "imagedata"))
            {
                string? relId = vImg.Attribute(R + "id")?.Value;
                if (!string.IsNullOrEmpty(relId) && relMap.TryGetValue(relId, out string? imgTarget))
                {
                    AddEmbeddedZipImage(p, zip, imgTarget);
                }
            }

            foreach (var node in rEl.Elements())
            {
                if (node.Name == W + "t" || node.Name == W + "delText")
                {
                    if (!string.IsNullOrEmpty(node.Value))
                    {
                        var run = new Run(node.Value);
                        if (bold) run.FontWeight = FontWeights.Bold;
                        if (italic) run.FontStyle = FontStyles.Italic;
                        if (underline) run.TextDecorations = TextDecorations.Underline;
                        if (fontSizePt.HasValue) run.FontSize = fontSizePt.Value;
                        if (fgBrush != null) run.Foreground = fgBrush;
                        p.Inlines.Add(run);
                    }
                }
                else if (node.Name == W + "tab")
                {
                    p.Inlines.Add(new Run("    "));
                }
                else if (node.Name == W + "br")
                {
                    p.Inlines.Add(new LineBreak());
                }
            }
        }

        private static bool IsWordPropEnabled(XElement? rPr, XName propName)
        {
            var el = rPr?.Element(propName);
            if (el == null) return false;
            string? val = el.Attribute(W + "val")?.Value;
            return !string.Equals(val, "0", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(val, "false", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddEmbeddedZipImage(Paragraph p, ZipArchive zip, string entryPath)
        {
            try
            {
                var entry = zip.GetEntry(entryPath) ??
                            zip.Entries.FirstOrDefault(e => e.FullName.Equals(entryPath, StringComparison.OrdinalIgnoreCase));
                if (entry == null) return;

                using var es = entry.Open();
                using var ms = new MemoryStream();
                es.CopyTo(ms);
                ms.Position = 0;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();

                double maxW = 580;
                double w = bmp.PixelWidth > maxW ? maxW : bmp.PixelWidth;
                var img = new Image
                {
                    Source = bmp,
                    Width = w,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 6, 0, 6)
                };
                p.Inlines.Add(new InlineUIContainer(img));
            }
            catch
            {
                // Ignore unsupported image codecs (e.g. EMF/WMF) gracefully
            }
        }

        private static Table ParseWordTable(XElement tblEl, ZipArchive zip, Dictionary<string, string> relMap)
        {
            var table = new Table
            {
                CellSpacing = 0,
                BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                BorderThickness = new Thickness(0.75),
                Margin = new Thickness(0, 6, 0, 12)
            };

            var trList = tblEl.Elements(W + "tr").ToList();
            int maxCols = trList.Select(tr => tr.Elements(W + "tc").Count()).DefaultIfEmpty(1).Max();
            for (int i = 0; i < maxCols; i++)
            {
                table.Columns.Add(new TableColumn());
            }

            var rg = new TableRowGroup();
            foreach (var trEl in trList)
            {
                var row = new TableRow();
                foreach (var tcEl in trEl.Elements(W + "tc"))
                {
                    var cell = new TableCell
                    {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                        BorderThickness = new Thickness(0.5),
                        Padding = new Thickness(6, 4, 6, 4)
                    };

                    string? shdFill = tcEl.Element(W + "tcPr")?.Element(W + "shd")?.Attribute(W + "fill")?.Value;
                    if (!string.IsNullOrEmpty(shdFill) && shdFill.Length == 6 && !shdFill.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            byte r = Convert.ToByte(shdFill.Substring(0, 2), 16);
                            byte g = Convert.ToByte(shdFill.Substring(2, 2), 16);
                            byte b = Convert.ToByte(shdFill.Substring(4, 2), 16);
                            cell.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
                        }
                        catch { }
                    }

                    bool hasBlocks = false;
                    foreach (var cellChild in tcEl.Elements())
                    {
                        if (cellChild.Name == W + "p")
                        {
                            cell.Blocks.Add(ParseWordParagraph(cellChild, zip, relMap, false));
                            hasBlocks = true;
                        }
                        else if (cellChild.Name == W + "tbl")
                        {
                            cell.Blocks.Add(ParseWordTable(cellChild, zip, relMap));
                            hasBlocks = true;
                        }
                    }

                    if (!hasBlocks)
                    {
                        cell.Blocks.Add(new Paragraph());
                    }

                    row.Cells.Add(cell);
                }
                rg.Rows.Add(row);
            }

            table.RowGroups.Add(rg);
            return table;
        }

        private static void LoadOdtIntoFlowDocument(FlowDocument doc, byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var contentEntry = zip.GetEntry("content.xml");
            if (contentEntry == null) return;

            using var cs = contentEntry.Open();
            var xdoc = XDocument.Load(cs);
            foreach (var node in xdoc.Descendants().Where(e => e.Name.LocalName == "p" || e.Name.LocalName == "h"))
            {
                string text = string.Concat(node.DescendantNodes().OfType<XText>().Select(t => t.Value)).Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    var p = new Paragraph(new Run(text))
                    {
                        Margin = new Thickness(0, 0, 0, 6),
                        FontWeight = node.Name.LocalName == "h" ? FontWeights.Bold : FontWeights.Normal,
                        FontSize = node.Name.LocalName == "h" ? 16 : 13
                    };
                    if (ContainsArabic(text)) p.FlowDirection = FlowDirection.RightToLeft;
                    doc.Blocks.Add(p);
                }
            }
        }

        private static void LoadHtmlOrXmlTextIntoFlowDocument(FlowDocument doc, string markup)
        {
            string bodyOnly = Regex.Replace(markup, @"<(script|style|head)[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
            bodyOnly = Regex.Replace(bodyOnly, @"<(br|p|div|li|tr|h[1-6])[^>]*>", "\n", RegexOptions.IgnoreCase);
            bodyOnly = Regex.Replace(bodyOnly, @"<td[^>]*>", "  |  ", RegexOptions.IgnoreCase);
            bodyOnly = Regex.Replace(bodyOnly, @"<[^>]+>", "");
            bodyOnly = System.Net.WebUtility.HtmlDecode(bodyOnly);

            foreach (var rawLine in bodyOnly.Split('\n'))
            {
                string line = rawLine.Trim();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var p = new Paragraph(new Run(line)) { Margin = new Thickness(0, 0, 0, 5) };
                    if (ContainsArabic(line)) p.FlowDirection = FlowDirection.RightToLeft;
                    doc.Blocks.Add(p);
                }
            }
        }

        private static void LoadLegacyDocTextIntoFlowDocument(FlowDocument doc, byte[] bytes, bool isAr)
        {
            // Extract readable Unicode (UTF-16LE) and Arabic Windows-1256 runs from binary OLE2 .doc
            var extractedLines = new List<string>();

            // 1. Scan for UTF-16LE segments (most modern Word .doc files store Arabic & English in UTF-16LE)
            string utf16Raw = Encoding.Unicode.GetString(bytes);
            foreach (Match m in Regex.Matches(utf16Raw, @"[\u0600-\u06FF\u0750-\u077F\u0020-\u007E]{4,}"))
            {
                string candidate = m.Value.Trim();
                if (IsMeaningfulExtractedLine(candidate))
                {
                    extractedLines.Add(candidate);
                }
            }

            // 2. If few UTF-16 runs were found, also scan ASCII / Windows-1256
            if (extractedLines.Count < 3)
            {
                Encoding cp1256 = Encoding.GetEncoding(1256);
                string raw1256 = cp1256.GetString(bytes);
                foreach (Match m in Regex.Matches(raw1256, @"[\u0600-\u06FF\u0020-\u007E]{5,}"))
                {
                    string candidate = m.Value.Trim();
                    if (IsMeaningfulExtractedLine(candidate) && !extractedLines.Contains(candidate))
                    {
                        extractedLines.Add(candidate);
                    }
                }
            }

            foreach (string line in extractedLines.Distinct())
            {
                var p = new Paragraph(new Run(line)) { Margin = new Thickness(0, 0, 0, 6) };
                if (ContainsArabic(line)) p.FlowDirection = FlowDirection.RightToLeft;
                doc.Blocks.Add(p);
            }
        }

        private static bool IsMeaningfulExtractedLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 4) return false;
            string[] ignoredMarkers =
            {
                "Microsoft Word", "Root Entry", "WordDocument", "1Table", "0Table",
                "SummaryInformation", "DocumentSummaryInformation", "CompObj", "MsoDataStore",
                "Normal.dotm", "Times New Roman", "Cambria Math", "Calibri"
            };
            foreach (string marker in ignoredMarkers)
            {
                if (text.Equals(marker, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return text.Any(char.IsLetter);
        }

        private static bool ContainsArabic(string text)
        {
            foreach (char c in text)
            {
                if (c >= '\u0600' && c <= '\u06FF') return true;
            }
            return false;
        }

        #endregion

        #region Printing & Direct PDF Export Pipeline

        public static void PrintOrExportFlowDocument(
            PrintDialog printDlg,
            FlowDocument sourceDoc,
            string jobTitle,
            Window? owner,
            Func<FlowDocument>? freshDocFactory = null,
            byte[]? rawWordBytes = null,
            string? wordExt = null)
        {
            bool isAr = LocalizationService.Instance.IsArabic;

            if (IsVirtualPdfPrinter(printDlg.PrintQueue))
            {
                var saveDlg = new SaveFileDialog
                {
                    Title = isAr ? "حفظ المستند بصيغة PDF" : "Save Document as PDF",
                    Filter = "PDF Document (*.pdf)|*.pdf",
                    DefaultExt = ".pdf",
                    AddExtension = true,
                    FileName = $"{SanitizeFileName(jobTitle)}.pdf"
                };

                if (saveDlg.ShowDialog(owner) != true)
                    return;

                try
                {
                    bool exportedViaWord = false;
                    if (rawWordBytes != null && !string.IsNullOrEmpty(wordExt))
                    {
                        exportedViaWord = TryExportWordBytesToPdfViaOffice(rawWordBytes, wordExt, saveDlg.FileName);
                    }

                    if (!exportedViaWord)
                    {
                        FlowDocument printDoc = freshDocFactory != null
                            ? freshDocFactory()
                            : CloneFlowDocument(sourceDoc);
                        ExportFlowDocumentToPdfFile(printDoc, saveDlg.FileName);
                    }

                    VerifyAndAnnouncePdfSaved(saveDlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        isAr ? $"تعذر حفظ ملف PDF:\n{ex.Message}" : $"Failed to save PDF file:\n{ex.Message}",
                        LocalizationService.Instance["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                // Physical printer: always paginate a fresh detached FlowDocument with a sanitized title
                FlowDocument printDoc = freshDocFactory != null
                    ? freshDocFactory()
                    : CloneFlowDocument(sourceDoc);

                printDoc.Background = Brushes.White;
                printDoc.PageWidth = printDlg.PrintableAreaWidth > 0 ? printDlg.PrintableAreaWidth : 793.7;
                printDoc.PageHeight = printDlg.PrintableAreaHeight > 0 ? printDlg.PrintableAreaHeight : 1122.5;
                printDoc.ColumnWidth = printDoc.PageWidth;

                string safeTitle = SanitizePrintJobTitle(jobTitle);
                printDlg.PrintDocument(((IDocumentPaginatorSource)printDoc).DocumentPaginator, safeTitle);
            }
        }

        public static void SavePdfFromBytesFileDialog(Window? owner, byte[] pdfBytes, string originalFileName)
        {
            bool isAr = LocalizationService.Instance.IsArabic;
            var saveDlg = new SaveFileDialog
            {
                Title = isAr ? "حفظ نسخة PDF من المستند" : "Save Copy of PDF Document",
                Filter = "PDF Document (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                AddExtension = true,
                FileName = $"{SanitizeFileName(originalFileName)}.pdf"
            };

            if (saveDlg.ShowDialog(owner) != true)
                return;

            try
            {
                File.WriteAllBytes(saveDlg.FileName, pdfBytes);
                VerifyAndAnnouncePdfSaved(saveDlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    isAr ? $"تعذر حفظ ملف PDF:\n{ex.Message}" : $"Failed to save PDF file:\n{ex.Message}",
                    LocalizationService.Instance["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void SaveBitmapToPdfFileDialog(Window? owner, BitmapSource bitmap, string originalFileName)
        {
            bool isAr = LocalizationService.Instance.IsArabic;
            var saveDlg = new SaveFileDialog
            {
                Title = isAr ? "حفظ الصورة بصيغة PDF" : "Save Image as PDF",
                Filter = "PDF Document (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                AddExtension = true,
                FileName = $"{SanitizeFileName(originalFileName)}.pdf"
            };

            if (saveDlg.ShowDialog(owner) != true)
                return;

            try
            {
                ExportBitmapsToPdfFile(new[] { bitmap }, saveDlg.FileName);
                VerifyAndAnnouncePdfSaved(saveDlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    isAr ? $"تعذر تصدير الصورة إلى PDF:\n{ex.Message}" : $"Failed to export image to PDF:\n{ex.Message}",
                    LocalizationService.Instance["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static bool PrintExistingPdfToPhysicalPrinter(PrintDialog printDlg, string pdfFilePath)
        {
            bool isAr = LocalizationService.Instance.IsArabic;
            if (!File.Exists(pdfFilePath)) return false;

            string printerName = printDlg.PrintQueue?.FullName ?? string.Empty;
            if (string.IsNullOrEmpty(printerName)) return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = pdfFilePath,
                    Verb = "printto",
                    Arguments = $"\"{printerName}\"",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    isAr
                        ? $"تعذر إرسال ملف PDF مباشرة للطابعة المحددة ({ex.Message}).\nيمكنك فتح الملف عبر زر (فتح خارجي) ثم طباعته."
                        : $"Unable to send PDF directly to the selected printer ({ex.Message}).\nYou can open the file externally and print it.",
                    LocalizationService.Instance["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        public static void VerifyAndAnnouncePdfSaved(string filePath)
        {
            bool isAr = LocalizationService.Instance.IsArabic;
            var fi = new FileInfo(filePath);
            if (!fi.Exists || fi.Length == 0)
            {
                throw new IOException(isAr
                    ? "تم إنشاء الملف ولكنه فارغ (0 بايت)."
                    : "The output file was created but is empty (0 bytes).");
            }

            string kb = (fi.Length / 1024.0).ToString("N1", CultureInfo.InvariantCulture);
            var res = MessageBox.Show(
                isAr
                    ? $"تم حفظ ملف PDF بنجاح وبشكل كامل!\n\nالمسار:\n{filePath}\nالحجم: {kb} كيلوبايت\n\nهل ترغب في فتح الملف الآن للتحقق منه؟"
                    : $"PDF file saved successfully!\n\nPath:\n{filePath}\nSize: {kb} KB\n\nWould you like to open the file now?",
                LocalizationService.Instance["Success"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (res == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
        }

        private static bool TryExportWordBytesToPdfViaOffice(byte[] wordBytes, string ext, string outputPdfPath)
        {
            Type? wordType = Type.GetTypeFromProgID("Word.Application");
            if (wordType == null) return false;

            string tempDir = Path.Combine(Path.GetTempPath(), "DocuArchive_WordPdfExport");
            Directory.CreateDirectory(tempDir);
            string tempInput = Path.Combine(tempDir, $"src_{Guid.NewGuid():N}{ext}");

            dynamic? wordApp = null;
            dynamic? wordDoc = null;
            try
            {
                File.WriteAllBytes(tempInput, wordBytes);
                wordApp = Activator.CreateInstance(wordType);
                if (wordApp == null) return false;

                wordApp.Visible = false;
                wordApp.DisplayAlerts = 0; // wdAlertsNone

                wordDoc = wordApp.Documents.Open(tempInput, ReadOnly: true, Visible: false);
                wordDoc.ExportAsFixedFormat(
                    OutputFileName: outputPdfPath,
                    ExportFormat: 17, // wdExportFormatPDF
                    OpenAfterExport: false);

                return File.Exists(outputPdfPath) && new FileInfo(outputPdfPath).Length > 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    if (wordDoc != null)
                    {
                        wordDoc.Close(false);
                        Marshal.FinalReleaseComObject(wordDoc);
                    }
                }
                catch { }

                try
                {
                    if (wordApp != null)
                    {
                        wordApp.Quit(false);
                        Marshal.FinalReleaseComObject(wordApp);
                    }
                }
                catch { }

                try
                {
                    if (File.Exists(tempInput)) File.Delete(tempInput);
                }
                catch { }
            }
        }

        public static FlowDocument CloneFlowDocument(FlowDocument source)
        {
            try
            {
                string xaml = XamlWriter.Save(source);
                using var sr = new StringReader(xaml);
                using var xr = System.Xml.XmlReader.Create(sr);
                if (XamlReader.Load(xr) is FlowDocument clone)
                {
                    clone.Background = Brushes.White;
                    return clone;
                }
            }
            catch
            {
                // Fallback if document contains non-serializable InlineUIContainer images
            }

            // Manual shallow-block clone fallback
            var fallback = new FlowDocument
            {
                Background = Brushes.White,
                Foreground = Brushes.Black,
                FlowDirection = source.FlowDirection,
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                PagePadding = source.PagePadding
            };
            string plain = new TextRange(source.ContentStart, source.ContentEnd).Text;
            fallback.Blocks.Add(new Paragraph(new Run(plain)));
            return fallback;
        }

        public static void ExportFlowDocumentToPdfFile(FlowDocument doc, string outputPdfPath)
        {
            // A4 at 96 DIP: 793.7 x 1122.5
            const double pageWidthDip = 794.0;
            const double pageHeightDip = 1123.0;
            const double renderDpi = 192.0; // Crisp 2x print quality
            double scale = renderDpi / 96.0;

            doc.Background = Brushes.White;
            doc.PageWidth = pageWidthDip;
            doc.PageHeight = pageHeightDip;
            doc.ColumnWidth = pageWidthDip;
            if (doc.PagePadding.Left == 0 && doc.PagePadding.Top == 0)
            {
                doc.PagePadding = new Thickness(40);
            }

            DocumentPaginator paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
            paginator.PageSize = new Size(pageWidthDip, pageHeightDip);
            paginator.ComputePageCount();

            int count = Math.Max(1, paginator.PageCount);
            var renderedPages = new List<BitmapSource>(count);

            for (int i = 0; i < count; i++)
            {
                using DocumentPage page = paginator.GetPage(i);
                var host = new DrawingVisual();
                using (var dc = host.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageWidthDip, pageHeightDip));
                    if (page.Visual != null)
                    {
                        var vb = new VisualBrush(page.Visual)
                        {
                            Stretch = Stretch.None,
                            AlignmentX = AlignmentX.Left,
                            AlignmentY = AlignmentY.Top
                        };
                        dc.DrawRectangle(vb, null, new Rect(0, 0, page.Size.Width, page.Size.Height));
                    }
                }

                int pxW = (int)Math.Round(pageWidthDip * scale);
                int pxH = (int)Math.Round(pageHeightDip * scale);
                var rtb = new RenderTargetBitmap(pxW, pxH, renderDpi, renderDpi, PixelFormats.Pbgra32);
                rtb.Render(host);
                rtb.Freeze();
                renderedPages.Add(rtb);
            }

            ExportBitmapsToPdfFile(renderedPages, outputPdfPath);
        }

        /// <summary>
        /// Writes a standards-compliant PDF 1.4 file with one or more A4 pages containing high-quality JPEG streams.
        /// Guaranteed non-empty and compatible with Adobe Acrobat, Foxit, Chrome, Edge, and Windows Preview.
        /// </summary>
        public static void ExportBitmapsToPdfFile(IList<BitmapSource> pages, string outputPdfPath)
        {
            if (pages == null || pages.Count == 0)
                throw new InvalidOperationException("No pages to export.");

            string? dir = Path.GetDirectoryName(outputPdfPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // A4 size in PDF points (72 points/inch): 595 x 842
            const double a4WidthPt = 595.0;
            const double a4HeightPt = 842.0;

            var pageImageBytes = new List<(byte[] JpegBytes, int PixelW, int PixelH, double DrawW, double DrawH, double DrawX, double DrawY)>();
            foreach (var bmp in pages)
            {
                // Flatten onto white background so transparent PNGs never turn black in JPEG
                int w = Math.Max(1, bmp.PixelWidth);
                int h = Math.Max(1, bmp.PixelHeight);

                var whiteDv = new DrawingVisual();
                using (var dc = whiteDv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                    dc.DrawImage(bmp, new Rect(0, 0, w, h));
                }
                var flat = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                flat.Render(whiteDv);

                var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
                encoder.Frames.Add(BitmapFrame.Create(flat));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                byte[] jpeg = ms.ToArray();

                // Fit image inside A4 page with slight safety fit while preserving aspect ratio
                double ratio = Math.Min(a4WidthPt / w, a4HeightPt / h);
                double drawW = w * ratio;
                double drawH = h * ratio;
                double drawX = (a4WidthPt - drawW) / 2.0;
                double drawY = (a4HeightPt - drawH) / 2.0;

                pageImageBytes.Add((jpeg, w, h, drawW, drawH, drawX, drawY));
            }

            // Build PDF object table
            // Obj 1: Catalog
            // Obj 2: Pages
            // For page i (0..N-1):
            //   pageObj = 3 + i * 3
            //   imgObj  = 4 + i * 3
            //   cntObj  = 5 + i * 3
            int pageCount = pageImageBytes.Count;
            int totalObjects = 2 + pageCount * 3;
            var offsets = new long[totalObjects + 1];

            using var fs = new FileStream(outputPdfPath, FileMode.Create, FileAccess.Write, FileShare.None);

            void WriteAscii(string s)
            {
                byte[] b = Encoding.ASCII.GetBytes(s);
                fs.Write(b, 0, b.Length);
            }

            WriteAscii("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

            // 1: Catalog
            offsets[1] = fs.Position;
            WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

            // 2: Pages
            var kids = new StringBuilder();
            for (int i = 0; i < pageCount; i++)
            {
                kids.Append(3 + i * 3).Append(" 0 R ");
            }
            offsets[2] = fs.Position;
            WriteAscii($"2 0 obj\n<< /Type /Pages /Kids [ {kids}] /Count {pageCount} >>\nendobj\n");

            for (int i = 0; i < pageCount; i++)
            {
                int pageObj = 3 + i * 3;
                int imgObj = 4 + i * 3;
                int cntObj = 5 + i * 3;
                var info = pageImageBytes[i];

                // Page object
                offsets[pageObj] = fs.Position;
                WriteAscii(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {1:F2} {2:F2}] /Resources << /XObject << /Im0 {3} 0 R >> >> /Contents {4} 0 R >>\nendobj\n",
                    pageObj, a4WidthPt, a4HeightPt, imgObj, cntObj));

                // Image XObject
                offsets[imgObj] = fs.Position;
                WriteAscii(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} 0 obj\n<< /Type /XObject /Subtype /Image /Width {1} /Height {2} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {3} >>\nstream\n",
                    imgObj, info.PixelW, info.PixelH, info.JpegBytes.Length));
                fs.Write(info.JpegBytes, 0, info.JpegBytes.Length);
                WriteAscii("\nendstream\nendobj\n");

                // Page content stream
                string contentStream = string.Format(
                    CultureInfo.InvariantCulture,
                    "q\n{0:F2} 0 0 {1:F2} {2:F2} {3:F2} cm\n/Im0 Do\nQ\n",
                    info.DrawW, info.DrawH, info.DrawX, info.DrawY);
                byte[] contentBytes = Encoding.ASCII.GetBytes(contentStream);

                offsets[cntObj] = fs.Position;
                WriteAscii($"{cntObj} 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
                fs.Write(contentBytes, 0, contentBytes.Length);
                WriteAscii("\nendstream\nendobj\n");
            }

            long xrefPos = fs.Position;
            WriteAscii($"xref\n0 {totalObjects + 1}\n");
            WriteAscii("0000000000 65535 f \n");
            for (int i = 1; i <= totalObjects; i++)
            {
                WriteAscii(string.Format(CultureInfo.InvariantCulture, "{0:D10} 00000 n \n", offsets[i]));
            }

            WriteAscii($"trailer\n<< /Size {totalObjects + 1} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n");
            fs.Flush();
        }

        #endregion
    }
}
