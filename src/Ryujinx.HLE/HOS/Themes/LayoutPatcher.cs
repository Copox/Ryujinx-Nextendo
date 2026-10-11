using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Ryujinx.HLE.HOS.Themes
{
    /// <summary>
    /// [Nextendo, HOME Menu themes] Applies what a theme's layout.json asks to a layout file:
    /// pane placement, visibility, colours, user data, materials, groups and pane order. A pane or
    /// a material the firmware does not have is skipped, so themes made for older firmwares
    /// still apply what they can.
    /// </summary>
    public static class LayoutPatcher
    {
        // Offsets inside a pane section.
        private const int Flags = 0x08, Origin = 0x09, Translate = 0x2C, Rotate = 0x38, Scale = 0x44, Size = 0x4C;

        /// <summary>False when nothing of the file may be changed (a group names a missing pane).</summary>
        public static bool Apply(LayoutFile layout, LayoutFilePatch patch)
        {
            if (patch.Materials != null)
            {
                ApplyMaterials(layout, patch.Materials);
            }

            foreach (PanePatch pane in patch.Patches ?? [])
            {
                ApplyPane(layout, pane);
            }

            if (patch.AddGroups is { Length: > 0 })
            {
                List<string> groups = layout.GroupNames();
                HashSet<string> panes = layout.Sections.Where(s => s.IsPane).Select(s => s.PaneName).ToHashSet();

                foreach (GroupPatch group in patch.AddGroups)
                {
                    if (groups.Contains(group.GroupName))
                    {
                        continue;
                    }

                    if ((group.Panes ?? []).Any(p => !panes.Contains(p)))
                    {
                        return false;
                    }

                    layout.AddGroup(group.GroupName, group.Panes ?? []);
                }
            }

            foreach (string name in patch.PullFrontPanes ?? [])
            {
                layout.MovePane(name, toFront: true);
            }

            foreach (string name in patch.PushBackPanes ?? [])
            {
                layout.MovePane(name, toFront: false);
            }

            return true;
        }

        /// <summary>
        /// Where a pane is drawn: the centre of the screen at 0, as its parents place it (their
        /// rotations left out), and whether it and its parents are shown. Null when not found.
        /// </summary>
        public static (float X, float Y, float ScaleX, float ScaleY, bool Visible)? ScreenPlace(LayoutFile layout, string paneName)
        {
            var parents = new Stack<(float X, float Y, float ScaleX, float ScaleY, bool Visible)>();
            (float X, float Y, float ScaleX, float ScaleY, bool Visible) current = (0, 0, 1, 1, true);
            (float X, float Y, float ScaleX, float ScaleY, bool Visible) last = current;

            foreach (LayoutFile.Section section in layout.Sections)
            {
                if (section.Magic == "pas1")
                {
                    parents.Push(current);
                    current = last;
                    continue;
                }

                if (section.Magic == "pae1")
                {
                    current = parents.Count > 0 ? parents.Pop() : (0, 0, 1, 1, true);
                    continue;
                }

                if (!section.IsPane)
                {
                    continue;
                }

                byte[] d = section.Data;
                float x = BitConverter.ToSingle(d, Translate), y = BitConverter.ToSingle(d, Translate + 4);
                float sx = BitConverter.ToSingle(d, Scale), sy = BitConverter.ToSingle(d, Scale + 4);

                last = (current.X + current.ScaleX * x, current.Y + current.ScaleY * y, current.ScaleX * sx, current.ScaleY * sy, current.Visible && (d[Flags] & 1) != 0);

                if (section.PaneName == paneName)
                {
                    return last;
                }
            }

            return null;
        }

        /// <summary>Hides a pane (false when not found).</summary>
        public static bool Hide(LayoutFile layout, string paneName)
        {
            LayoutFile.Section pane = layout.Pane(paneName);

            if (pane == null)
            {
                return false;
            }

            pane.Data[Flags] &= 0xFE;

            return true;
        }

        private static void Set3(byte[] data, int offset, Vector v)
        {
            // An axis a theme leaves out is 0, as the themes are written for.
            LayoutFile.SetFloat(data, offset, v.X ?? 0);
            LayoutFile.SetFloat(data, offset + 4, v.Y ?? 0);
            LayoutFile.SetFloat(data, offset + 8, v.Z ?? 0);
        }

        private static void Set2(byte[] data, int offset, Vector v)
        {
            LayoutFile.SetFloat(data, offset, v.X ?? 0);
            LayoutFile.SetFloat(data, offset + 4, v.Y ?? 0);
        }

        private static void ApplyPane(LayoutFile layout, PanePatch patch)
        {
            int index = layout.FindPane(patch.PaneName);

            if (index < 0)
            {
                return;
            }

            LayoutFile.Section section = layout.Sections[index];
            byte[] d = section.Data;

            if (patch.Visible is bool visible)
            {
                d[Flags] = (byte)(visible ? d[Flags] | 1 : d[Flags] & ~1);
            }

            if (patch.Position != null)
            {
                Set3(d, Translate, patch.Position);
            }

            if (patch.Rotation != null)
            {
                Set3(d, Rotate, patch.Rotation);
            }

            if (patch.Scale != null)
            {
                Set2(d, Scale, patch.Scale);
            }

            if (patch.Size != null)
            {
                Set2(d, Size, patch.Size);
            }

            if (patch.OriginX is byte ox)
            {
                d[Origin] = (byte)((d[Origin] & ~0x03) | (ox & 3));
            }

            if (patch.OriginY is byte oy)
            {
                d[Origin] = (byte)((d[Origin] & ~0x0C) | ((oy & 3) << 2));
            }

            if (patch.ParentOriginX is byte px)
            {
                d[Origin] = (byte)((d[Origin] & ~0x30) | ((px & 3) << 4));
            }

            if (patch.ParentOriginY is byte py)
            {
                d[Origin] = (byte)((d[Origin] & ~0xC0) | ((py & 3) << 6));
            }

            if (section.Magic == "pic1")
            {
                SetColor(d, 0x54, patch.ColorTL);
                SetColor(d, 0x58, patch.ColorTR);
                SetColor(d, 0x5C, patch.ColorBL);
                SetColor(d, 0x60, patch.ColorBR);
            }
            else if (section.Magic == "txt1")
            {
                SetColor(d, 0x68, patch.ColorTL); // font, top
                SetColor(d, 0x94, patch.ColorTR); // shadow, top
                SetColor(d, 0x6C, patch.ColorBL); // font, bottom
                SetColor(d, 0x98, patch.ColorBR); // shadow, bottom
            }

            if (patch.UsdPatches is { Length: > 0 } && index + 1 < layout.Sections.Count && layout.Sections[index + 1].Magic == "usd1")
            {
                LayoutFile.Section usd = layout.Sections[index + 1];
                List<LayoutFile.UserProperty> properties = LayoutFile.ReadUserData(usd.Data);

                foreach (UserDataPatch p in patch.UsdPatches)
                {
                    string[] values = p.PropValues ?? [];
                    LayoutFile.UserProperty existing = properties.FirstOrDefault(x => x.Name == p.PropName);

                    if (existing == null)
                    {
                        properties.Add(new LayoutFile.UserProperty
                        {
                            Name = p.PropName,
                            Type = (byte)p.Type,
                            Count = p.Type == 0 ? Encoding.UTF8.GetByteCount(values.FirstOrDefault() ?? "") : values.Length,
                            Value = EncodeUserValue(p.Type, values),
                        });
                    }
                    else if (existing.Type == p.Type && existing.Count == values.Length && p.Type is 1 or 2)
                    {
                        existing.Value = EncodeUserValue(p.Type, values);
                    }
                }

                usd.Data = LayoutFile.WriteUserData(properties);
            }
        }

        private static void SetColor(byte[] data, int offset, string color)
        {
            if (color != null)
            {
                LayoutFile.SetColor(data, offset, color);
            }
        }

        private static byte[] EncodeUserValue(int type, string[] values)
        {
            if (type == 0)
            {
                return Encoding.UTF8.GetBytes(values.FirstOrDefault() ?? "");
            }

            byte[] data = new byte[4 * values.Length];

            for (int i = 0; i < values.Length; i++)
            {
                if (type == 2)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4 * i), float.Parse(values[i], CultureInfo.InvariantCulture));
                }
                else
                {
                    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4 * i), int.Parse(values[i], CultureInfo.InvariantCulture));
                }
            }

            return data;
        }

        private static void ApplyMaterials(LayoutFile layout, MaterialPatch[] patches)
        {
            List<byte[]> materials = layout.Materials();

            if (materials.Count == 0)
            {
                return;
            }

            List<string> textures = layout.TextureNames();

            foreach (MaterialPatch patch in patches)
            {
                foreach (byte[] material in materials.Where(m => LayoutFile.MaterialName(m) == patch.MaterialName))
                {
                    // name (0x1C), flags, colour layout, black (foreground), white (background).
                    SetColor(material, 0x24, patch.ForegroundColor);
                    SetColor(material, 0x28, patch.BackgroundColor);

                    if (patch.Refs == null && patch.Transforms == null)
                    {
                        continue;
                    }

                    uint flags = BinaryPrimitives.ReadUInt32LittleEndian(material.AsSpan(0x1C));
                    int refCount = (int)(flags & 3);
                    int srtCount = (int)((flags >> 2) & 3);
                    const int Refs = 0x2C;
                    int srts = Refs + 4 * refCount;

                    int RefOf(string texture)
                    {
                        for (int i = 0; i < refCount; i++)
                        {
                            int id = BinaryPrimitives.ReadUInt16LittleEndian(material.AsSpan(Refs + 4 * i));

                            if (id < textures.Count && textures[id] == texture)
                            {
                                return i;
                            }
                        }

                        return -1;
                    }

                    foreach (TextureReferencePatch r in patch.Refs ?? [])
                    {
                        int i = RefOf(r.Name);

                        if (i >= 0)
                        {
                            if (r.WrapS is byte s)
                            {
                                material[Refs + 4 * i + 2] = s;
                            }

                            if (r.WrapT is byte t)
                            {
                                material[Refs + 4 * i + 3] = t;
                            }
                        }
                    }

                    foreach (TextureTransformPatch t in patch.Transforms ?? [])
                    {
                        int i = RefOf(t.Name);

                        if (i >= 0 && i < srtCount)
                        {
                            int at = srts + 20 * i;

                            if (t.X is float x)
                            {
                                LayoutFile.SetFloat(material, at, x);
                            }

                            if (t.Y is float y)
                            {
                                LayoutFile.SetFloat(material, at + 4, y);
                            }

                            if (t.Rotation is float rotation)
                            {
                                LayoutFile.SetFloat(material, at + 8, rotation);
                            }

                            if (t.ScaleX is float sx)
                            {
                                LayoutFile.SetFloat(material, at + 12, sx);
                            }

                            if (t.ScaleY is float sy)
                            {
                                LayoutFile.SetFloat(material, at + 16, sy);
                            }
                        }
                    }
                }
            }

            layout.SetMaterials(materials);
        }

        /// <summary>
        /// The wallpaper of a part: a new picture pane named after the part, the size of the
        /// screen, drawn with the background texture, in place of the panes that drew it.
        /// </summary>
        public static bool AddWallpaperPane(LayoutFile layout, string paneName, string textureName, string[] replacedPanes)
        {
            if (layout.FindPane(paneName) >= 0)
            {
                return true;
            }

            int target = -1;

            foreach (string name in replacedPanes)
            {
                int index = layout.FindPane(name);

                if (index < 0)
                {
                    continue;
                }

                if (target < 0)
                {
                    target = index;
                }

                // Out of the screen.
                Set3(layout.Sections[index].Data, Translate, new Vector { X = 5000, Y = 60000, Z = 0 });
            }

            if (target < 0)
            {
                return false;
            }

            List<string> textures = layout.TextureNames();

            if (!textures.Contains(textureName))
            {
                textures.Add(textureName);
                layout.SetTextureNames(textures);
            }

            List<byte[]> materials = layout.Materials();

            materials.Add(WallpaperMaterial((ushort)textures.IndexOf(textureName)));
            layout.SetMaterials(materials);

            byte[] pane = new byte[0x88];

            Encoding.ASCII.GetBytes("pic1").CopyTo(pane, 0);
            pane[Flags] = 1;        // visible
            pane[0x0A] = 0xFF;      // alpha
            pane[0x0B] = 4;
            Encoding.ASCII.GetBytes(paneName).AsSpan(0, Math.Min(paneName.Length, 0x18)).CopyTo(pane.AsSpan(0x0C));
            Set2(pane, Scale, new Vector { X = 1, Y = 1 });
            Set2(pane, Size, new Vector { X = 1280, Y = 720 });
            pane.AsSpan(0x54, 0x10).Fill(0xFF); // white corners
            BinaryPrimitives.WriteUInt16LittleEndian(pane.AsSpan(0x64), (ushort)(materials.Count - 1));
            pane[0x66] = 1; // one set of texture coordinates
            LayoutFile.SetFloat(pane, 0x68 + 0x08, 1); // top right u
            LayoutFile.SetFloat(pane, 0x68 + 0x14, 1); // bottom left v
            LayoutFile.SetFloat(pane, 0x68 + 0x18, 1); // bottom right u
            LayoutFile.SetFloat(pane, 0x68 + 0x1C, 1); // bottom right v

            layout.Sections.Insert(target, new LayoutFile.Section("pic1", pane));

            return true;
        }

        private static byte[] WallpaperMaterial(ushort texture)
        {
            byte[] material = new byte[0x54];

            Encoding.ASCII.GetBytes("P_Custm").CopyTo(material, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(material.AsSpan(0x1C), 0x15);       // a texture, its transform, its coordinates
            BinaryPrimitives.WriteUInt32LittleEndian(material.AsSpan(0x20), 0x08040200); // colour layout
            BinaryPrimitives.WriteUInt32LittleEndian(material.AsSpan(0x24), 0x00000000); // black
            BinaryPrimitives.WriteUInt32LittleEndian(material.AsSpan(0x28), 0xFFFFFFFF); // white
            BinaryPrimitives.WriteUInt16LittleEndian(material.AsSpan(0x2C), texture);
            LayoutFile.SetFloat(material, 0x30 + 12, 1); // scale x
            LayoutFile.SetFloat(material, 0x30 + 16, 1); // scale y

            return material;
        }

        /// <summary>Texture coordinates of a picture back to the whole texture.</summary>
        public static bool ResetTextureCoordinates(LayoutFile layout, string paneName)
        {
            LayoutFile.Section pane = layout.Pane(paneName);

            if (pane == null || pane.Magic != "pic1")
            {
                return false;
            }

            int sets = pane.Data[0x66];
            float[] whole = [0, 0, 1, 0, 0, 1, 1, 1];

            for (int i = 0; i < sets; i++)
            {
                for (int k = 0; k < 8; k++)
                {
                    LayoutFile.SetFloat(pane.Data, 0x68 + 0x20 * i + 4 * k, whole[k]);
                }
            }

            return true;
        }

        /// <summary>Points the panes of a layout from one texture to another.</summary>
        public static bool RenameTexture(LayoutFile layout, string from, string to)
        {
            List<string> textures = layout.TextureNames();
            bool changed = false;

            for (int i = 0; i < textures.Count; i++)
            {
                if (textures[i] == from)
                {
                    textures[i] = to;
                    changed = true;
                }
            }

            if (changed)
            {
                layout.SetTextureNames(textures);
            }

            return changed;
        }
    }
}
