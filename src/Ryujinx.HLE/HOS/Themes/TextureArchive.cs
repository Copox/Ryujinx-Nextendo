using Ryujinx.Graphics.Texture;
using SkiaSharp;
using System;
using System.Buffers.Binary;
using System.Text;

namespace Ryujinx.HLE.HOS.Themes
{
    /// <summary>
    /// [Nextendo, HOME Menu themes] The texture archive of a layout archive (BNTX): a texture is
    /// replaced by an image of another size. The new image data goes after the existing data,
    /// the texture's description is changed to it, and the relocation table follows.
    /// </summary>
    public static class TextureArchive
    {
        public const int ScreenWidth = 1280, ScreenHeight = 720;

        private const uint FormatBc1 = 0x1A01;  // BC1, unorm
        private const int GobBlocksLog2 = 4;    // 16 GOBs per block, as the system's 1280x720 textures

        public static bool Has(byte[] bntx, string name) => FindTexture(bntx, name) >= 0;

        private static int FindTexture(byte[] b, string name)
        {
            int count = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0x24));
            int table = (int)BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(0x28));

            for (int i = 0; i < count; i++)
            {
                int info = (int)BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(table + 8 * i));
                int nameAt = (int)BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(info + 0x60));
                int length = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(nameAt));

                if (Encoding.ASCII.GetString(b, nameAt + 2, length) == name)
                {
                    return info;
                }
            }

            return -1;
        }

        /// <summary>The texture named becomes the image (a 1280x720 BC1 texture).</summary>
        public static byte[] ReplaceWithImage(byte[] bntx, string name, byte[] image) =>
            Replace(bntx, name, ScreenWidth, ScreenHeight, FormatBc1, 0x05040302, GobBlocksLog2, Bc1Blocks(image));

        /// <summary>
        /// The texture named becomes an icon: a BC3 image of that size (colour and coverage),
        /// with the channels given (0x05050505: every channel reads the coverage, the layout
        /// colours it).
        /// </summary>
        public static byte[] ReplaceWithIcon(byte[] bntx, string name, byte[] image, int width, int height, uint channels) =>
            Replace(bntx, name, width, height, FormatBc3, channels, 0, EncodeBc3(Pixels(image, width, height), width, height));

        private const uint FormatBc3 = 0x1C01; // BC3, unorm

        private static byte[] Replace(byte[] bntx, string name, int width, int height, uint format, uint channels, int gobBlocksLog2, byte[] blocks)
        {
            int info = FindTexture(bntx, name);

            if (info < 0)
            {
                return null;
            }

            int bytesPerBlock = format == FormatBc1 ? 8 : 16;
            SizeInfo size = SizeCalculator.GetBlockLinearTextureSize(width, height, 1, 1, 1, 4, 4, bytesPerBlock, 1 << gobBlocksLog2, 1, 1);
            byte[] swizzled = new byte[size.TotalSize];

            LayoutConverter.ConvertLinearToBlockLinear(swizzled, width, height, 1, 1, 1, 1, 4, 4, bytesPerBlock, 1 << gobBlocksLog2, 1, 1, size, blocks);

            int brtd = (int)BinaryPrimitives.ReadInt64LittleEndian(bntx.AsSpan(0x30));
            long brtdSize = BinaryPrimitives.ReadInt64LittleEndian(bntx.AsSpan(brtd + 8));
            int rlt = BinaryPrimitives.ReadInt32LittleEndian(bntx.AsSpan(0x18));
            byte[] relocations = bntx[rlt..];

            int dataAt = Align(brtd + (int)brtdSize, 0x200);
            int newRlt = Align(dataAt + swizzled.Length, 0x1000);
            byte[] output = new byte[newRlt + relocations.Length];

            bntx.AsSpan(0, Math.Min(rlt, brtd + (int)brtdSize)).CopyTo(output);
            swizzled.CopyTo(output, dataAt);
            relocations.CopyTo(output, newRlt);

            // The texture's description.
            Span<byte> t = output.AsSpan(info);

            t[0x10] = 1;                                                     // flags
            t[0x11] = 2;                                                     // 2D
            BinaryPrimitives.WriteUInt16LittleEndian(t[0x16..], 1);          // levels
            BinaryPrimitives.WriteUInt32LittleEndian(t[0x18..], 1);          // samples
            BinaryPrimitives.WriteUInt32LittleEndian(t[0x1C..], format);
            BinaryPrimitives.WriteUInt32LittleEndian(t[0x20..], 0x20);       // access
            BinaryPrimitives.WriteInt32LittleEndian(t[0x24..], width);
            BinaryPrimitives.WriteInt32LittleEndian(t[0x28..], height);
            BinaryPrimitives.WriteInt32LittleEndian(t[0x2C..], 1);           // depth
            BinaryPrimitives.WriteInt32LittleEndian(t[0x30..], 1);           // layers
            BinaryPrimitives.WriteInt32LittleEndian(t[0x34..], gobBlocksLog2);
            BinaryPrimitives.WriteInt32LittleEndian(t[0x50..], swizzled.Length);
            BinaryPrimitives.WriteInt32LittleEndian(t[0x54..], 0x200);       // alignment
            BinaryPrimitives.WriteUInt32LittleEndian(t[0x58..], channels);

            int levels = (int)BinaryPrimitives.ReadInt64LittleEndian(t[0x70..]);
            BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(levels), dataAt);

            // The data block, the relocation table and the file.
            BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(brtd + 8), newRlt - brtd);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x18), newRlt);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(newRlt + 4), newRlt); // the table knows where it is
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x1C), output.Length);

            return output;
        }

        private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

        // ---- the image ----

        // The image as linear BC1 blocks of 1280x720: a DXT1 DDS of that size is taken as it is,
        // any other picture (JPEG, PNG) is scaled to the screen and encoded.
        private static byte[] Bc1Blocks(byte[] image)
        {
            if (image.Length > 0x80 && Encoding.ASCII.GetString(image, 0, 4) == "DDS " &&
                Encoding.ASCII.GetString(image, 0x54, 4) == "DXT1" &&
                BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x0C)) == ScreenHeight &&
                BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x10)) == ScreenWidth)
            {
                return image[0x80..(0x80 + ScreenWidth / 4 * (ScreenHeight / 4) * 8)];
            }

            using SKBitmap decoded = SKBitmap.Decode(image) ?? throw new InvalidOperationException("unreadable image");
            using SKBitmap screen = new(new SKImageInfo(ScreenWidth, ScreenHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            using (SKCanvas canvas = new(screen))
            using (SKPaint paint = new() { FilterQuality = SKFilterQuality.High })
            {
                canvas.Clear(SKColors.Black);
                canvas.DrawBitmap(decoded, new SKRect(0, 0, ScreenWidth, ScreenHeight), paint);
            }

            return EncodeBc1(screen.Bytes, ScreenWidth, ScreenHeight);
        }

        // A picture as RGBA pixels of that size.
        private static byte[] Pixels(byte[] image, int width, int height)
        {
            using SKBitmap decoded = SKBitmap.Decode(image) ?? throw new InvalidOperationException("unreadable image");
            using SKBitmap sized = new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            using (SKCanvas canvas = new(sized))
            using (SKPaint paint = new() { FilterQuality = SKFilterQuality.High })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(decoded, new SKRect(0, 0, width, height), paint);
            }

            return sized.Bytes;
        }

        /// <summary>RGBA pixels to BC3 blocks: the coverage of each block, then its colours.</summary>
        public static byte[] EncodeBc3(byte[] rgba, int width, int height)
        {
            int bw = (width + 3) / 4, bh = (height + 3) / 4;
            int paddedWidth = bw * 4, paddedHeight = bh * 4;
            byte[] padded = new byte[paddedWidth * paddedHeight * 4];

            for (int y = 0; y < height; y++)
            {
                rgba.AsSpan(y * width * 4, width * 4).CopyTo(padded.AsSpan(y * paddedWidth * 4));
            }

            byte[] colours = EncodeBc1(padded, paddedWidth, paddedHeight);
            byte[] output = new byte[bw * bh * 16];

            for (int by = 0; by < bh; by++)
            {
                for (int bx = 0; bx < bw; bx++)
                {
                    int block = by * bw + bx;
                    int min = 255, max = 0;

                    for (int i = 0; i < 16; i++)
                    {
                        int a = padded[((by * 4 + i / 4) * paddedWidth + bx * 4 + i % 4) * 4 + 3];

                        min = Math.Min(min, a);
                        max = Math.Max(max, a);
                    }

                    // Eight levels from max (code 0) to min (code 1), the six between as codes 2 to 7.
                    int o = block * 16;
                    ulong indices = 0;

                    output[o] = (byte)max;
                    output[o + 1] = (byte)min;

                    if (max != min)
                    {
                        for (int i = 0; i < 16; i++)
                        {
                            int a = padded[((by * 4 + i / 4) * paddedWidth + bx * 4 + i % 4) * 4 + 3];
                            int step = (int)Math.Round((max - a) * 7.0 / (max - min));
                            int code = step == 0 ? 0 : step == 7 ? 1 : step + 1;

                            indices |= (ulong)code << (3 * i);
                        }
                    }

                    for (int k = 0; k < 6; k++)
                    {
                        output[o + 2 + k] = (byte)(indices >> (8 * k));
                    }

                    colours.AsSpan(block * 8, 8).CopyTo(output.AsSpan(o + 8));
                }
            }

            return output;
        }

        /// <summary>RGBA pixels to BC1 blocks (opaque): the two ends of each block's colour box.</summary>
        public static byte[] EncodeBc1(ReadOnlySpan<byte> rgba, int width, int height)
        {
            int bw = width / 4, bh = height / 4;
            byte[] output = new byte[bw * bh * 8];
            Span<int> r = stackalloc int[16], g = stackalloc int[16], b = stackalloc int[16];
            Span<int> pr = stackalloc int[4], pg = stackalloc int[4], pb = stackalloc int[4];

            for (int by = 0; by < bh; by++)
            {
                for (int bx = 0; bx < bw; bx++)
                {
                    int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;

                    for (int i = 0; i < 16; i++)
                    {
                        int p = ((by * 4 + i / 4) * width + bx * 4 + i % 4) * 4;

                        r[i] = rgba[p];
                        g[i] = rgba[p + 1];
                        b[i] = rgba[p + 2];
                        minR = Math.Min(minR, r[i]); maxR = Math.Max(maxR, r[i]);
                        minG = Math.Min(minG, g[i]); maxG = Math.Max(maxG, g[i]);
                        minB = Math.Min(minB, b[i]); maxB = Math.Max(maxB, b[i]);
                    }

                    ushort c0 = To565(maxR, maxG, maxB);
                    ushort c1 = To565(minR, minG, minB);

                    if (c0 < c1)
                    {
                        (c0, c1) = (c1, c0);
                    }

                    // The four colours of the block (four-colour mode: c0 > c1).
                    From565(c0, out pr[0], out pg[0], out pb[0]);
                    From565(c1, out pr[1], out pg[1], out pb[1]);
                    pr[2] = (2 * pr[0] + pr[1]) / 3; pg[2] = (2 * pg[0] + pg[1]) / 3; pb[2] = (2 * pb[0] + pb[1]) / 3;
                    pr[3] = (pr[0] + 2 * pr[1]) / 3; pg[3] = (pg[0] + 2 * pg[1]) / 3; pb[3] = (pb[0] + 2 * pb[1]) / 3;

                    uint indices = 0;

                    if (c0 != c1)
                    {
                        for (int i = 0; i < 16; i++)
                        {
                            int best = 0, bestDistance = int.MaxValue;

                            for (int k = 0; k < 4; k++)
                            {
                                int dr = r[i] - pr[k], dg = g[i] - pg[k], db = b[i] - pb[k];
                                int distance = dr * dr * 3 + dg * dg * 4 + db * db * 2;

                                if (distance < bestDistance)
                                {
                                    bestDistance = distance;
                                    best = k;
                                }
                            }

                            indices |= (uint)best << (2 * i);
                        }
                    }

                    int o = (by * bw + bx) * 8;

                    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(o), c0);
                    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(o + 2), c1);
                    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(o + 4), indices);
                }
            }

            return output;
        }

        private static ushort To565(int r, int g, int b) => (ushort)(((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | ((b * 31 + 127) / 255));

        private static void From565(ushort c, out int r, out int g, out int b)
        {
            r = (c >> 11 & 31) * 255 / 31;
            g = (c >> 5 & 63) * 255 / 63;
            b = (c & 31) * 255 / 31;
        }
    }
}
