using System;
using System.Drawing;
using System.IO;

namespace FMUtils.AnimatedGifEncoder
{
    internal class GifFileFormat
    {
        public static string GIF89A_HEADER = "GIF89a";
        public static byte EXTENSION_BLOCK = 0x21;
        public static byte TRAILER = 0x3b;
        public static byte GRAPHIC_CONTROL_EXTENSION = 0xf9;
        public static byte IMAGE_DESCRIPTOR = 0x2c;
        public static byte NETSCAPE_APPLICATION_EXTENSION = 0x21;
        public static byte APPLICATION_EXTENSION_LABEL = 0xff;
        public static byte BLOCK_TERMINATOR = 0x00;

        /// <summary>
        /// Writes Netscape application extension to define repeat count.
        /// </summary>
        internal static void WriteNetscapeApplicationExtension(Stream output, ushort repeat)
        {
            output.WriteByte(GifFileFormat.NETSCAPE_APPLICATION_EXTENSION);
            output.WriteByte(GifFileFormat.APPLICATION_EXTENSION_LABEL);

            // block size (fixed)
            output.WriteByte(11);

            // app id + auth code
            WriteString(output, "NETSCAPE" + "2.0");

            // sub-block size: id + 2 byte unsigned int repeat count
            output.WriteByte(3);

            // loop sub-block id
            output.WriteByte(1);

            // loop count
            WriteShort(output, repeat);

            output.WriteByte(GifFileFormat.BLOCK_TERMINATOR);
        }


        /// <summary>
        /// 23. Graphic Control Extension.
        /// 
        /// The Graphic Control Extension contains parameters used
        /// when processing a graphic rendering block. The scope of this extension is
        /// the first graphic rendering block to follow. The extension contains only
        /// one data sub-block.
        /// </summary>
        internal static void WriteGraphicControlExtension(Stream output, Frame frame, byte transIndex)
        {
            output.WriteByte(GifFileFormat.EXTENSION_BLOCK);
            output.WriteByte(GifFileFormat.GRAPHIC_CONTROL_EXTENSION);

            // data block size
            output.WriteByte(4);

            byte transp = 0x00;
            var disposal = DisposalMethod.Unspecified;

            if (frame.Transparent != Color.Empty)
            {
                transp = 1;

                // force clear if using transparent color
                disposal = DisposalMethod.RestoreBackgroundColor;
            }

            if (frame.Dispose != DisposalMethod.Unspecified)
            {
                disposal = frame.Dispose;
            }

            // packed fields
            // 1:3 reserved
            // 4:6 disposal
            // 7 user input - 0 = none
            // 8 transparency flag

            byte reserved = 0x00 << 7;
            byte dispose = (byte)((byte)disposal << 4);
            byte user = 0x00 << 1;
            byte transparent = (byte)(transp << 0);

            output.WriteByte((byte)(reserved | dispose | user | transparent));

            // delay in hundredths
            WriteShort(output, (ushort)(frame.Delay / 10));

            // transparent color index
            output.WriteByte(transIndex);

            output.WriteByte(GifFileFormat.BLOCK_TERMINATOR);
        }

        /// <summary>
        /// 20. Image Descriptor.
        /// 
        /// Each image in the Data Stream is composed of an Image
        /// Descriptor, an optional Local Color Table, and the image data.  Each
        /// image must fit within the boundaries of the Logical Screen, as defined
        /// in the Logical Screen Descriptor.
        /// </summary>
        internal static void WriteImageDescriptor(Stream output, ushort width, ushort height, bool firstFrame)
        {
            output.WriteByte(GifFileFormat.IMAGE_DESCRIPTOR);

            // image position x,y = 0,0
            WriteShort(output, 0);
            WriteShort(output, 0);

            // image size
            WriteShort(output, width);
            WriteShort(output, height);

            // packed fields
            if (firstFrame)
            {
                // no LCT, GCT is used for first (or only) frame
                output.WriteByte(0);
            }
            else
            {
                // specify normal LCT
                // 1 local color table 1=yes
                // 2 interlace - 0=no
                // 3 sorted - 0=no
                // 4-5 reserved
                // 6-8 size of color table

                byte lct = 0x1 << 7;
                byte interlace = 0 << 6;
                byte sorted = 0 << 5;
                byte table_size = 0x7 << 0;

                output.WriteByte((byte)(lct | interlace | sorted | table_size));
            }
        }

        /// <summary>
        /// 18. Logical Screen Descriptor.
        /// 
        /// The Logical Screen Descriptor contains the parameters
        /// necessary to define the area of the display device within which the
        /// images will be rendered.
        /// </summary>
        internal static void WriteLogicalScreenDescriptor(Stream output, ushort width, ushort height)
        {
            // logical screen size
            WriteShort(output, width);
            WriteShort(output, height);

            // packed fields
            byte gct = 0x1 << 7; // 1 : global color table flag = 1 (gct used)
            byte res = 0x7 << 4; // 2-4 : color resolution = 7
            byte sort = 0x0 << 3; // 5 : gct sort flag = 0
            byte size = 0x7 << 0; // 6-8 : gct size

            output.WriteByte((byte)(gct | res | sort | size));

            // background color index
            output.WriteByte(0);

            // pixel aspect ratio - assume 1:1
            output.WriteByte(0);
        }

        /// <summary>
        /// Writes color table
        /// </summary>
        internal static void WriteColorTable(Stream output, byte[] colorTab)
        {
            output.Write(colorTab, 0, colorTab.Length);
            int n = (3 * 256) - colorTab.Length;
            for (int i = 0; i < n; i++)
            {
                output.WriteByte(0);
            }
        }

        /// <summary>
        /// Write 16-bit value to output stream, LSB first
        /// </summary>
        static void WriteShort(Stream output, ushort value)
        {
            output.WriteByte((byte)(value & 0xff));
            output.WriteByte((byte)((value >> 8) & 0xff));
        }

        /// <summary>
        /// Writes string to output stream
        /// </summary>
        static void WriteString(Stream output, String s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                output.WriteByte((byte)s[i]);
            }
        }

        internal static void WriteFileHeader(Stream output)
        {
            WriteString(output, GifFileFormat.GIF89A_HEADER);
        }

        internal static void WriteFileTrailer(Stream output)
        {
            output.WriteByte(GifFileFormat.TRAILER);
        }
    }
}
