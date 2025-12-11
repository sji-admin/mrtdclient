using cmrtd.Core.Model;
using cmrtd.Core.Service;
using Serilog;
using System.Drawing;
using System.Drawing.Imaging;

namespace cmrtd.Infrastructure.ThalesDevice
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class ThalesDevicesManager
    {
        MMM.Readers.FullPage.ReaderDocumentDetectionState prDetectState;
        private int prDetectStateCounter;
        private bool prIsAT10K550;
        private string imagebase54;
        private string imagebase64Chip;
        private ThalesDataSend _dataToSendConfig;
        public DeviceSettings _deviceSettings;
        private readonly CallbackSettings _callbackSettings;
        private readonly ApiService _apiService;
        private string mrzString = "";
        private string facebase64 = "";
        public string format = "";
        public string faceLocation = "";
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

        // Constructor - inject dependencies (avoid null refs)
        public ThalesDevicesManager(CallbackSettings callbackSettings, ApiService apiService, DeviceSettings deviceSettings)
        {
            _deviceSettings = deviceSettings ?? throw new ArgumentNullException(nameof(deviceSettings));
            _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
            _callbackSettings = callbackSettings ?? new CallbackSettings();

            // any other initialization
        }

        public ThalesDevicesManager(CallbackSettings callback, DeviceSettings deviceSettings, ApiService apiService)
        {
            this.callback = callback;
            _deviceSettings = deviceSettings;
            _apiService = apiService;
        }

        public void Terminet()
        {
            MMM.Readers.FullPage.Reader.Shutdown();
        }

        void DataCallback(MMM.Readers.FullPage.DataType aDataType, object aData)
        {
            try
            {
                // Use string overload for logging to avoid overload resolution issues
                LogDataItem(aDataType, aData);

                if (aData == null)
                {
                    Log.Information($"[DATA Callback] {aDataType} => null");
                    return;
                }

                switch (aDataType)
                {
                    case MMM.Readers.FullPage.DataType.CD_CODELINE_DATA:
                        {
                            MMM.Readers.CodelineData codeline = (MMM.Readers.CodelineData)aData;

                            Log.Information(codeline.Line1);
                            Log.Information(codeline.Line2);
                            Log.Information(codeline.Line3);
                            mrzString = codeline.Line1 + codeline.Line2 + codeline.Line3;

                            HighlightCodelineCheckDigits(codeline);

                            Log.Information("Surname: " + codeline.Surname);
                            Log.Information("Forenames: " + codeline.Forenames);
                            Log.Information("Nationality: " + codeline.Nationality);
                            Log.Information("Sex: " + codeline.Sex);
                            Log.Information(
                                "DateOfBirth: " +
                                string.Format("{0:00}-{1:00}-{2:00}",
                                    codeline.DateOfBirth.Day,
                                    codeline.DateOfBirth.Month,
                                    codeline.DateOfBirth.Year
                                )
                            );

                            Log.Information("DocumentNumber: " + codeline.DocNumber);
                            Log.Information("DocumentType: " + codeline.DocType);

                            break;
                        }
                    case MMM.Readers.FullPage.DataType.CD_CODELINE:
                        {
                            MMM.Readers.CodelineData codeline = (MMM.Readers.CodelineData)aData;

                            Log.Information(codeline.Line1);
                            Log.Information(codeline.Line2);
                            Log.Information(codeline.Line3);

                            HighlightCodelineCheckDigits(codeline);

                            Log.Information("Surname: " + codeline.Surname);
                            Log.Information("Forenames: " + codeline.Forenames);
                            Log.Information("Nationality: " + codeline.Nationality);
                            Log.Information("Sex: " + codeline.Sex);
                            Log.Information(
                                "DateOfBirth: " +
                                string.Format("{0:00}-{1:00}-{2:00}",
                                    codeline.DateOfBirth.Day,
                                    codeline.DateOfBirth.Month,
                                    codeline.DateOfBirth.Year
                                )
                            );

                            Log.Information("DocumentNumber: " + codeline.DocNumber);
                            Log.Information("DocumentType: " + codeline.DocType);

                            break;
                        }
                    case MMM.Readers.FullPage.DataType.CD_IMAGEIR:
                    case MMM.Readers.FullPage.DataType.CD_IMAGEVIS:
                        if (aData is Bitmap bmpvis)
                        {
                            Log.Information($"[{aDataType}] Image received. Size: {bmpvis.Width}x{bmpvis.Height}");
                            format = ImageFormat.Jpeg.ToString();
                            imagebase54 = ConvertBitmapToBase64(bmpvis, ImageFormat.Jpeg);
                            _lastScanResult.Data.RgbImage.ImgBase64 = imagebase54;
                            Log.Information($"base64 length : {imagebase54.Length}");
                            SaveBitmapAsync(bmpvis, aDataType.ToString());
                        }
                        else
                        {
                            Log.Information($"[{aDataType}] (null bitmap)");
                        }
                        break;
                    case MMM.Readers.FullPage.DataType.CD_IMAGEPHOTO:
                        if (aData is Bitmap bmpphoto)
                        {
                            Log.Information($"[{aDataType}] Image received. Size: {bmpphoto.Width}x{bmpphoto.Height}");
                            format = ImageFormat.Jpeg.ToString();
                            facebase64 = ConvertBitmapToBase64(bmpphoto, ImageFormat.Jpeg);
                            //_lastScanResult.Data.RgbImage.ImgBase64 = imagebase54;
                            Log.Information($"base64 length : {facebase64.Length}");
                            SaveBitmapAsync(bmpphoto, aDataType.ToString());
                        }
                        else
                        {
                            Log.Information($"[{aDataType}] (null bitmap)");
                        }
                        break;
                    case MMM.Readers.FullPage.DataType.CD_IMAGEUV:
                    case MMM.Readers.FullPage.DataType.CD_IMAGEIRREAR:
                    case MMM.Readers.FullPage.DataType.CD_IMAGEVISREAR:
                    case MMM.Readers.FullPage.DataType.CD_IMAGEUVREAR:
                        {
                            if (aData is Bitmap bmp)
                            {
                                Log.Information($"[{aDataType}] Image received. Size: {bmp.Width}x{bmp.Height}");
                                imagebase54 = ConvertBitmapToBase64(bmp, ImageFormat.Jpeg);
                                //facebase64 = imagebase54;
                                Log.Information($"base64 length : {imagebase54.Length}");
                                SaveBitmapAsync(bmp, aDataType.ToString());
                            }
                            else
                            {
                                Log.Information($"[{aDataType}] (null bitmap)");
                            }
                            break;
                        }
                    case MMM.Readers.FullPage.DataType.CD_IMAGEPHOTODATA:
                        {
                            Log.Information("[IMAGEPHOTODATA] Received payload type: " + (aData?.GetType().ToString() ?? "(null)"));
                            // If you need to process this, enqueue background worker and parse there.
                            break;
                        }
                    case MMM.Readers.FullPage.DataType.CD_SCDG1_CODELINE:
                    case MMM.Readers.FullPage.DataType.CD_SCDG1_CODELINE_DATA:
                        {
                            MMM.Readers.CodelineData codeline = (MMM.Readers.CodelineData)aData;

                            Log.Information(codeline.Line1);
                            Log.Information(codeline.Line2);
                            Log.Information(codeline.Line3);

                            Log.Information("Surname: " + codeline.Surname);
                            Log.Information("Forenames: " + codeline.Forenames);
                            Log.Information("Nationality: " + codeline.Nationality);
                            Log.Information("Sex: " + codeline.Sex);
                            Log.Information(
                                "DateOfBirth: " +
                                string.Format("{0:00}-{1:00}-{2:00}",
                                    codeline.DateOfBirth.Day,
                                    codeline.DateOfBirth.Month,
                                    codeline.DateOfBirth.Year
                                )
                            );

                            Log.Information("DocumentNumber: " + codeline.DocNumber);
                            Log.Information("DocumentType: " + codeline.DocType);
                            break;
                        }
                    case MMM.Readers.FullPage.DataType.CD_SCDG2_PHOTO:
                    case MMM.Readers.FullPage.DataType.CD_SCDG6_EDL_PHOTO:
                        {
                            if (aData is Bitmap bmp)
                            {
                                Log.Information($"[{aDataType}] Image received. Size: {bmp.Width}x{bmp.Height}");
                                imagebase64Chip = ConvertBitmapToBase64(bmp, ImageFormat.Jpeg);
                                Log.Information($"base64 length : {imagebase64Chip.Length}");
                                SaveBitmapAsync(bmp, aDataType.ToString());
                            }
                            else
                            {
                                Log.Information($"[{aDataType}] (null bitmap)");
                            }

                            break;
                        }
                    case MMM.Readers.FullPage.DataType.CD_SCDG1_VALIDATE:
                    case MMM.Readers.FullPage.DataType.CD_SCSIGNEDATTRS_VALIDATE:
                    case MMM.Readers.FullPage.DataType.CD_SCSIGNATURE_VALIDATE:
                    case MMM.Readers.FullPage.DataType.CD_SCDG2_VALIDATE:
                    case MMM.Readers.FullPage.DataType.CD_SCDG2_FILE:
                    case MMM.Readers.FullPage.DataType.CD_SCBAC_STATUS:
                    case MMM.Readers.FullPage.DataType.CD_ACTIVE_AUTHENTICATION:
                    case MMM.Readers.FullPage.DataType.CD_SAC_STATUS:
                    case MMM.Readers.FullPage.DataType.CD_CHECKSUM:
                        {
                            if (aData is int checksum)
                                Log.Information($"[CHECKSUM] Value: {checksum}");
                            else
                                Log.Information($"[CHECKSUM] Payload type: {aData?.GetType()}; value: {aData}");
                            break;
                        }
                    default:
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Log.Information($"[DATA] {aDataType} => Unhandled data type.");
                            Console.ResetColor();
                            break;
                        }
                }

                Thread.Sleep(200);
            }
            catch (Exception e)
            {
                // Log full exception including stack trace to help diagnose native/interop issues
                LogError(MMM.Readers.ErrorCode.UNKNOWN_ERROR_OCCURRED, e.ToString());
                Log.Information(e.ToString());
            }
        }

        // --- Utilities ---
        void SaveBitmapAsync(Bitmap bmp, string tag)
        {
            var copy = new Bitmap(bmp); 
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {

                    string outDir = Path.Combine(Environment.CurrentDirectory, "output");
                    Directory.CreateDirectory(outDir);
                    string fileName = $"{tag}.png";
                    string path = Path.Combine(outDir, fileName);
                    
                    if(tag == "CD_IMAGEVIS")
                    {
                        _lastScanResult.Data.RgbImage.Location = path;
                        faceLocation = path;
                    }
                    
                    copy.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    copy.Dispose();
                    Log.Information($"[SAVED] {path}");
                }
                catch (Exception ex)
                {
                    Log.Information("[WARN] SaveBitmapAsync failed: " + ex.Message);
                }
            });
        }

        void EventCallbackThreadHelper(MMM.Readers.FullPage.EventCode aEventType)
        {
            EventCallback(aEventType);
        }

        MMM.Readers.FullPage.ReaderSettings _settings;
        void EventCallback(MMM.Readers.FullPage.EventCode aEventType)
        {
            try
            {
                LogEvent(aEventType);

                switch (aEventType)
                {
                    case MMM.Readers.FullPage.EventCode.SETTINGS_INITIALISED:
                        {
                            MMM.Readers.ErrorCode errorCode = MMM.Readers.FullPage.Reader.GetSettings(
                                out _settings
                            );

                            if (errorCode == MMM.Readers.ErrorCode.NO_ERROR_OCCURRED)
                            {
                                if (_settings.puCameraSettings.puSplitImage == false)
                                {
                                    // TabControl remove → Console equivalent
                                    Log.Information("[UI] Removing ImagesRearTab");
                                }

                                _settings.puDataToSend.send |=
                                    MMM.Readers.FullPage.DataSendSet.Flags.DOCMARKERS;
                                _settings.puDataToSend.special |=
                                    MMM.Readers.FullPage.DataSendSet.Flags.VISIBLEIMAGE |
                                    MMM.Readers.FullPage.DataSendSet.Flags.IRIMAGE;

                                _settings.puDataToSend.special &=
                                    ~MMM.Readers.FullPage.DataSendSet.Flags.UVIMAGE;

                                MMM.Readers.FullPage.Reader.UpdateSettings(_settings);

                                MMM.Readers.FullPage.Reader.EnableLogging(
                                    true,
                                    _settings.puLoggingSettings.logLevel,
                                    (int)_settings.puLoggingSettings.logMask,
                                    "HLNonBlockingExample.Net.log"
                                );

                                String lFeatureInfo = "";
                                errorCode = MMM.Readers.FullPage.Reader.GetLicenseFeatures(ref lFeatureInfo);
                                if (errorCode == MMM.Readers.ErrorCode.NO_ERROR_OCCURRED &&
                                    lFeatureInfo.Length > 0)
                                {
                                    Log.Information($"{DateTime.UtcNow.ToLongTimeString()} | LicenseInfo | {lFeatureInfo}");
                                }
                            }
                            else
                            {
                                Log.Information(
                                    $"[ERROR] GetSettings failure: {errorCode}"
                                );
                            }
                            break;
                        }
                    case MMM.Readers.FullPage.EventCode.DOC_ON_WINDOW:
                        {
                            //if (_deviceSettings.AutoScan)
                            //{

                            //} else
                            //{
                            //    Console.WriteLine("[DOC] DOC_ON_WINDOW detected, but AutoScan is disabled.");
                            //    MMM.Readers.FullPage.Reader.SetState(MMM.Readers.FullPage.ReaderState.READER_DISABLED, false);
                            //}
                            Log.Information("[DOC] DOC_ON_WINDOW detected");
                            if (MMM.Readers.FullPage.Reader.GetState() == MMM.Readers.FullPage.ReaderState.READER_DISABLED)
                            {
                                MMM.Readers.FullPage.Reader.InsertDocument();
                                MMM.Readers.FullPage.Reader.SetState(MMM.Readers.FullPage.ReaderState.READER_ENABLED, false);
                            }
                            prDocStartTime = DateTime.UtcNow;
                            Clear();
                            break;
                        }
                    case MMM.Readers.FullPage.EventCode.PLUGINS_INITIALISED:
                        {
                            int lIndex = 0;
                            string lPluginName = "";

                            while (
                                MMM.Readers.FullPage.Reader.GetPluginName(ref lPluginName, lIndex)
                                == MMM.Readers.ErrorCode.NO_ERROR_OCCURRED &&
                                lPluginName.Length > 0
                            )
                            {
                                Log.Information($"{DateTime.UtcNow.ToLongTimeString()} | Plugin Found | {lPluginName}");
                                ++lIndex;
                            }

                            prIsAT10K550 = false;

                            MMM.Readers.FullPage.HardwareConfig hardwareConfig =
                                MMM.Readers.Modules.Reader.GetHardwareConfig(false);

                            var firmwareVersion = new string(hardwareConfig.puLCBPartNum);
                            if (!string.IsNullOrEmpty(firmwareVersion) &&
                                (firmwareVersion.StartsWith("FW00286") ||
                                 firmwareVersion.StartsWith("FW00318")))
                            {
                                prIsAT10K550 = true;
                            }

                            break;
                        }
                    case MMM.Readers.FullPage.EventCode.END_OF_DOCUMENT_DATA:
                        {
                            var _deviceSettingslocal = _deviceSettings;

                            Log.Information("[DOC] END_OF_DOCUMENT_DATA data received");
                            TimeSpan duration = (DateTime.UtcNow - prDocStartTime);
                            float docTime = duration.Ticks / TimeSpan.TicksPerSecond;

                            if (MMM.Readers.FullPage.Reader.GetState() != MMM.Readers.FullPage.ReaderState.READER_DISABLED)
                            {
                                MMM.Readers.FullPage.Reader.SetState(MMM.Readers.FullPage.ReaderState.READER_DISABLED, false);
                            }

                            _ = Task.Run(async () =>
                            {
                                if (_deviceSettingslocal.Callback.Enable)
                                {
                                    await _apiService.SendCallbackAsync(
                                        mrzString,
                                        _lastScanResult.Data.RgbImage.ImgBase64,
                                        _lastScanResult.Data.RgbImage.Location,
                                        facebase64,
                                        _deviceSettings.Callback.Url,
                                        format,
                                        faceLocation,
                                        _lastScanResult.Err_msg
                                        );
                                }
                            });

                            // statusBar.Panels[1].Text replacement:
                            Log.Information($"Time: {docTime}s");

                            break;
                        }
                }

                UpdateState(MMM.Readers.FullPage.Reader.GetState());
            }
            catch (Exception e)
            {
                Log.Information($"[ERROR] EventCallback failed: {e.Message}");
                LogError(0, e.Message);
            }
        }

        void ErrorCallbackThreadHelper(MMM.Readers.ErrorCode aErrorCode, string aErrorMessage)
        {
            ErrorCallback(aErrorCode, aErrorMessage);
        }

        void ErrorCallback(MMM.Readers.ErrorCode aErrorCode, string aErrorMessage)
        {
            LogError(aErrorCode, aErrorMessage);
        }

        void WarningCallbackThreadHelper(MMM.Readers.WarningCode aWarningCode, string aWarningMessage)
        {
            string threadInfo = Thread.CurrentThread.ManagedThreadId.ToString();
            Log.Information($"[THREAD] Warning callback triggered on thread {threadInfo}");
            WarningCallback(aWarningCode, aWarningMessage);
        }

        void WarningCallback(MMM.Readers.WarningCode aWarningCode, string aWarningMessage)
        {
            LogWarning(aWarningCode, aWarningMessage);
        }

        bool CertificateCallbackThreadHelper(byte[] aCertIdentifier, MMM.Readers.Modules.RF.CertType aCertType, out byte[] aCertBuffer)
        {
            Log.Information("[THREAD] Running certificate callback in current thread (console mode)");
            return CertificateCallback(aCertIdentifier, aCertType, out aCertBuffer);
        }

        bool CertificateCallback(
        byte[] aCertIdentifier,
        MMM.Readers.Modules.RF.CertType aCertType,
        out byte[] aCertBuffer
        )
        {
            bool lSuccess = false;
            aCertBuffer = Array.Empty<byte>();

            try
            {
                string certDir = @"C:\certs\";
                Log.Information($"[CERT] Loading external certificate for: {aCertType}");
                Log.Information($"[INFO] Searching certificates in: {certDir}");

                if (!Directory.Exists(certDir))
                {
                    Log.Information($"[ERROR] Certificate directory not found: {certDir}");
                    return false;
                }

                // Cari file dengan ekstensi yang sesuai
                string[] files = Directory.GetFiles(certDir, "*.*")
                    .Where(f => f.EndsWith(".cer", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".der", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".cvcert", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".pkcs8", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (files.Length == 0)
                {
                    Log.Information("[WARN] No certificate files found in directory.");
                    return false;
                }

                // Ambil file pertama (atau bisa diatur berdasarkan nama tertentu)
                string selectedFile = files[0];
                Log.Information($"[INFO] Using certificate file: {Path.GetFileName(selectedFile)}");

                // Baca file ke buffer
                //using (var fs = File.OpenRead(selectedFile))
                //{
                //    aCertBuffer = new byte[fs.Length];
                //    fs.Read(aCertBuffer, 0, aCertBuffer.Length);
                //}

                Log.Information($"[OK] Loaded certificate ({aCertBuffer.Length} bytes)");
                lSuccess = true;
            }
            catch (Exception ex)
            {
                Log.Information($"[ERROR] Could not read certificate file: {ex.Message}");
            }

            return lSuccess;
        }

        void Clear()
        {
            Log.Information("----------------------------------------------------");
            Log.Information("[CLEAR] Resetting document data before new scan...");
            Log.Information("----------------------------------------------------");

            // currentMrz = null;
            // currentPhoto = null;
            // lastScanData.Clear();

            //// Reset variabel status jika diperlukan
            //prDetectStateCounter = 0;
            //prDetectState = MMM.Readers.FullPage.ReaderDocumentDetectionState.MovingDocument;

            //// Reset timer start dokumen
            //prDocStartTime = DateTime.UtcNow;
        }

        void UpdateState(MMM.Readers.FullPage.ReaderState state)
        {
            Log.Information($">>> [STATE] Reader state changed: {state}");

            TimeSpan duration = DateTime.UtcNow - prDocStartTime;
            float docTime = (float)duration.TotalSeconds;

            Log.Information($">>> [TIME] Document process duration: {docTime:F2} seconds");
        }

        void HighlightCodelineCheckDigits(MMM.Readers.CodelineData aCodeline)
        {
            if (aCodeline.CheckDigitDataListCount == 0 &&
                string.IsNullOrEmpty(aCodeline.Line1) &&
                string.IsNullOrEmpty(aCodeline.Line2) &&
                string.IsNullOrEmpty(aCodeline.Line3))
            {
                Log.Information("[MRZ] No codeline data available to highlight.");
                return;
            }

            Log.Information("----------------------------------------------------");
            Log.Information("[MRZ] Highlighting check digits...");
            Log.Information("----------------------------------------------------");

            for (int loop = 0; loop < aCodeline.CheckDigitDataListCount; loop++)
            {
                var cdData = aCodeline.CheckDigitDataList[loop];
                int index = cdData.puCodelinePos;

                for (int line = 1; line < cdData.puCodelineNumber; line++)
                {
                    switch (line)
                    {
                        case 1:
                            index += aCodeline.Line1?.Length ?? 0;
                            index++; 
                            break;
                        case 2:
                            index += aCodeline.Line2?.Length ?? 0;
                            index++;
                            break;
                    }
                }

                bool valid = cdData.puValueExpected == cdData.puValueRead;
                Console.ForegroundColor = valid ? ConsoleColor.Green : ConsoleColor.Red;

                Log.Information(
                    $"[MRZ] Check Digit #{loop + 1} | Line: {cdData.puCodelineNumber} | Pos: {index} | " +
                    $"Expected: {cdData.puValueExpected} | Read: {cdData.puValueRead} | " +
                    $"{(valid ? "VALID" : "INVALID")}"
                );

                Console.ResetColor();
            }
        }

        void LogDataItem(MMM.Readers.FullPage.DataType aDataType, object aData)
        {
            if (aDataType == MMM.Readers.FullPage.DataType.CD_SWIPE_MSR_DATA)
            {
                var msrData = (MMM.Readers.Modules.Swipe.MsrData)aData;

                LogDataItem("MSR_TRACK_1", msrData.Track1);
                LogDataItem("MSR_TRACK_2", msrData.Track2);
                LogDataItem("MSR_TRACK_3", msrData.Track3);
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_AAMVA_DATA)
            {
                var aamvaData = (MMM.Readers.AAMVAData)aData;

                LogDataItem("AAMVA_FULL_NAME", aamvaData.Parsed.FullName);
                LogDataItem("AAMVA_LICENCE_NUMBER", aamvaData.Parsed.LicenceNumber);
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_DIGITAL_GREEN_CERTIFICATE)
            {
                var dgcData = (MMM.Readers.eDV_DigitalGreenCertificateData)aData;

                LogDataItem("CD_DIGITAL_GREEN_CERTIFICATE", "JSON: " + dgcData.CertificateClaim.HealthCertificate);
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_DGC_SIGNATURE_VALIDATE)
            {
                var lValidationResult = (MMM.Readers.Modules.RF.ValidationCode)aData;

                LogDataItem("CD_DGC_SIGNATURE_VALIDATE", lValidationResult.ToString());
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_DGC_DOC_SIGNER_CERT_VALIDATE)
            {
                var lValidationResult = (MMM.Readers.Modules.RF.ValidationCode)aData;

                LogDataItem("CD_DGC_DOC_SIGNER_CERT_VALIDATE", lValidationResult.ToString());
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_MDL_DATA)
            {
                var mdlData = (MMM.Readers.eDV_mDLData)aData;

                LogDataItem("CD_MDL_DATA", "DocumentNumber: " + mdlData.DocumentNumber);
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_CUSTOM_MDL_DATA)
            {
                string jsonResponse = System.Text.Encoding.ASCII.GetString((byte[])aData);

                LogDataItem("CD_CUSTOM_MDL_DATA", "JSON Response: " + jsonResponse);
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_VDSNC_DATA)
            {
                var vdsnc = (MMM.Readers.eDV_VDSNCData)aData;

                LogDataItem("ICAO_VDSNC_TYPE", vdsnc.Type);
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_VDSNE_DATA)
            {
                var vdsne = (MMM.Readers.eDV_VDSNEData)aData;

                LogDataItem("ICAO_VDSNE_DOCUMENT_TYPE_CATEGORY", vdsne.Header.DocumentTypeCategory);
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_VDSNC_SIGNATURE_VALIDATE)
            {
                var lValidationResult = (MMM.Readers.Modules.RF.ValidationCode)aData;

                LogDataItem("CD_VDSNC_SIGNATURE_VALIDATE", lValidationResult.ToString());
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_VDSNC_DOC_SIGNER_CERT_VALIDATE)
            {
                var lValidationResult = (MMM.Readers.Modules.RF.ValidationCode)aData;

                LogDataItem("CD_VDSNC_DOC_SIGNER_CERT_VALIDATE", lValidationResult.ToString());
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_VDSNE_SIGNATURE_VALIDATE)
            {
                var lValidationResult = (MMM.Readers.Modules.RF.ValidationCode)aData;

                LogDataItem("CD_VDSNE_SIGNATURE_VALIDATE", lValidationResult.ToString());
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_VDSNE_DOC_SIGNER_CERT_VALIDATE)
            {
                var lValidationResult = (MMM.Readers.Modules.RF.ValidationCode)aData;

                LogDataItem("CD_VDSNE_DOC_SIGNER_CERT_VALIDATE", lValidationResult.ToString());
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_2D_DOC_IDENTITY_DOC_DATA)
            {
                var dddoc = (MMM.Readers.eDV_2D_DocData_IdentityDocuments)aData;

                LogDataItem("2D_DOC_IDENTITY_DOC_ID_NUMBER", dddoc.IdNumber);
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_2D_DOC_SIGNATURE_VALIDATE)
            {
                var lValidationResult = (MMM.Readers.Modules.RF.ValidationCode)aData;

                LogDataItem("CD_2D_DOC_SIGNATURE_VALIDATE", lValidationResult.ToString());
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_2D_DOC_DOC_SIGNER_CERT_VALIDATE)
            {
                var lValidationResult = (MMM.Readers.Modules.RF.ValidationCode)aData;

                LogDataItem("CD_2D_DOC_DOC_SIGNER_CERT_VALIDATE", lValidationResult.ToString());
            }
            else if (aDataType > MMM.Readers.FullPage.DataType.CD_PLUGIN)
            {
                var pluginData = (MMM.Readers.FullPage.PluginData)aData;

                Console.WriteLine(pluginData.puDataFormat.ToString()); // listView.Items.Add

                string lInfo = pluginData.puFeatureName + " " + pluginData.puFieldName + ": ";

                if (pluginData.puData is string)
                    LogDataItem(aDataType.ToString(), lInfo + (string)pluginData.puData);
                else if (pluginData.puData is byte[])
                    LogDataItem(aDataType.ToString(), lInfo + ((byte[])pluginData.puData).Length + " bytes");
                else
                    LogDataItem(aDataType.ToString(), lInfo + aData);
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_DATA_CAPTURE_LITE)
            {
                LogDataItem(aDataType.ToString(), aData);
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_TEXT_DATA_EXTRACTED)
            {
                LogDataItem(aDataType.ToString(), aData);
            }
            else if (aDataType == MMM.Readers.FullPage.DataType.CD_DETECT_PROGRESS)
            {
                var lDocumentDetect = (MMM.Readers.FullPage.ReaderDocumentDetection)aData;
                var lState = lDocumentDetect.state;

                string name = lState.ToString();

                if (prDetectState != lState)
                {
                    prDetectState = lState;
                    LogDataItem(aDataType.ToString(), name);
                }
                else
                {
                    if (prDetectStateCounter > 6)
                    {
                        LogDataItem(aDataType.ToString(), name);
                        prDetectStateCounter = 0;
                    }
                    prDetectStateCounter++;
                }
            }
            else
            {
                LogDataItem(aDataType.ToString(), aData);
            }
        }

        void LogDataItem(string aDataType, object aData)
        {
            TimeSpan duration = (DateTime.UtcNow - prDocStartTime);
            float dataItemTime = duration.Ticks / TimeSpan.TicksPerSecond;

            // Console output menggantikan ListViewItem
            Console.Write($"[{dataItemTime:0.00}s] {aDataType} -> ");

            if (aData != null)
            {
                if (aData is string || aData is int)
                {
                    Log.Information(aData.ToString());
                }
                else
                {
                    Log.Information(aData.GetType().ToString());
                }
            }
            else
            {
                Log.Information("(null)");
            }

            //TimeSpan duration = DateTime.UtcNow - prDocStartTime;
            //float seconds = (float)duration.TotalSeconds;

            //Console.WriteLine($"{seconds}s | {aDataType}");

            //if (aData != null)
            //{
            //    if (aData is string || aData is int)
            //        Console.WriteLine($"  → {aData}");
            //    else
            //        Console.WriteLine($"  → {aData.GetType()}");
            //}

            //Console.WriteLine("--------------------------------------");
        }

        void LogEvent(MMM.Readers.FullPage.EventCode aEventType)
        {
            string timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
            string stateInfo = "";

            if (aEventType == MMM.Readers.FullPage.EventCode.READER_STATE_CHANGED)
            {
                try
                {
                    var currentState = MMM.Readers.FullPage.Reader.GetState();
                    stateInfo = $" | State: {currentState}";
                }
                catch
                {
                    stateInfo = " | State: (unavailable)";
                }
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Log.Information($"[{timestamp}] [EVENT] {aEventType}{stateInfo}");
            Console.ResetColor();
        }

        void LogError(MMM.Readers.ErrorCode aErrorCode, string aErrorMessage)
        {
            string timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
            Console.ForegroundColor = ConsoleColor.Red;
            Log.Information($"[{timestamp}] [ERROR] Code: {aErrorCode} | Message: {aErrorMessage}");
            Console.ResetColor();
        }

        protected System.DateTime prDocStartTime = System.DateTime.UtcNow;
        private CallbackSettings callback;

        void LogWarning(MMM.Readers.WarningCode aWarningCode, string aWarningMessage)
        {
            string timestamp = DateTime.UtcNow.ToString("HH:mm:ss.fff");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Log.Information($"[{timestamp}] [WARNING] Code: {aWarningCode} | Message: {aWarningMessage}");
            Console.ResetColor();
        }

        bool PasswordCorrectionCallback(ref MMM.Readers.Modules.RF.RFAccessControlPasswords aPasswordsInOut,
                MMM.Readers.Modules.RF.AllowedPasswordMethods aAllowedPasswordMethods,
                ref MMM.Readers.Modules.RF.AccessControlPasswordMethod aPasswordMethodOut,
               ref bool aChipRemovedFromFieldOut)
        {
            bool result = false;
            //FormBACKeyCorrection lForm = new FormBACKeyCorrection();
            if (aPasswordMethodOut == MMM.Readers.Modules.RF.AccessControlPasswordMethod.PASSWORD_MRZ)
            {
                String Line1 = "", Line2 = "", Line3 = "";

                // look for "\r" as line delimiters, max3 lines
                int lastEndPoint = 0, endPoint = aPasswordsInOut.puFullMRZ.IndexOf('\r');
                if (endPoint > 0)
                {
                    Line1 = aPasswordsInOut.puFullMRZ.Substring(0, endPoint);
                    if (aPasswordsInOut.puFullMRZ.Length > endPoint)
                    {
                        lastEndPoint = endPoint + 1;
                        endPoint = aPasswordsInOut.puFullMRZ.IndexOf('\r', lastEndPoint);
                        if (endPoint > 0)
                        {
                            Line2 = aPasswordsInOut.puFullMRZ.Substring(lastEndPoint, endPoint - lastEndPoint);
                            if (aPasswordsInOut.puFullMRZ.Length > endPoint)
                            {
                                lastEndPoint = endPoint + 1;
                                endPoint = aPasswordsInOut.puFullMRZ.IndexOf('\r', lastEndPoint);
                                if (endPoint > 0)
                                {
                                    Line3 = aPasswordsInOut.puFullMRZ.Substring(lastEndPoint, endPoint - lastEndPoint);
                                }
                                else
                                {
                                    Line3 = aPasswordsInOut.puFullMRZ.Substring(lastEndPoint);
                                }
                            }
                        }
                    }
                }

                Log.Information("[INFO] Using existing MRZ data automatically.");

                // Gunakan MRZ yang sudah ada dari aPasswordsInOut
                if (!string.IsNullOrWhiteSpace(aPasswordsInOut.puFullMRZ))
                {
                    aPasswordsInOut.puFullMRZ = $"{Line1}\r{Line2}\r{Line3}".Trim();
                    result = true;
                }
                else
                {
                    Log.Information("[WARN] No MRZ data found. Using empty default MRZ.");
                    aPasswordsInOut.puFullMRZ = $"{Line1}\r{Line2}\r{Line3}".Trim();
                    result = true;
                }
            }
            else if (aPasswordMethodOut == MMM.Readers.Modules.RF.AccessControlPasswordMethod.PASSWORD_CAN)
            {
                if (!string.IsNullOrWhiteSpace(aPasswordsInOut.puCardAccessNumber))
                {
                    Log.Information($"[INFO] Using existing CAN: {aPasswordsInOut.puCardAccessNumber}");
                    aPasswordsInOut.puFullMRZ = aPasswordsInOut.puCardAccessNumber;
                    result = true;
                }
                else
                {
                    Log.Information("[WARN] CAN is missing — using default fallback CAN.");
                    aPasswordsInOut.puFullMRZ = "000000"; // fallback default
                    result = true;
                }
            }

            return result;
        }

        bool PasswordCorrectionCallbackThreadHelper(ref MMM.Readers.Modules.RF.RFAccessControlPasswords aPasswordsInOut, MMM.Readers.Modules.RF.AllowedPasswordMethods aAllowedPasswordMethods, ref MMM.Readers.Modules.RF.AccessControlPasswordMethod aPasswordMethodOut, ref bool aChipRemovedFromFieldOut)
        {
            try
            {
                // Langsung panggil callback tanpa Invoke
                bool result = PasswordCorrectionCallback(
                    ref aPasswordsInOut,
                    aAllowedPasswordMethods,
                    ref aPasswordMethodOut,
                    ref aChipRemovedFromFieldOut
                );

                return result;
            }
            catch (Exception ex)
            {
                Log.Information($"[ERROR] PasswordCorrectionCallback failed: {ex.Message}");
                return false;
            }
        }

        public void InitialiseReader()
        {
            Log.Information(">>> [INIT] Starting reader initialization...");

            try
            {
                // Enable logging
                MMM.Readers.FullPage.Reader.EnableLogging(true, 1, -1, "HLNonBlockingExample.Net.log");
                Log.Information(">>> [INIT] Logging enabled.");

                UpdateState(MMM.Readers.FullPage.ReaderState.READER_NOT_INITIALISED);
                MMM.Readers.ErrorCode lResult = MMM.Readers.ErrorCode.NO_ERROR_OCCURRED;

                //Console.WriteLine("[INFO] PowerModeChanged listener skipped (console app).");

                lResult = MMM.Readers.FullPage.Reader.Initialise(
                    new MMM.Readers.FullPage.DataDelegate(DataCallback),
                    new MMM.Readers.FullPage.EventDelegate(EventCallbackThreadHelper),
                    new MMM.Readers.ErrorDelegate(ErrorCallbackThreadHelper),
                    new MMM.Readers.FullPage.CertificateDelegate(CertificateCallbackThreadHelper),
                    true,
                    false
                );

                if (lResult != MMM.Readers.ErrorCode.NO_ERROR_OCCURRED)
                {
                    Log.Information($"[ERROR] Initialise failed: {lResult}");
                    //return;
                }

                Log.Information(">>> [INIT] Reader initialized successfully.");

                // Set warning callback
                MMM.Readers.FullPage.Reader.SetWarningCallback(
                    new MMM.Readers.WarningDelegate(WarningCallbackThreadHelper)
                );
                Log.Information(">>> [INIT] Warning callback set.");

                MMM.Readers.FullPage.ReaderSettings settings;
                MMM.Readers.ErrorCode errorCode = MMM.Readers.FullPage.Reader.GetSettings(
                    out settings
                );

                // Retrieve settings and configure RF password callback if needed
                //if (MMM.Readers.FullPage.Reader.GetSettings(out MMM.Readers.FullPage.ReaderSettings settings)
                //    == MMM.Readers.ErrorCode.NO_ERROR_OCCURRED)
                //{
                if ((settings.puDataToSend.send & MMM.Readers.FullPage.DataSendSet.Flags.SMARTCARD) != 0)
                {
                    MMM.Readers.FullPage.Reader.SetRFPasswordCallback(
                        new MMM.Readers.FullPage.RFPasswordDelegate(PasswordCorrectionCallbackThreadHelper)
                    );
                    Log.Information(">>> [INIT] RF Password callback set.");
                }
                //}

                Log.Information(">>> [INIT] Reader setup complete.");
            }
            //catch (DllNotFoundException ex)
            //{
            //    Console.WriteLine(
            //        $"[ERROR] {ex.Message}\n" +
            //        "Ensure your working directory is set to the Page Reader\\bin folder.\n" +
            //        "When running in Visual Studio, set this under: Properties → Debug → Working Directory."
            //    );
            //}
            catch (Exception ex)
            {
                Log.Information($"[ERROR] Unhandled exception during initialization: {ex.Message}");
            }
        }

        private void SettingsDataToSendConsole(Object aSender, EventArgs aEventArgs)
        {
            MMM.Readers.FullPage.ReaderState state = MMM.Readers.FullPage.Reader.GetState();
            if (state == MMM.Readers.FullPage.ReaderState.READER_NOT_INITIALISED ||
                state == MMM.Readers.FullPage.ReaderState.READER_ERRORED ||
                state == MMM.Readers.FullPage.ReaderState.READER_FATAL_ERRORED ||
                state == MMM.Readers.FullPage.ReaderState.READER_TERMINATED ||
                state == MMM.Readers.FullPage.ReaderState.READER_READING)
            {
                Log.Information("Reader state unavailable for configuration change");
                return;
            }
            ThalesDataSend lDataToSendDlg = new ThalesDataSend();
            //lDataToSendDlg.ShowDialog();
            if (lDataToSendDlg.Result == true)
            {
                MMM.Readers.FullPage.ReaderSettings settings;
                MMM.Readers.ErrorCode errorCode = MMM.Readers.FullPage.Reader.GetSettings(
                    out settings
                );
                if ((settings.puDataToSend.send & MMM.Readers.FullPage.DataSendSet.Flags.SMARTCARD) != 0)
                    MMM.Readers.FullPage.Reader.SetRFPasswordCallback(new MMM.Readers.FullPage.RFPasswordDelegate(PasswordCorrectionCallbackThreadHelper));

            }
        }

        private void SettingsSaveConsole(Object aSender, EventArgs aEventArgs)
        {
            MMM.Readers.FullPage.ReaderState state = MMM.Readers.FullPage.Reader.GetState();
            if (state == MMM.Readers.FullPage.ReaderState.READER_NOT_INITIALISED ||
                state == MMM.Readers.FullPage.ReaderState.READER_ERRORED ||
                state == MMM.Readers.FullPage.ReaderState.READER_FATAL_ERRORED ||
                state == MMM.Readers.FullPage.ReaderState.READER_TERMINATED ||
                state == MMM.Readers.FullPage.ReaderState.READER_READING)
            {
                Log.Information("Reader state unavailable for configuration change");
                return;
            }
            MMM.Readers.FullPage.Reader.SaveSettings();
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
