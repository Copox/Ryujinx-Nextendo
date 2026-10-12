using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Ryujinx.HLE.HOS.Services.Sdb.Pl
{
    /// <summary>The controller of player 1, for the symbols of the HOME Menu.</summary>
    public enum ControllerStyle
    {
        Nintendo,
        PlayStation4,
        PlayStation5,
        Xbox,
    }

    /// <summary>
    /// [Nextendo] The symbols of the system's button font follow the controller of player 1:
    /// <list type="bullet">
    /// <item>PlayStation: A/B/X/Y become circle/cross/triangle/square (by position: A right, B
    /// bottom, X top, Y left), L/R/ZL/ZR become L1/R1/L2/R2, the stick presses L3/R3, +/- the
    /// Options and Share (PS4) or Create (PS5) buttons;</item>
    /// <item>PlayStation and Xbox: the Pro Controller pictogram (HOME Menu footer, Controllers
    /// page, which colour it as they colour the original) becomes that controller.</item>
    /// </list>
    /// The symbols are Kenney's Input Prompts (CC0), built as TrueType glyphs (ButtonGlyphs_*.bin:
    /// count, then per glyph its code point, size and data).
    /// </summary>
    public static class ControllerButtonFont
    {
        /// <summary>Set before the emulation starts, from the controller of player 1.</summary>
        public static ControllerStyle Style { get; set; }

        /// <summary>The style of a controller, from the name the system gives it.</summary>
        public static ControllerStyle StyleOf(string name)
        {
            string n = (name ?? "").ToLowerInvariant();

            if (n.Contains("dualsense") || n.Contains("ps5"))
            {
                return ControllerStyle.PlayStation5;
            }

            if (n.Contains("dualshock") || n.Contains("ps3") || n.Contains("ps4") || n.Contains("playstation"))
            {
                return ControllerStyle.PlayStation4;
            }

            return n.Contains("xbox") ? ControllerStyle.Xbox : ControllerStyle.Nintendo;
        }

        public static byte[] Apply(byte[] ttf)
        {
            string[] sets = Style switch
            {
                ControllerStyle.PlayStation4 => ["ps", "ps4"],
                ControllerStyle.PlayStation5 => ["ps", "ps5"],
                ControllerStyle.Xbox => ["xbox"],
                _ => [],
            };

            if (sets.Length == 0)
            {
                return ttf;
            }

            try
            {
                Dictionary<uint, byte[]> glyphs = [];

                foreach (string set in sets)
                {
                    ReadGlyphs(set, glyphs);
                }

                return Patch(ttf, glyphs) ?? ttf;
            }
            catch (Exception)
            {
                return ttf;
            }
        }

        private static void ReadGlyphs(string set, Dictionary<uint, byte[]> glyphs)
        {
            using Stream stream = typeof(ControllerButtonFont).Assembly.GetManifestResourceStream($"Ryujinx.HLE.HOS.Services.Sdb.Pl.ButtonGlyphs_{set}.bin");
            using BinaryReader reader = new(stream);

            uint count = reader.ReadUInt32();

            for (uint i = 0; i < count; i++)
            {
                uint codePoint = reader.ReadUInt32();
                int size = reader.ReadInt32();

                glyphs[codePoint] = reader.ReadBytes(size);
            }
        }

        // The glyph table is rebuilt with the new glyphs and put at the end of the font; the index
        // (long offsets) is rewritten in place.
        private static byte[] Patch(byte[] ttf, Dictionary<uint, byte[]> glyphs)
        {
            ReadOnlySpan<byte> font = ttf;
            int tables = BinaryPrimitives.ReadUInt16BigEndian(font[4..]);
            int head = -1, maxp = -1, loca = -1, glyf = -1, cmap = -1, glyfEntry = -1;

            for (int i = 0; i < tables; i++)
            {
                int entry = 12 + i * 16;
                int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(font[(entry + 8)..]);

                switch (System.Text.Encoding.ASCII.GetString(ttf, entry, 4))
                {
                    case "head": head = offset; break;
                    case "maxp": maxp = offset; break;
                    case "loca": loca = offset; break;
                    case "glyf": glyf = offset; glyfEntry = entry; break;
                    case "cmap": cmap = offset; break;
                }
            }

            if (head < 0 || maxp < 0 || loca < 0 || glyf < 0 || cmap < 0 || BinaryPrimitives.ReadInt16BigEndian(font[(head + 50)..]) != 1)
            {
                return null;
            }

            int numGlyphs = BinaryPrimitives.ReadUInt16BigEndian(font[(maxp + 4)..]);
            Dictionary<int, byte[]> byGlyph = [];

            foreach ((uint codePoint, byte[] data) in glyphs)
            {
                int id = GlyphOf(font, cmap, codePoint);

                if (id > 0 && id < numGlyphs)
                {
                    byGlyph[id] = data;
                }
            }

            if (byGlyph.Count == 0)
            {
                return null;
            }

            using MemoryStream newGlyf = new();
            uint[] newLoca = new uint[numGlyphs + 1];

            for (int id = 0; id < numGlyphs; id++)
            {
                newLoca[id] = (uint)newGlyf.Length;

                int start = (int)BinaryPrimitives.ReadUInt32BigEndian(font[(loca + id * 4)..]);
                int end = (int)BinaryPrimitives.ReadUInt32BigEndian(font[(loca + id * 4 + 4)..]);

                newGlyf.Write(byGlyph.TryGetValue(id, out byte[] data) ? data : font.Slice(glyf + start, end - start));

                while (newGlyf.Length % 4 != 0)
                {
                    newGlyf.WriteByte(0);
                }
            }

            newLoca[numGlyphs] = (uint)newGlyf.Length;

            int at = (ttf.Length + 3) & ~3;
            byte[] result = new byte[at + newGlyf.Length];

            ttf.CopyTo(result, 0);
            newGlyf.ToArray().CopyTo(result, at);

            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(glyfEntry + 8), (uint)at);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(glyfEntry + 12), (uint)newGlyf.Length);

            for (int id = 0; id <= numGlyphs; id++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(loca + id * 4), newLoca[id]);
            }

            return result;
        }

        // The glyph of a code point, from the Windows Unicode BMP subtable (format 4).
        private static int GlyphOf(ReadOnlySpan<byte> font, int cmap, uint codePoint)
        {
            int count = BinaryPrimitives.ReadUInt16BigEndian(font[(cmap + 2)..]);

            for (int i = 0; i < count; i++)
            {
                int record = cmap + 4 + i * 8;

                if (BinaryPrimitives.ReadUInt16BigEndian(font[record..]) != 3 || BinaryPrimitives.ReadUInt16BigEndian(font[(record + 2)..]) != 1)
                {
                    continue;
                }

                int table = cmap + (int)BinaryPrimitives.ReadUInt32BigEndian(font[(record + 4)..]);

                if (BinaryPrimitives.ReadUInt16BigEndian(font[table..]) != 4)
                {
                    continue;
                }

                int segments = BinaryPrimitives.ReadUInt16BigEndian(font[(table + 6)..]) / 2;
                int ends = table + 14;
                int starts = ends + segments * 2 + 2;
                int deltas = starts + segments * 2;
                int ranges = deltas + segments * 2;

                for (int s = 0; s < segments; s++)
                {
                    int end = BinaryPrimitives.ReadUInt16BigEndian(font[(ends + s * 2)..]);
                    int start = BinaryPrimitives.ReadUInt16BigEndian(font[(starts + s * 2)..]);

                    if (codePoint > end || codePoint < start)
                    {
                        continue;
                    }

                    short delta = BinaryPrimitives.ReadInt16BigEndian(font[(deltas + s * 2)..]);
                    int range = BinaryPrimitives.ReadUInt16BigEndian(font[(ranges + s * 2)..]);

                    if (range == 0)
                    {
                        return (int)((codePoint + delta) & 0xFFFF);
                    }

                    int glyph = BinaryPrimitives.ReadUInt16BigEndian(font[(ranges + s * 2 + range + (int)(codePoint - start) * 2)..]);

                    return glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
                }
            }

            return 0;
        }
    }
}
