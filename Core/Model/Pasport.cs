namespace cmrtd.Core.Model
{
    public class Pasport
    {
        public class ScanApiResponse
        {
            public int Code { get; set; }
            public ScanData Data { get; set; }
            public bool Valid { get; set; }
            public string Err_msg { get; set; } = string.Empty;
        }

        public class ScanData
        {
            public string MRZ { get; set; }
            public string Bcbp { get; set; }
            public string DocType { get; set; }

            public ImageResult RgbImage { get; set; }
            public ImageResult UvImage { get; set; }
            public ImageResult IrImage { get; set; }
        }

        public class ImageResult
        {
            public bool MotionBlur { get; set; }
            public FaceResult Face { get; set; }
            public bool IsUvDull { get; set; }
            public bool IsB900Ink { get; set; }
            public string Location { get; set; }
            public string FaceLocation { get; set; }
            public string ImgBase64 { get; set; }
            public string ImgFaceBase64 { get; set; }
        }

        public class FaceResult
        {
            public bool Empty { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int Left { get; set; }
            public int Top { get; set; }
        }

    }
}
