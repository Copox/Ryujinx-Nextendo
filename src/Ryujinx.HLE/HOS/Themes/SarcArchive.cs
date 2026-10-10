using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Ryujinx.HLE.HOS.Themes
{
    /// <summary>
    /// [Nextendo, HOME Menu themes] A SARC archive (the layout archives of the system, .szs once
    /// Yaz0-compressed): its files by name, read and written again. Each file keeps the alignment
    /// it had, which the system needs (the texture archive is mapped as GPU memory).
    /// </summary>
    public sealed class SarcArchive
    {
        private const uint HashKey = 0x65;

        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

        private readonly Dictionary<string, int> _alignment = new(StringComparer.Ordinal);

        public static SarcArchive Read(byte[] data)
        {
            if (data.Length < 0x20 || Encoding.ASCII.GetString(data, 0, 4) != "SARC")
            {
                throw new InvalidDataException("not a SARC archive");
            }

            SarcArchive archive = new();
            ReadOnlySpan<byte> b = data;

            int dataOffset = BinaryPrimitives.ReadInt32LittleEndian(b[0x0C..]);
            const int Sfat = 0x14;
            int count = BinaryPrimitives.ReadUInt16LittleEndian(b[(Sfat + 6)..]);
            int names = Sfat + 12 + 16 * count + 8;

            for (int i = 0; i < count; i++)
            {
                int node = Sfat + 12 + 16 * i;
                uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(b[(node + 4)..]);
                int start = dataOffset + BinaryPrimitives.ReadInt32LittleEndian(b[(node + 8)..]);
                int end = dataOffset + BinaryPrimitives.ReadInt32LittleEndian(b[(node + 12)..]);
                int nameStart = names + (int)(attributes & 0xFFFF) * 4;
                int nameEnd = Array.IndexOf(data, (byte)0, nameStart);
                string name = Encoding.ASCII.GetString(data, nameStart, nameEnd - nameStart);

                archive.Files[name] = b[start..end].ToArray();
                archive._alignment[name] = NaturalAlignment(start);
            }

            return archive;
        }

        // The largest power of two the offset of the file is a multiple of (up to 0x2000).
        private static int NaturalAlignment(int offset)
        {
            int alignment = 4;

            while (alignment < 0x2000 && offset % (alignment * 2) == 0)
            {
                alignment *= 2;
            }

            return alignment;
        }

        private static uint Hash(string name)
        {
            uint hash = 0;

            foreach (char c in name)
            {
                hash = hash * HashKey + (byte)c;
            }

            return hash;
        }

        private int AlignmentOf(string name)
        {
            if (_alignment.TryGetValue(name, out int alignment))
            {
                return alignment;
            }

            return name.EndsWith(".bntx", StringComparison.Ordinal) ? 0x1000 : 0x10;
        }

        public byte[] Write()
        {
            List<(string Name, uint Hash)> entries = Files.Keys.Select(n => (n, Hash(n))).OrderBy(e => e.Item2).ToList();

            using MemoryStream names = new();
            List<int> nameOffsets = [];

            foreach ((string name, _) in entries)
            {
                nameOffsets.Add((int)names.Position / 4);

                byte[] text = Encoding.ASCII.GetBytes(name);

                names.Write(text);
                names.WriteByte(0);

                while (names.Position % 4 != 0)
                {
                    names.WriteByte(0);
                }
            }

            int headerEnd = 0x14 + 12 + 16 * entries.Count + 8 + (int)names.Length;
            int maxAlignment = entries.Count == 0 ? 4 : entries.Max(e => AlignmentOf(e.Name));
            int dataOffset = Align(headerEnd, maxAlignment);

            using MemoryStream body = new();
            List<(int Start, int End)> spans = [];

            foreach ((string name, _) in entries)
            {
                int alignment = AlignmentOf(name);

                while ((dataOffset + body.Position) % alignment != 0)
                {
                    body.WriteByte(0);
                }

                int start = (int)body.Position;

                body.Write(Files[name]);
                spans.Add((start, (int)body.Position));
            }

            byte[] output = new byte[dataOffset + body.Length];
            Span<byte> o = output;

            Encoding.ASCII.GetBytes("SARC").CopyTo(o);
            BinaryPrimitives.WriteUInt16LittleEndian(o[4..], 0x14);
            BinaryPrimitives.WriteUInt16LittleEndian(o[6..], 0xFEFF);
            BinaryPrimitives.WriteInt32LittleEndian(o[8..], output.Length);
            BinaryPrimitives.WriteInt32LittleEndian(o[0x0C..], dataOffset);
            BinaryPrimitives.WriteUInt16LittleEndian(o[0x10..], 0x100);

            const int Sfat = 0x14;
            Encoding.ASCII.GetBytes("SFAT").CopyTo(o[Sfat..]);
            BinaryPrimitives.WriteUInt16LittleEndian(o[(Sfat + 4)..], 0xC);
            BinaryPrimitives.WriteUInt16LittleEndian(o[(Sfat + 6)..], (ushort)entries.Count);
            BinaryPrimitives.WriteUInt32LittleEndian(o[(Sfat + 8)..], HashKey);

            for (int i = 0; i < entries.Count; i++)
            {
                int node = Sfat + 12 + 16 * i;

                BinaryPrimitives.WriteUInt32LittleEndian(o[node..], entries[i].Hash);
                BinaryPrimitives.WriteUInt32LittleEndian(o[(node + 4)..], 0x01000000u | (uint)nameOffsets[i]);
                BinaryPrimitives.WriteInt32LittleEndian(o[(node + 8)..], spans[i].Start);
                BinaryPrimitives.WriteInt32LittleEndian(o[(node + 12)..], spans[i].End);
            }

            int sfnt = Sfat + 12 + 16 * entries.Count;
            Encoding.ASCII.GetBytes("SFNT").CopyTo(o[sfnt..]);
            BinaryPrimitives.WriteUInt16LittleEndian(o[(sfnt + 4)..], 8);
            names.ToArray().CopyTo(o[(sfnt + 8)..]);
            body.ToArray().CopyTo(o[dataOffset..]);

            return output;
        }

        private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
    }
}
