using cmrtd.Core;
using cmrtd.Core.Model;
using cmrtd.Core.Service;
using Desko.EPass;
using Desko.FullPage;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using static cmrtd.Core.Helper;
using static System.Net.Mime.MediaTypeNames;

namespace cmrtd.Infrastructure.DeskoDevice
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class DevicePscan
    {
        private string _fallbackPortraitBase64;
        private string _ocrString;
        private string _imageFormat;
        private string _faceLocation;
        private string _erpotSatu;
        private string _erpotDua;
        private string _erpotTiga;
        private readonly Helper _helper = new Helper();
        private readonly DeviceSettings _deviceSettings;
        private readonly bool _connectOnPlug = true;
        private readonly CallbackSettings _callbackSettings;
        private readonly ApiService _apiService;
        private readonly Epassport _epassport = new Epassport();
        private static readonly ImageFormat DefaultFormat = ImageFormat.Jpeg;
        private MRZData _dataparse = new MRZData();
        private TaskCompletionSource<Pasport.ScanApiResponse> _scanCompletionSource;
        public Pasport.ScanApiResponse LastScanResult => _lastScanResult;
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
        public Epassport Epassport => _epassport;
        
        public DevicePscan(CallbackSettings callbackSettings, DeviceSettings deviceSettings, ApiService apiService)
        {
            _deviceSettings = deviceSettings ?? throw new ArgumentNullException(nameof(deviceSettings));
            _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
            _callbackSettings = callbackSettings ?? new CallbackSettings();
        }

        public void InitializeApp()
        {
            RegisterEvent();
            updateScanSettings();
            updateDeviceInfo();
        }


        #region Device

        public void RegisterEvent()
        {
            UnRegisterEvent();

            Api.OnDevicePlugged += UpdatePlugState;
            Api.OnDocumentPresent += OnDocPresented;
            Api.OnDocumentRemove += OnDocRemoved;
            Api.OnOcr += OnEventOcr;
            Api.OnMsr += OnEventMagStripe;
            Api.OnBarcode += OnEventBarcode;

            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [INFO] Register Event...");
        }

        public void UnRegisterEvent()
        {
            Api.OnDevicePlugged -= UpdatePlugState;
            Api.OnDocumentPresent -= OnDocPresented;
            Api.OnDocumentRemove -= OnDocRemoved;
            Api.OnOcr -= OnEventOcr;
            Api.OnMsr -= OnEventMagStripe;
            Api.OnBarcode -= OnEventBarcode;

            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [INFO] UnRegister Event...");
        }

        public void ConnectDevice()
        {
            DeviceToolsPscan.HandleApiExceptions(() =>
            {
                var sw = Stopwatch.StartNew();
                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [INFO] Connecting...");
                Api.ConnectToDevice();
                setScanSettings();
                DeviceInfo info = Api.GetDeviceInfo();

                Console.WriteLine($"FW: {info.Version:X8}.{info.Number:X8}");                
                Console.WriteLine($"Date/Time: {info.CompileDate} / {info.CompileTime}");                
                Console.WriteLine($"VID/PID: {info.Vid:X4} / {info.Pid:X4}");                
                Console.WriteLine($"Features: {info.Features}");

                int generation = Api.GetPropertyInt(PropertyKey.DeviceIlluminationGeneration);                
                int revision = Api.GetPropertyInt(PropertyKey.DeviceIlluminationRevision);
                int variant = Api.GetPropertyInt(PropertyKey.DeviceIlluminationVariant);                
                string variantVerb = Api.GetPropertyString(PropertyKey.DeviceIlluminationVariantVerbose);
                
                Console.WriteLine($"Illumination: {generation}.{revision}/{variant} ({variantVerb})");

                int barcodeSupport = Api.GetPropertyInt(PropertyKey.DeviceSupportBarcodeOnPc);
                
                Console.WriteLine($"Barcode supported: {(barcodeSupport != 0 ? "Yes" : "No")}");

                updateDeviceInfo();

                Console.WriteLine("[OK] Connected.");
                sw.Stop();
                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [INFO] Done in {sw.ElapsedMilliseconds} ms");
            });
        }

        public void DisconnectDevice()
        {
            DeviceToolsPscan.HandleApiExceptions(() =>
            {
                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [INFO] Disconnecting...");
                Api.DisconnectFromDevice();
                updateDeviceInfo();
                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [OK] Disconnected.");
            });
        }

        #endregion

        #region System information and device state
        void updateDeviceInfo()
        {
            var infoDevice = new Epassport();
            bool connected = Api.IsDeviceConnected();
            var infoList = new List<SystemInfoEntry>();

            infoList.Add(new SystemInfoEntry("API", Api.GetProperty(PropertyKey.ApiVersionString, "n/a")));
            infoList.Add(new SystemInfoEntry("DLL", string.Format("{0} ({1} {2})",
                Api.GetProperty(PropertyKey.DllVersionString, "?"),
                Api.GetProperty(PropertyKey.DllCompileDate, "?"),
                Api.GetProperty(PropertyKey.DllCompileTime, "?")
            )));

            if (connected)
            {
                infoList.Add(new SystemInfoEntry("Firmware", string.Format("{0} ({1} {2})",
                    
                    Api.GetProperty(PropertyKey.DeviceFirmwareVersionString, "n/a"),                    
                    Api.GetProperty(PropertyKey.DeviceFirmwareDate, "n/a"),                    
                    Api.GetProperty(PropertyKey.DeviceFirmwareTime, "n/a")

                )));

                infoList.Add(new SystemInfoEntry("S/N", Api.GetProperty(PropertyKey.DeviceSerialNumber, "n/a")));
                _epassport.SerialCki = new string[] { Api.GetProperty(PropertyKey.DeviceSerialNumber, "n/a") };
                infoList.Add(new SystemInfoEntry("Production", Api.GetProperty(PropertyKey.DeviceProductionId, "n/a")));                
                infoList.Add(new SystemInfoEntry("PCB", Api.GetProperty(PropertyKey.DevicePcbRevision, "n/a")));                
                infoList.Add(new SystemInfoEntry("USB VID/PID", string.Format("{0}/{1}",
                    Api.GetProperty(PropertyKey.DeviceVid, (uint)0).ToString("X4"),                    
                    Api.GetProperty(PropertyKey.DevicePid, (uint)0).ToString("X4"))));

                infoList.Add(new SystemInfoEntry("Illumination", string.Format(
                    "{0}.{1}/{2}",                   
                    Api.GetProperty(PropertyKey.DeviceIlluminationGenerationVerbose, "?"),                    
                    Api.GetProperty(PropertyKey.DeviceIlluminationRevisionVerbose, "?"),                    
                    Api.GetProperty(PropertyKey.DeviceIlluminationVariantVerbose, "?")
                )));

                infoList.Add(new SystemInfoEntry("Barcode on PC",
                    Api.GetProperty(PropertyKey.DeviceSupportBarcodeOnPc, (uint)0) == 0 ? "No" : "Yes"));

                DeviceInfo info = Api.GetDeviceInfo();
                foreach (DeviceInfoFlags flag in Enum.GetValues(typeof(DeviceInfoFlags)))
                {
                    if (flag == DeviceInfoFlags.None)
                        continue;

                    bool supported = (flag & info.Features) != 0;
                    infoList.Add(new SystemInfoEntry(flag.ToString(), supported ? "YES" : "NO"));
                }
            }

            // Output ke console dalam bentuk tabel sederhana
            Console.WriteLine("=========================================");
            
            Console.WriteLine(" DEVICE INFORMATION");
            
            Console.WriteLine("=========================================");
            
            foreach (var entry in infoList)
            {
                Console.WriteLine($"{entry.Feature,-25} : {entry.Value}");
            }
            
            Console.WriteLine("=========================================");

            // (opsional) Simpan ke file JSON untuk log CLI
            File.WriteAllText("device_info.json", System.Text.Json.JsonSerializer.Serialize(infoList, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            }));

            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [INFO] Device information updated.");
        }
        
        void UpdatePlugState(object sender, PlugEventArgs args)
        {
            if (args.State == PlugState.Plugged)
            {
                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [EVENT] Device plugged in");

                if (_connectOnPlug == true)
                {
                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [ACTION] Auto-connect on plug is enabled, connecting...");
                    
                    bool connected = Api.IsDeviceConnected();
                    
                    if (connected)
                    {
                        Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [INFO] Device is already connected, skipping Reconnect.");
                    }
                    else
                    {
                        ConnectDevice();
                    }
                    
                }
            }
            else
            {
                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [EVENT] Device unplugged");
            }

            updateDeviceInfo();
        }

        #endregion

        #region Settings

        void setScanSettings(
            bool useInfrared = true, 
            bool useVisible = true, 
            bool useUltraviolet = true,
            bool useAntiBg = true, 
            string resolution = "low", 
            string colorScheme = "desko",
            double exposureIr = 0, 
            double exposureVis = 0, 
            double exposureUv = 0
            )
        {
            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  Menyiapkan konfigurasi scan...");

            var settings = new ScanSettings();

            ScanLightFlags irFlags = ScanLightFlags.None;
            if (useInfrared)
                irFlags |= ScanLightFlags.Use;
            if (useAntiBg)
                irFlags |= ScanLightFlags.AmbientLightElimination;
            settings.Infrared = irFlags;

            ScanLightFlags visFlags = ScanLightFlags.None;
            if (useVisible)
                visFlags |= ScanLightFlags.Use;
            if (useAntiBg)
                visFlags |= ScanLightFlags.AmbientLightElimination;
            settings.Visible = visFlags;

            ScanLightFlags uvFlags = ScanLightFlags.None;
            if (useUltraviolet)
                uvFlags |= ScanLightFlags.Use;
            if (useAntiBg)
                uvFlags |= ScanLightFlags.AmbientLightElimination;
            settings.Ultraviolet = uvFlags;

            switch (resolution.ToLower())
            {
                case "high":
                    settings.Resolution = ScanResolution.High;
                    break;
                case "default":
                    settings.Resolution = ScanResolution.Default;
                    break;
                case "low":
                    settings.Resolution = ScanResolution.Low;
                    break;
                default:
                    throw new PsaException(Result.Fail, $"Invalid resolution: {resolution}");
            }

            switch (colorScheme.ToLower())
            {
                case "none":
                    settings.Flags = ScanFlags.None;
                    break;
                case "raw":
                    settings.Flags = ScanFlags.ForceRawColors;
                    break;
                case "desko":
                    settings.Flags = ScanFlags.ForceDeskoColors;
                    break;
                default:
                    throw new PsaException(Result.Fail, $"Invalid color scheme: {colorScheme}");
            }

            settings.Flags |= ScanFlags.MotionDetectionNegativeOnFail;

            Api.SetScanSettings(settings);
            Api.SetExposureFactor(LightSource.Infrared, Math.Pow(2, Math.Sqrt(2) * exposureIr));            
            Api.SetExposureFactor(LightSource.Visible, Math.Pow(2, Math.Sqrt(2) * exposureVis));            
            Api.SetExposureFactor(LightSource.Ultraviolet, Math.Pow(2, Math.Sqrt(2) * exposureUv));

            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [SCAN SETTINGS] Konfigurasi selesai:");            
            Console.WriteLine($" - Infrared: {useInfrared}, Visible: {useVisible}, UV: {useUltraviolet}");            
            Console.WriteLine($" - AntiBG: {useAntiBg}");            
            Console.WriteLine($" - Resolution: {resolution}");            
            Console.WriteLine($" - ColorScheme: {colorScheme}");            
            Console.WriteLine($" - Exposure IR:{exposureIr}, VIS:{exposureVis}, UV:{exposureUv}");
        }


        bool skipUpdate = false;
        void updateScanSettings(object o, EventArgs args)
        {
            DeviceToolsPscan.HandleApiExceptions(delegate ()
            {
                if (Api.IsDeviceConnected() && !skipUpdate)
                {
                    setScanSettings();
                }
            });
        }

        void updateScanSettings()
        {
            updateScanSettings(null, EventArgs.Empty);
        }

        #endregion

        #region Scan workflow

        void onScanOcr()
        {
            var uepass = new UePass();
            Console.WriteLine("[MRZ] Mulai proses OCR...");

            string mrz = Api.GetOcrPc();
            if (mrz != null)
            {
                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [OCR] Membaca MRZ...");

                if (!string.IsNullOrEmpty(mrz))
                {

                    string mrzLine = mrz.Replace("\r", "").Replace("\n", " ");
                    _lastScanResult.Data.MRZ = mrzLine;
                    _ocrString = mrzLine;

                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [OCR] RESULT : {mrzLine}");
                }
            }
            else
            {
                Console.WriteLine("[MRZ] Gagal membaca MRZ.");                
            }
        }

        void onScanBcr()
        {
            // Karena gak ada GUI, kita anggap scanning BCR selalu jalan
            Console.WriteLine("[INFO] Mulai proses baca Barcode (BCR)...");

            // Cek apakah device support barcode
            if (Api.GetPropertyInt(PropertyKey.DeviceSupportBarcodeOnPc) == 0)
            {
                Console.WriteLine("[BCR] Tidak didukung oleh perangkat ini.");
                return;
            }

            while (true)
            {
                byte[] bcr = Api.GetBarcodePc();

                if (bcr == null)
                {
                    // Tidak ada data barcode lagi
                    break;
                }

                string theText = DeviceToolsPscan.MaskNonAscii(bcr);

                // Tulis ke console
                Console.WriteLine("[BCR DATA] " + theText);

                // Kalau mau simpan hasil ke file:
                string filePath = Path.Combine(Directory.GetCurrentDirectory(), "barcode_log.txt");
                File.AppendAllText(filePath, theText + Environment.NewLine);
            }

            Console.WriteLine("[INFO] Proses scan BCR selesai.");
        }

        public Task<Pasport.ScanApiResponse> ScanAsync()
        {
            _scanCompletionSource = new TaskCompletionSource<Pasport.ScanApiResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {

                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [SCAN] Mulai proses scan dokumen...");

                Api.Scan();

                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [SCAN] Selesai pemindaian dokumen.");

                buzzerStart();

                bool isB900InkDetected = Api.CheckB900Ink();
                if (isB900InkDetected)               
                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [B900 Ink] detected.");                
                else                
                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [B900 Ink] Not detected.");                                                        

                bool isUvDullDetected = Api.CheckUvDullness();
                if (isUvDullDetected)
                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [UV DULL] Dull detected");
                else
                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [UV DULL] Bright detected");


                _lastScanResult.Data.RgbImage.IsB900Ink = isB900InkDetected;
                _lastScanResult.Data.IrImage.IsB900Ink = isB900InkDetected;
                _lastScanResult.Data.UvImage.IsB900Ink = isB900InkDetected;
                _lastScanResult.Data.RgbImage.IsUvDull = isUvDullDetected;
                _lastScanResult.Data.IrImage.IsUvDull = isUvDullDetected;
                _lastScanResult.Data.UvImage.IsUvDull = isUvDullDetected;

                var dens = Api.GetCurrentPixelPerMeter();

                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [SCAN] Pixel Density: {dens.X} x {dens.Y} ppm");


                try
                {
                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [IR] Mengambil gambar Infrared...");

                    var irImage = Api.GetImage(LightSource.Infrared, ImageClipping.Document);
                    irImage.Save("Infrared.png");

                    _lastScanResult.Data.IrImage.ImgBase64 = ConvertBitmapToBase64(irImage);

                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [IMAGE] Infrared.png tersimpan.");

                    var faceImg = Api.GetImage(LightSource.Infrared, ImageClipping.Face);

                    if (faceImg != null)
                    {
                        string filePath = Path.Combine(Directory.GetCurrentDirectory(), "FaceInfrared.Jpeg");
                        faceImg.Save(filePath, ImageFormat.Jpeg);
                        _faceLocation = filePath;
                        _imageFormat = ImageFormat.Jpeg.ToString();

                        if (_lastScanResult.Data.IrImage.Face == null)
                            _lastScanResult.Data.IrImage.Face = new Pasport.FaceResult();
                        
                        _lastScanResult.Data.IrImage.FaceLocation = filePath;
                        _lastScanResult.Data.IrImage.Location = filePath;
                        Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [FACE] Gambar wajah tersimpan: {filePath}");

                        // Todo: Convert ke Base64
                        string base64String;
                        using (var ms = new MemoryStream())
                        {
                            faceImg.Save(ms, ImageFormat.Jpeg);
                            byte[] imageBytes = ms.ToArray();
                            base64String = Convert.ToBase64String(imageBytes);
                            _lastScanResult.Data.IrImage.ImgFaceBase64 = base64String;

                            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [FACE] Base64 length: {base64String.Length}");
                            //Console.WriteLine(base64String); // atau kirim ke API / JSON response
                        }

                        _lastScanResult.Data.IrImage.Face.Empty = false;

                        // Todo: Debug hasil Base64 Simpan ke file teks
                        //string base64FilePath = "FaceBase64.txt";
                        //File.WriteAllText(base64FilePath, base64String);
                        //Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [FACE] Base64 tersimpan di file: {base64FilePath}");

                    }
                    else
                    {
                        Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [ERROR] Gagal mengambil gambar wajah.");
                    }

                    _erpotSatu = null;

                }
                catch (PsaException ex)
                {
                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [IR] Gagal mengambil gambar Infrared: " + ex.Message);
                    _erpotSatu = "IR : " + ex.Message;
                }

                try
                {
                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [VIS] Mengambil gambar Visible...");

                    var visImage = Api.GetImage(LightSource.Visible, ImageClipping.Document);
                    visImage.Save("Visible.png");

                    _lastScanResult.Data.RgbImage.ImgBase64 = ConvertBitmapToBase64(visImage);

                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [IMAGE] Visible.png tersimpan.");

                    // TODO: Ambil gambar wajah (Face Portrait)
                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [FACE] Mengambil gambar wajah...");

                    var faceImg = Api.GetImage(LightSource.Visible, ImageClipping.Face);

                    if (faceImg != null)
                    {
                        string filePath = Path.Combine(Directory.GetCurrentDirectory(), "FaceVisible.Jpeg");
                        faceImg.Save(filePath, ImageFormat.Jpeg);
                        _faceLocation = filePath;
                        _imageFormat = ImageFormat.Jpeg.ToString();

                        if (_lastScanResult.Data.RgbImage.Face == null)
                            _lastScanResult.Data.RgbImage.Face = new Pasport.FaceResult();
                        
                        _lastScanResult.Data.RgbImage.FaceLocation = filePath;
                        _lastScanResult.Data.RgbImage.Location = filePath;
                        Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [FACE] Gambar wajah tersimpan: {filePath}");

                        // Todo: Convert ke Base64
                        string base64String;
                        using (var ms = new MemoryStream())
                        {
                            faceImg.Save(ms, ImageFormat.Jpeg);
                            byte[] imageBytes = ms.ToArray();
                            base64String = Convert.ToBase64String(imageBytes);
                            _fallbackPortraitBase64 = base64String;

                            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [FACE] Base64 length: {base64String.Length}");
                            //Console.WriteLine(base64String); // atau kirim ke API / JSON response
                        }

                        _lastScanResult.Data.RgbImage.Face.Empty = false;

                        // Todo: Debug hasil Base64 Simpan ke file teks
                        //string base64FilePath = "FaceBase64.txt";
                        //File.WriteAllText(base64FilePath, base64String);
                        //Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [FACE] Base64 tersimpan di file: {base64FilePath}");

                    }
                    else
                    {
                        Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [ERROR] Gagal mengambil gambar wajah.");
                    }

                    _erpotDua = null;

                }
                catch (PsaException ex)
                {
                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [VIS] Gagal mengambil gambar Visible: " + ex.Message);
                    _erpotDua = "Visible: " + ex.Message;
                }

                try
                {
                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [UV] Mengambil gambar UV...");
                    var uvImage = Api.GetImage(LightSource.Ultraviolet, ImageClipping.Document);
                    uvImage.Save("Ultraviolet.png");
                    _lastScanResult.Data.UvImage.ImgBase64 = ConvertBitmapToBase64(uvImage);

                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [IMAGE] Ultraviolet.jpeg tersimpan.");
                    var faceImg = Api.GetImage(LightSource.Ultraviolet, ImageClipping.Face);

                    if (faceImg != null)
                    {
                        string filePath = Path.Combine(Directory.GetCurrentDirectory(), "FaceUltraviolet.Jpeg");
                        faceImg.Save(filePath, ImageFormat.Jpeg);
                        _faceLocation = filePath;
                        _imageFormat = ImageFormat.Jpeg.ToString();

                        if (_lastScanResult.Data.UvImage.Face == null)
                            _lastScanResult.Data.UvImage.Face = new Pasport.FaceResult();

                        _lastScanResult.Data.UvImage.FaceLocation = filePath;
                        _lastScanResult.Data.UvImage.Location = filePath;
                        Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [FACE] Gambar wajah tersimpan: {filePath}");

                        // Todo: Convert ke Base64
                        string base64String;
                        using (var ms = new MemoryStream())
                        {
                            faceImg.Save(ms, ImageFormat.Jpeg);
                            byte[] imageBytes = ms.ToArray();
                            base64String = Convert.ToBase64String(imageBytes);
                            _lastScanResult.Data.UvImage.ImgFaceBase64 = base64String;

                            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [FACE] Base64 length: {base64String.Length}");
                            //Console.WriteLine(base64String); // atau kirim ke API / JSON response
                        }

                        _lastScanResult.Data.UvImage.Face.Empty = false;

                        // Todo: Debug hasil Base64 Simpan ke file teks
                        //string base64FilePath = "FaceBase64.txt";
                        //File.WriteAllText(base64FilePath, base64String);
                        //Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [FACE] Base64 tersimpan di file: {base64FilePath}");

                    }
                    else
                    {
                        Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [ERROR] Gagal mengambil gambar wajah.");
                    }

                }
                catch (PsaException ex)
                {
                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [UV] Gagal mengambil gambar UV: " + ex.Message);
                    _erpotTiga = "UV: " + ex.Message;
                }

                //Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [BCR] Try To Read Barcode...");
                //if (Api.GetPropertyInt(PropertyKey.DeviceSupportBarcodeOnPc) == 0)
                //{
                //    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [BCR] Barcode Not Found.");
                //}
                //else
                //{
                //    while (true)
                //    {
                //        byte[] bcr = Api.GetBarcodePc();
                //        if (bcr == null)
                //            break;

                //        string text = DeviceToolsPscan.MaskNonAscii(bcr);
                //        Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [BCR DATA] " + text);
                //    }
                //}

                if (!string.IsNullOrEmpty(_erpotSatu) && !string.IsNullOrEmpty(_erpotDua))
                {
                    _lastScanResult.Err_msg = $"{_erpotSatu} {_erpotDua} {_erpotTiga}".Trim();
                }
                else
                {
                    _lastScanResult.Err_msg = null;
                }

                return _scanCompletionSource.Task;
            }
            catch (PsaException ex)
            {
                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [ERROR] Terjadi kesalahan saat scan: " + ex.Message);
                _scanCompletionSource.TrySetException(ex);
                throw;
            }
        }

        #endregion

        #region Device event handlers

        void OnDocPresented(object sender, EventArgs args)
        {
            Console.WriteLine($"[EVENT] Document detected (DOC ON)");
            bool docPresent = Api.IsDocumentPresent();
            if (docPresent)
            {
                if (_deviceSettings.AutoScan) {
                    Console.WriteLine($"[ACTION] Auto-scan triggered...");
                    ScanAsync();
                }
            }
        }

        void OnDocRemoved(object sender, EventArgs args)
        {
            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [EVENT] Document Ejected (DOC OFF)");
            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [DEVICE] Ready For Next Scan");
        }

        void OnEventOcr(object sender, OcrEventArgs args)
        {
            var uepass = new UePass();
            var parser = new MRZParser();
            var deviceSettingsLocal = _deviceSettings;
            string data = args.Data.Replace("\r", "").Replace("\n", "").Replace(" ", "");
            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [OCR] {data}");

            _lastScanResult.Data.MRZ = data;
            _ocrString = data;
            _dataparse = parser.ParseTD3(data);            
            _epassport.FaceBase64 = null;
            _epassport.ImageFormat = null;

        _ = Task.Run(async () =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [UEPASS] MRZ: {data}");
                    uepass.ReadPassportCkiData(data, _epassport);

                    Console.WriteLine($"[UEPASS] Waktu baca chip: {sw.Elapsed.TotalSeconds:F2} detik");
                    sw.Restart();

                    string faceBase64 = null;
                    string docType = null;

                    if (!string.IsNullOrEmpty(_epassport.FaceBase64))
                    {
                        faceBase64 = _epassport.FaceBase64;
                        docType = "CHIP";
                        Console.WriteLine($"[BASE64] Use FaceBase64 From chip ({faceBase64.Length} chars)");
                    }
                    else if (!string.IsNullOrEmpty(_fallbackPortraitBase64))
                    {
                        faceBase64 = _fallbackPortraitBase64;
                        docType = "DOKUMEN";
                        Console.WriteLine($"[BASE64] Use fallback portrait ({faceBase64.Length} chars)");
                    }
                    else
                    {
                        Console.WriteLine("[BASE64] Tidak ada image yang bisa dikirim");
                        //return;
                    }

                    _lastScanResult.Data.DocType = docType;
                    _lastScanResult.Data.RgbImage.ImgFaceBase64 = faceBase64;

                    _scanCompletionSource?.TrySetResult(_lastScanResult);

                    var sendTasks = new List<Task>();
                    string format = _epassport.ImageFormat ?? _imageFormat ?? ImageFormat.Jpeg.ToString();
                    string faceLocation = _epassport.faceLocation ?? _faceLocation;

                    if (deviceSettingsLocal.SensepassKai.Enabled)
                    {
                        sendTasks.Add(_apiService.AddMemberToGroupAsync(
                            _dataparse.PassportNumber,
                            faceBase64,
                            _dataparse.Surname,
                            _dataparse.GivenNames
                        ));
                    }

                    if (deviceSettingsLocal.Callback.Enable)
                    {
                        sendTasks.Add(_apiService.SendCallbackAsync(                            
                            _ocrString,
                            _lastScanResult.Data.RgbImage.ImgBase64,
                            _lastScanResult.Data.RgbImage.Location,
                            faceBase64,
                            _deviceSettings.Callback.Url,
                            format,
                            _lastScanResult.Data.RgbImage.FaceLocation,
                            _lastScanResult.Err_msg
                        ));
                    }

                    if (sendTasks.Count > 0)
                    {
                        await Task.WhenAll(sendTasks).ConfigureAwait(false);
                        buzzerReady();
                    }

                    Console.WriteLine($"[API] Semua task selesai dalam {sw.Elapsed.TotalSeconds:F2} detik");

                    _helper.Cleaner
                    (
                        _lastScanResult.Data.MRZ,
                        _ocrString,
                        _lastScanResult.Data.RgbImage.ImgBase64,
                        _lastScanResult.Data.RgbImage.ImgFaceBase64,
                        _lastScanResult.Data.RgbImage.Location,
                        faceBase64,
                        _deviceSettings.Callback.Url,
                        format,
                        _lastScanResult.Data.RgbImage.FaceLocation                        
                        );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [ERROR] >>> {ex}");
                }
            });
        }
        
        void OnEventMagStripe(object sender, MsrEventArgs args)
        {
            string theText = DeviceToolsPscan.MaskNonAscii(args.Data);
            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [MSR] {theText}");
        }

        void OnEventBarcode(object sender, BarcodeEventArgs args)
        {
            string theText = DeviceToolsPscan.MaskNonAscii(args.Data);
            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [BARCODE] {theText}");
        }

        #endregion

        #region Feedback
        void buzzerStart()
        {
            //if (!checkBoxBuzzer.Checked) return;

            BuzzerSettings settings = new BuzzerSettings();
            settings.Duration = 50;
            settings.HighTime = 5;
            settings.LowTime = 5;
            settings.Volume = 255;

            Api.SetBuzzer(settings);
            Api.UseBuzzer();
        }

        void buzzerReady()
        {
            //if (!checkBoxBuzzer.Checked) return;

            BuzzerSettings settings = new BuzzerSettings();
            settings.Duration = 100;
            settings.HighTime = 100;
            settings.LowTime = 0;
            settings.Volume = 255;
            Api.SetBuzzer(settings);
            Api.UseBuzzer();
        }

        private static string ConvertBitmapToBase64(Bitmap bitmap)
        {
            using var ms = new MemoryStream(4096 * 1024); // preallocate 4MB
            bitmap.Save(ms, DefaultFormat);
            return Convert.ToBase64String(ms.GetBuffer(), 0, (int)ms.Length);
        }

        #endregion

    }
}
