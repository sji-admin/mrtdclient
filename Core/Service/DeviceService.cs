using cmrtd.Core.Model;
using cmrtd.Infrastructure.DeskoDevice;
using Desko.DDA;
using Desko.ePass;
using Desko.EPass;
using Desko.FullPage;
using System.Diagnostics;

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
        private bool _disposed;
        private static readonly ManualResetEvent scanDoneEvent = new(false);
        private string _lastErrorMessage;
        public Pasport.ScanApiResponse LastScanResult => _deviceHandler.LastScanResult;
        public Pasport.ScanApiResponse LastScanResultCki => _devicePscan.LastScanResult;

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
                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE] Default TargetImageFormat was set to JPG");
            }
            
            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE] Initialized");

            _deviceManager = new DeviceManager();

            _deviceManager.ConnectionChanged += (s, e) =>
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE] Connection Changed");

            _deviceManager.DeviceListChanged += (s, e) =>
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE] Device List Changed");

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
                    Console.WriteLine($">>> [DEVICE] cannot be connected: {e.LogMessage}");
                }
                else if (e.LogMessage.Contains("DIMIS entry added for device id"))
                {
                    Console.WriteLine($">>> [DEVICE] New device detected, trying to connect...");

                    if (_deviceManager.IsConnected)
                    {
                        Console.WriteLine($">>> [DEVICE] Device Already Connected");
                    }
                    else
                    {
                        Task.Run(() =>
                        {
                            try
                            {
                                SafeDisconnect();
                                Start();

                                if (_deviceManager != null && _deviceManager.IsConnected)
                                {
                                    Console.WriteLine($">>> [DEVICE] Auto-connected successfully.");
                                    _lastErrorMessage = null;
                                }
                                else
                                {
                                    Console.WriteLine($">>> [DEVICE] Auto-connect failed (device not ready).");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($">>> [DEVICE] Auto-connect exception: {ex.Message}");
                            }
                        });
                    }
                }
                else if (!e.LogMessage.Contains("Ignoring unknown argument"))
                {
                    Console.WriteLine($">>> [DEVICE] Debug: {e.LogMessage}");
                }
            };

            try
            {
                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE] Connecting...");
                _deviceManager.Connect($"{_deviceSettings.Type}\\*");

                if (_deviceManager.IsConnected)
                {
                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE] Connected!");

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
                        Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [DEVICE] Ready For Next Scan");
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
                            _deviceManager.Log(" [DEVICE] Dokumen Masuk");
                            _ = _deviceHandler.DoScanRequestAsync();
                        }
                    }
                }
                else
                {
                    _deviceManager.Log("[DEVICE] Device Not Connected");
                    _deviceManager.Dispose();
                }
            }
            catch (DDAException ex)
            {
                Console.WriteLine($"[ERROR] Device connection failed: {ex.Message}");
                _deviceManager.Disconnect(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Device connection unexpected error: {ex.Message}");
                _deviceManager?.Disconnect(true);
            }
        }

        private static readonly SemaphoreSlim _scanLock = new SemaphoreSlim(1, 1);

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

            await _scanLock.WaitAsync();
            try
            {
                if (_deviceManager == null || !_deviceManager.IsConnected)
                    throw new InvalidOperationException("Device is not connected");

                if (_deviceHandler == null)
                    throw new InvalidOperationException("Device handler is not initialized");

                Console.WriteLine("[SCAN] Waiting for document to be inserted...");
                
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    var status = _deviceManager.Device.DocumentStatus;
                    if (status.HasFlag(DDADocumentStatusFlag.IsDocPresent))
                    {
                        Console.WriteLine($">>> [DEVICE] DokumenT Present after {sw.Elapsed.TotalSeconds:F1} seconds");
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

                Console.WriteLine("[SCAN] Starting manual scan...");
                //_deviceHandler.LastScanResult = null;
                await _deviceHandler.DoScanRequestAsync();

                // Tunggu sampai scan selesai
                bool completed = scanDoneEvent.WaitOne(TimeSpan.FromSeconds(30));
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
                if (result == null)
                {
                    return new Pasport.ScanApiResponse
                    {
                        Code = 500,
                        Valid = false,
                        Err_msg = "No scan result received."
                    };
                }

                Console.WriteLine($"[SCAN] Done in {sw.Elapsed.TotalSeconds:F1} seconds");
                return result;
            }
            finally
            {
                _scanLock.Release();
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
                _deviceManager?.Log($"[ERROR] Device reconnect failed: {ex.Message}");
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

        private void SafeDisconnect()
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
                Console.WriteLine("[WARN] _deviceManager atau Serial4x belum siap atau null");
            }

            if (_devicePscan?.Epassport?.SerialCki != null)
            {
                foreach (var serial in _devicePscan.Epassport.SerialCki)
                    list.Add(("Penta CKI Scanner", serial));
            }
            else
            {
                Console.WriteLine("[WARN] _devicePscan atau SerialCki belum siap atau null");
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
                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE] Connecting...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Device connection failed: {ex.Message}");
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

            await _scanLock.WaitAsync();
            try
            {
                Console.WriteLine("[SCAN] Waiting for document to be inserted...");

                var sw = Stopwatch.StartNew();
                while (true)
                {
                    var status = Desko.FullPage.Api.IsDocumentPresent();
                    if (status == true)
                    {
                        Console.WriteLine($">>> [DEVICE] DokumenT Present after {sw.Elapsed.TotalSeconds:F1} seconds");
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

                Console.WriteLine("[SCAN] Starting manual scan Cki...");
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

                Console.WriteLine($"[SCAN] Done in {sw.Elapsed.TotalSeconds:F1} seconds");
                return result;
            }
            finally
            {                
                _scanLock.Release();
            }
        }

        public void StopCki()
        {
            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE] Disconnecting...");
            _devicePscan = new DevicePscan(_deviceSettings.Callback, _deviceSettings, _apiService);
            _devicePscan.DisconnectDevice();
        }

        #endregion 

    }
}