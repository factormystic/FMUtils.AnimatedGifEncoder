using System;
using System.Drawing;
using System.IO;

namespace FMUtils.AnimatedGifEncoder
{
    internal class GifFileFormat
    {
        /// <summary>
        /// 17.c.i Signature
        /// -
        /// Identifies the GIF Data Stream. This field contains the fixed value 'GIF'.
        /// </summary>
        public static string HEADER_SIGNATURE = "GIF";

        /// <summary>
        /// 17.c.ii Version
        /// -
        /// Version number used to format the data stream.
        /// Identifies the minimum set of capabilities necessary to a decoder to fully process the contents of the Data Stream.
        /// </summary>
        public static string HEADER_VERSION = "89a";

        /// <summary>
        /// 23.c.i Extension Introducer
        /// -
        /// Identifies the beginning of an extension block.
        /// This field contains the fixed value 0x21.
        /// </summary>
        public static byte EXTENSION_INTRODUCER = 0x21;

        /// <summary>
        /// 27 Trailer
        /// -
        /// This block is a single-field block indicating the end of the GIF Data Stream.
        /// It contains the fixed value 0x3B.
        /// </summary>
        public static byte TRAILER = 0x3b;

        /// <summary>
        /// 23.c.ii Graphic Control Label
        /// -
        /// Identifies the current block as a Graphic Control Extension.
        /// This field contains the fixed value 0xF9.
        /// </summary>
        public static byte GRAPHIC_CONTROL_EXTENSION = 0xf9;

        /// <summary>
        /// 23.c.iii Block Size
        /// -
        /// Number of bytes in the block, after the Block Size field and up to but not including the Block Terminator.
        /// This field contains the fixed value 4.
        /// </summary>
        public static byte GRAPHIC_CONTROL_EXTENSION_BLOCK_SIZE = 4;

        /// <summary>
        /// 20.c.i Image Separator
        /// -
        /// Identifies the beginning of an Image Descriptor.
        /// This field contains the fixed value 0x2C.
        /// </summary>
        public static byte IMAGE_SEPARATOR = 0x2c;

        /// <summary>
        /// 26.c.ii Application Extension Label
        /// -
        /// Identifies the block as an Application Extension.
        /// This field contains the fixed value 0xFF.
        /// </summary>
        public static byte APPLICATION_EXTENSION_LABEL = 0xff;

        /// <summary>
        /// 16.c.i Block Terminator (Size)
        /// -
        /// Number of bytes in the Data Sub-block; this field contains the fixed value 0x00.
        /// </summary>
        public static byte BLOCK_TERMINATOR = 0x00;

        /// <summary>
        /// Defines a non-standard but commonly implemented extension, that indicates how many times the GIF should loop.
        /// </summary>
        public static byte NETSCAPE_APPLICATION_EXTENSION = 0x21;

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
            output.WriteByte(GifFileFormat.EXTENSION_INTRODUCER);
            output.WriteByte(GifFileFormat.GRAPHIC_CONTROL_EXTENSION);
            output.WriteByte(GifFileFormat.GRAPHIC_CONTROL_EXTENSION_BLOCK_SIZE);

            byte transp = 0x00;
            var disposal = FrameDisposalMethod.Unspecified;

            if (frame.Transparent != Color.Empty)
            {
                transp = 1;

                // force clear if using transparent color
                disposal = FrameDisposalMethod.RestoreBackgroundColor;
            }

            if (frame.DisposalMethod != FrameDisposalMethod.Unspecified)
            {
                disposal = frame.DisposalMethod;
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
        internal static void WriteImageDescriptor(Stream output, Rectangle changeRect, int colorTableLength, bool firstFrame)
        {
            output.WriteByte(GifFileFormat.IMAGE_SEPARATOR);

            // image position x,y = 0,0
            WriteShort(output, (ushort)changeRect.Left);
            WriteShort(output, (ushort)changeRect.Top);

            // image size
            WriteShort(output, (ushort)changeRect.Width);
            WriteShort(output, (ushort)changeRect.Height);

            // packed fields
            if (firstFrame)
            {
                // no LCT, GCT is used for first (or only) frame
                output.WriteByte(0);
            }
            else
            {
                // packed fields
                // 1 local color table 1=yes, specify normal LCT
                // 2 interlace - 0=no
                // 3 sorted - 0=no
                // 4-5 reserved
                // 6-8 size of color table (see GCT for explanation)

                byte lct = 0x1 << 7;
                byte interlace = 0 << 6;
                byte sorted = 0 << 5;
                byte size = (byte)(Math.Log(colorTableLength / 3, 2) - 1);

                output.WriteByte((byte)(lct | interlace | sorted | size));
            }
        }

        /// <summary>
        /// 18. Logical Screen Descriptor.
        /// 
        /// The Logical Screen Descriptor contains the parameters
        /// necessary to define the area of the display device within which the
        /// images will be rendered.
        /// </summary>
        internal static void WriteLogicalScreenDescriptor(Stream output, ushort width, ushort height, int colorTableLength)
        {
            // logical screen size
            WriteShort(output, width);
            WriteShort(output, height);

            // packed fields
            // 1 : global color table flag = 1 (gct used)
            // 2-4 : color resolution = 7
            // 5 : gct sort flag = 0
            // 6-8 : gct size

            // the color table size calculation is going backwards from the size calculation defined in the spec:
            // 3 x 2^(Size of Global Color Table + 1) => Log2(colorTableLength / 3) - 1

            // it's especially irritating because this means the length of the color table byte
            // array may ONLY EVER BE: 6 (1 color), 12 (4 colors), 24 (8 colors), 48 (16 colors), 96 (32 colors), 192 (64 colors), 384 (128 colors), or 768 (256 colors) bytes long!
            // This makes optimizing modern gifs for less than 256 colors kind of pointless,
            // since you'd have to half the number of colors down to 128 to actually write out a smaller color table

            // even though this code supports color tables of any valid width, it probably doesn't need to
            // the alternate implementation would be to simply always claim the max color table length,
            // and then zero-pad the color table byte array to 768 bytes long

            byte gct = 0x1 << 7;
            byte res = 0x7 << 4;
            byte sort = 0x0 << 3;
            byte size = (byte)(Math.Log(colorTableLength / 3, 2) - 1);

            output.WriteByte((byte)(gct | res | sort | size));

            // background color index
            output.WriteByte(0);

            // pixel aspect ratio - assume 1:1
            output.WriteByte(0);
        }

        /// <summary>
        /// 19. Global Color Table.
        /// 21. Local Color Table.
        /// 
        /// A color table is a sequence of bytes representing red-green-blue color
        /// triplets. Its presence is marked by the Local/Global Color Table Flag
        /// being set to 1 in the Image Descriptor (for a Local Color Table) or
        /// Logical Screen Descriptor (for the Global Color Table); if present,
        /// the contains a number of bytes equal to
        /// 
        ///                      3x2^(Size of Color Table+1)
        /// (Note: "Size of Color Table" is 0..7, defined ID/LSD immediately prior)
        /// 
        /// If present, this color table temporarily becomes the active color table
        /// and the following image should be processed using it.
        /// </summary>
        internal static void WriteColorTable(Stream output, byte[] colorTable)
        {
            output.Write(colorTable, 0, colorTable.Length);
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
            WriteString(output, GifFileFormat.HEADER_SIGNATURE);
            WriteString(output, GifFileFormat.HEADER_VERSION);
        }

        internal static void WriteFileTrailer(Stream output)
        {
            output.WriteByte(GifFileFormat.TRAILER);
        }
    }
}
