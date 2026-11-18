using System.Collections.Generic;
using System.Threading.Tasks;

namespace DocuArchivePro.Services
{
    public class ScannerDeviceInfo
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public override string ToString() => Name;
    }

    public enum ScanColorMode
    {
        Color = 1,      // ألوان 24-bit
        Grayscale = 2,  // تدرج رمادي 8-bit
        BlackAndWhite = 4 // أبيض وأسود (فاكس/نصوص) 1-bit
    }

    public class ScanParameters
    {
        public string? DeviceId { get; set; }
        public ScanColorMode ColorMode { get; set; } = ScanColorMode.Color;
        public int ResolutionDpi { get; set; } = 300;
        public bool UseFeeder { get; set; } = false;
        public string OutputFormat { get; set; } = "PNG"; // PNG, JPEG, TIFF, PDF
    }

    public interface IScannerService
    {
        Task<List<ScannerDeviceInfo>> GetAvailableScannersAsync();
        Task<byte[]> ScanDocumentAsync(ScanParameters parameters);
        bool IsWiaAvailable();
    }
}
