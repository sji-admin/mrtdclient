namespace cmrtd.Core.Model
{
    public class Callback
    {
        public Body body { get; set; }
        public object headers { get; set; } = new { };
        public string statusCode { get; set; }
        public int statusCodeValue { get; set; }
    }

    public class Body
    {
        public int code { get; set; }
        public Data data { get; set; }
        public string err_msg { get; set; }
    }

    public class Data
    {
        public string mrz { get; set; }
        public string bcbp { get; set; }
        public string docType { get; set; }
        public RgbImage rgbImage { get; set; }
        public string uuid { get; set; }
        public bool valid { get; set; }
    }

    public class RgbImage
    {
        public object motionBlur { get; set; }
        public object face { get; set; }
        public bool isUvDull { get; set; }
        public bool isB900Ink { get; set; }
        public string location { get; set; }
        public string faceLocation { get; set; }
        public string imgBase64 { get; set; }
        public string imgFaceBase64 { get; set; }
        public string imgFormat { get; set; }
    }
}
