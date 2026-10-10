using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Ryujinx.HLE.HOS.Themes
{
    /// <summary>
    /// [Nextendo, HOME Menu themes] An animation of a theme (the "AnimJson" of its layout.json,
    /// a description of a BFLAN file) written as the BFLAN file the system reads: the pat1
    /// section (name, groups) and the pai1 section (textures, then per pane or material the
    /// animated values and their key frames).
    /// </summary>
    public static class AnimationWriter
    {
        private const int GroupNameLength = 0x24;

        /// <summary>The animation, for a firmware whose animations have that version.</summary>
        public static byte[] Write(string json, uint version)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            byte[] pat = Pat1(root.GetProperty("pat1"));
            byte[] pai = Pai1(root.GetProperty("pai1"));

            using MemoryStream file = new();
            using BinaryWriter w = new(file);

            w.Write(Encoding.ASCII.GetBytes("FLAN"));
            w.Write((ushort)0xFEFF);
            w.Write((ushort)0x14);
            w.Write(version);
            w.Write(0u); // size, below
            w.Write((ushort)2);
            w.Write((ushort)0);
            w.Write(Section("pat1", pat));
            w.Write(Section("pai1", pai));
            w.Flush();

            byte[] output = file.ToArray();

            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x0C), output.Length);

            return output;
        }

        private static byte[] Section(string magic, byte[] body)
        {
            byte[] section = new byte[8 + body.Length];

            Encoding.ASCII.GetBytes(magic).CopyTo(section, 0);
            BinaryPrimitives.WriteInt32LittleEndian(section.AsSpan(4), section.Length);
            body.CopyTo(section, 8);

            return section;
        }

        private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";

        private static long Num(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? (long)v.GetDouble() : 0;

        private static float Float(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : 0;

        private static byte[] Bytes(JsonElement e, string name)
        {
            string text = Str(e, name);

            return string.IsNullOrEmpty(text) ? [] : Convert.FromBase64String(text);
        }

        private static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
            e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

        // Offsets in a section body are counted from the section's start, 8 bytes before it.
        private static void Patch32(MemoryStream stream, long at, long value)
        {
            byte[] bytes = BitConverter.GetBytes((uint)value);
            long back = stream.Position;

            stream.Position = at;
            stream.Write(bytes);
            stream.Position = back;
        }

        private static void Pad4(MemoryStream stream)
        {
            while (stream.Position % 4 != 0)
            {
                stream.WriteByte(0);
            }
        }

        private static byte[] Pat1(JsonElement pat)
        {
            string[] groups = Array(pat, "Groups").Select(g => g.GetString() ?? "").ToArray();

            using MemoryStream s = new();
            using BinaryWriter w = new(s);

            w.Write((ushort)Num(pat, "AnimationOrder"));
            w.Write((ushort)groups.Length);

            long offsets = s.Position;

            w.Write(0u); // name
            w.Write(0u); // groups
            w.Write((ushort)Num(pat, "Unk_StartOfFile"));
            w.Write((ushort)Num(pat, "Unk_EndOfFile"));
            w.Write((byte)Num(pat, "ChildBinding"));
            w.Write(Bytes(pat, "Unk_EndOfHeader"));
            w.Flush();

            Patch32(s, offsets, s.Position + 8);
            w.Write(Encoding.ASCII.GetBytes(Str(pat, "Name")));
            w.Write((byte)0);
            w.Flush();
            Pad4(s);

            Patch32(s, offsets + 4, s.Position + 8);

            foreach (string group in groups)
            {
                byte[] name = new byte[GroupNameLength];

                Encoding.ASCII.GetBytes(group).AsSpan(0, Math.Min(group.Length, GroupNameLength - 1)).CopyTo(name);
                w.Write(name);
            }

            w.Flush();

            return s.ToArray();
        }

        private static byte[] Pai1(JsonElement pai)
        {
            string[] textures = Array(pai, "Textures").Select(t => t.GetString() ?? "").ToArray();
            JsonElement[] entries = Array(pai, "Entries").ToArray();

            using MemoryStream s = new();
            using BinaryWriter w = new(s);

            w.Write((ushort)Num(pai, "FrameSize"));
            w.Write((byte)Num(pai, "Flags"));
            w.Write((byte)0);
            w.Write((ushort)textures.Length);
            w.Write((ushort)entries.Length);

            long entryTableAt = s.Position;

            w.Write(0u);
            w.Flush();

            if (textures.Length > 0)
            {
                long table = s.Position;

                w.Write(new byte[4 * textures.Length]);
                w.Flush();

                for (int i = 0; i < textures.Length; i++)
                {
                    Patch32(s, table + 4 * i, s.Position - table);
                    w.Write(Encoding.ASCII.GetBytes(textures[i]));
                    w.Write((byte)0);
                    w.Flush();
                }

                Pad4(s);
            }

            long entryTable = s.Position;

            Patch32(s, entryTableAt, entryTable + 8);
            w.Write(new byte[4 * entries.Length]);
            w.Flush();

            for (int i = 0; i < entries.Length; i++)
            {
                Patch32(s, entryTable + 4 * i, s.Position + 8);
                WriteEntry(s, w, entries[i]);
            }

            return s.ToArray();
        }

        private static void WriteEntry(MemoryStream s, BinaryWriter w, JsonElement entry)
        {
            long start = s.Position;
            JsonElement[] tags = Array(entry, "Tags").ToArray();
            byte target = (byte)Num(entry, "Target");
            byte[] name = new byte[28];
            string text = Str(entry, "Name");

            Encoding.ASCII.GetBytes(text).AsSpan(0, Math.Min(text.Length, 27)).CopyTo(name);
            w.Write(name);
            w.Write((byte)tags.Length);
            w.Write(target);
            w.Write((ushort)0);

            long tagTable = s.Position;

            w.Write(new byte[4 * tags.Length]);
            w.Write(Bytes(entry, "UnkwnownData"));
            w.Flush();

            for (int i = 0; i < tags.Length; i++)
            {
                Patch32(s, tagTable + 4 * i, s.Position - start);
                WriteTag(s, w, tags[i], target);
            }
        }

        private static void WriteTag(MemoryStream s, BinaryWriter w, JsonElement tag, byte target)
        {
            // An animated user data value carries a word before its tag.
            if (target == 2)
            {
                w.Write((uint)Num(tag, "Unknown"));
            }

            w.Flush();

            long start = s.Position;
            string kind = Str(tag, "TagType");
            bool userData = kind == "FLEU";
            JsonElement[] entries = Array(tag, "Entries").ToArray();

            w.Write(Encoding.ASCII.GetBytes(kind.PadRight(4)[..4]));
            w.Write((uint)entries.Length);

            long table = s.Position;

            w.Write(new byte[4 * entries.Length]);
            w.Flush();

            for (int i = 0; i < entries.Length; i++)
            {
                Patch32(s, table + 4 * i, s.Position - start);
                WriteTagEntry(s, w, entries[i], userData);
            }
        }

        private static void WriteTagEntry(MemoryStream s, BinaryWriter w, JsonElement entry, bool userData)
        {
            JsonElement[] keys = Array(entry, "KeyFrames").ToArray();
            ushort dataType = (ushort)Num(entry, "DataType");

            w.Write((byte)Num(entry, "Index"));
            w.Write((byte)Num(entry, "AnimationTarget"));
            w.Write(dataType);
            w.Write((ushort)keys.Length);
            w.Write((ushort)0);
            w.Write(0x0Cu); // the key frames follow

            foreach (JsonElement key in keys)
            {
                w.Write(Float(key, "Frame"));

                if (dataType == 1)
                {
                    // Steps: whole values.
                    w.Write((short)Float(key, "Value"));
                    w.Write((short)Float(key, "Blend"));
                }
                else
                {
                    w.Write(Float(key, "Value"));
                    w.Write(Float(key, "Blend"));
                }
            }

            if (userData)
            {
                w.Write((uint)Num(entry, "FLEUUnknownInt"));
                w.Write(Encoding.ASCII.GetBytes(Str(entry, "FLEUEntryName")));
                w.Write((byte)0);
                w.Flush();
                Pad4(s);
            }

            w.Flush();
        }

        /// <summary>The version of an existing animation file.</summary>
        public static uint VersionOf(byte[] bflan) => BinaryPrimitives.ReadUInt32LittleEndian(bflan.AsSpan(8));
    }
}
