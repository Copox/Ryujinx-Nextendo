using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Ryujinx.HLE.HOS.Themes
{
    /// <summary>
    /// [Nextendo, HOME Menu themes] One part of a theme (a .nxtheme file of a Themezer pack: the
    /// HOME Menu, the lock screen, the settings...). The file is a zip (or, for old ones, a
    /// Yaz0 SARC) holding info.json, an optional wallpaper (image.jpg / image.dds), an optional
    /// layout.json and common.json, and optional icons.
    /// </summary>
    public sealed class ThemePart
    {
        public string Target;
        public string Name;
        public string Author;

        public byte[] Wallpaper;
        public LayoutPatch Layout;
        public LayoutPatch CommonLayout;
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        internal static readonly JsonSerializerOptions Json = new()
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        };

        public static ThemePart Load(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            ThemePart part = new();

            if (data.Length > 4 && data[0] == 'P' && data[1] == 'K')
            {
                using ZipArchive zip = new(new MemoryStream(data), ZipArchiveMode.Read);

                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    using Stream stream = entry.Open();
                    using MemoryStream copy = new();

                    stream.CopyTo(copy);
                    part.Files[entry.FullName] = copy.ToArray();
                }
            }
            else
            {
                foreach ((string name, byte[] file) in SarcArchive.Read(NextendoBranding.Yaz0Decode(data)).Files)
                {
                    part.Files[name] = file;
                }
            }

            if (!part.Files.TryGetValue("info.json", out byte[] info))
            {
                throw new InvalidDataException("no info.json");
            }

            using (JsonDocument manifest = JsonDocument.Parse(info))
            {
                part.Target = manifest.RootElement.TryGetProperty("Target", out JsonElement t) ? t.GetString() : null;
                part.Name = manifest.RootElement.TryGetProperty("ThemeName", out JsonElement n) ? n.GetString() : null;
                part.Author = manifest.RootElement.TryGetProperty("Author", out JsonElement a) ? a.GetString() : null;
            }

            part.Wallpaper = part.Files.GetValueOrDefault("image.dds") ?? part.Files.GetValueOrDefault("image.jpg") ?? part.Files.GetValueOrDefault("image.png");
            part.Layout = ReadLayout(part.Files.GetValueOrDefault("layout.json"));
            part.CommonLayout = ReadLayout(part.Files.GetValueOrDefault("common.json"));

            return part;
        }

        private static LayoutPatch ReadLayout(byte[] json) =>
            json == null ? null : JsonSerializer.Deserialize<LayoutPatch>(Encoding.UTF8.GetString(json), Json);
    }

    // ---- what a layout.json holds (the format of Themezer themes) ----

    public sealed class LayoutPatch
    {
        public string PatchName;
        public string AuthorName;
        public string TargetName;
        public string ID;
        public bool? HideOnlineBtn;
        public int? TargetFirmware;
        public LayoutFilePatch[] Files;
        public AnimFilePatch[] Anims;
    }

    public sealed class LayoutFilePatch
    {
        public string FileName;
        public PanePatch[] Patches;
        public GroupPatch[] AddGroups;
        public MaterialPatch[] Materials;
        public string[] PushBackPanes;
        public string[] PullFrontPanes;
    }

    public sealed class AnimFilePatch
    {
        public string FileName;
        public string AnimJson;
    }

    public sealed class GroupPatch
    {
        public string GroupName;
        public string[] Panes;
    }

    public sealed class Vector
    {
        public float? X;
        public float? Y;
        public float? Z;
    }

    public sealed class PanePatch
    {
        public string PaneName;
        public Vector Position;
        public Vector Rotation;
        public Vector Scale;
        public Vector Size;
        public bool? Visible;
        public UserDataPatch[] UsdPatches;
        public byte? OriginX;
        public byte? OriginY;
        public byte? ParentOriginX;
        public byte? ParentOriginY;

        // Pictures: the four corner colours; texts: font top, shadow top, font bottom, shadow bottom.
        public string ColorTL;
        public string ColorTR;
        public string ColorBL;
        public string ColorBR;
    }

    public sealed class UserDataPatch
    {
        public string PropName;
        public string[] PropValues;
        public int Type;
    }

    public sealed class MaterialPatch
    {
        public string MaterialName;
        public string ForegroundColor;
        public string BackgroundColor;
        public TextureReferencePatch[] Refs;
        public TextureTransformPatch[] Transforms;
    }

    public sealed class TextureReferencePatch
    {
        public string Name;
        public byte? WrapS;
        public byte? WrapT;
    }

    public sealed class TextureTransformPatch
    {
        public string Name;
        public float? X;
        public float? Y;
        public float? Rotation;
        public float? ScaleX;
        public float? ScaleY;
    }
}
