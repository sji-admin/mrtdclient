using Desko.DDA;
using Desko.FullPage;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using static cmrtd.Infrastructure.DeskoDevice.DeviceToolsPscan;

namespace cmrtd.Core
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class Helper
    {
        #region Helpers Penta4x
        public static Bitmap RawToBitmap(byte[] rawData, int width, int height)
        {
            Bitmap bmp;
            unsafe
            {
                bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                BitmapData bmp_data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                byte* bmpPtr = (byte*)(bmp_data.Scan0);

                for (int r = 0; r < height; r++)
                {
                    bmpPtr = ((byte*)(bmp_data.Scan0)) + (bmp_data.Stride * r);

                    for (int c = 0; c < width; c++)
                    {
                        bmpPtr[c * 3 + 2] = (byte)rawData[(r * width + c) * 3 + 0];
                        bmpPtr[c * 3 + 1] = (byte)rawData[(r * width + c) * 3 + 1];
                        bmpPtr[c * 3 + 0] = (byte)rawData[(r * width + c) * 3 + 2];
                    }
                }

                bmp.UnlockBits(bmp_data);
            }

            return bmp;
        }

        public static Bitmap Raw8BitToBitmap(byte[] rawData, int width, int height)
        {
            Bitmap bmp;
            unsafe
            {
                bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                BitmapData bmp_data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                byte* bmpPtr = (byte*)(bmp_data.Scan0);

                for (int r = 0; r < height; r++)
                {
                    bmpPtr = ((byte*)(bmp_data.Scan0)) + (bmp_data.Stride * r);

                    for (int c = 0; c < width; c++)
                    {
                        byte b = (byte)rawData[(r * width + c)];
                        bmpPtr[c * 3 + 2] = b;
                        bmpPtr[c * 3 + 1] = b;
                        bmpPtr[c * 3 + 0] = b;
                    }
                }

                bmp.UnlockBits(bmp_data);
            }

            return bmp;
        }

        public static Image CreateImageFromScanImage(DDAScanImageData scanImage)
        {
            Bitmap bmp = null;

            if (scanImage.Channels == 3 && scanImage.ChannelDepth == 8)
            {
                bmp = RawToBitmap(scanImage.ImageData, scanImage.Width, scanImage.Height);
            }
            else if (scanImage.Channels == 1 && scanImage.ChannelDepth == 8)
            {
                bmp = Raw8BitToBitmap(scanImage.ImageData, scanImage.Width, scanImage.Height);
            }
            int hppm = scanImage.HorizontalPpm;
            int vppm = scanImage.HorizontalPpm;
            bmp.SetResolution(hppm * 25.4f / 1000.0f, vppm * 25.4f / 1000.0f);

            return (Image)bmp;
        }

        public static string GetHexDump(byte[] data, int offsetLen = 4, int blocks = 2)
        {
            StringBuilder sb = new StringBuilder();
            string text = System.Text.Encoding.ASCII.GetString(data);

            for (int addr = 0; addr < data.Length; addr = addr + (blocks * 8))
            {
                sb.Append(Convert.ToString(addr, 16).PadLeft(offsetLen, '0'));
                sb.Append(":");

                for (int page = 0; page < blocks; page++)
                {
                    sb.Append(" ");
                    for (int offset = 0; offset < 8; offset++)
                    {
                        int index = addr + offset + page * 8;
                        if (index < data.Length)
                        {
                            sb.Append(" ");
                            sb.Append(Convert.ToString(data[index], 16).PadLeft(2, '0'));
                        }
                        else
                        {
                            sb.Append("   ");
                        }
                    }
                }

                sb.Append("  ");
                for (int page = 0; page < blocks; page++)
                {
                    for (int offset = 0; offset < 8; offset++)
                    {
                        int index = addr + offset + page * 8;
                        if (index < text.Length)
                        {
                            if (char.IsControl(text[index]))
                                sb.Append('.');
                            else
                                sb.Append(text[index]);
                        }
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }
        #endregion

        #region Helpers PageScan
        private static LabelState toLabelState(UvDullTestResult r)
        {
            switch (r)
            {
                case UvDullTestResult.Fail:
                    return LabelState.Bad;
                case UvDullTestResult.NotAvailable:
                    return LabelState.Attention;
                case UvDullTestResult.Success:
                    return LabelState.Good;

            }
            return LabelState.Off;
        }


        public void onOperationResult(object sender, EventArgsOperationResult eventArgs)
        {
            if (eventArgs.Result.Result == Result.Success)
            {
                Console.WriteLine($"Good : {LabelState.Good}");
                //labelLastResultMessage.Text = null;
            }
            else
            {
                Console.WriteLine($"stateLabelLastResult : {LabelState.Bad}");
                Console.WriteLine($"stateLabelLastResult : {eventArgs.Result.Result.ToString()}");
                Console.WriteLine($"labelLastResultMessage : {eventArgs.Result.Message}");
                Console.WriteLine("Operation failed: " + eventArgs.Result.Result.ToString() + " " + eventArgs.Result.Message);
            }
        }

        public class SystemInfoEntry
        {
            public SystemInfoEntry(string feature, string value)
            {
                Feature = feature;
                Value = value;
            }
            public string Feature { get; set; }
            public string Value { get; set; }
        }

        public enum LabelState
        {
            Good,
            Bad,
            Attention,
            Off
        }

        #endregion

        public void Cleaner(
            string mrz,
            string ocrString,
            string faceBase64,
            string ImgBase64,
            string Location,
            string Url,
            string format,
            string FaceLocation
            )
        {
            mrz = null;
            ocrString = null;
            faceBase64 = null;
            ImgBase64 = null;
            Location = null;
            Url = null;
            format = null;
            FaceLocation = null;

            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  CleanUp Data In Memory");
        }
    }
}