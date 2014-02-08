using System;
using System.Collections.Generic;
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

        public FrameOptimization optimization { get; private set; }

        Stream output;
        bool IsFirstFrame = true;

        List<Frame> frames = new List<Frame>();
        byte[] CompositePixelBytes;

        public Gif89a(Stream writeableStream, FrameOptimization optimization = FrameOptimization.None)
        {
            this.output = writeableStream;
            this.optimization = optimization;

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

            if (this.optimization.HasFlag(FrameOptimization.AutoTransparency) && frame.Transparent != Color.Empty)
                throw new ArgumentException("Frames may not have a manually specified transparent color when using AutoTransparency optimization", "frame");

            // first frame sets the image dimensions
            if (this.Size == Size.Empty)
                this.Size = frame.Image.Size;

            frame.PixelBytes = frame.GetPixelBytes();
            frames.Add(frame);

            if (this.IsFirstFrame)
                this.CompositePixelBytes = frame.PixelBytes;

            // build color table & map pixels
            var analysis = this.AnalyzePixels(frame);
            var indexedPixels = analysis.Item1;
            var ColorTable = analysis.Item2;

            if (indexedPixels.Length == 0)
                return;

            if (this.IsFirstFrame)
            {
                // logical screen descriptior
                GifFileFormat.WriteLogicalScreenDescriptor(this.output, (ushort)this.Size.Width, (ushort)this.Size.Height, ColorTable.Length);

                // global color table
                GifFileFormat.WriteColorTable(this.output, ColorTable);

                if (this.Repeat >= 0)
                {
                    // use NS app extension to indicate reps
                    GifFileFormat.WriteNetscapeApplicationExtension(this.output, this.Repeat);
                }
            }

            // write graphic control extension
            frame.OutputStreamGCEIndex = this.output.Position;
            GifFileFormat.WriteGraphicControlExtension(output, frame, frame.transIndex);

            // image descriptor
            GifFileFormat.WriteImageDescriptor(this.output, (ushort)frame.Image.Size.Width, (ushort)frame.Image.Size.Height, ColorTable.Length, IsFirstFrame);

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

        Tuple<byte[], byte[]> AnalyzePixels(Frame frame)
        {
            MemoryStream OpaqueFramePixelBytes;

            if (this.IsFirstFrame)
            {
                OpaqueFramePixelBytes = new MemoryStream(frame.PixelBytes);
            }
            else
            {
                OpaqueFramePixelBytes = new MemoryStream();
                var FrameContributesChange = false;

                for (int i = 0; i < frame.PixelBytes.Length; i += 3)
                {
                    var PixelContributesChange = frame.PixelBytes[i] != this.CompositePixelBytes[i] || frame.PixelBytes[i + 1] != this.CompositePixelBytes[i + 1] || frame.PixelBytes[i + 2] != this.CompositePixelBytes[i + 2];
                    FrameContributesChange = FrameContributesChange || PixelContributesChange;

                    if (PixelContributesChange)
                    {
                        OpaqueFramePixelBytes.Write(frame.PixelBytes, i, 3);

                        // retain a composite image, since we might be overwriting pixel bytes with transparency colors and won't be able to use those pixel bytes for future frame comparison
                        this.CompositePixelBytes[i] = frame.PixelBytes[i];
                        this.CompositePixelBytes[i + 1] = frame.PixelBytes[i + 1];
                        this.CompositePixelBytes[i + 2] = frame.PixelBytes[i + 2];
                    }

                    if (!PixelContributesChange && this.optimization.HasFlag(FrameOptimization.AutoTransparency))
                    {
                        // todo: find a color not present in the current frame to use as transparency color
                        frame.Transparent = Color.Magenta;

                        frame.PixelBytes[i] = frame.Transparent.B;
                        frame.PixelBytes[i + 1] = frame.Transparent.G;
                        frame.PixelBytes[i + 2] = frame.Transparent.R;
                    }
                }

                if (!FrameContributesChange && this.optimization.HasFlag(FrameOptimization.DiscardDuplicates))
                {
                    // hang on to where we currently are in the output stream
                    var here = this.output.Position;

                    // the last written GCE might be more than one frame back, if there's a bunch of duplicates
                    var prev = frame;
                    while (prev.OutputStreamGCEIndex == 0)
                        prev = frames[frames.IndexOf(prev) - 1];

                    // jump back to the GCE in the previous frame
                    this.output.Position = prev.OutputStreamGCEIndex;

                    prev.Delay += frame.Delay;
                    GifFileFormat.WriteGraphicControlExtension(output, prev, prev.transIndex);

                    // jump forward to where we just were to continue on normally
                    this.output.Position = here;

                    return Tuple.Create<byte[], byte[]>(new byte[0], new byte[0]);
                }
            }


            // totally exclude the transparency color from the quantization process, if there is one
            // reduce the quantizer max color space by 1 if we need to reserve a color table slot for the transparent color
            var quantizer = new NeuQuant(OpaqueFramePixelBytes.ToArray(), 256 - (frame.Transparent.IsEmpty ? 0 : 1), (int)frame.Quality);
            var ColorTable = quantizer.process();


            // map image pixels to new palette
            var indexedPixels = new byte[frame.PixelBytes.Length / 3];
            var QuantizedIndexToColorTableIndex = new Dictionary<int, byte>();
            var ColorTableBytes = new MemoryStream();
            var TransparentColorWritten = false;


            for (int i = 0; i < indexedPixels.Length; i++)
            {
                // if the frame has a transparency color, and this pixel is the transparency color, ignore the quantizer's result and write the color table & indexed pixel ourselves
                var PixelIsTransparent = !frame.Transparent.IsEmpty && frame.PixelBytes[i * 3] == frame.Transparent.B && frame.PixelBytes[i * 3 + 1] == frame.Transparent.G && frame.PixelBytes[i * 3 + 2] == frame.Transparent.R;
                if (PixelIsTransparent)
                {
                    if (!TransparentColorWritten)
                    {
                        TransparentColorWritten = true;

                        frame.transIndex = (byte)ColorTableBytes.Position;

                        ColorTableBytes.WriteByte(frame.Transparent.R);
                        ColorTableBytes.WriteByte(frame.Transparent.G);
                        ColorTableBytes.WriteByte(frame.Transparent.B);
                    }

                    indexedPixels[i] = frame.transIndex;
                    continue;
                }

                // "index" according to the quantizer's internal structure
                int index = quantizer.map(frame.PixelBytes[i * 3], frame.PixelBytes[i * 3 + 1], frame.PixelBytes[i * 3 + 2]);

                if (QuantizedIndexToColorTableIndex.ContainsKey(index))
                {
                    indexedPixels[i] = QuantizedIndexToColorTableIndex[index];
                    continue;
                }

                // if the mapping between quantizer index and compact color table index is unknown, find it
                // (aka, this index's color is not yet recorded in the compact color table)
                // due to the quantizer's internal structure, this means looking at each of its entries
                for (int n = 0; n < ColorTable.Length; n++)
                {
                    if (ColorTable[n][3] == index)
                    {
                        // when we've found the color this index is for,
                        // record both the color table index and the mapping in case it comes up again
                        indexedPixels[i] = (byte)(ColorTableBytes.Position / 3);
                        QuantizedIndexToColorTableIndex.Add(index, (byte)(ColorTableBytes.Position / 3));

                        // then write out the RGB data at this point
                        ColorTableBytes.WriteByte((byte)ColorTable[n][2]); // R
                        ColorTableBytes.WriteByte((byte)ColorTable[n][1]); // B
                        ColorTableBytes.WriteByte((byte)ColorTable[n][0]); // G

                        break;
                    }
                }
            }


            // GIF color tables are essentially powers of 2 length only
            // pad out the compact color table to the next largest power of two (times 3) bytes
            // see the notes where the Global Color Table is written for details.
            var padding = 0;
            for (int i = 7; i >= 0; i--)
            {
                var difference = (3 * System.Math.Pow(2, i + 1)) - ColorTableBytes.Length;
                if (difference >= 0)
                    padding = (int)difference;
            }

            if (padding > 0)
                ColorTableBytes.Write(new byte[padding], 0, padding);


            return Tuple.Create<byte[], byte[]>(indexedPixels, ColorTableBytes.ToArray());
        }
    }
}
