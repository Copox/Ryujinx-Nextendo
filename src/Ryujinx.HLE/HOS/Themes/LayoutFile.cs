using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Ryujinx.HLE.HOS.Themes
{
    /// <summary>
    /// [Nextendo, HOME Menu themes] A layout of the system (BFLYT, version 9): its sections in
    /// order, each kept as bytes and changed in place where a theme changes it. The pane tree is
    /// the order of the sections (a pane, its user data, then its children between pas1 and pae1).
    /// </summary>
    public sealed class LayoutFile
    {
        private static readonly HashSet<string> _paneKinds = ["pan1", "pic1", "txt1", "wnd1", "prt1", "bnd1", "scr1", "ali1"];

        private readonly byte[] _header;

        public List<Section> Sections { get; } = [];

        public sealed class Section
        {
            public string Magic;
            public byte[] Data;

            public Section(string magic, byte[] data)
            {
                Magic = magic;
                Data = data;
            }

            public bool IsPane => _paneKinds.Contains(Magic);

            public string PaneName => IsPane ? CString(Data, 0x0C, 0x18) : null;
        }

        public LayoutFile(byte[] data)
        {
            if (Encoding.ASCII.GetString(data, 0, 4) != "FLYT")
            {
                throw new InvalidDataException("not a layout");
            }

            int headerSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6));
            int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x10));

            _header = data[..headerSize];

            int offset = headerSize;

            for (int i = 0; i < count; i++)
            {
                int size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 4));

                Sections.Add(new Section(Encoding.ASCII.GetString(data, offset, 4), data[offset..(offset + size)]));
                offset += size;
            }
        }

        public byte[] Write()
        {
            using MemoryStream stream = new();

            stream.Write(_header);

            foreach (Section section in Sections)
            {
                BinaryPrimitives.WriteInt32LittleEndian(section.Data.AsSpan(4), section.Data.Length);
                stream.Write(section.Data);
            }

            byte[] output = stream.ToArray();

            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x0C), output.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(0x10), (ushort)Sections.Count);

            return output;
        }

        internal static string CString(byte[] data, int offset, int max)
        {
            int end = offset;

            while (end < offset + max && end < data.Length && data[end] != 0)
            {
                end++;
            }

            return Encoding.ASCII.GetString(data, offset, end - offset);
        }

        // ---- panes ----

        public int FindPane(string name)
        {
            for (int i = 0; i < Sections.Count; i++)
            {
                if (Sections[i].IsPane && Sections[i].PaneName == name)
                {
                    return i;
                }
            }

            return -1;
        }

        public Section Pane(string name)
        {
            int index = FindPane(name);

            return index < 0 ? null : Sections[index];
        }

        /// <summary>The sections of a pane with all it holds: [first, end).</summary>
        public (int First, int End) Subtree(int index)
        {
            int end = index + 1;

            if (end < Sections.Count && Sections[end].Magic == "usd1")
            {
                end++;
            }

            if (end < Sections.Count && Sections[end].Magic == "pas1")
            {
                int depth = 0;

                for (; end < Sections.Count; end++)
                {
                    if (Sections[end].Magic == "pas1")
                    {
                        depth++;
                    }
                    else if (Sections[end].Magic == "pae1" && --depth == 0)
                    {
                        end++;

                        break;
                    }
                }
            }

            return (index, end);
        }

        // The children of the pane that holds the section at index: [first child, its pae1).
        private (int Start, int End) SiblingRange(int index)
        {
            int depth = 0;
            int start = 0;

            for (int i = index - 1; i >= 0; i--)
            {
                if (Sections[i].Magic == "pae1")
                {
                    depth++;
                }
                else if (Sections[i].Magic == "pas1")
                {
                    if (depth == 0)
                    {
                        start = i + 1;

                        break;
                    }

                    depth--;
                }
            }

            int end = Sections.Count;
            depth = 0;

            for (int i = index; i < Sections.Count; i++)
            {
                if (Sections[i].Magic == "pas1")
                {
                    depth++;
                }
                else if (Sections[i].Magic == "pae1")
                {
                    if (depth == 0)
                    {
                        end = i;

                        break;
                    }

                    depth--;
                }
            }

            return (start, end);
        }

        /// <summary>Moves a pane (with its children) first or last among its siblings.</summary>
        public bool MovePane(string name, bool toFront)
        {
            int index = FindPane(name);

            if (index < 0)
            {
                return false;
            }

            (int first, int end) = Subtree(index);
            (int start, int stop) = SiblingRange(index);
            List<Section> moved = Sections.GetRange(first, end - first);

            Sections.RemoveRange(first, end - first);

            int target = toFront ? start : stop - moved.Count;

            Sections.InsertRange(target, moved);

            return true;
        }

        public static void SetFloat(byte[] data, int offset, float value) => BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offset), value);

        public static void SetColor(byte[] data, int offset, string hex)
        {
            if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint color))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), color);
            }
        }

        // ---- textures and materials ----

        public Section FindSection(string magic) => Sections.FirstOrDefault(s => s.Magic == magic);

        public List<string> TextureNames()
        {
            Section txl = FindSection("txl1");

            if (txl == null)
            {
                return [];
            }

            int count = BinaryPrimitives.ReadUInt16LittleEndian(txl.Data.AsSpan(8));
            List<string> names = [];

            for (int i = 0; i < count; i++)
            {
                int at = 0x0C + BinaryPrimitives.ReadInt32LittleEndian(txl.Data.AsSpan(0x0C + 4 * i));

                names.Add(CString(txl.Data, at, txl.Data.Length - at));
            }

            return names;
        }

        public void SetTextureNames(List<string> names)
        {
            using MemoryStream stream = new();
            int tableSize = 4 * names.Count;

            stream.Write(new byte[0x0C + tableSize]);

            List<int> offsets = [];

            foreach (string name in names)
            {
                offsets.Add((int)stream.Position - 0x0C);
                stream.Write(Encoding.ASCII.GetBytes(name));
                stream.WriteByte(0);
            }

            while (stream.Length % 4 != 0)
            {
                stream.WriteByte(0);
            }

            byte[] data = stream.ToArray();

            Encoding.ASCII.GetBytes("txl1").CopyTo(data, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), (ushort)names.Count);

            for (int i = 0; i < offsets.Count; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x0C + 4 * i), offsets[i]);
            }

            Section txl = FindSection("txl1");

            if (txl != null)
            {
                txl.Data = data;
            }
            else
            {
                // After lyt1, where the system keeps it.
                Sections.Insert(Sections.FindIndex(s => s.Magic == "lyt1") + 1, new Section("txl1", data));
            }
        }

        /// <summary>The materials (each one's bytes), in order.</summary>
        public List<byte[]> Materials()
        {
            Section mat = FindSection("mat1");

            if (mat == null)
            {
                return [];
            }

            int count = BinaryPrimitives.ReadUInt16LittleEndian(mat.Data.AsSpan(8));
            List<int> starts = [];

            for (int i = 0; i < count; i++)
            {
                starts.Add(BinaryPrimitives.ReadInt32LittleEndian(mat.Data.AsSpan(0x0C + 4 * i)));
            }

            List<byte[]> materials = [];

            for (int i = 0; i < count; i++)
            {
                int end = i + 1 < count ? starts[i + 1] : mat.Data.Length;

                materials.Add(mat.Data[starts[i]..end]);
            }

            return materials;
        }

        public void SetMaterials(List<byte[]> materials)
        {
            Section mat = FindSection("mat1");
            int tableEnd = 0x0C + 4 * materials.Count;
            byte[] data = new byte[tableEnd + materials.Sum(m => m.Length)];

            Encoding.ASCII.GetBytes("mat1").CopyTo(data, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), (ushort)materials.Count);

            int at = tableEnd;

            for (int i = 0; i < materials.Count; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x0C + 4 * i), at);
                materials[i].CopyTo(data, at);
                at += materials[i].Length;
            }

            mat.Data = data;
        }

        public static string MaterialName(byte[] material) => CString(material, 0, 0x1C);

        // ---- user data of a pane ----

        public sealed class UserProperty
        {
            public string Name;
            public byte Type;    // 0 string, 1 int, 2 float, 3 system data
            public byte Extra;
            public int Count;
            public byte[] Value; // its bytes
        }

        public static List<UserProperty> ReadUserData(byte[] usd)
        {
            int count = BinaryPrimitives.ReadUInt16LittleEndian(usd.AsSpan(8));
            List<(int Entry, int Data, int Name)> places = [];
            List<UserProperty> properties = [];

            for (int i = 0; i < count; i++)
            {
                int entry = 0x0C + 0x0C * i;
                int name = entry + BinaryPrimitives.ReadInt32LittleEndian(usd.AsSpan(entry));
                int data = entry + BinaryPrimitives.ReadInt32LittleEndian(usd.AsSpan(entry + 4));

                places.Add((entry, data, name));
            }

            // Where the data of an entry stops: the next thing written after it.
            List<int> boundaries = places.SelectMany(p => new[] { p.Data, p.Name }).Append(usd.Length).Distinct().Order().ToList();

            for (int i = 0; i < count; i++)
            {
                (int entry, int data, int name) = places[i];
                int end = boundaries.First(b => b > data);

                properties.Add(new UserProperty
                {
                    Name = CString(usd, name, usd.Length - name),
                    Count = BinaryPrimitives.ReadUInt16LittleEndian(usd.AsSpan(entry + 8)),
                    Type = usd[entry + 10],
                    Extra = usd[entry + 11],
                    Value = usd[data..end],
                });
            }

            return properties;
        }

        public static byte[] WriteUserData(List<UserProperty> properties)
        {
            int tableEnd = 0x0C + 0x0C * properties.Count;

            using MemoryStream body = new();
            List<int> dataAt = [];

            foreach (UserProperty property in properties)
            {
                dataAt.Add(tableEnd + (int)body.Position);
                body.Write(property.Value);

                while (body.Position % 4 != 0)
                {
                    body.WriteByte(0);
                }
            }

            List<int> nameAt = [];

            foreach (UserProperty property in properties)
            {
                nameAt.Add(tableEnd + (int)body.Position);
                body.Write(Encoding.ASCII.GetBytes(property.Name));
                body.WriteByte(0);
            }

            while (body.Position % 4 != 0)
            {
                body.WriteByte(0);
            }

            byte[] data = new byte[tableEnd + body.Length];

            Encoding.ASCII.GetBytes("usd1").CopyTo(data, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), (ushort)properties.Count);

            for (int i = 0; i < properties.Count; i++)
            {
                int entry = 0x0C + 0x0C * i;

                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(entry), nameAt[i] - entry);
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(entry + 4), dataAt[i] - entry);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entry + 8), (ushort)properties[i].Count);
                data[entry + 10] = properties[i].Type;
                data[entry + 11] = properties[i].Extra;
            }

            body.ToArray().CopyTo(data, tableEnd);

            return data;
        }

        // ---- groups ----

        public List<string> GroupNames() =>
            Sections.Where(s => s.Magic == "grp1").Select(s => CString(s.Data, 8, 0x22)).ToList();

        /// <summary>Adds a group of panes under the root group.</summary>
        public void AddGroup(string name, IReadOnlyList<string> panes)
        {
            byte[] data = new byte[0x2C + 0x18 * panes.Count];

            Encoding.ASCII.GetBytes("grp1").CopyTo(data, 0);
            Encoding.ASCII.GetBytes(name).AsSpan(0, Math.Min(name.Length, 0x21)).CopyTo(data.AsSpan(8));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x2A), (ushort)panes.Count);

            for (int i = 0; i < panes.Count; i++)
            {
                Encoding.ASCII.GetBytes(panes[i]).AsSpan(0, Math.Min(panes[i].Length, 0x18)).CopyTo(data.AsSpan(0x2C + 0x18 * i));
            }

            int root = Sections.FindIndex(s => s.Magic == "grp1");
            Section group = new("grp1", data);

            if (root < 0)
            {
                Sections.Add(group);

                return;
            }

            if (root + 1 < Sections.Count && Sections[root + 1].Magic == "grs1")
            {
                // Before the end of the root's children.
                int depth = 0;

                for (int i = root + 1; i < Sections.Count; i++)
                {
                    if (Sections[i].Magic == "grs1")
                    {
                        depth++;
                    }
                    else if (Sections[i].Magic == "gre1" && --depth == 0)
                    {
                        Sections.Insert(i, group);

                        return;
                    }
                }
            }

            Sections.InsertRange(root + 1, [new Section("grs1", Header("grs1")), group, new Section("gre1", Header("gre1"))]);
        }

        internal static byte[] Header(string magic)
        {
            byte[] data = new byte[8];

            Encoding.ASCII.GetBytes(magic).CopyTo(data, 0);

            return data;
        }
    }
}
