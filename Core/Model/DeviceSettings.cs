namespace cmrtd.Core.Model
{
    public class DeviceSettings
    {
        public string Type { get; set; }
        public int Dpi { get; set; }
        public CallbackSettings Callback { get; set; }
        public bool AutoScan { get; set; }
        public string PathE { get; set; }
        public SensepassKaiSettings SensepassKai { get; set; }
    }

    public class CallbackSettings
    {
        public string Url { get; set; }
        public bool Enable { get; set; }
    }

    public class SensepassKaiSettings
    {
        public string Host { get; set; }
        public bool Enabled { get; set; }
        public string Token { get; set; }
    }
}
