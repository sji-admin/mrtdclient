namespace cmrtd.Infrastructure.Model
{
    public class DeviceDescriptor
    {
        #region Public Members

        public string DeviceClass;
        public string Description;
        public string DevicePath;

        #endregion

        #region C-tors

        public DeviceDescriptor(string deviceClass, string description, string devicePath)
        {
            DevicePath = devicePath;
            DeviceClass = deviceClass;
            Description = description;
        }

        public DeviceDescriptor(string deviceClass, string description)
        {
            DevicePath = null;
            DeviceClass = deviceClass;
            Description = description;
        }

        public DeviceDescriptor(DeviceDescriptor descriptor, string devicePath)
        {
            DevicePath = devicePath;
            DeviceClass = descriptor.DeviceClass;
            Description = descriptor.Description;
        }

        #endregion

        #region Overrides

        public override string ToString()
        {
            if (DevicePath == null)
            {
                return DevicePath;
            }

            return Description + " (" + DevicePath + ")";
        }

        #endregion
    }

    static class DeviceClassInfo
    {
        #region Constants

        public const string DDA_DEVICE_CLASS_SWIPEREADER = "SR";
        public const string DDA_DEVICE_CLASS_SWIPEREADER_BT = "SR-BT";
        public const string DDA_DEVICE_CLASS_QUEUEBUSTER = "BGR";
        public const string DDA_DEVICE_CLASS_IDONE = "ID1";
        public const string DDA_DEVICE_CLASS_PENTA = "PENTA";
        public const string DDA_DEVICE_CLASS_VIRTUALDEVICE = "VIRTUALDEVICE";

        #endregion

        #region Private Member

        private static Dictionary<string, DeviceDescriptor> _classDescriptionMap = new Dictionary<string, DeviceDescriptor>();

        #endregion

        #region C-tors

        static DeviceClassInfo()
        {
            _classDescriptionMap.Add(DDA_DEVICE_CLASS_VIRTUALDEVICE, new DeviceDescriptor(DDA_DEVICE_CLASS_VIRTUALDEVICE, "Virtual Device"));
            _classDescriptionMap.Add(DDA_DEVICE_CLASS_QUEUEBUSTER, new DeviceDescriptor(DDA_DEVICE_CLASS_QUEUEBUSTER, "QUEUE BUSTER"));
            _classDescriptionMap.Add(DDA_DEVICE_CLASS_IDONE, new DeviceDescriptor(DDA_DEVICE_CLASS_IDONE, "ID1 Scanner"));
            _classDescriptionMap.Add(DDA_DEVICE_CLASS_SWIPEREADER, new DeviceDescriptor(DDA_DEVICE_CLASS_SWIPEREADER, "Swipe Reader"));
            _classDescriptionMap.Add(DDA_DEVICE_CLASS_SWIPEREADER_BT, new DeviceDescriptor(DDA_DEVICE_CLASS_SWIPEREADER_BT, "Swipe Reader (via Bluetooth)"));
            _classDescriptionMap.Add(DDA_DEVICE_CLASS_PENTA, new DeviceDescriptor(DDA_DEVICE_CLASS_SWIPEREADER_BT, "PENTA Scanner 4X"));
        }

        #endregion

        #region Methods

        public static bool IsSupportedDevice(string devicePath)
        {
            string[] tokens = devicePath.Split('\\');

            if (tokens.Length < 1)
            {
                return false;
            }

            string deviceClass = tokens[0].ToUpper();

            if (!_classDescriptionMap.ContainsKey(deviceClass))
            {
                return false;
            }

            return true;
        }

        public static DeviceDescriptor GetDeviceDescriptor(string devicePath)
        {
            string[] tokens = devicePath.Split('\\');

            if (tokens.Length < 1)
            {
                throw new ArgumentException("Invalid device path specified", "devicePath");
            }

            string deviceClass = tokens[0].ToUpper();

            if (!_classDescriptionMap.ContainsKey(deviceClass))
            {
                throw new ArgumentException("Unsupported device class specified", "devicePath");
            }

            return _classDescriptionMap[deviceClass];
        }

        #endregion
    }
}