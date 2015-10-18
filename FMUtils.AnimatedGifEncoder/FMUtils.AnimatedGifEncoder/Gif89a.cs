using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
        /// </summary>
        public ushort Repeat { get; private set; }

        public FrameOptimization optimization { get; private set; }

        Stream output;

        List<Frame> frames = new List<Frame>();

        BlockingCollection<Frame> FrameLoadingQueue = new BlockingCollection<Frame>();
        ManualResetEvent FrameLoadingDelay = new ManualResetEvent(true);
        ManualResetEvent FrameLoadingComplete = new ManualResetEvent(false);

        BlockingCollection<Frame> ProcessingQueue = new BlockingCollection<Frame>();
        ManualResetEvent ProcessingComplete = new ManualResetEvent(false);

        BlockingCollection<Frame> FrameWriteQueue = new BlockingCollection<Frame>();
        ManualResetEvent FrameWriteComplete = new ManualResetEvent(false);

        // lock object for this class's instance properties
        object _gif = new object();

        int _processingTaskCount;

        public Gif89a(Stream writeableStream, FrameOptimization optimization = FrameOptimization.None, ushort repeat = 0, Size? frameSize = null)
        {
            this.output = writeableStream;
            this.optimization = optimization;
            this.Repeat = repeat;
            this.Size = frameSize ?? Size.Empty;

            Task.Factory.StartNew(this.LoadFrames);

            // seems to be a bit faster if we leave one core of capacity for the calling thread
            this._processingTaskCount = 1;
            var tasks = new Task[this._processingTaskCount];

            for (int i = 0; i < this._processingTaskCount; i++)
            {
                tasks[i] = Task.Factory.StartNew(this.ProcessFrames);
            }

            // after all the workers complete, mark the completion queue as finished
            Task.Factory.StartNew(new Action(() =>
            {
                Task.WaitAll(tasks);

                Debug.WriteLine("Processing complete", string.Format("Gif89a [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));
                ProcessingComplete.Set();
            }));

            if (!this.optimization.HasFlag(FrameOptimization.DeferredStreamWrite))
            {
                Task.Factory.StartNew(this.WriteGifToStream);
            }
        }

        public void Dispose()
        {
            Trace.WriteLine(string.Format("Gif89a.Dispose [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));

            // by disposal, all frames have definitely been added
            FrameLoadingQueue.CompleteAdding();
            
            // drain out the loading thread
            FrameLoadingComplete.WaitOne();

            // drain out the processing threads
            ProcessingQueue.CompleteAdding();

            // wait for all the processing threads to complete
            ProcessingComplete.WaitOne();

            // once processing is done, all the frames have definitely been added to the write queue
            FrameWriteQueue.CompleteAdding();

            if (this.optimization.HasFlag(FrameOptimization.DeferredStreamWrite))
            {
                this.WriteGifToStream();
            }

            // wait for the frame write to complete (might have already been completed just now, above)
            FrameWriteComplete.WaitOne();
        }

        /// <summary>
        /// Queue a frame to be loaded & processed. This method is thread safe.
        /// </summary>
        public bool AddFrame(Frame frame)
        {
            // Trace.WriteLine(string.Format("Gif89a.AddFrame [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));

            if (frame == null)
                throw new ArgumentNullException("frame");

            if (this.optimization.HasFlag(FrameOptimization.AutoTransparency) && frame.Transparent != Color.Empty)
                throw new ArgumentException("Frames may not have a manually specified transparent color when using AutoTransparency optimization", "frame");

            try
            {
                FrameLoadingQueue.Add(frame);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        void LoadFrames()
        {
            Trace.WriteLine(string.Format("Gif89a.LoadFrames [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));

            while (!this.FrameLoadingQueue.IsCompleted)
            {
                Frame frame = null;
                try
                {
                    frame = this.FrameLoadingQueue.Take();
                }
                catch (InvalidOperationException)
                {
                    Trace.WriteLine("No more frames to load", string.Format("Gif89a.LoadFrames [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));
                    break;
                }

                frame.LoadPixelBytes(this.Size);

                // we have to add the frame to the frames list before adding it to to the processing queue,
                // but if the processing queue is closed, we need to take it back out again, since it won't be written
                // this can happen when AddFrame is called after Dispose
                lock (_gif)
                {
                    // first frame sets the image dimensions (unless specified in the constructor)
                    if (this.Size == Size.Empty)
                        this.Size = frame.Size;

                    this.frames.Add(frame);

                    try
                    {
                        this.ProcessingQueue.Add(frame);

                        if (!this.optimization.HasFlag(FrameOptimization.DeferredStreamWrite) && this.ProcessingQueue.Count >= this._processingTaskCount)
                        {
                            Trace.WriteLine("Delaying frame loading...", string.Format("Gif89a.LoadFrames [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));
                            FrameLoadingDelay.Reset();
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        this.frames.Remove(frame);
                    }
                }

                if (!this.optimization.HasFlag(FrameOptimization.DeferredStreamWrite))
                {
                    FrameLoadingDelay.WaitOne();
                }
            }

            Trace.WriteLine("Done", string.Format("Gif89a.LoadFrames [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));

            FrameLoadingComplete.Set();
        }

        void ProcessFrames()
        {
            Trace.WriteLine(string.Format("Gif89a.ProcessFrames [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));

            while (!ProcessingQueue.IsCompleted)
            {
                Frame frame = null;

                try
                {
                    frame = ProcessingQueue.Take();
                }
                catch (InvalidOperationException)
                {
                    Trace.WriteLine("No more frames to process", string.Format("Gif89a.ProcessFrames [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));
                    break;
                }

                Trace.WriteLine("Processing frame...", string.Format("Gif89a.ProcessFrames [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));

                var sw = new Stopwatch();
                sw.Start();

                // find transparent/opaque pixels and update the composite image
                this.AnalyzeFrame(frame);

                // build color table & map pixels
                this.AnalyzePixels(frame);

                // after analysis is complete, the frame is ready to be written out
                FrameWriteQueue.Add(frame);

                sw.Stop();
                Trace.WriteLine($"Done ({ sw.Elapsed })", string.Format("Gif89a.ProcessFrames [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));
            }

            Trace.WriteLine("Done", string.Format("Gif89a.ProcessFrames [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));
        }

        void WriteGifToStream()
        {
            Trace.WriteLine(string.Format("Gif89a.WriteGifToStream [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));

            GifFileFormat.WriteFileHeader(this.output);

            var nextFrameIndex = 0;
            var pendingFrames = new List<Frame>();

            while (!FrameWriteQueue.IsCompleted)
            {
                Frame someFrame = null;
                try
                {
                    someFrame = FrameWriteQueue.Take();
                }
                catch (InvalidOperationException)
                {
                    Trace.WriteLine("No more frames to write", string.Format("Gif89a.WriteGifToStream [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));
                    break;
                }

                // frame analysis could can (and does!) occur out-of-order
                // so every time a frame is popped from the FrameWriteQueue, it might not be the "next" frame
                // no problem: just hang on to the writable frames and then write as much as possible in order
                pendingFrames.Add(someFrame);

                while (nextFrameIndex < this.frames.Count && pendingFrames.Contains(this.frames[nextFrameIndex]))
                {
                    this.WriteFrame(this.frames[nextFrameIndex]);

                    // no need to hang on to it here any more
                    pendingFrames.Remove(someFrame);

                    // we're done with the raw frame data, so let it unload
                    if (nextFrameIndex > 0)
                    {
                        // don't dispose the current frame, since it could be a "prev" frame for a frame that's currently being analyzed
                        this.frames[nextFrameIndex - 1].Dispose();
                    }

                    // allow more frames to be loaded
                    FrameLoadingDelay.Set();

                    nextFrameIndex++;
                }
            }

            // the final frame is now safe to dispose since there's no "next" for it to be a "prev" for
            this.frames.Last().Dispose();

            GifFileFormat.WriteFileTrailer(output);
            output.Flush();

            Trace.WriteLine("Done", string.Format("Gif89a.WriteGifToStream [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));

            // announce that stream writing is done
            FrameWriteComplete.Set();
        }

        void WriteFrame(Frame frame)
        {
            Trace.WriteLine(string.Format("Gif89a.WriteFrame [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));

            var sw = new Stopwatch();
            sw.Start();

            if (frame.OpaqueFramePixelBytes == null)
                return;

            // if we're discarding duplicate frames
            if (frame.OpaqueFramePixelBytes.Length == 0 && this.optimization.HasFlag(FrameOptimization.DiscardDuplicates))
            {
                // hang on to where we currently are in the output stream
                var here = this.output.Position;

                // the last written GCE might be more than one frame back, if there's a bunch of duplicates
                var prev = frame;
                while (prev.OutputStreamGCEIndex == 0)
                    prev = this.frames[this.frames.IndexOf(prev) - 1];

                // jump back to the GCE in the previous frame
                this.output.Position = prev.OutputStreamGCEIndex;

                prev.Delay += frame.Delay;
                GifFileFormat.WriteGraphicControlExtension(output, prev, prev.transIndex);

                // jump forward to where we just were to continue on normally
                this.output.Position = here;

                return;
            }

            if (frame.IndexedPixels.Length == 0)
                return;

            if (frame == this.frames.First())
            {
                // logical screen descriptor
                GifFileFormat.WriteLogicalScreenDescriptor(this.output, (ushort)this.Size.Width, (ushort)this.Size.Height, frame.ColorTableBytes.Length);

                // global color table
                GifFileFormat.WriteColorTable(this.output, frame.ColorTableBytes);

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
            GifFileFormat.WriteImageDescriptor(this.output, frame.ChangeRect, frame.ColorTableBytes.Length, frame == this.frames.First());

            if (frame != this.frames.First())
            {
                // local color table
                GifFileFormat.WriteColorTable(this.output, frame.ColorTableBytes);
            }

            // encode and write pixel data
            var lzw = new LZWEncoder(frame.ChangeRect.Width, frame.ChangeRect.Height, frame.IndexedPixels, 8);
            lzw.encode(this.output);

            sw.Stop();
            Trace.WriteLine($"Done ({ sw.Elapsed })", string.Format("Gif89a.WriteFrame [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));
        }

        void AnalyzeFrame(Frame frame)
        {
            // Trace.WriteLine(string.Format("Gif89a.AnalyzeFrame [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));

            frame.ChangeRect = new Rectangle(0, 0, frame.Size.Width, frame.Size.Height);
            frame.TransparentPixelIndexes = new bool[frame.PixelBytes.Length / 3];

            if (frame == this.frames.First())
            {
                //this.CompositePixelBytes = new byte[frame.PixelBytes.Length];
                //frame.PixelBytes.CopyTo(this.CompositePixelBytes, 0);

                frame.OpaqueFramePixelBytes = new byte[frame.PixelBytes.Length];
                frame.PixelBytes.CopyTo(frame.OpaqueFramePixelBytes, 0);
                return;
            }

            var OpaqueFramePixelBytes = new MemoryStream();
            var FrameContributesChange = false;

            var LeftmostChange = frame.Size.Width;
            var RightmostChange = 0;
            var TopmostChange = frame.Size.Height;
            var BottommostChange = 0;

            var prev = this.frames[this.frames.IndexOf(frame) - 1];

            for (int i = 0; i < frame.PixelBytes.Length; i += 3)
            {
                //var PixelContributesChange = frame.PixelBytes[i] != this.CompositePixelBytes[i] || frame.PixelBytes[i + 1] != this.CompositePixelBytes[i + 1] || frame.PixelBytes[i + 2] != this.CompositePixelBytes[i + 2];
                var PixelContributesChange = frame.PixelBytes[i] != prev.PixelBytes[i] || frame.PixelBytes[i + 1] != prev.PixelBytes[i + 1] || frame.PixelBytes[i + 2] != prev.PixelBytes[i + 2];
                FrameContributesChange = FrameContributesChange || PixelContributesChange;

                if (PixelContributesChange || !this.optimization.HasFlag(FrameOptimization.AutoTransparency))
                {
                    OpaqueFramePixelBytes.Write(frame.PixelBytes, i, 3);

                    // keep track of the growing rect where pixels differ between frames
                    // we can then clip the frame size to only this changed area
                    if (this.optimization.HasFlag(FrameOptimization.ClipFrame))
                    {
                        var x = (i / 3) % frame.Size.Width;
                        var y = (i / 3) / frame.Size.Width;

                        LeftmostChange = Math.Min(LeftmostChange, x);
                        RightmostChange = Math.Max(RightmostChange, x);

                        TopmostChange = Math.Min(TopmostChange, y);
                        BottommostChange = Math.Max(BottommostChange, y);
                    }
                }

                frame.TransparentPixelIndexes[i / 3] = !PixelContributesChange && this.optimization.HasFlag(FrameOptimization.AutoTransparency);
            }

            frame.OpaqueFramePixelBytes = OpaqueFramePixelBytes.ToArray();

            // construct the shrunk frame rect out of the known bounds where the frame was different
            // difference should be inclusive (eg, left = 14px and right = 14px then 1px width) hence adding 1
            if (FrameContributesChange && this.optimization.HasFlag(FrameOptimization.ClipFrame))
            {
                frame.ChangeRect = new Rectangle(LeftmostChange, TopmostChange, RightmostChange - LeftmostChange + 1, BottommostChange - TopmostChange + 1);
            }
        }

        void AnalyzePixels(Frame frame)
        {
            // Trace.WriteLine(string.Format("Gif89a.AnalyzePixels [{0}]", System.Threading.Thread.CurrentThread.ManagedThreadId));

            // totally exclude the transparency color from the quantization process, if there is one
            // reduce the quantizer max color space by 1 if we need to reserve a color table slot for the transparent color
            var quantizer = new NeuQuant(frame.OpaqueFramePixelBytes, 256 - (frame.TransparentPixelIndexes.Any(i => i) ? 1 : 0), (int)frame.Quality);
            var ColorTable = quantizer.Process();

            // map image pixels to new palette
            var indexedPixels = new MemoryStream();
            var QuantizedIndexToColorTableIndex = new Dictionary<int, byte>();
            var ColorTableBytes = new MemoryStream();
            var TransparentColorWritten = false;

            for (int i = 0; i < frame.PixelBytes.Length / 3; i++)
            {
                if (this.optimization.HasFlag(FrameOptimization.ClipFrame))
                {
                    var x = i % frame.Size.Width;
                    var y = i / frame.Size.Width;

                    if (!frame.ChangeRect.Contains(x, y))
                        continue;
                }

                // if this pixel is known as transparent, ignore the quantizer's result and write the color table & indexed pixel ourselves
                if (frame.TransparentPixelIndexes[i])
                {
                    if (!TransparentColorWritten)
                    {
                        TransparentColorWritten = true;

                        // todo: find a color not present in the current frame to use as transparency color
                        frame.Transparent = Color.Magenta;
                        frame.transIndex = (byte)(ColorTableBytes.Position / 3);

                        ColorTableBytes.WriteByte(frame.Transparent.R);
                        ColorTableBytes.WriteByte(frame.Transparent.G);
                        ColorTableBytes.WriteByte(frame.Transparent.B);
                    }

                    indexedPixels.WriteByte(frame.transIndex);
                    continue;
                }

                // "index" according to the quantizer's internal structure
                int index = quantizer.Map(frame.PixelBytes[i * 3], frame.PixelBytes[i * 3 + 1], frame.PixelBytes[i * 3 + 2]);

                if (QuantizedIndexToColorTableIndex.ContainsKey(index))
                {
                    indexedPixels.WriteByte(QuantizedIndexToColorTableIndex[index]);
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
                        indexedPixels.WriteByte((byte)(ColorTableBytes.Position / 3));
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
            if (ColorTableBytes.Length / 3 > 256)
                throw new InvalidOperationException(string.Format("Gif89a color tables may have at most 256 colors, but {0} colors were used in the frame", ColorTableBytes.Length / 3));

            var padding = 0;
            for (int i = 7; i >= 0; i--)
            {
                var difference = (3 * System.Math.Pow(2, i + 1)) - ColorTableBytes.Length;
                if (difference >= 0)
                    padding = (int)difference;
            }

            if (padding > 0)
                ColorTableBytes.Write(new byte[padding], 0, padding);

            // if we ended up not needing the transparency color (ex: used frame clipping, and all the pixels inside were changed colors)
            // then make sure to reset the frame transparency so we don't try to use it later
            if (!TransparentColorWritten)
            {
                frame.Transparent = Color.Empty;
                frame.transIndex = 0;
            }

            frame.IndexedPixels = indexedPixels.ToArray();
            frame.ColorTableBytes = ColorTableBytes.ToArray();
        }
    }
}
