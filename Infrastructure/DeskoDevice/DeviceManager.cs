using cmrtd.Core.Model;
using cmrtd.Infrastructure.Model;
using Desko.DDA;
using System;

namespace cmrtd.Infrastructure.DeskoDevice
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class DeviceManager : IDisposable
    {
        private DDADevice _device = null;
        private DDABuzzer[] _buzzers = null;
        private DDADevice[] _buzzerDevices = null;
        private DDALed[] _leds = null;
        private DDADevice[] _ledDevices = null;
        private DDADisplay _display = null;
        private DDADevice _displayDevice = null;
        private DDAImager _imager = null;
        private DDADevice _imagerDevice = null;
        private string _devicePath = "";
        private DeviceDescriptor _deviceDescriptor = null;
        private Epassport _epassport = new Epassport();
        public Epassport Epassport => _epassport;

        public DeviceManager()
        {
            Disconnect(true);
            DDALib.DebugEvent += DDALib_DebugEvent;
            DDADeviceManager.DeviceListChangedEvent += DDADeviceManager_DeviceListChangedEvent;
        }
        private static readonly object _logLock = new object();
        private static readonly string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_log.txt");
        public event EventHandler ConnectionChanged;
        public event EventHandler DeviceListChanged;
        public event DDALib.DebugHandler DebugEvent;
        public bool IsConnected => _device != null && _device.IsConnected();
        public DeviceDescriptor Descriptor => _deviceDescriptor;
        public string DevicePath => _devicePath;
        public string DeviceClass => _deviceDescriptor?.Description ?? string.Empty;
        public DDADevice Device => _device;
        public DDADisplay Display
        {
            get
            {
                if (_device == null || _device.NumberOfDisplays == 0)
                {
                    return null;
                }

                if (_display == null)
                {
                    _display = new DDADisplay(_device, 0);
                    _displayDevice = _device;
                }
                else if (_displayDevice != _device)
                {
                    _display.Dispose();
                    _display = new DDADisplay(_device, 0);
                    _displayDevice = _device;
                }

                return _display;
            }
        }

        public DDAImager Imager
        {
            get
            {
                if (_device == null || _device.NumberOfImagers == 0)
                    return null;

                if (_imager == null || _imagerDevice != _device)
                {
                    if (_imager != null)
                        _imager.Dispose();
                    _imager = new DDAImager(_device, 0);
                    _imagerDevice = _device;
                }

                return _imager;
            }
        }

        public DDABuzzer GetBuzzer(int index)
        {
            if (_device == null || _device.NumberOfBuzzers <= index)
                return null;

            _buzzers ??= new DDABuzzer[_device.NumberOfBuzzers];
            _buzzerDevices ??= new DDADevice[_device.NumberOfBuzzers];

            if (_buzzers[index] == null || _buzzerDevices[index] != _device)
            {
                _buzzers[index]?.Dispose();
                _buzzers[index] = new DDABuzzer(_device, index);
                _buzzerDevices[index] = _device;
            }
            return _buzzers[index];
        }

        public DDALed GetLed(int index)
        {
            if (_device == null || _device.NumberOfLeds <= index)
                return null;

            _leds ??= new DDALed[_device.NumberOfLeds];
            _ledDevices ??= new DDADevice[_device.NumberOfLeds];

            if (_leds[index] == null || _ledDevices[index] != _device)
            {
                _leds[index]?.Dispose();
                _leds[index] = new DDALed(_device, index);
                _ledDevices[index] = _device;
            }

            return _leds[index];
        }

        public void Connect()
        {
            if (_device == null)
                return;

            _device.Connect();

            _leds = new DDALed[_device.NumberOfLeds];
            _ledDevices = new DDADevice[_device.NumberOfLeds];
            _buzzers = new DDABuzzer[_device.NumberOfBuzzers];
            _buzzerDevices = new DDADevice[_device.NumberOfBuzzers];

            FireConnectionChanged();
        }

        public void Connect(string devicePath)
        {
            if (_device != null)
                Disconnect(true);

            _device = DDADeviceManager.CreateDevice(devicePath);
            _devicePath = devicePath;
            _deviceDescriptor = DeviceClassInfo.GetDeviceDescriptor(devicePath);
            _device.PluggedEvent += device_PluggedEvent;
            _device.Connect();

            _epassport.Serial4x = new string[] { _device.SerialNumber };

            _leds = new DDALed[_device.NumberOfLeds];
            _ledDevices = new DDADevice[_device.NumberOfLeds];
            _buzzers = new DDABuzzer[_device.NumberOfBuzzers];
            _buzzerDevices = new DDADevice[_device.NumberOfBuzzers];

            FireConnectionChanged();
        }

        public void Disconnect(bool dispose)
        {
            if (_device == null)
                return;

            if (dispose)
            {
                _device.Dispose();
                _device = null;
                _devicePath = "";
                _deviceDescriptor = null;
                _leds = null;
                _ledDevices = null;
            }
            else
            {
                _device.Disconnect();
            }

            FireConnectionChanged();
        }
        public void Dispose()
        {            
            DDADeviceManager.DeviceListChangedEvent -= DDADeviceManager_DeviceListChangedEvent;
            DDALib.DebugEvent -= DDALib_DebugEvent;
        }

        protected void FireConnectionChanged() =>
            ConnectionChanged?.Invoke(this, EventArgs.Empty);

        protected void FireDeviceListChanged() =>
            DeviceListChanged?.Invoke(this, EventArgs.Empty);

        protected void FireDebugEvent(DDADebugEventArgs args) =>
            DebugEvent?.Invoke(this, args);

        private void DDALib_DebugEvent(object sender, DDADebugEventArgs args) =>
            FireDebugEvent(args);

        private void DDADeviceManager_DeviceListChangedEvent(object sender, DDADeviceListChangedEventArgs args) =>
            FireDeviceListChanged();

        private void device_PluggedEvent(object sender, DDADeviceAttachStatusEventArgs args)
        {
            if (!args.Plugged)
            {
                Disconnect(false);
            }
            else if (_device == sender)
            {
                try
                {
                    Connect();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }
        
        public void Log(string message)
        {
            //string logMessage = $">>> {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [INFO] >>> {message}";

            Console.WriteLine($">>> {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [INFO] >>> {message}");
            //// Tulis ke console
            //Console.WriteLine(logMessage);

            //// Tulis juga ke file
            //lock (_logLock)
            //{
            //    File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);
            //}
        }

        public void UnregiterDispose(bool dispose)
        {
            if (_device == null)
                return;

            if (dispose)
            {
                _device.Dispose();
                _device = null;
                _devicePath = "";
                _deviceDescriptor = null;
                _leds = null;
                _ledDevices = null;
            }
            else
            {
                _device.Disconnect();
            }

            FireConnectionChanged();
        }
    }
}