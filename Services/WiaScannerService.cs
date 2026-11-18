using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace DocuArchivePro.Services
{
    public class WiaScannerService : IScannerService
    {
        // WIA Format GUIDs
        private const string WiaFormatBMP = "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}";
        private const string WiaFormatPNG = "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}";
        private const string WiaFormatJPEG = "{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}";
        private const string WiaFormatTIFF = "{B96B3CB1-0728-11D3-9D7B-0000F81EF32E}";

        // Common WIA HResult codes
        private const int WIA_ERROR_PAPER_EMPTY = unchecked((int)0x80210003);
        private const int WIA_ERROR_OFFLINE = unchecked((int)0x80210005);
        private const int WIA_ERROR_BUSY = unchecked((int)0x80210006);
        private const int WIA_S_NO_DEVICE_AVAILABLE = unchecked((int)0x80210015);
        private const int WIA_ERROR_USER_CANCELLED = unchecked((int)0x80210064);

        public const string InteractiveDialogDeviceId = "WINDOWS_INTERACTIVE_WIA_DIALOG";

        public bool IsWiaAvailable()
        {
            try
            {
                var type = Type.GetTypeFromProgID("WIA.DeviceManager");
                return type != null;
            }
            catch
            {
                return false;
            }
        }

        public Task<List<ScannerDeviceInfo>> GetAvailableScannersAsync()
        {
            return Task.Run(() =>
            {
                bool isAr = LocalizationService.Instance.IsArabic;
                var list = new List<ScannerDeviceInfo>();

                // 1. Enumerate active WIA Scanners and Imaging Devices
                try
                {
                    var mgrType = Type.GetTypeFromProgID("WIA.DeviceManager");
                    if (mgrType != null)
                    {
                        dynamic? deviceManagerObj = Activator.CreateInstance(mgrType);
                        if (deviceManagerObj is not null)
                        {
                            dynamic deviceManager = deviceManagerObj;
                            foreach (dynamic info in deviceManager.DeviceInfos)
                            {
                                try
                                {
                                    string id = (string)info.DeviceID;
                                    string name = GetWiaPropertyString(info.Properties, 7, "Name");
                                    string desc = GetWiaPropertyString(info.Properties, 4, "Description");

                                    if (string.IsNullOrWhiteSpace(name))
                                    {
                                        name = !string.IsNullOrWhiteSpace(desc) ? desc : id;
                                    }

                                    list.Add(new ScannerDeviceInfo
                                    {
                                        DeviceId = id,
                                        Name = isAr ? $"{name} (متصل - WIA)" : $"{name} (Connected - WIA)",
                                        Description = string.IsNullOrWhiteSpace(desc)
                                            ? (isAr ? "ماسح ضوئي معرف عبر خدمة Windows WIA" : "Scanner detected via Windows WIA service")
                                            : desc
                                    });
                                }
                                catch
                                {
                                    // Skip malformed device entry
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"WIA Device Enumeration Warning: {ex.Message}");
                }

                // 2. Enumerate Pre-Defined / Installed Scanners in Windows Registry (StillImage Class {6bdd1fc6-810f-11d0-bec7-08002be2092f})
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{6bdd1fc6-810f-11d0-bec7-08002be2092f}");
                    if (key != null)
                    {
                        foreach (string subKeyName in key.GetSubKeyNames())
                        {
                            if (subKeyName.Length == 4 && int.TryParse(subKeyName, out _))
                            {
                                using var subKey = key.OpenSubKey(subKeyName);
                                string? driverDesc = subKey?.GetValue("DriverDesc")?.ToString();
                                string? friendlyName = subKey?.GetValue("FriendlyName")?.ToString() ?? driverDesc;
                                string? provider = subKey?.GetValue("ProviderName")?.ToString() ?? "Windows Imaging";

                                if (!string.IsNullOrWhiteSpace(friendlyName) &&
                                    !list.Any(s => s.Name.Contains(friendlyName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    list.Add(new ScannerDeviceInfo
                                    {
                                        DeviceId = $"WIN_DEF_SCANNER_{subKeyName}",
                                        Name = isAr ? $"{friendlyName} (معرّف على الجهاز)" : $"{friendlyName} (Installed on PC)",
                                        Description = isAr ? $"المزود: {provider} - معرف في النظام" : $"Provider: {provider} - System Registered"
                                    });
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore registry permission restrictions if any
                }

                // 3. Add Interactive Windows Scanner Selector option
                list.Add(new ScannerDeviceInfo
                {
                    DeviceId = InteractiveDialogDeviceId,
                    Name = isAr
                        ? "نافذة اختيار السكانر القياسية في الويندوز (Windows WIA / TWAIN Dialog)"
                        : "Standard Windows Scanner Selector (WIA / TWAIN Dialog)",
                    Description = isAr
                        ? "فتح مربع حوار أجهزة المسح الضوئي المعرفة في نظام التشغيل ويندوز مباشرة"
                        : "Opens native Windows scanning hardware dialog directly"
                });

                return list;
            });
        }

        public Task<byte[]> ScanDocumentAsync(ScanParameters parameters)
        {
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() =>
            {
                bool isAr = LocalizationService.Instance.IsArabic;
                try
                {
                    byte[] bytes = ExecuteWiaScanOnStaThread(parameters, isAr);
                    tcs.TrySetResult(bytes);
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    var translated = TranslateWiaException(ex, isAr);
                    if (translated is OperationCanceledException)
                    {
                        tcs.TrySetCanceled();
                    }
                    else
                    {
                        tcs.TrySetException(translated);
                    }
                }
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return tcs.Task;
        }

        private static byte[] ExecuteWiaScanOnStaThread(ScanParameters parameters, bool isAr)
        {
            var dialogType = Type.GetTypeFromProgID("WIA.CommonDialog");
            var mgrType = Type.GetTypeFromProgID("WIA.DeviceManager");
            if (dialogType == null || mgrType == null)
            {
                throw new InvalidOperationException(isAr
                    ? "خدمة المسح الضوئي (Windows WIA) غير مفعلة على هذا النظام."
                    : "Windows Image Acquisition (WIA) service is not available on this system.");
            }

            dynamic? dialogObj = Activator.CreateInstance(dialogType);
            dynamic? deviceManagerObj = Activator.CreateInstance(mgrType);
            if (dialogObj is null || deviceManagerObj is null)
            {
                throw new InvalidOperationException(isAr
                    ? "تعذر تهيئة مكونات المسح الضوئي في الويندوز."
                    : "Failed to initialize Windows scanning components.");
            }

            dynamic dialog = dialogObj;
            dynamic deviceManager = deviceManagerObj;

            dynamic? targetDevice = null;
            bool useInteractiveDialog = parameters.DeviceId == InteractiveDialogDeviceId;

            if (!useInteractiveDialog)
            {
                dynamic? firstDeviceInfo = null;
                foreach (dynamic info in deviceManager.DeviceInfos)
                {
                    firstDeviceInfo ??= info;
                    string devId = string.Empty;
                    try { devId = (string)info.DeviceID; } catch { }

                    if (!string.IsNullOrEmpty(parameters.DeviceId) &&
                        string.Equals(devId, parameters.DeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        targetDevice = info.Connect();
                        break;
                    }
                }

                // Fallback to the first connected WIA scanner if WIN_DEF_SCANNER_* or legacy ID was stored
                if (targetDevice is null && firstDeviceInfo is not null)
                {
                    targetDevice = firstDeviceInfo.Connect();
                }
            }

            if (targetDevice is not null)
            {
                dynamic connectedDevice = targetDevice;
                ConfigureDeviceHandling(connectedDevice, parameters.UseFeeder);

                dynamic? itemObj = null;
                foreach (dynamic subItem in connectedDevice.Items)
                {
                    itemObj = subItem;
                    break;
                }

                if (itemObj is null)
                {
                    throw new InvalidOperationException(isAr
                        ? "لم يتم العثور على وحدة مسح ضوئي صالحة داخل جهاز السكانر المتصل."
                        : "No scannable item was found on the connected scanner device.");
                }

                dynamic item = itemObj;
                SetWiaProperty(item.Properties, 6146, (int)parameters.ColorMode); // Current Intent
                SetWiaProperty(item.Properties, 6147, parameters.ResolutionDpi);  // Horizontal Resolution
                SetWiaProperty(item.Properties, 6148, parameters.ResolutionDpi);  // Vertical Resolution

                if (parameters.UseFeeder)
                {
                    var acquiredImages = new List<dynamic>();

                    dynamic? firstPage = null;
                    try
                    {
                        firstPage = dialog.ShowTransfer(item, WiaFormatBMP, false);
                    }
                    catch (Exception transferEx)
                    {
                        if (IsCancelOrPaperEmptyError(transferEx))
                        {
                            throw;
                        }
                        firstPage = item.Transfer(WiaFormatBMP);
                    }

                    if (firstPage is null)
                    {
                        throw new OperationCanceledException(isAr
                            ? "تم إلغاء عملية المسح الضوئي."
                            : "Scanning operation was cancelled.");
                    }

                    acquiredImages.Add(firstPage);

                    const int maxFeederPages = 100;
                    for (int pageIdx = 1; pageIdx < maxFeederPages; pageIdx++)
                    {
                        try
                        {
                            dynamic? nextPage = item.Transfer(WiaFormatBMP);
                            if (nextPage is null) break;
                            acquiredImages.Add(nextPage);
                        }
                        catch (Exception nextEx)
                        {
                            var comEx = FindComException(nextEx);
                            if (comEx != null && (comEx.HResult == WIA_ERROR_PAPER_EMPTY || comEx.HResult == WIA_ERROR_USER_CANCELLED))
                            {
                                break;
                            }
                            break;
                        }
                    }

                    if (acquiredImages.Count == 1)
                    {
                        return ExtractPngBytesFromWiaImage(acquiredImages[0]);
                    }

                    return ExtractMultiPagePdfBytesFromWiaImages(acquiredImages);
                }
                else
                {
                    dynamic? scannedImage = null;
                    try
                    {
                        scannedImage = dialog.ShowTransfer(item, WiaFormatBMP, false);
                    }
                    catch (Exception transferEx)
                    {
                        if (IsCancelOrPaperEmptyError(transferEx))
                        {
                            throw;
                        }
                        // Fallback to direct transfer when UI transfer dialog fails on specific drivers
                        scannedImage = item.Transfer(WiaFormatBMP);
                    }

                    if (scannedImage is null)
                    {
                        throw new OperationCanceledException(isAr
                            ? "تم إلغاء عملية المسح الضوئي."
                            : "Scanning operation was cancelled.");
                    }

                    return ExtractPngBytesFromWiaImage(scannedImage);
                }
            }
            else
            {
                // Interactive Windows scanner selection dialog
                dynamic? imageFile = dialog.ShowAcquireImage(
                    1, // ScannerDeviceType
                    (int)parameters.ColorMode,
                    parameters.ResolutionDpi,
                    WiaFormatBMP,
                    false,
                    true,
                    false);

                if (imageFile is null)
                {
                    throw new OperationCanceledException(isAr
                        ? "تم إلغاء عملية المسح الضوئي."
                        : "Scanning operation was cancelled.");
                }

                return ExtractPngBytesFromWiaImage(imageFile);
            }
        }

        private static void ConfigureDeviceHandling(dynamic targetDevice, bool useFeeder)
        {
            try
            {
                int caps = GetWiaPropertyInt(targetDevice.Properties, 3086); // Document Handling Capabilities
                bool supportsFeeder = (caps & 1) != 0;
                bool supportsFlatbed = (caps & 2) != 0;

                if (useFeeder || (supportsFeeder && !supportsFlatbed))
                {
                    SetWiaProperty(targetDevice.Properties, 3088, 1); // Document Handling Select = Feeder
                    SetWiaProperty(targetDevice.Properties, 3096, useFeeder ? 0 : 1); // Pages = 0 (all pages) when feeder enabled
                }
                else if (!useFeeder && supportsFlatbed)
                {
                    SetWiaProperty(targetDevice.Properties, 3088, 2); // Document Handling Select = Flatbed
                }
            }
            catch
            {
                // Some scanner drivers manage document handling internally
            }
        }

        private static byte[] ExtractMultiPagePdfBytesFromWiaImages(IList<dynamic> wiaImages)
        {
            var bitmaps = new List<BitmapSource>(wiaImages.Count);
            foreach (dynamic img in wiaImages)
            {
                string tempBmp = Path.Combine(Path.GetTempPath(), $"wia_page_{Guid.NewGuid():N}.bmp");
                try
                {
                    img.SaveFile(tempBmp);
                    byte[] bmpBytes = File.ReadAllBytes(tempBmp);
                    using var ms = new MemoryStream(bmpBytes);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    bitmaps.Add(bitmap);
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempBmp)) File.Delete(tempBmp);
                    }
                    catch { }
                }
            }

            string tempPdf = Path.Combine(Path.GetTempPath(), $"wia_multi_{Guid.NewGuid():N}.pdf");
            try
            {
                DocumentPrintExportService.ExportBitmapsToPdfFile(bitmaps, tempPdf);
                return File.ReadAllBytes(tempPdf);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPdf)) File.Delete(tempPdf);
                }
                catch { }
            }
        }

        private static byte[] ExtractPngBytesFromWiaImage(dynamic wiaImageFile)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"wia_scan_{Guid.NewGuid():N}.bmp");
            try
            {
                wiaImageFile.SaveFile(tempPath);

                using var bmp = new System.Drawing.Bitmap(tempPath);
                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Ignore temp cleanup failure
                }
            }
        }

        private static string GetWiaPropertyString(dynamic properties, int propId, string propName)
        {
            try
            {
                foreach (dynamic prop in properties)
                {
                    int id = 0;
                    string name = string.Empty;
                    try { id = Convert.ToInt32(prop.PropertyID); } catch { }
                    try { name = Convert.ToString(prop.Name) ?? string.Empty; } catch { }

                    if (id == propId || string.Equals(name, propName, StringComparison.OrdinalIgnoreCase))
                    {
                        return Convert.ToString(prop.Value) ?? string.Empty;
                    }
                }
            }
            catch
            {
                // Ignore unreadable property collections
            }
            return string.Empty;
        }

        private static int GetWiaPropertyInt(dynamic properties, int propId)
        {
            try
            {
                foreach (dynamic prop in properties)
                {
                    int id = 0;
                    try { id = Convert.ToInt32(prop.PropertyID); } catch { }

                    if (id == propId)
                    {
                        return Convert.ToInt32(prop.Value);
                    }
                }
            }
            catch
            {
                // Ignore unreadable property
            }
            return 0;
        }

        private static void SetWiaProperty(dynamic properties, int propId, object val)
        {
            try
            {
                foreach (dynamic prop in properties)
                {
                    int id = 0;
                    try { id = Convert.ToInt32(prop.PropertyID); } catch { }

                    if (id == propId)
                    {
                        prop.Value = val;
                        break;
                    }
                }
            }
            catch
            {
                // Some scanner drivers restrict specific property writes
            }
        }

        private static COMException? FindComException(Exception ex)
        {
            Exception? current = ex;
            while (current != null)
            {
                if (current is COMException comEx)
                {
                    return comEx;
                }
                current = current.InnerException;
            }
            return null;
        }

        private static bool IsCancelOrPaperEmptyError(Exception ex)
        {
            var comEx = FindComException(ex);
            if (comEx == null) return false;
            return comEx.HResult == WIA_ERROR_USER_CANCELLED || comEx.HResult == WIA_ERROR_PAPER_EMPTY;
        }

        private static Exception TranslateWiaException(Exception ex, bool isAr)
        {
            var comEx = FindComException(ex);
            if (comEx != null)
            {
                switch (comEx.HResult)
                {
                    case WIA_ERROR_USER_CANCELLED:
                        return new OperationCanceledException(isAr
                            ? "تم إلغاء عملية المسح الضوئي."
                            : "Scanning operation was cancelled.");

                    case WIA_ERROR_PAPER_EMPTY:
                        return new Exception(isAr
                            ? "لا يوجد ورق في الماسح الضوئي (ساحب الأوراق فارغ). يرجى وضع الورقة في السكانر والمحاولة مرة أخرى."
                            : "No paper detected in the scanner feeder. Please load paper and try again.");

                    case WIA_ERROR_OFFLINE:
                        return new Exception(isAr
                            ? "الماسح الضوئي غير متصل أو قيد الإيقاف. يرجى التأكد من توصيل السكانر وتشغيله."
                            : "The scanner is offline or disconnected. Please turn it on and check the connection.");

                    case WIA_ERROR_BUSY:
                        return new Exception(isAr
                            ? "الماسح الضوئي مشغول حالياً. يرجى الانتظار قليلاً والمحاولة مرة أخرى."
                            : "The scanner is currently busy. Please wait a moment and try again.");

                    case WIA_S_NO_DEVICE_AVAILABLE:
                        return new Exception(isAr
                            ? "لم يتم العثور على أي جهاز ماسح ضوئي متصل بالكمبيوتر."
                            : "No connected scanner device was found.");
                }
            }

            return ex;
        }
    }
}
