using SkiaSharp;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ryujinx.HLE.HOS
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The pictures of an animated PNG (APNG, as Discord serves its
    /// avatar decorations), which the image library only reads as its first picture. Each part of
    /// the animation is rebuilt as a PNG of its own, read, then laid on the picture before it as
    /// the file says (blend and dispose operations).
    /// </summary>
    public static class NextendoApng
    {
        private static readonly byte[] _signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        private static readonly uint[] _crcTable = MakeCrcTable();

        private sealed class Part
        {
            public int Width, Height, X, Y, Milliseconds, Dispose, Blend;
            public readonly MemoryStream Data = new();
        }

        /// <summary>The whole pictures of the animation with how long each shows, or null when it is not one.</summary>
        public static List<(SKBitmap Picture, int Milliseconds)> Frames(byte[] png, int most)
        {
            if (png == null || png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(_signature))
            {
                return null;
            }

            byte[] header = null;
            List<(string Type, byte[] Data)> shared = []; // PLTE, tRNS, ...: the same for every part
            List<Part> parts = [];
            Part current = null;
            bool animated = false;

            for (int at = 8; at + 12 <= png.Length;)
            {
                int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
                string type = Encoding.ASCII.GetString(png, at + 4, 4);

                if (length < 0 || at + 12 + length > png.Length)
                {
                    break;
                }

                byte[] data = png.AsSpan(at + 8, length).ToArray();

                at += 12 + length;

                switch (type)
                {
                    case "IHDR":
                        header = data;
                        break;
                    case "acTL":
                        animated = true;
                        break;
                    case "fcTL" when data.Length >= 26:
                        int numerator = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(20));
                        int denominator = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(22));

                        current = new Part
                        {
                            Width = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)),
                            Height = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8)),
                            X = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12)),
                            Y = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16)),
                            Milliseconds = numerator * 1000 / (denominator == 0 ? 100 : denominator),
                            Dispose = data[24],
                            Blend = data[25],
                        };
                        parts.Add(current);
                        break;
                    case "IDAT":
                        current?.Data.Write(data);
                        break;
                    case "fdAT" when data.Length > 4:
                        current?.Data.Write(data, 4, data.Length - 4);
                        break;
                    case "IEND":
                        break;
                    default:
                        if (current == null)
                        {
                            shared.Add((type, data));
                        }

                        break;
                }

                if (parts.Count > most)
                {
                    break;
                }
            }

            if (!animated || header == null || parts.Count < 2)
            {
                return null;
            }

            int width = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0));
            int height = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));

            // a file from the network: small pictures only (Discord's are 256 x 256)
            if (width <= 0 || height <= 0 || width > 512 || height > 512)
            {
                return null;
            }

            List<(SKBitmap, int)> frames = [];
            using SKBitmap canvasBitmap = new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using SKCanvas canvas = new(canvasBitmap);
            using SKPaint over = new() { BlendMode = SKBlendMode.SrcOver };
            using SKPaint source = new() { BlendMode = SKBlendMode.Src };

            canvas.Clear(SKColors.Transparent);

            foreach (Part part in parts)
            {
                // each part inside the picture, and not more data than a picture that size can hold
                if (part.Data.Length == 0 || part.Width <= 0 || part.Height <= 0 || part.X < 0 || part.Y < 0 ||
                    part.X + part.Width > width || part.Y + part.Height > height || part.Data.Length > 8 * 1024 * 1024)
                {
                    continue;
                }

                using SKBitmap piece = SKBitmap.Decode(Rebuild(header, shared, part));

                if (piece == null)
                {
                    continue;
                }

                SKRect area = SKRect.Create(part.X, part.Y, part.Width, part.Height);
                using SKBitmap before = part.Dispose == 2 ? canvasBitmap.Copy() : null;

                canvas.DrawBitmap(piece, area, part.Blend == 0 ? source : over);
                canvas.Flush();
                frames.Add((canvasBitmap.Copy(), part.Milliseconds));

                if (part.Dispose == 1)
                {
                    canvas.Save();
                    canvas.ClipRect(area);
                    canvas.Clear(SKColors.Transparent);
                    canvas.Restore();
                }
                else if (before != null)
                {
                    canvas.DrawBitmap(before, 0, 0, source);
                }
            }

            return frames;
        }

        // A part of the animation as a PNG of its own: the header with its size, the shared chunks, its data.
        private static byte[] Rebuild(byte[] header, List<(string Type, byte[] Data)> shared, Part part)
        {
            using MemoryStream png = new();

            png.Write(_signature);

            byte[] own = (byte[])header.Clone();

            BinaryPrimitives.WriteUInt32BigEndian(own.AsSpan(0), (uint)part.Width);
            BinaryPrimitives.WriteUInt32BigEndian(own.AsSpan(4), (uint)part.Height);
            Chunk(png, "IHDR", own);

            foreach ((string type, byte[] data) in shared)
            {
                Chunk(png, type, data);
            }

            Chunk(png, "IDAT", part.Data.ToArray());
            Chunk(png, "IEND", []);

            return png.ToArray();
        }

        private static void Chunk(Stream png, string type, byte[] data)
        {
            Span<byte> number = stackalloc byte[4];
            byte[] name = Encoding.ASCII.GetBytes(type);

            BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
            png.Write(number);
            png.Write(name);
            png.Write(data);

            uint crc = 0xFFFFFFFF;

            foreach (byte b in name)
            {
                crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            foreach (byte b in data)
            {
                crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ 0xFFFFFFFF);
            png.Write(number);
        }

        private static uint[] MakeCrcTable()
        {
            uint[] table = new uint[256];

            for (uint n = 0; n < 256; n++)
            {
                uint c = n;

                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
