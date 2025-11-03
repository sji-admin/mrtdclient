using Desko.DDA;
using System.Drawing;

namespace cmrtd.Infrastructure.DeskoDevice
{
    public class ScanImageTask
    {
        public DDAScanImageData ScanImage { get; set; }
        public RotateFlipType Rotation { get; set; }
        public string OcrString { get; set; }
    }
}
