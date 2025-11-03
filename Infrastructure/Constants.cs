using Desko.DDA;

namespace cmrtd.Infrastructure
{
    public class Constants
    {
        public class Defaults
        {
            public const string ColorScheme = "default";
            public const string ImageFilter = "Bitmap files (*.bmp)|*.bmp|PNG files (*.png)|*.png|Jpeg files (*.jpg)|*.jpg";
            public const string DemoTag = "DDA_PENTA_SAMPLE";
            public const double MinZoom = 0.25;
            public const double MaxZoom = 4.0;
            public const int MoveDocumentTimeout = 7000;
            public const int ImageRequestDoneTimeout = 30000;
            public const int OneSecondTimeout = 1000;
            public const int LineCount = 0;

            public static readonly Dictionary<DDALightSource, DDAImageRequestFlag> RequestFlag = new Dictionary<DDALightSource, DDAImageRequestFlag>()
        {
            { DDALightSource.Ir, DDAImageRequestFlag.Cropped | DDAImageRequestFlag.Mrz } ,
            { DDALightSource.White, DDAImageRequestFlag.Cropped | DDAImageRequestFlag.Portrait | DDAImageRequestFlag.Rotation },
            { DDALightSource.Uv, DDAImageRequestFlag.Cropped |DDAImageRequestFlag.CheckUvDull },
            { DDALightSource.Coaxial, DDAImageRequestFlag.Cropped },
            { DDALightSource.Ovd, DDAImageRequestFlag.Cropped }
        };

            public const DDAPageMode PageMode = DDAPageMode.Default;
            public const DDAImageFormat ImageFormat = DDAImageFormat.Raw;
        }

        public class Labels
        {
            public const string DocError = "Document Error";
            public const string DocPresent = "Ready For Scan";
            public const string DocInserted = "Fully Inserted";
            public const string DocEjected = "Document Ejected";
            public const string NoDoc = "No Document";
        }

        public class Exceptions
        {
            public const string DocNotInStartPos = "Cannot request images, document not in start position.";
        }
    }
}