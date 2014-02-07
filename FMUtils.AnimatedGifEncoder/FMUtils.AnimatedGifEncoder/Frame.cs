using System.Drawing;
using System.Runtime.InteropServices;

namespace FMUtils.AnimatedGifEncoder
{
    public class Frame
    {
        /// <summary>
        /// Frame image data
        /// </summary>
        public Bitmap Image { get; private set; }

        /// <summary>
        /// Frame delay (milliseconds)
        /// </summary>
        public ushort Delay = 0;

        /// <summary>
        /// Sets the transparent color for the frame. Since all colors are subject to
        /// modification in the quantization process, the color in the final palette
        /// for each frame closest to the given color becomes the transparent color
        /// for that frame. May be set to Empty to indicate no transparent color.
        /// </summary>
        public Color Transparent = Color.Empty;

        /// <summary>
        /// Sets the GIF frame disposal code for the last added frame and any
        /// subsequent frames. Default is 0 if no transparent color has been set,
        /// otherwise 2.
        /// </summary>
        public DisposalMethod Dispose = DisposalMethod.Unspecified;

        /// <summary>
        /// default sample interval for quantizer
        /// 
        /// Sets quality of color quantization (conversion of images to the maximum 256
        /// colors allowed by the GIF specification). Lower values (minimum = 1)
        /// produce better colors, but slow processing significantly. 10 is the
        /// default, and produces good color mapping at reasonable speeds. Values
        /// greater than 20 do not yield significant improvements in speed.
        /// </summary>
        public ColorQuantizationQuality Quality = ColorQuantizationQuality.Reasonable;

        internal byte[] PixelBytes { get; set; }


        internal long OutputStreamGCEIndex { get; set; }
        
        internal byte transIndex = 0;


        public Frame(string filename, ushort delay = 67, ColorQuantizationQuality quality = ColorQuantizationQuality.Reasonable)
        {
            this.Image = new Bitmap(filename);
            this.Delay = delay;
            this.Quality = quality;

            //todo: normalize frames?

            //int w = image.Width;
            //int h = image.Height;
            //int type = image.getType();
            //if ((w != width) || (h != height) || (type != Bitmap.TYPE_3BYTE_BGR))
            //{
            //    // create new image with right size/format
            //    Bitmap temp = new Bitmap(width, height, Bitmap.TYPE_3BYTE_BGR);
            //    Graphics2D g = temp.createGraphics();
            //    g.drawImage(image, 0, 0, null);
            //    image = temp;
            //}
        }

        public Frame(Bitmap image, ushort delay = 67, ColorQuantizationQuality quality = ColorQuantizationQuality.Reasonable)
        {
            this.Image = image;
            this.Delay = delay;
            this.Quality = quality;
        }

        /// <summary>
        /// Extracts image pixels into byte array "pixels"
        /// </summary>
        internal byte[] GetPixelBytes()
        {
            // I'm pretty sure that "Format24bppRgb" is a lie, since the data is coming out BGR
            var ImgData = this.Image.LockBits(new Rectangle(0, 0, this.Image.Width, this.Image.Height), System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            // stride != run of pixel data bytes, cf. http://bobpowell.net/lockingbits.aspx
            var ByteDepth = System.Drawing.Bitmap.GetPixelFormatSize(ImgData.PixelFormat) / 8;
            var Run = ImgData.Width * ByteDepth;

            byte[] PixelBytes = new byte[ImgData.Width * ImgData.Height * ByteDepth];
            var offset = 0;

            for (int i = 0; i < ImgData.Height; i++)
            {
                Marshal.Copy(ImgData.Scan0 + (Run * i) + offset, PixelBytes, Run * i, Run);
                offset += (ImgData.Stride - Run);
            }

            this.Image.UnlockBits(ImgData);
            return PixelBytes;
        }
    }
}
