using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DocuArchivePro.Services
{
    public static class BarcodeQrGenerator
    {
        public static BitmapSource GenerateDocumentQrAndBarcodeStamp(
            string registrationNumber,
            DateTime registrationDate,
            string docType,
            string sha256Digest = "")
        {
            const int widthDip = 440;
            const int heightDip = 126;
            const double dpi = 192.0;
            double scale = dpi / 96.0;

            string safeReg = string.IsNullOrWhiteSpace(registrationNumber) ? "DOC-0000" : registrationNumber.Trim();
            string payload = $"DOCUARCHIVE|{safeReg}|{docType}|{registrationDate:yyyy-MM-dd}|{sha256Digest}";

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // White card background with subtle slate border
                var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(203, 213, 225)), 1.0);
                dc.DrawRoundedRectangle(Brushes.White, borderPen, new Rect(0.5, 0.5, widthDip - 1, heightDip - 1), 6, 6);

                // 1. Draw 25x25 QR Matrix on the left
                DrawQrMatrix(dc, payload, originX: 12, originY: 11, matrixPixelSize: 104);

                // 2. Draw Linear Code-128 Style Barcode on the right
                double barAreaX = 132;
                double barAreaY = 16;
                double barAreaW = 294;
                double barAreaH = 54;
                DrawLinearBarcode(dc, safeReg, barAreaX, barAreaY, barAreaW, barAreaH);

                // 3. Draw human-readable labels underneath linear barcode
                var typeface = new Typeface(new FontFamily("Consolas, Segoe UI, Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                var regText = new FormattedText(
                    safeReg,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    14,
                    Brushes.Black,
                    1.0);
                dc.DrawText(regText, new Point(barAreaX + (barAreaW - regText.Width) / 2.0, 74));

                var subTypeface = new Typeface(new FontFamily("Segoe UI, Arial"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
                string metaLine = $"{docType}  |  {registrationDate:yyyy-MM-dd}";
                var metaText = new FormattedText(
                    metaLine,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    subTypeface,
                    10.5,
                    new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                    1.0);
                dc.DrawText(metaText, new Point(barAreaX + (barAreaW - metaText.Width) / 2.0, 95));
            }

            var rtb = new RenderTargetBitmap(
                (int)Math.Round(widthDip * scale),
                (int)Math.Round(heightDip * scale),
                dpi,
                dpi,
                PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private static void DrawQrMatrix(DrawingContext dc, string payload, double originX, double originY, double matrixPixelSize)
        {
            const int n = 25;
            bool[,] modules = new bool[n, n];
            bool[,] reserved = new bool[n, n];

            void PlaceFinder(int startRow, int startCol)
            {
                for (int r = -1; r <= 7; r++)
                {
                    for (int c = -1; c <= 7; c++)
                    {
                        int rr = startRow + r;
                        int cc = startCol + c;
                        if (rr < 0 || rr >= n || cc < 0 || cc >= n) continue;

                        reserved[rr, cc] = true;
                        bool isBorderOrInner =
                            (r >= 0 && r <= 6 && c >= 0 && c <= 6) &&
                            (r == 0 || r == 6 || c == 0 || c == 6 || (r >= 2 && r <= 4 && c >= 2 && c <= 4));
                        modules[rr, cc] = isBorderOrInner;
                    }
                }
            }

            PlaceFinder(0, 0);
            PlaceFinder(0, n - 7);
            PlaceFinder(n - 7, 0);

            // Alignment pattern (5x5) near bottom-right (16, 16)
            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 5; c++)
                {
                    int rr = 16 + r;
                    int cc = 16 + c;
                    reserved[rr, cc] = true;
                    modules[rr, cc] = (r == 0 || r == 4 || c == 0 || c == 4 || (r == 2 && c == 2));
                }
            }

            // Timing patterns
            for (int i = 8; i < n - 8; i++)
            {
                if (!reserved[6, i])
                {
                    reserved[6, i] = true;
                    modules[6, i] = (i % 2 == 0);
                }
                if (!reserved[i, 6])
                {
                    reserved[i, 6] = true;
                    modules[i, 6] = (i % 2 == 0);
                }
            }

            // Fill remaining modules deterministically from SHA-256 + payload bytes
            byte[] seedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
            int bitCursor = 0;

            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    if (reserved[r, c]) continue;

                    byte b1 = seedBytes[(bitCursor / 8) % seedBytes.Length];
                    byte b2 = payloadBytes[(bitCursor / 3) % Math.Max(1, payloadBytes.Length)];
                    int bit = ((b1 ^ (b2 << (bitCursor % 4))) >> (bitCursor % 8)) & 1;
                    // Apply standard checkerboard mask (r + c) % 2 == 0
                    bool masked = (bit == 1) ^ (((r + c) % 2) == 0);
                    modules[r, c] = masked;
                    bitCursor++;
                }
            }

            double cell = matrixPixelSize / n;
            dc.DrawRectangle(Brushes.White, null, new Rect(originX, originY, matrixPixelSize, matrixPixelSize));
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    if (modules[r, c])
                    {
                        dc.DrawRectangle(Brushes.Black, null, new Rect(originX + c * cell, originY + r * cell, cell + 0.15, cell + 0.15));
                    }
                }
            }
        }

        private static void DrawLinearBarcode(DrawingContext dc, string text, double x, double y, double totalWidth, double height)
        {
            // Build bar pattern: Start guard (101) + encoded characters + Stop guard (1011)
            var pattern = new StringBuilder("11010010000");
            foreach (char ch in text)
            {
                int val = (ch & 0x7F) % 64;
                // Deterministic 11-bit bar pattern with balanced density
                pattern.Append("1");
                for (int b = 5; b >= 0; b--)
                {
                    pattern.Append(((val >> b) & 1) == 1 ? "11" : "0");
                }
                pattern.Append("010");
            }
            pattern.Append("1100011101011");

            string bits = pattern.ToString();
            double moduleW = totalWidth / bits.Length;

            for (int i = 0; i < bits.Length; i++)
            {
                if (bits[i] == '1')
                {
                    dc.DrawRectangle(Brushes.Black, null, new Rect(x + i * moduleW, y, Math.Max(0.8, moduleW), height));
                }
            }
        }
    }
}
