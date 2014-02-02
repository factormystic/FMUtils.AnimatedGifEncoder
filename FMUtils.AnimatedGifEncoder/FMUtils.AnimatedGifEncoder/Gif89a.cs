using System;
using System.Drawing;
using System.IO;

namespace FMUtils.AnimatedGifEncoder
{
    public class Gif89a : IDisposable
    {
        /// <summary>
        /// Image size, set by the first frame
        /// </summary>
        public Size Size { get; private set; }

        /// <summary>
        /// Sets the number of times the set of GIF frames should be played.
        /// 0 means play indefinitely.
        /// 
        /// Must be invoked before the first image is added.
        /// </summary>
        public ushort Repeat = 0;

        Stream output;
        bool IsFirstFrame = true;

        public Gif89a(Stream writeableStream)
        {
            this.output = writeableStream;
            GifFileFormat.WriteFileHeader(this.output);
        }

        public void Dispose()
        {
            GifFileFormat.WriteFileTrailer(output);
            output.Flush();
        }

        public void AddFrame(Frame frame)
        {
            if (frame == null)
                throw new ArgumentNullException("frame");

            // first frame sets the image dimensions
            if (this.Size == Size.Empty)
                this.Size = frame.Image.Size;

            // build color table & map pixels
            var analysis = this.AnalyzePixels(frame);
            var indexedPixels = analysis.Item1;
            var ColorTable = analysis.Item2;
            var usedEntry = analysis.Item3;

            // get closest match to transparent color if specified
            byte transIndex = 0;
            if (frame.Transparent != Color.Empty)
            {
                transIndex = getClosestPaletteIndex(frame.Transparent, ColorTable, usedEntry);
            }

            if (this.IsFirstFrame)
            {
                // logical screen descriptior
                GifFileFormat.WriteLogicalScreenDescriptor(this.output, (ushort)this.Size.Width, (ushort)this.Size.Height);

                // global color table
                GifFileFormat.WriteColorTable(this.output, ColorTable);

                if (this.Repeat >= 0)
                {
                    // use NS app extension to indicate reps
                    GifFileFormat.WriteNetscapeApplicationExtension(this.output, this.Repeat);
                }
            }

            // write graphic control extension
            GifFileFormat.WriteGraphicControlExtension(output, frame, transIndex);

            // image descriptor
            GifFileFormat.WriteImageDescriptor(this.output, (ushort)frame.Image.Size.Width, (ushort)frame.Image.Size.Height, IsFirstFrame);

            if (!this.IsFirstFrame)
            {
                // local color table
                GifFileFormat.WriteColorTable(output, ColorTable);
            }

            // encode and write pixel data
            var lzw = new LZWEncoder(this.Size.Width, this.Size.Height, indexedPixels, 8);
            lzw.encode(this.output);

            this.IsFirstFrame = false;
        }

        Tuple<byte[], byte[], bool[]> AnalyzePixels(Frame frame)
        {
            byte[] PixelBytes = frame.GetPixelBytes();

            int len = pixels.Length;
            int nPix = len / 3;
            var indexedPixels = new byte[nPix];
            var indexedPixels = new byte[PixelBytes.Length / 3];
            var usedEntry = new bool[256];

            var quantizer = new NeuQuant(PixelBytes, PixelBytes.Length, (int)frame.Quality);
            var ColorTable = quantizer.process();

            // convert map from BGR to RGB
            for (int i = 0; i < ColorTable.Length; i += 3)
            {
                byte temp = ColorTable[i];
                ColorTable[i] = ColorTable[i + 2];
                ColorTable[i + 2] = temp;
            }

            // map image pixels to new palette
            int k = 0;
            for (int i = 0; i < indexedPixels.Length; i++)
            {
                int index = quantizer.map(PixelBytes[k++] & 0xff, PixelBytes[k++] & 0xff, PixelBytes[k++] & 0xff);
                usedEntry[index] = true;
                indexedPixels[i] = (byte)index;
            }

            return Tuple.Create<byte[], byte[], bool[]>(indexedPixels, ColorTable, usedEntry);
        }

        byte getClosestPaletteIndex(Color c, byte[] colorTable, bool[] usedEntry)
        {
            if (colorTable == null)
                throw new ArgumentNullException("colorTable");

            int r = c.R;
            int g = c.G;
            int b = c.B;

            byte minpos = 0;
            int dmin = 256 * 256 * 256;

            for (byte i = 0; i < colorTable.Length; )
            {
                int dr = r - (colorTable[i++] & 0xff);
                int dg = g - (colorTable[i++] & 0xff);
                int db = b - (colorTable[i] & 0xff);
                int d = dr * dr + dg * dg + db * db;
                byte index = (byte)(i / 3);

                if (usedEntry[index] && (d < dmin))
                {
                    dmin = d;
                    minpos = index;
                }

                i++;
            }

            return minpos;
        }
    }
}
