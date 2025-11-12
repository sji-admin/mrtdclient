using cmrtd.Core;
using cmrtd.Core.Model;
using cmrtd.Core.Service;
using Desko.DDA;
using Desko.EPass;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace cmrtd.Infrastructure.DeskoDevice
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class DeviceHandler
    {
        private readonly DeviceManager _deviceManager;
        private readonly Channel<ScanImageTask> _imageProcessingChannel = Channel.CreateUnbounded<ScanImageTask>();
        private readonly SemaphoreSlim _scanLock = new SemaphoreSlim(1, 1);        
        private readonly int _targetDpi;
        private readonly DeviceSettings _deviceSettings;
        private readonly CallbackSettings _callbackSettings;
        private readonly ApiService _apiService;
        private readonly Epassport _epassport = new Epassport();
        private readonly Helper _helper = new Helper();
        public Pasport.ScanApiResponse LastScanResult => _lastScanResult;
        private TaskCompletionSource<Pasport.ScanApiResponse> _scanCompletionSource;
        private Pasport.ScanApiResponse _lastScanResult = new Pasport.ScanApiResponse
        {
            Code = 200,
            Valid = true,
            Data = new Pasport.ScanData
            {
                RgbImage = new Pasport.ImageResult(),
                UvImage = new Pasport.ImageResult(),
                IrImage = new Pasport.ImageResult()
            }
        };
        private string _fallbackPortraitBase64;
        private string _imageFormat;
        private string _faceLocation;
        private RotateFlipType _rotationCorrection = RotateFlipType.RotateNoneFlipNone;



        public DeviceHandler(DeviceManager deviceManager, int targetDpi, CallbackSettings callbackSettings, DeviceSettings deviceSettings, ApiService apiService)
        {
            _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));            
            _targetDpi = targetDpi;            
            _callbackSettings = callbackSettings ?? new CallbackSettings();            
            _apiService = apiService;            
            _deviceSettings = deviceSettings;
        }

        // Image Scan
        public async Task<Pasport.ScanApiResponse> DoScanRequestAsync(CancellationToken cancellationToken = default)
        {
            await _scanLock.WaitAsync();
            try
            {
                _lastOcrString = null;

                _scanCompletionSource = new TaskCompletionSource<Pasport.ScanApiResponse>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                bool doCoax = false;
                bool doOvd = false;

                var lights = new List<DDALightSource> { DDALightSource.Ir, DDALightSource.White, DDALightSource.Uv };
                
                if (doCoax) lights.Add(DDALightSource.Coaxial);
                
                if (doOvd) lights.Add(DDALightSource.Ovd);

                foreach (var light in lights)
                {
                    using var request = new DDAImageRequest
                    {
                        ColorScheme = Constants.Defaults.ColorScheme,
                        Flags = Constants.Defaults.RequestFlag[light],                        
                        PageMode = Constants.Defaults.PageMode,                        
                        SensorResolution = DDASensorResolution.Autoselect,                        
                        ImageFormat = Constants.Defaults.ImageFormat,                        
                        TargetDpi = _targetDpi
                    };

                    if (light == DDALightSource.White)
                    {
                        request.Flags |= DDAImageRequestFlag.Mrz;
                    }

                    _deviceManager.Imager.PrepareImageRequest(light, request);
                }

                _deviceManager.Imager.PrepareImageRequest(DDALightSource.Red, null);

                // Jalankan eksekusi scan di thread terpisah
                await Task.Run(() =>
                {
                    _deviceManager.Imager.ExecuteImageRequest(DDAScanAction.Snapshot, Constants.Defaults.DemoTag);
                }, cancellationToken);

                FeedbackStartScan();
                _deviceManager.Log($"[SCAN] Scan in progress...");

                using (cancellationToken.Register(() => _scanCompletionSource.TrySetCanceled()))
                {
                    return await _scanCompletionSource.Task;
                }
            }
            catch (DDAException ex)
            {
                _deviceManager.Log("--- DDA Exception ---");
                _deviceManager.Log($"--- Result code: {ex.ResultCode}");                
                _deviceManager.Log($"--- Message:     {ex.Message}");
                
                return new Pasport.ScanApiResponse
                {
                    Code = -1,
                    Valid = false,
                    Err_msg = $"DDAException: {ex.Message}"
                };
            }
            catch (OperationCanceledException)
            {
                _deviceManager.Log("Scan canceled by token.");

                return new Pasport.ScanApiResponse
                {
                    Code = -2,
                    Valid = false,
                    Err_msg = "Scan canceled"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine("--- General Exception ---");

                Console.WriteLine($"Message: {ex.Message}");
                
                return new Pasport.ScanApiResponse
                {
                    Code = -99,
                    Valid = false,
                    Err_msg = $"Exception: {ex.Message}"
                };
            }
            finally
            {
                _scanLock.Release();
            }
        }
        
        // handler
        public void RegisterDeviceHandler(bool autoScan)
        {                        
            UnregisterDeviceHandler();

            try
            {
                if (_deviceManager.IsConnected)
                {
                    _deviceManager.DebugEvent += (s, e) =>
                    {
                        if (!e.LogMessage.Contains("Ignoring unknown argument"))
                        {
                            if (e.LogMessage.Contains("Too few boxes with contrast"))
                            {
                                _epassport.LastError = true;
                                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE]  Prepareing Restart Application : {_epassport.LastError}");
                            }
                        }
                    };

                    if (autoScan == true)
                    {
                        _deviceManager.Device.DocumentStatusChanged += Device_DocumentStatusChanged;
                    }
                    _deviceManager.Device.ImageAvailableEvent += Device_ImageAvailableEvent;
                    _deviceManager.Device.ImageRequestDoneEvent += Device_ImageRequestDoneEvent;                    
                    _deviceManager.Device.BarcodeEvent += Device_BarcodeEvent;                    
                    _deviceManager.Device.MsrEvent += Device_MsrEvent;                    
                    _deviceManager.Device.OcrEvent += Device_OcrEvent;

                    
                    _deviceManager.Log($"[SCAN] Register Event Handler Success");
                }
                StartImageProcessingWorker();
            }
            catch (Exception ex)
            {
                _deviceManager.Log($"Error: {ex.Message}");
            }
        }

        public void UnregisterDeviceHandler()
        {
            try
            {
                if (_deviceManager.Device != null)
                {
                    _deviceManager.Device.DocumentStatusChanged -= Device_DocumentStatusChanged;
                    _deviceManager.Device.ImageAvailableEvent -= Device_ImageAvailableEvent;
                    _deviceManager.Device.ImageRequestDoneEvent -= Device_ImageRequestDoneEvent;
                    _deviceManager.Device.BarcodeEvent -= Device_BarcodeEvent;
                    _deviceManager.Device.MsrEvent -= Device_MsrEvent;
                    _deviceManager.Device.OcrEvent -= Device_OcrEvent;
                    _deviceManager.Log("[SCAN] Unregister Event Handler Success");
                }
            }
            catch (Exception ex)
            {
                _deviceManager.Log($"Error: {ex.Message}");
            }
        }

        // event
        bool docPresentLastTime = false;
        public void Device_DocumentStatusChanged(object sender, DDADocumentStatusChangedEventArgs args)
        {
            try
            {
                var status = _deviceManager.Device.DocumentStatus;
                bool docPresent = status.HasFlag(DDADocumentStatusFlag.IsDocPresent);
                bool hasFlipped = status.HasFlag(DDADocumentStatusFlag.IsDocFlipped);
                bool docPresenceChanged = docPresent != docPresentLastTime || hasFlipped;

                if (docPresenceChanged)
                {
                    docPresentLastTime = docPresent;
                    if (docPresent)
                    {
                        if (_epassport.LastError)
                        {
                            _deviceManager.Log($" [DEVICE] Track Error Scan Sebelumnya : {_epassport.LastError}");
                            _deviceManager.Log("Prepare Restart App ...");
                            UnregisterDeviceHandler();
                            _deviceManager.Disconnect(true);

                        }
                        else
                        {
                            Thread.Sleep(500);
                            _deviceManager.Log(" [DEVICE] Dokumen Masuk");
                            _deviceManager.Log($" [DEVICE] Track Error Scan Sebelumnya : {_epassport.LastError}");
                            FeedbackDocPresent();
                            _ = DoScanRequestAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private void Device_ImageRequestDoneEvent(object sender, DDAImageRequestDoneEventArgs args)
        {
            _deviceManager.Log(" [SCAN] Scan Complete");
            FeedbackCompletedScan();                
        }

        private void Device_ImageAvailableEvent(object sender, DDAImageAvailableEventArgs args)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var light = args.ScanImage.LightSource;

                    // deteksi rotasi (tidak memproses image langsung)
                    if (light == DDALightSource.White || light == DDALightSource.Ir)
                    {
                        double rotation = args.ScanImage.Rotation;

                        if (rotation >= 179.0 && rotation <= 181.0)
                            _rotationCorrection = RotateFlipType.Rotate180FlipNone;
                        else if (rotation >= 89.0 && rotation <= 91.0)
                            _rotationCorrection = RotateFlipType.Rotate90FlipNone;
                        else if (rotation >= 269.0 && rotation <= 271.0)
                            _rotationCorrection = RotateFlipType.Rotate270FlipNone;
                        else
                            _rotationCorrection = RotateFlipType.RotateNoneFlipNone;
                    }

                    // Tambahkan ke antrian untuk diproses async
                    _imageProcessingChannel.Writer.TryWrite(new ScanImageTask
                    {
                        ScanImage = args.ScanImage,
                        Rotation = _rotationCorrection,
                        OcrString = _lastOcrString
                    });
                }
                catch (Exception ex)
                {
                    _deviceManager.Log($"Error: {ex.Message}");
                }
            });
            
        }

        private async Task ProcessScanImageAsync(ScanImageTask task)
        {
            var scanImage = task.ScanImage;
            var light = scanImage.LightSource;

            if (light == DDALightSource.Ir && scanImage.IsB900Ink == true)
            {
                _lastScanResult.Data.RgbImage.IsB900Ink = scanImage.IsB900Ink ?? false;
                _lastScanResult.Data.IrImage.IsB900Ink = scanImage.IsB900Ink ?? false;
                _lastScanResult.Data.UvImage.IsB900Ink = scanImage.IsB900Ink ?? false;
                _deviceManager.Log(" [SCAN] IR: B900 Ink detected.");
            }

            if (light == DDALightSource.Uv)
            {
                if (scanImage.IsUvDull == true)
                {
                    _lastScanResult.Data.RgbImage.IsUvDull = scanImage.IsUvDull ?? false;
                    _lastScanResult.Data.UvImage.IsUvDull = scanImage.IsUvDull ?? false;                    
                    _lastScanResult.Data.IrImage.IsUvDull = scanImage.IsUvDull ?? false;                    
                    _lastScanResult.Data.UvImage.MotionBlur = scanImage.MotionBlur ?? false;                    
                    _lastScanResult.Data.RgbImage.MotionBlur = scanImage.MotionBlur ?? false;                    
                    _lastScanResult.Data.IrImage.MotionBlur = scanImage.MotionBlur ?? false;
                    
                    _deviceManager.Log(" [SCAN] UV dull detected.");
                }

                if (scanImage.IsUvDullDocument == true) _deviceManager.Log(" [SCAN] UV dull on document.");                
                if (scanImage.IsUvDullMrz == true) _deviceManager.Log(" [SCAN] UV dull on MRZ.");                
                if (scanImage.IsUvDullFace == true) _deviceManager.Log(" [SCAN] UV dull on face.");
            }

            using (Image img = Helper.CreateImageFromScanImage(scanImage))
            using (Bitmap bmp = new Bitmap(img))
            {
                if (task.Rotation != RotateFlipType.RotateNoneFlipNone)
                    bmp.RotateFlip(task.Rotation);

                try
                {
                    if (light == DDALightSource.White && scanImage.Portrait.Width > 0 && scanImage.Portrait.Height > 0)
                    {
                        _deviceManager.Log(" [SCAN] Data Page Masuk");

                        _deviceManager.Log($" [SCAN] Image dimensions = left: {scanImage.Portrait.Left}, Top: {scanImage.Portrait.Top} px");

                        _deviceManager.Log($" [SCAN] Image dimensions = Width: {scanImage.Portrait.Width}, Height: {scanImage.Portrait.Height} px");

                        _lastScanResult.Data.RgbImage.ImgBase64 = ConvertBitmapToBase64(bmp, ImageFormat.Jpeg);

                        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ScanResult");

                        Directory.CreateDirectory(folder);

                        // Simpan full WHITE image
                        string patheWhite = GetUniqueFilePath(folder, $"full_{light}_{DateTime.Now:yyyyMMdd_HHmmss}", ".jpeg");
                        bmp.Save(patheWhite, ImageFormat.Jpeg);

                        _deviceManager.Log($" [SCAN] WHITE Full Image saved to: {patheWhite}");
                        _lastScanResult.Data.RgbImage.Location = patheWhite;

                        using (Bitmap portrait = bmp.Clone(scanImage.Portrait, bmp.PixelFormat))
                        {
                            string base64Portrait = ConvertBitmapToBase64(portrait, ImageFormat.Jpeg);

                            _fallbackPortraitBase64 = base64Portrait;
                            _imageFormat = ImageFormat.Jpeg.ToString();

                            Rectangle portraitRect = scanImage.Portrait;
                            //string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ScanResult");
                            //Directory.CreateDirectory(folder);

                            string path = GetUniqueFilePath(folder, $"portrait_{light}_{DateTime.Now:yyyyMMdd_HHmmss}", ".jpeg");

                            portrait.Save(path, ImageFormat.Jpeg);

                            _deviceManager.Log($" [SCAN] Image Portrait saved to: {path}");
                            _faceLocation = path;

                            if (_lastScanResult.Data.RgbImage.Face == null)
                            {
                                _lastScanResult.Data.RgbImage.Face = new Pasport.FaceResult();
                            }

                            _lastScanResult.Data.RgbImage.FaceLocation = path;
                            _lastScanResult.Data.RgbImage.Face.Empty = false;
                            _lastScanResult.Data.RgbImage.Face.Width = scanImage.Portrait.Width;
                            _lastScanResult.Data.RgbImage.Face.Height = scanImage.Portrait.Height;
                            _lastScanResult.Data.RgbImage.Face.Left = scanImage.Portrait.Left;
                            _lastScanResult.Data.RgbImage.Face.Top = scanImage.Portrait.Top;

                        }
                    }

                }
                catch (Exception ex)
                {
                    _deviceManager.Log($"Error processing White rotation: {ex.Message}");
                }

                try
                {
                    if (light == DDALightSource.Ir)
                    {
                        _deviceManager.Log(" [SCAN] IR Data Page Masuk");

                        if (_lastScanResult.Data.IrImage == null)
                            _lastScanResult.Data.IrImage = new Pasport.ImageResult();

                        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ScanResult");
                        Directory.CreateDirectory(folder);

                        // Simpan full IR image
                        string pathIr = GetUniqueFilePath(folder, $"full_{light}_{DateTime.Now:yyyyMMdd_HHmmss}", ".jpeg");
                        bmp.Save(pathIr, ImageFormat.Jpeg);
                        _deviceManager.Log($" [SCAN] IR Full Image saved to: {pathIr}");

                        _lastScanResult.Data.IrImage.Location = pathIr;
                        _lastScanResult.Data.IrImage.ImgBase64 = ConvertBitmapToBase64(bmp, ImageFormat.Jpeg);


                        // Tentukan rectangle portrait
                        Rectangle portraitRect = scanImage.Portrait;

                        // Kalau IR tidak punya bounding box, fallback dari RGB
                        if (portraitRect.Width <= 0 || portraitRect.Height <= 0)
                        {
                            if (_lastScanResult.Data.RgbImage != null && _lastScanResult.Data.RgbImage.Face != null)
                            {
                                portraitRect = new Rectangle(

                                    _lastScanResult.Data.RgbImage.Face.Left,
                                    _lastScanResult.Data.RgbImage.Face.Top,
                                    _lastScanResult.Data.RgbImage.Face.Width,
                                    _lastScanResult.Data.RgbImage.Face.Height
                                );

                                _deviceManager.Log(" [SCAN] IR Portrait fallback to RGB face area.");
                            }
                            else
                            {
                                _deviceManager.Log(" [SCAN] IR Portrait not found (no fallback available).");
                                return;
                            }
                        }

                        // Simpan portrait berdasarkan bounding box
                        using (Bitmap portrait = bmp.Clone(portraitRect, bmp.PixelFormat))
                        {
                            string base64Portrait = ConvertBitmapToBase64(portrait, ImageFormat.Jpeg);
                            _lastScanResult.Data.IrImage.ImgFaceBase64 = base64Portrait;

                            string pathPortrait = GetUniqueFilePath(folder, $"portrait_{light}_{DateTime.Now:yyyyMMdd_HHmmss}", ".jpeg");
                            portrait.Save(pathPortrait, ImageFormat.Jpeg);

                            if (_lastScanResult.Data.IrImage.Face == null)
                                _lastScanResult.Data.IrImage.Face = new Pasport.FaceResult();

                            _lastScanResult.Data.IrImage.FaceLocation = pathPortrait;
                            _lastScanResult.Data.IrImage.Face.Empty = false;
                            _lastScanResult.Data.IrImage.Face.Width = portraitRect.Width;
                            _lastScanResult.Data.IrImage.Face.Height = portraitRect.Height;
                            _lastScanResult.Data.IrImage.Face.Left = portraitRect.Left;
                            _lastScanResult.Data.IrImage.Face.Top = portraitRect.Top;

                            _deviceManager.Log($" [SCAN] IR Portrait saved to: {pathPortrait}");
                        }
                    }

                }
                catch (Exception ex)
                {
                    _deviceManager.Log($"Error processing IR image: {ex.Message}");
                }

                try
                {
                    if (light == DDALightSource.Uv)
                    {
                        _deviceManager.Log(" [SCAN] UV Data Page Masuk");

                        if (_lastScanResult.Data.UvImage == null)
                            _lastScanResult.Data.UvImage = new Pasport.ImageResult();

                        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ScanResult");
                        Directory.CreateDirectory(folder);

                        // Clone agar thread-safe
                        Bitmap bmpCopy;
                        lock (bmp)
                            bmpCopy = (Bitmap)bmp.Clone();

                        // Simpan full UV
                        string pathFullUv = GetUniqueFilePath(folder, $"full_{light}_{DateTime.Now:yyyyMMdd_HHmmss}", ".jpeg");
                        bmpCopy.Save(pathFullUv, ImageFormat.Jpeg);
                        _lastScanResult.Data.UvImage.Location = pathFullUv;
                        _lastScanResult.Data.UvImage.ImgBase64 = ConvertBitmapToBase64(bmpCopy, ImageFormat.Jpeg);

                        // Tentukan portrait rect
                        Rectangle portraitRect = scanImage.Portrait;

                        if (portraitRect.Width <= 0 || portraitRect.Height <= 0)
                        {
                            if (_lastScanResult.Data.RgbImage?.Face != null)
                            {
                                portraitRect = new Rectangle(
                                    _lastScanResult.Data.RgbImage.Face.Left,
                                    _lastScanResult.Data.RgbImage.Face.Top,
                                    _lastScanResult.Data.RgbImage.Face.Width,
                                    _lastScanResult.Data.RgbImage.Face.Height
                                );

                                _deviceManager.Log(" [SCAN] UV Portrait fallback ke RGB face area.");
                            }
                            else
                            {
                                _deviceManager.Log(" [SCAN] UV Portrait tidak ditemukan (no fallback).");
                                return;
                            }
                        }

                        // Crop portrait UV
                        using (Bitmap portrait = bmpCopy.Clone(portraitRect, bmpCopy.PixelFormat))
                        {
                            string base64Portrait = ConvertBitmapToBase64(portrait, ImageFormat.Jpeg);
                            _lastScanResult.Data.UvImage.ImgFaceBase64 = base64Portrait;

                            string pathPortrait = GetUniqueFilePath(folder, $"portrait_{light}_{DateTime.Now:yyyyMMdd_HHmmss}", ".jpeg");
                            portrait.Save(pathPortrait, ImageFormat.Jpeg);

                            _lastScanResult.Data.UvImage.Face ??= new Pasport.FaceResult();
                            _lastScanResult.Data.UvImage.FaceLocation = pathPortrait;
                            _lastScanResult.Data.UvImage.Face.Empty = false;
                            _lastScanResult.Data.UvImage.Face.Width = portraitRect.Width;
                            _lastScanResult.Data.UvImage.Face.Height = portraitRect.Height;
                            _lastScanResult.Data.UvImage.Face.Left = portraitRect.Left;
                            _lastScanResult.Data.UvImage.Face.Top = portraitRect.Top;

                            _deviceManager.Log($" [SCAN] UV Portrait saved to: {pathPortrait}");
                        }
                    }

                }
                catch (Exception ex)
                {
                    _deviceManager.Log($"Error processing UV image: {ex.Message}");
                }
            }

            await Task.CompletedTask;
        }

        private void Device_BarcodeEvent(object sender, DDABarcodeEventArgs args)
        {
            if (args.Content is { Length: > 0 })
            {
                string bcrString = Helper.GetHexDump(args.Content, 4, 2);
                _deviceManager.Log($">>> BCR : {bcrString}");
            }
            _deviceManager.Log(" Barcode inserted " + args.Content.Length + " Bytes");
        }

        private void Device_MsrEvent(object sender, DDAMsrEventArgs args)
        {
            if (args.Content is { Length: > 0 })
            {
                string mcrString = Helper.GetHexDump(args.Content, 4, 1);
                _deviceManager.Log($">>> Msr : {mcrString}");

                // Convert byte[] ke string
                string ocrString = Encoding.ASCII.GetString(args.Content);
                ocrString = ocrString.Replace("\r", Environment.NewLine);

                _deviceManager.Log($">>> MSR : {ocrString}");

            }

            _deviceManager.Log("--- Msr inserted ---");
            _deviceManager.Log("--- Index:     " + args.Index);
            _deviceManager.Log("--- Size:      " + args.Content.Length + " Bytes");
            _deviceManager.Log("--- Timestamp: " + args.Timestamp);

        }

        private string _lastOcrString;

        private void Device_OcrEvent(object sender, DDAOcrEventArgs args)
        {
            
            try
            {
                if (args.Content is { Length: > 0 })
                {
                    var uepass = new UePass();
                    var pasport = new Pasport();
                    _deviceManager.Log("[SCAN] Ocr inserted Size: " + args.Content.Length + " Bytes");
                    string ocrString = Encoding.ASCII.GetString(args.Content);
                    ocrString = Regex.Replace(ocrString, @"\r\n?|\n", "");                    
                    _deviceManager.Log($"[SCAN] MRZ : {ocrString}");
                    _lastOcrString = ocrString;
                    _lastScanResult.Data.MRZ = ocrString;

                    var parser = new MRZParser();
                    MRZData data = parser.ParseTD3(ocrString);

                    _epassport.FaceBase64 = null;
                    _epassport.ImageFormat = null;
                    
                    _ = Task.Run(async () =>
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            uepass.ReadPassportPentaData(ocrString, _epassport);
                            Console.WriteLine($"[UEPASS] Waktu baca chip: {sw.Elapsed.TotalSeconds:F2} detik");
                            sw.Restart();
                            // cek hasil
                            string faceBase64 = null;

                            if (!string.IsNullOrEmpty(_epassport.FaceBase64))
                            {
                                faceBase64 = _epassport.FaceBase64;
                                _lastScanResult.Data.DocType = "chip";
                                _deviceManager.Log($"[BASE64] Use FaceBase64 From chip ({faceBase64.Length} chars)");
                            }
                            else if (!string.IsNullOrEmpty(_fallbackPortraitBase64))
                            {
                                faceBase64 = _fallbackPortraitBase64;
                                _lastScanResult.Data.DocType = "document";
                                _deviceManager.Log($"[BASE64] Use fallback portrait ({faceBase64.Length} chars)");
                            }
                            else
                            {
                                _deviceManager.Log("[BASE64] Tidak ada image yang bisa dikirim");
                                //return;
                            }

                            _lastScanResult.Data.RgbImage.ImgFaceBase64 = faceBase64;
                            _scanCompletionSource?.TrySetResult(_lastScanResult);
                            

                            if (_deviceSettings.SensepassKai.Enabled)
                            {
                                _deviceManager.Log($" [BASE64] faceBase64 hasil Length: {faceBase64.Length} characters");
                                
                                await _apiService.AddMemberToGroupAsync(
                                    data.PassportNumber,
                                    faceBase64,
                                    data.Surname,
                                    data.GivenNames
                                );
                                //_deviceManager.Log($" [BASE64] faceBase64 hasil : {faceBase64}");
                                //_deviceManager.Log("[API] Done send to SensepassKai");
                            }

                            string format = _epassport.ImageFormat ?? _imageFormat ?? ImageFormat.Jpeg.ToString();
                            string faceLocation = _epassport.faceLocation ?? _faceLocation;

                            if (_deviceSettings.Callback.Enable)
                            {
                                await _apiService.SendCallbackAsync(
                                    ocrString,
                                    _lastScanResult.Data.RgbImage.ImgBase64,
                                    _lastScanResult.Data.RgbImage.Location,
                                    faceBase64,
                                    _deviceSettings.Callback.Url,
                                    format,
                                    faceLocation,
                                    _lastScanResult.Err_msg
                                 );
                            }
                            Console.WriteLine($"[API] Semua task selesai dalam {sw.Elapsed.TotalSeconds:F2} detik");

                            _helper.Cleaner
                            (
                                _lastScanResult.Data.MRZ,
                                ocrString,
                                _lastScanResult.Data.RgbImage.ImgBase64,
                                _lastScanResult.Data.RgbImage.Location,
                                faceBase64,
                                _deviceSettings.Callback.Url,
                                format,
                                _lastScanResult.Data.RgbImage.FaceLocation
                                );
                        }
                        catch (Exception ex)
                        {
                            // log atau handle error
                            Console.WriteLine(ex);
                        }
                    });

                }                
            }
            catch (Exception ex)
            {
                _deviceManager.Log($"Error: {ex.Message}");
            }
        }

        // Feedback
        protected void FeedbackDocPresent()
        {
            if (_deviceManager.Device != null && _deviceManager.Device.NumberOfBuzzers > 0)
            {
                using (DDABuzzer buzzer = new DDABuzzer(_deviceManager.Device, 0))
                {
                    buzzer.HighTime = 300;
                    buzzer.LowTime = 100;
                    buzzer.Duration = 100;
                    buzzer.Volume = 100;
                    buzzer.UseBuzzer();
                }
            }
        }
        protected void FeedbackStartScan()
        {
            if (_deviceManager.Device != null && _deviceManager.Device.NumberOfLeds > 0)
            {

                using (DDALed led = new DDALed(_deviceManager.Device, 0))
                {
                    led.HighTime = 500;
                    led.LowTime = 500;
                    led.Duration = 15000;
                    led.Color = DDAColor.Yellow;
                    led.UseLed();
                }

            }
        }
        protected void FeedbackCompletedScan()
        {
            if (_deviceManager.Device != null && _deviceManager.Device.NumberOfBuzzers > 0)
            {
                using (DDABuzzer buzzer = new DDABuzzer(_deviceManager.Device, 0))
                {
                    buzzer.HighTime = 100;
                    buzzer.LowTime = 0;
                    buzzer.Duration = 100;
                    buzzer.Volume = 100;
                    buzzer.UseBuzzer();
                }
                if (_deviceManager.Device != null && _deviceManager.Device.NumberOfLeds > 0)
                {

                    using (DDALed led = new DDALed(_deviceManager.Device, 0))
                    {
                        led.HighTime = 2000;
                        led.LowTime = 0;
                        led.Duration = 2000;
                        led.Color = DDAColor.Green;
                        led.UseLed();
                    }

                }

            }
        }
        
        public string GetUniqueFilePath(string folderPath, string baseFileName, string extension)
        {
            int counter = 1;
            string filePath = Path.Combine(folderPath, baseFileName + extension);

            while (File.Exists(filePath))
            {
                filePath = Path.Combine(folderPath, $"{baseFileName}_{counter}{extension}");
                counter++;
            }

            return filePath;
        }

        private void StartImageProcessingWorker(int workerCount = 4)
        {
            for (int i = 0; i < workerCount; i++)
            {
                Task.Run(async () =>
                {
                    await foreach (var task in _imageProcessingChannel.Reader.ReadAllAsync())
                    {
                        try
                        {
                            await ProcessScanImageAsync(task);
                        }
                        catch (Exception ex)
                        {
                            _deviceManager.Log($"Worker error: {ex.Message}");
                        }
                    }
                });
            }
        }

        private string ConvertBitmapToBase64(Bitmap bitmap, ImageFormat format)
        {
            using (var ms = new MemoryStream())
            {
                bitmap.Save(ms, format); // Format bisa JPEG, PNG, dsb.
                byte[] imageBytes = ms.ToArray();
                return Convert.ToBase64String(imageBytes);
            }
        }

    }
}