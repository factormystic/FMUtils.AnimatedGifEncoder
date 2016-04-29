using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace animated_gif_encoder
{
    // Class AnimatedGifEncoder - Encodes a GIF file consisting of one or more
    // frames.
    // 
    // <pre>
    //  Example:
    //     AnimatedGifEncoder e = new AnimatedGifEncoder();
    //     e.start(outputFileName);
    //     e.setDelay(1000);   // 1 frame per sec
    //     e.addFrame(image1);
    //     e.addFrame(image2);
    //     e.finish();
    // </pre>
    // 
    // No copyright asserted on the source code of this class. May be used for any
    // purpose, however, refer to the Unisys LZW patent for restrictions on use of
    // the associated LZWEncoder class. Please forward any corrections to
    // kweiner@fmsware.com.
    // 
    // @author Kevin Weiner, FM Software
    // @version 1.03 November 2003
    // Originated from: http://www.java2s.com/Code/Java/2D-Graphics-GUI/AnimatedGifEncoder.htm

    public class AnimatedGifEncoder
    {
        /// <summary>
        /// image size
        /// </summary>
        protected int width;

        /// <summary>
        /// image size
        /// </summary>
        protected int height;

        /// <summary>
        /// transparent color if given
        /// </summary>
        protected Color transparent = Color.Empty;

        /// <summary>
        /// transparent index in color table
        /// </summary>
        protected int transIndex;

        /// <summary>
        /// no repeat
        /// </summary>
        protected int repeat = 0;

        /// <summary>
        /// frame delay (hundredths)
        /// </summary>
        protected int delay = 0;

        /// <summary>
        /// ready to output frames
        /// </summary>
        protected bool started = false;

        protected MemoryStream output;

        /// <summary>
        /// current frame
        /// </summary>
        protected Bitmap image;

        /// <summary>
        ///  BGR byte array from frame
        /// </summary>
        protected byte[] pixels;

        /// <summary>
        /// converted frame indexed to palette
        /// </summary>
        protected byte[] indexedPixels;

        /// <summary>
        /// number of bit planes
        /// </summary>
        protected int colorDepth;

        /// <summary>
        /// RGB palette
        /// </summary>
        protected byte[] colorTab;

        /// <summary>
        /// active palette entries
        /// </summary>
        protected bool[] usedEntry = new bool[256];

        /// <summary>
        /// color table size (bits-1)
        /// </summary>
        protected int palSize = 7;

        /// <summary>
        /// disposal code (-1 = use default)
        /// </summary>
        protected int dispose = -1;

        /// <summary>
        /// close stream when finished
        /// </summary>
        protected bool closeStream = false;

        protected bool firstFrame = true;

        /// <summary>
        /// if false, get size from first frame
        /// </summary>
        protected bool sizeSet = false;

        /// <summary>
        /// default sample interval for quantizer
        /// </summary>
        protected int sample = 1;

        /// <summary>
        /// Sets the delay time between each frame, or changes it for subsequent frames (applies to last frame added).
        /// </summary>
        public void setDelay(int ms)
        {
            //delay = Math.Round(ms / 10.0f);
            delay = ms / 10;
        }

        /// <summary>
        /// Sets the GIF frame disposal code for the last added frame and any
        /// subsequent frames. Default is 0 if no transparent color has been set,
        /// otherwise 2.
        /// </summary>
        public void setDispose(int code)
        {
            if (code >= 0)
            {
                dispose = code;
            }
        }

        /// <summary>
        /// Sets the number of times the set of GIF frames should be played. Default is 1; 0 means play indefinitely. Must be invoked before the first image is added.
        /// </summary>
        public void setRepeat(int iter)
        {
            if (iter >= 0)
            {
                repeat = iter;
            }
        }

        /// <summary>
        /// Sets the transparent color for the last added frame and any subsequent
        /// frames. Since all colors are subject to modification in the quantization
        /// process, the color in the final palette for each frame closest to the given
        /// color becomes the transparent color for that frame. May be set to null to
        /// indicate no transparent color.
        /// </summary>
        public void setTransparent(Color c)
        {
            transparent = c;
        }

        /// <summary>
        /// Adds next GIF frame. The frame is not written immediately, but is actually
        /// deferred until the next frame is received so that timing data can be
        /// inserted. Invoking <code>finish()</code> flushes all frames. If
        /// <code>setSize</code> was not invoked, the size of the first image is used
        /// for all subsequent frames.
        /// 
        /// @param im
        ///          BufferedImage containing frame to write.
        /// @return true if successful.
        /// </summary>
        public bool addFrame(Bitmap im)
        {
            if ((im == null) || !started)
            {
                return false;
            }
            bool ok = true;
            try
            {
                if (!sizeSet)
                {
                    // use first frame's size
                    setSize(im.Width, im.Height);
                }
                image = im;

                // convert to correct format if necessary
                getImagePixels();

                // build color table & map pixels
                analyzePixels();

                if (firstFrame)
                {
                    // logical screen descriptior
                    writeLSD();

                    // global color table
                    writePalette();

                    if (repeat >= 0)
                    {
                        // use NS app extension to indicate reps
                        writeNetscapeExt();
                    }
                }

                // write graphic control extension
                writeGraphicCtrlExt();

                // image descriptor
                writeImageDesc();

                if (!firstFrame)
                {
                    // local color table
                    writePalette();
                }

                // encode and write pixel data
                writePixels();

                firstFrame = false;
            }
            catch (IOException e)
            {
                ok = false;
            }

            return ok;
        }

        /// <summary>
        /// Flushes any pending data and closes output file. If writing to an OutputStream, the stream is not closed.
        /// </summary>
        public bool finish()
        {
            if (!started)
                return false;
            bool ok = true;
            started = false;
            try
            {
                // gif trailer
                output.WriteByte(0x3b);

                output.Flush();

                if (closeStream)
                {
                    output.Close();
                }
            }
            catch (IOException e)
            {
                ok = false;
            }

            // reset for subsequent use
            transIndex = 0;
            output = null;
            image = null;
            pixels = null;
            indexedPixels = null;
            colorTab = null;
            closeStream = false;
            firstFrame = true;

            return ok;
        }

        /// <summary>
        /// Sets frame rate in frames per second. Equivalent to
        /// <code>setDelay(1000/fps)</code>.
        /// @param fps
        ///          float frame rate (frames per second)
        /// </summary>
        public void setFrameRate(float fps)
        {
            if (fps != 0f)
            {
                //delay = Math.round(100f / fps);
                delay = (int)(100f / fps);
            }
        }

        ///<summary>
        /// Sets quality of color quantization (conversion of images to the maximum 256
        /// colors allowed by the GIF specification). Lower values (minimum = 1)
        /// produce better colors, but slow processing significantly. 10 is the
        /// default, and produces good color mapping at reasonable speeds. Values
        /// greater than 20 do not yield significant improvements in speed.
        /// 
        /// @param quality
        ///          int greater than 0.
        /// @return
        ///</summary>
        public void setQuality(int quality)
        {
            if (quality < 1)
                quality = 1;
            sample = quality;
        }

        ///<summary>
        ///Sets the GIF frame size. The default size is the size of the first frame
        ///added if this method is not invoked.
        ///
        ///@param w
        ///         int frame width.
        ///@param h
        ///         int frame width.
        ///</summary>
        public void setSize(int w, int h)
        {
            if (started && !firstFrame)
                return;
            width = w;
            height = h;
            if (width < 1)
                width = 320;
            if (height < 1)
                height = 240;
            sizeSet = true;
        }

        ///<summary>
        ///Initiates GIF file creation on the given stream. The stream is not closed
        ///automatically.
        ///
        ///@param os
        ///         OutputStream on which GIF images are written.
        ///@return false if initial write failed.
        ///</summary>
        public bool start(MemoryStream os)
        {
            if (os == null)
                return false;
            bool ok = true;
            closeStream = false;
            output = os;
            try
            {
                writeString("GIF89a"); // header
            }
            catch (IOException e)
            {
                ok = false;
            }
            return started = ok;
        }

        ///<summary>
        ///Initiates writing of a GIF file with the specified name.
        ///
        ///@param file
        ///         String containing output file name.
        ///@return false if open or initial write failed.
        ///</summary>
        public bool start(String file)
        {
            bool ok = true;
            try
            {
                output = new MemoryStream();
                ok = start(output);
                closeStream = true;
            }
            catch (IOException e)
            {
                ok = false;
            }
            return started = ok;
        }

        /// <summary>
        /// Analyzes image colors and creates color map.
        /// </summary>
        protected void analyzePixels()
        {
            int len = pixels.Length;
            int nPix = len / 3;
            indexedPixels = new byte[nPix];

            // initialize quantizer
            NeuQuant nq = new NeuQuant(pixels, len, sample);

            // create reduced palette
            colorTab = nq.process();

            // convert map from BGR to RGB
            for (int i = 0; i < colorTab.Length; i += 3)
            {
                byte temp = colorTab[i];
                colorTab[i] = colorTab[i + 2];
                colorTab[i + 2] = temp;
                usedEntry[i / 3] = false;
            }

            // map image pixels to new palette
            int k = 0;
            for (int i = 0; i < nPix; i++)
            {
                int index = nq.map(pixels[k++] & 0xff, pixels[k++] & 0xff, pixels[k++] & 0xff);
                usedEntry[index] = true;
                indexedPixels[i] = (byte)index;
            }

            pixels = null;
            colorDepth = 8;
            palSize = 7;

            // get closest match to transparent color if specified
            if (transparent != Color.Empty)
            {
                transIndex = findClosest(transparent);
            }
        }

        /// <summary>
        /// Returns index of palette color closest to c
        /// </summary>
        protected int findClosest(Color c)
        {
            if (colorTab == null)
                return -1;
            int r = c.R;
            int g = c.G;
            int b = c.B;
            int minpos = 0;
            int dmin = 256 * 256 * 256;
            int len = colorTab.Length;
            for (int i = 0; i < len; )
            {
                int dr = r - (colorTab[i++] & 0xff);
                int dg = g - (colorTab[i++] & 0xff);
                int db = b - (colorTab[i] & 0xff);
                int d = dr * dr + dg * dg + db * db;
                int index = i / 3;
                if (usedEntry[index] && (d < dmin))
                {
                    dmin = d;
                    minpos = index;
                }
                i++;
            }
            return minpos;
        }

        /// <summary>
        /// Extracts image pixels into byte array "pixels"
        /// </summary>
        protected void getImagePixels()
        {
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

            //pixels = ((DataBufferByte)image.getRaster().getDataBuffer()).getData();

            var ImgData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

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

            image.UnlockBits(ImgData);
            pixels = PixelBytes;
        }

        /// <summary>
        /// Writes Graphic Control Extension
        /// </summary>
        protected void writeGraphicCtrlExt()
        {
            output.WriteByte(0x21); // extension introducer
            output.WriteByte(0xf9); // GCE label
            output.WriteByte(4); // data block size
            int transp, disp;
            if (transparent == Color.Empty)
            {
                transp = 0;
                disp = 0; // dispose = no action
            }
            else
            {
                transp = 1;
                disp = 2; // force clear if using transparent color
            }
            if (dispose >= 0)
            {
                disp = dispose & 7; // user override
            }
            disp <<= 2;

            // packed fields
            output.WriteByte((byte)(0x00 | // 1:3 reserved
                                    disp | // 4:6 disposal
                                    0x00 | // 7 user input - 0 = none
                                    transp)); // 8 transparency flag


            writeShort(delay); // delay x 1/100 sec
            output.WriteByte((byte)transIndex); // transparent color index
            output.WriteByte(0); // block terminator
        }

        /// <summary>
        /// Writes Image Descriptor
        /// </summary>
        protected void writeImageDesc()
        {
            output.WriteByte(0x2c); // image separator
            writeShort(0); // image position x,y = 0,0
            writeShort(0);
            writeShort(width); // image size
            writeShort(height);

            // packed fields
            if (firstFrame)
            {
                // no LCT - GCT is used for first (or only) frame
                output.WriteByte(0);
            }
            else
            {
                // specify normal LCT
                output.WriteByte((byte)(0x80 | // 1 local color table 1=yes
                    //0 | // 2 interlace - 0=no
                    //0 | // 3 sorted - 0=no
                    //0 | // 4-5 reserved
                                        palSize)); // 6-8 size of color table
            }
        }

        /// <summary>
        /// Writes Logical Screen Descriptor
        /// </summary>
        protected void writeLSD()
        {
            // logical screen size
            writeShort(width);
            writeShort(height);

            // packed fields
            output.WriteByte((byte)(0x80 | // 1 : global color table flag = 1 (gct used)
                                    0x70 | // 2-4 : color resolution = 7
                                    0x00 | // 5 : gct sort flag = 0
                                    palSize)); // 6-8 : gct size

            // background color index
            output.WriteByte(0);

            // pixel aspect ratio - assume 1:1
            output.WriteByte(0);
        }


        /// <summary>
        /// Writes Netscape application extension to define repeat count.
        /// </summary>
        protected void writeNetscapeExt()
        {
            // extension introducer
            output.WriteByte(0x21);

            // app extension label
            output.WriteByte(0xff);

            // block size
            output.WriteByte(11);

            // app id + auth code
            writeString("NETSCAPE" + "2.0");

            // sub-block size
            output.WriteByte(3);

            // loop sub-block id
            output.WriteByte(1);

            // loop count (extra iterations, 0=repeat forever)
            writeShort(repeat);

            // block terminator
            output.WriteByte(0);
        }


        /// <summary>
        /// Writes color table
        /// </summary>
        protected void writePalette()
        {
            output.Write(colorTab, 0, colorTab.Length);
            int n = (3 * 256) - colorTab.Length;
            for (int i = 0; i < n; i++)
            {
                output.WriteByte(0);
            }
        }

        /// <summary>
        /// Encodes and writes pixel data
        /// </summary>
        protected void writePixels()
        {
            LZWEncoder encoder = new LZWEncoder(width, height, indexedPixels, colorDepth);
            encoder.encode(output);
        }

        /// <summary>
        /// Write 16-bit value to output stream, LSB first
        /// </summary>
        protected void writeShort(int value)
        {
            output.WriteByte((byte)(value & 0xff));
            output.WriteByte((byte)((value >> 8) & 0xff));
        }


        /// <summary>
        /// Writes string to output stream
        /// </summary>
        protected void writeString(String s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                output.WriteByte((byte)s[i]);
            }
        }
    }
}
