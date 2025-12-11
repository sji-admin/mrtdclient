using cmrtd.Core.Model;
using cmrtd.Infrastructure.DeskoDevice;
using cmrtd.Infrastructure.ThalesDevice;
using Desko.DDA;
using Desko.ePass;
using Desko.EPass;
using Desko.FullPage;
using Serilog;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Text.Json;

namespace cmrtd.Core.Service
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class DeviceService : IDisposable
    {
        private readonly DeviceSettings _deviceSettings;
        private readonly ApiService _apiService;
        private DeviceManager _deviceManager;
        private DeviceHandler _deviceHandler;
        private DevicePscan _devicePscan;
        private ThalesDevicesManager _thalesDevicesManager;
        private bool _disposed;
        //private readonly Epassport _epassport = new Epassport();
        private static readonly ManualResetEvent scanDoneEvent = new(false);
        private string _lastErrorMessage;
        public Pasport.ScanApiResponse LastScanResult => _deviceHandler?.LastScanResult;
        public Pasport.ScanApiResponse LastScanResultCki => _devicePscan?.LastScanResult;

        #region penta 4x
        public DeviceService(IConfiguration config)
        {
            _deviceSettings = config.GetSection("DeviceSettings").Get<DeviceSettings>()
                              ?? throw new InvalidOperationException("DeviceSettings missing");
            _apiService = new ApiService(_deviceSettings.SensepassKai);
        }

        public void Start()
        {
            // Init library sekali di awal
            DDALib.Initialize();
            Desko.ePass.Api.Initialize();

            if (Desko.ePass.Api.Settings.TargetImageFormat == ImageFormat.Unknown)
            {
                Desko.ePass.Api.Settings.TargetImageFormat = ImageFormat.JPG;
            }            

            _deviceManager = new DeviceManager();

            _deviceManager.ConnectionChanged += (s, e) =>
                    Log.Information($"{DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE] Connection Changed");

            _deviceManager.DeviceListChanged += (s, e) =>
                Log.Information($"{DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE] Device List Changed");

            _deviceManager.DebugEvent += (s, e) =>
            {

                if (e.LogMessage.Contains("[ERROR]"))
                {
                    _lastErrorMessage = e.LogMessage;
                    Console.ForegroundColor = ConsoleColor.Red;
                    //Console.WriteLine($">>> [DEVICE] {e.LogMessage}");
                    Console.ResetColor();
                }
                else if (e.LogMessage.Contains("Device cannot be connected"))
                {
                    _lastErrorMessage = e.LogMessage;
                    Log.Information($">>> [DEVICE] cannot be connected: {e.LogMessage}");
                }
                else if (e.LogMessage.Contains("DIMIS entry added for device id"))
                {
                    if (_deviceManager.IsConnected)
                    {
                        Log.Information($">>> [DEVICE] Device Already Connected");
                    }
                    else
                    {
                        Task.Run(async () =>
                        {
                            try
                            {
                                SafeDisconnect();
                                Start();

                                if (_deviceManager != null && _deviceManager.IsConnected)
                                {
                                    Log.Information($">>> [DEVICE] Auto-connected successfully.");
                                    bool docPresentLastTime = false;
                                    var status = _deviceManager.Device.DocumentStatus;
                                    bool docPresent = status.HasFlag(DDADocumentStatusFlag.IsDocPresent);
                                    bool hasFlipped = status.HasFlag(DDADocumentStatusFlag.IsDocFlipped);
                                    bool docPresenceChanged = docPresent != docPresentLastTime || hasFlipped;

                                    if (docPresenceChanged)
                                    {
                                        docPresentLastTime = docPresent;
                                        if (docPresent)
                                        {
                                            Log.Information(" [DEVICE] Dokumen Masuk");
                                            _ = _deviceHandler.DoScanRequestAsync();
                                        }
                                    }
                                    _lastErrorMessage = null;
                                }
                                else
                                {
                                    Log.Information($">>> [DEVICE] Auto-connect failed (device not ready).");
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Information($">>> [DEVICE] Auto-connect exception: {ex.Message}");
                            }
                        });
                    }
                }                
                else
                {
                    // TODO : normal log
                    Log.Information($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE] {e.LogMessage}");
                }
            };
                    
            try
            {
                Log.Information($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE] Connecting...");
                _deviceManager.Connect($"{_deviceSettings.Type}\\*");

                if (_deviceManager.IsConnected)
                {
                    Log.Information($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE] Connected!");

                    var buzzer = _deviceManager.GetBuzzer(0);
                    buzzer.HighTime = 200;
                    buzzer.LowTime = 200;
                    buzzer.Duration = 1000;
                    buzzer.UseBuzzer();

                    var leds = _deviceManager.GetLed(0);
                    leds.HighTime = 2000;
                    leds.LowTime = 0;
                    leds.Duration = 2000;
                    leds.Color = DDAColor.Green;
                    leds.UseLed();

                    _deviceHandler = new DeviceHandler(_deviceManager, _deviceSettings.Dpi, _deviceSettings.Callback, _deviceSettings, _apiService);
                    _deviceHandler.RegisterDeviceHandler(_deviceSettings.AutoScan);

                    // event scan done
                    _deviceManager.Device.ImageRequestDoneEvent += (s, e) =>
                    {
                        scanDoneEvent.Set();
                    };

                    bool docPresentLastTime = false;
                    var status = _deviceManager.Device.DocumentStatus;
                    bool docPresent = status.HasFlag(DDADocumentStatusFlag.IsDocPresent);
                    bool hasFlipped = status.HasFlag(DDADocumentStatusFlag.IsDocFlipped);
                    bool docPresenceChanged = docPresent != docPresentLastTime || hasFlipped;

                    if (docPresenceChanged)
                    {
                        docPresentLastTime = docPresent;
                        if (docPresent)
                        {
                            Log.Information(" [DEVICE] Dokumen Masuk");
                            _ = _deviceHandler.DoScanRequestAsync();
                        }
                    }
                }
                else
                {
                    Log.Information("[DEVICE] Device Not Connected");
                    _deviceManager.Dispose();
                }
            }
            catch (DDAException ex)
            {
                Log.Information($"[ERROR] Device connection failed: {ex.Message}");
                _deviceManager.Disconnect(true);
            }
            catch (Exception ex)
            {
                Log.Information($"[ERROR] Device connection unexpected error: {ex.Message}");
                _deviceManager?.Disconnect(true);
            }
        }

        public async Task<Pasport.ScanApiResponse> DoScanAsync()
        {
            if (_deviceSettings.AutoScan)
            {
                return new Pasport.ScanApiResponse
                {
                    Code = 400,
                    Valid = false,
                    Err_msg = "Manual scan is disabled because AutoScan mode is enabled."
                };
            }

            if (_deviceManager == null || !_deviceManager.IsConnected)
                throw new InvalidOperationException("Device is not connected");

            if (_deviceHandler == null)
                throw new InvalidOperationException("Device handler is not initialized");


            var totalTimeoutSeconds = 21;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(totalTimeoutSeconds));

            var scanTask = DoScanInternalAsync(cts.Token);

            var completed = await Task.WhenAny(scanTask, Task.Delay(Timeout.Infinite, cts.Token));

            if (completed == scanTask)
                return await scanTask;

            return new Pasport.ScanApiResponse
            {
                Code = 408,
                Valid = false,
                Err_msg = $"Scan timeout : {totalTimeoutSeconds}s. Please Scan Again"
            };
        }

        public async Task<Pasport.ScanApiResponse> DoScanInternalAsync(CancellationToken token)
        {
            try
            {

                Log.Information("[SCAN] Waiting for document to be inserted...");

                var sw = Stopwatch.StartNew();
                while (true)
                {

                    var status = _deviceManager.Device.DocumentStatus;
                    if (status.HasFlag(DDADocumentStatusFlag.IsDocPresent))
                    {
                        Log.Information($">>> [DEVICE] DokumenT Present after {sw.Elapsed.TotalSeconds:F1} seconds");
                        break;
                    }

                    if (sw.Elapsed.TotalSeconds > 15)
                    {
                        return new Pasport.ScanApiResponse
                        {
                            Code = 408,
                            Valid = false,
                            Err_msg = "Timeout waiting for document insertion."
                        };
                    }

                    await Task.Delay(200);
                }

                scanDoneEvent.Reset();

                Log.Information("[SCAN] Starting manual scan...");
                var scanResponse = await _deviceHandler.DoScanRequestAsync(token);

                if (scanResponse != null && !scanResponse.Valid)
                {
                    // return immediately if handler reported an error
                    return scanResponse;
                }

                // Tunggu sampai scan selesai
                bool completed = scanDoneEvent.WaitOne(TimeSpan.FromSeconds(10));
                sw.Stop();

                if (!completed)
                {
                    return new Pasport.ScanApiResponse
                    {
                        Code = 408,
                        Valid = false,
                        Err_msg = "Scan timeout: device did not return result. Please Scan Again"
                    };
                }

                var result = _deviceHandler.LastScanResult;
                if (result != null)
                {
                    //Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [SCAN] Scan completed. {result}");
                    //Console.WriteLine($">>> [SCAN] Result Code={result.Code}, Valid={result.Valid}, Err='{result.Err_msg}'");
                    //Console.WriteLine($">>> [SCAN] MRZ length={(result.Data?.MRZ?.Length ?? 0)}");
                    //Console.WriteLine($">>> [SCAN] RGB image path='{result.Data?.RgbImage?.Location}'");
                    //Console.WriteLine($">>> [SCAN] RGB ImgBase64 length={(result.Data?.RgbImage?.ImgBase64?.Length ?? 0)}");
                    //Console.WriteLine($">>> [SCAN] Face ImgBase64 length={(result.Data?.RgbImage?.ImgFaceBase64?.Length ?? 0)}");

                    // save full result to file
                    try
                    {
                        var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScanResponses");
                        Directory.CreateDirectory(folder);
                        var options = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };
                        string json = JsonSerializer.Serialize(result, options);
                        var file = Path.Combine(folder, $"scan_response_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.json");
                        await File.WriteAllTextAsync(file, json, System.Text.Encoding.UTF8);
                        Log.Information($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [SCAN] Saved full response to: {file}");
                    }
                    catch (Exception ex)
                    {
                        Log.Information($">>> {DateTime.Now:HH:mm:ss.fff} [ERROR] >>> [SCAN] Failed to save response: {ex.Message}");
                    }
                } 
                else
                {
                    return new Pasport.ScanApiResponse
                    {
                        Code = 500,
                        Valid = false,
                        Err_msg = "No scan result received."
                    };
                }


                Log.Information($"[SCAN] Done in {sw.Elapsed.TotalSeconds:F1} seconds");
                return result;
            }
            finally
            {
                //_scanLock.Release();
            }
        }

        public string Reconnect()
        {
            if (_deviceManager != null && _deviceManager.IsConnected)
            {
                return "Device is already running";
            }

            try
            {

                _lastErrorMessage = null;

                _deviceManager?.Dispose();

                Start();

                if (!string.IsNullOrEmpty(_lastErrorMessage))
                {
                    return _lastErrorMessage;
                }

                return "Device reconnected successfully";
            }
            catch (Exception ex)
            {
                Log.Information($"[ERROR] Device reconnect failed: {ex.Message}");
                return _lastErrorMessage ?? ex.Message;
            }
        }

        public (bool IsConnected, string ErrorMessage) GetDeviceStatusWithError()
        {
            if (_deviceManager != null && _deviceManager.IsConnected)
            {
                return (true, "");
            }

            if (!string.IsNullOrEmpty(_lastErrorMessage))
            {
                // anggap "already exists" sebagai connected
                if (_lastErrorMessage.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                    return (true, "");
                }

                return (false, _lastErrorMessage.Split('\n').FirstOrDefault() ?? "");
            }

            return (false, ""); // default error kosong kalau memang hanya disconnect biasa
        }

        public void SafeDisconnect()
        {
            try
            {
                _deviceHandler = null;
                _deviceManager?.Disconnect(true);
                _deviceManager?.Dispose();
                _deviceManager = null;
            }
            catch
            {
                // swallow
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            SafeDisconnect();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion

        public List<(string Name, string Serial)> GetDeviceSerialList()
        {
            var list = new List<(string, string)>();

            if (_deviceManager?.Epassport?.Serial4x != null)
            {
                foreach (var serial in _deviceManager.Epassport.Serial4x)
                    list.Add(("Penta 4X Scanner", serial));
            }
            else
            {
                Log.Information("[WARN] _deviceManager atau Serial4x belum siap atau null");
            }

            if (_devicePscan?.Epassport?.SerialCki != null)
            {
                foreach (var serial in _devicePscan.Epassport.SerialCki)
                    list.Add(("Penta CKI Scanner", serial));
            }
            else
            {
                Log.Information("[WARN] _devicePscan atau SerialCki belum siap atau null");
            }

            return list;
        }

        #region penta cki
        public void startCki()
        {
            // Implementasi khusus untuk Penta CKI
            _devicePscan = new DevicePscan(_deviceSettings.Callback, _deviceSettings, _apiService);
            _devicePscan.InitializeApp();

            try { 
                _devicePscan.ConnectDevice();
                Log.Information($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE] Connecting...");
            }
            catch (Exception ex)
            {
                Log.Information($"[ERROR] Device connection failed: {ex.Message}");
            }
        }

        public async Task<Pasport.ScanApiResponse> DoScanCki()
        {            
            if (_deviceSettings.AutoScan)
            {
                return new Pasport.ScanApiResponse
                {
                    Code = 400,
                    Valid = false,
                    Err_msg = "Manual scan is disabled because AutoScan mode is enabled."
                };
            }

            //await _scanLock.WaitAsync();
            try
            {
                Log.Information("[SCAN] Waiting for document to be inserted...");

                var sw = Stopwatch.StartNew();
                while (true)
                {
                    var status = Desko.FullPage.Api.IsDocumentPresent();
                    if (status == true)
                    {
                        Log.Information($">>> [DEVICE] DokumenT Present after {sw.Elapsed.TotalSeconds:F1} seconds");
                        break;
                    }

                    if (sw.Elapsed.TotalSeconds > 15)
                    {
                        return new Pasport.ScanApiResponse
                        {
                            Code = 408,
                            Valid = false,
                            Err_msg = "Timeout waiting for document insertion."
                        };
                    }

                    await Task.Delay(200);
                }

                Log.Information("[SCAN] Starting manual scan Cki...");
                await _devicePscan.ScanAsync();
                sw.Stop();

                var result = LastScanResultCki;
                if (result == null)
                {
                    return new Pasport.ScanApiResponse
                    {
                        Code = 500,
                        Valid = false,
                        Err_msg = "No scan result received."
                    };
                }

                Log.Information($"[SCAN] Done in {sw.Elapsed.TotalSeconds:F1} seconds");
                return result;
            }
            finally
            {                
                //_scanLock.Release();
            }
        }

        public void StopCki()
        {
            Log.Information($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE] Disconnecting...");
            _devicePscan = new DevicePscan(_deviceSettings.Callback, _deviceSettings, _apiService);
            _devicePscan.DisconnectDevice();
        }

        #endregion

        #region Thales Non Blocking

        public void StartThales()
        {
            _thalesDevicesManager = new ThalesDevicesManager(_deviceSettings.Callback, _deviceSettings, _apiService);
            // Implementasi khusus untuk Thales Non Blocking
            _thalesDevicesManager.InitialiseReader();
        }

        public void StopThales()
        {
            _thalesDevicesManager.Terminet();
        }


        #endregion
    }
}