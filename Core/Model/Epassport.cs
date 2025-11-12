namespace cmrtd.Core.Model
{
    public class Epassport
    {
        public string MRZ { get; set; }
        public string ImageFormat { get; set; }
        public string FaceBase64 { get; set; }
        public string faceLocation { get; set; }
        public bool LastError { get; set; }
        public string[] Serial4x { get; set; }
        public string[] SerialCki { get; set; }

    }
}
