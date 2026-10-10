using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.RomFs;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Ryujinx.HLE.HOS
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The texts of the system (HOME Menu, user page, user selection...)
    /// name Nextendo: "Nintendo Switch Online" becomes the Nextendo website, every other
    /// "Nintendo" becomes "Nextendo", and the addresses of Nintendo's sites point to Nextendo's.
    /// Done on the message files (/message/&lt;language&gt;/*.msbt.szs: MSBT in Yaz0) when a system
    /// program is loaded; the formatting tags of the texts are kept as they are.
    /// </summary>
    public static class NextendoBranding
    {
        // "Nextendo Website" in each language of the console, by the folder of its messages.
        private static readonly Dictionary<string, string> _website = new()
        {
            ["fr"] = "Site Nextendo",
            ["en"] = "Nextendo Website",
            ["es"] = "Sitio web de Nextendo",
            ["de"] = "Nextendo-Website",
            ["it"] = "Sito web di Nextendo",
            ["nl"] = "Nextendo-website",
            ["pt"] = "Site da Nextendo",
            ["ru"] = "Сайт Nextendo",
            ["ja"] = "Nextendoウェブサイト",
            ["ko"] = "Nextendo 웹사이트",
            ["zh-CN"] = "Nextendo 网站",
            ["zh-TW"] = "Nextendo 網站",
        };

        private static readonly Regex _address = new(
            @"(https?://)?([a-z0-9-]+\.)*(nintendo|nintendoswitch)\.(com\.cn|co\.jp|com|net|jp)(/[^\s<""]*)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const string Kept = @"(?!\s*(Switch|eShop|3DS|2DS|DS(?![a-z])|Entertainment|64|Game\s?Boy|GameCube|Wii|Labo|Ring|Super\s?(NES|Nintendo)|\s+$))";
        private static readonly Regex _brand = new("Nintendo" + Kept, RegexOptions.Compiled);
        private static readonly Regex _brandUpper = new("NINTENDO" + Kept, RegexOptions.Compiled);
        private static readonly Regex _katakana = new(@"ニンテンドー(?!\s*(スイッチ|3DS|イーショップ|$))", RegexOptions.Compiled);
        private static readonly Regex _kanji = new(@"任天堂(?!\s*(Switch|eShop|$))", RegexOptions.Compiled);
        private static readonly Regex _hangul = new(@"닌텐도(?!\s*(스위치|Switch|e숍|eShop|$))", RegexOptions.Compiled);

        // "The console" is the emulator: "Ryujinx | Nextendo". Not when it names another console
        // (the previous one, the one data comes from, a Nintendo Switch 2...).
        private const string Emulator = "Ryujinx | Nextendo";
        private static readonly Regex _consoleFr = new(@"\b(?:[Ll]a|[Cc]ette) console(?!\s+(?:Nintendo|Nextendo|source|précédente|cible|d'origine|de destination|Switch|\d)|\s+(?:depuis|sur|dans|avec|à partir de|vers) laquelle)", RegexOptions.Compiled);
        private static readonly Regex _consoleEn = new(@"\b(?:[Tt]he|[Tt]his) console(?!\s+(?:you|from|to which|Nintendo|Nextendo|Switch|\d))", RegexOptions.Compiled);

        // The label before the system version on the System Update page, in each language: the
        // version of the emulator is put in front of it.
        private static readonly HashSet<string> _systemVersionLabels =
        [
            "Logiciel système : version ", "System version ", "Systemversion ", "Versione di sistema: ", "Versión: ", "Versie: ",
            "Versão da consola: ", "Версия системы ", "システムバージョン ", "시스템 버전 ", "系统版本 ", "系統版本 ",
        ];

        private static string EmulatorVersion
        {
            get
            {
                string version = Ryujinx.Common.ReleaseInformation.Version ?? "";
                int end = version.IndexOfAny(['+', '-']);

                return end > 0 ? version[..end] : version;
            }
        }

        public static bool Enabled => Environment.GetEnvironmentVariable("NEXTENDO_BRANDING") != "0";

        public static IStorage Appliquer(ulong programId, IStorage baseStorage, IStorage originalStorage = null)
        {
            if (!Enabled || baseStorage == null || programId < 0x0100000000001000 || programId > 0x0100000000001FFF)
            {
                return baseStorage;
            }

            try
            {
                return Patch(programId, baseStorage, originalStorage);
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.ModLoader, $"[Nextendo] texts of {programId:X16} left as they are ({e.GetType().Name}: {e.Message})");

                return baseStorage;
            }
        }

        private static IStorage Patch(ulong programId, IStorage baseStorage, IStorage originalStorage)
        {
            RomFsFileSystem baseRom = new(baseStorage);

            // [Nextendo, HOME Menu themes] With themes for the themes page, the layouts are the
            // system's own: a theme installed on top of them (NXThemes, in atmosphere/contents)
            // would be mixed with the one chosen, or stay with Basic White.
            RomFsFileSystem layoutRom = originalStorage != null && originalStorage != baseStorage && Themes.NextendoThemes.Packs().Count > 0 ? new(originalStorage) : null;
            Dictionary<string, byte[]> changed = new();

            // NEXTENDO_ROMFS_DUMP=<folder>: the layouts of the system programs are written there (study).
            if (Environment.GetEnvironmentVariable("NEXTENDO_ROMFS_DUMP") is { Length: > 0 } dump)
            {
                foreach (DirectoryEntryEx entry in baseRom.EnumerateEntries().Where(f => f.Type == DirectoryEntryType.File && f.FullPath.StartsWith("/lyt/", StringComparison.Ordinal)))
                {
                    string target = System.IO.Path.Combine(dump, programId.ToString("X16"), entry.FullPath.TrimStart('/'));

                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                    File.WriteAllBytes(target, Read(baseRom, entry.FullPath));
                }
            }
            int strings = 0;

            foreach (DirectoryEntryEx entry in baseRom.EnumerateEntries().Where(f => f.Type == DirectoryEntryType.File &&
                         f.FullPath.StartsWith("/message/", StringComparison.Ordinal) && f.FullPath.EndsWith(".msbt.szs", StringComparison.Ordinal)))
            {
                string language = LanguageOf(entry.FullPath);
                byte[] packed = Read(baseRom, entry.FullPath);
                byte[] msbt = Yaz0Decode(packed);
                string grep = Environment.GetEnvironmentVariable("NEXTENDO_MSBT_GREP");
                byte[] patched = PatchMsbt(msbt, text =>
                {
                    if (grep != null && Regex.IsMatch(text, grep))
                    {
                        Logger.Info?.Print(LogClass.ModLoader, $"[msbt] {programId:X16} {entry.FullPath}: {text.Replace('\n', '|')}");
                    }

                    if (entry.FullPath.EndsWith("/setting.msbt.szs", StringComparison.Ordinal) && _systemVersionLabels.Contains(text))
                    {
                        text = $"{Emulator} {EmulatorVersion} · {text}";
                    }

                    return Rebrand(text, language);
                }, out int count);

                if (count > 0)
                {
                    changed[entry.FullPath] = Yaz0Encode(patched);
                    strings += count;
                }
            }

            // [Nextendo, HOME Menu themes] The theme chosen changes the layout archives of its parts.
            foreach (DirectoryEntryEx entry in baseRom.EnumerateEntries().Where(f => f.Type == DirectoryEntryType.File &&
                         f.FullPath.StartsWith("/lyt/", StringComparison.Ordinal) && f.FullPath.EndsWith(".szs", StringComparison.Ordinal)).ToList())
            {
                try
                {
                    byte[] packed = Read(baseRom, entry.FullPath);
                    byte[] own = layoutRom != null && Exists(layoutRom, entry.FullPath) ? Read(layoutRom, entry.FullPath) : packed;
                    byte[] themed = Themes.NextendoThemes.Apply(programId, entry.Name, Yaz0Decode(own));

                    if (themed != null)
                    {
                        changed[entry.FullPath] = Yaz0Encode(themed);
                    }
                    else if (!own.AsSpan().SequenceEqual(packed))
                    {
                        changed[entry.FullPath] = own;
                        Logger.Info?.Print(LogClass.ModLoader, $"[theme] {entry.FullPath}: the system's own (a theme installed in atmosphere/contents left aside)");
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ModLoader, $"[theme] {entry.FullPath} left as it is: {e.Message}");
                }
            }

            // The "Nintendo Switch Online" button of the HOME Menu (and its picture on the screen
            // that resumes a system applet): the Nextendo logo instead, as single-channel masks the
            // layouts colour, of the size and GPU layout of the originals.
            if (programId == 0x0100000000001000)
            {
                foreach ((string path, string texture, string asset) in _logos)
                {
                    if (!Exists(baseRom, path))
                    {
                        continue;
                    }

                    byte[] layout = Yaz0Decode(changed.TryGetValue(path, out byte[] themedArchive) ? themedArchive : Read(baseRom, path));
                    bool replaced = ReplaceTexture(layout, texture, Ryujinx.Common.EmbeddedResources.Read("Ryujinx.HLE/HOS/NextendoAssets/" + asset));

                    if (replaced)
                    {
                        changed[path] = Yaz0Encode(layout);
                    }

                    Logger.Info?.Print(LogClass.ModLoader, $"[Nextendo] HOME Menu: Nextendo logo in {path} ({texture}) {(replaced ? "done" : "not done")}");
                }
            }

            // [Nextendo, HOME Menu themes] The pictures of the themes added to its themes page.
            List<(string Path, byte[] Data)> added = programId == 0x0100000000001000 ? Themes.QlaunchThemePage.Pictures().ToList() : [];

            if (added.Count > 0)
            {
                Logger.Info?.Print(LogClass.ModLoader, $"[theme] {added.Count} picture(s) for the themes page (system one present: {Exists(baseRom, "/theme/ThemeIcon_00.jpg")})");
            }

            if (changed.Count == 0 && added.Count == 0)
            {
                return baseStorage;
            }

            RomFsBuilder builder = new();

            foreach ((string path, byte[] data) in added)
            {
                builder.AddFile(path, new StorageFile(new MemoryStorage(data), OpenMode.Read));
            }

            foreach (DirectoryEntryEx entry in baseRom.EnumerateEntries().Where(f => f.Type == DirectoryEntryType.File).OrderBy(f => f.FullPath, StringComparer.Ordinal))
            {
                if (changed.TryGetValue(entry.FullPath, out byte[] data))
                {
                    builder.AddFile(entry.FullPath, new StorageFile(new MemoryStorage(data), OpenMode.Read));
                }
                else
                {
                    using UniqueRef<IFile> file = new();
                    baseRom.OpenFile(ref file.Ref, entry.FullPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();
                    builder.AddFile(entry.FullPath, file.Release());
                }
            }

            Logger.Info?.Print(LogClass.ModLoader, $"[Nextendo] texts of {programId:X16}: {strings} string(s) in {changed.Count} file(s) name Nextendo");

            return builder.Build();
        }

        // The online button of the HOME Menu, then its picture on the screen that resumes a system applet.
        private static readonly (string Path, string Texture, string Asset)[] _logos =
        [
            ("/lyt/ResidentMenu.szs", "RdtIcoLR_00^s", "RdtIcoLR_00.bc4"),
            ("/lyt/Entrance.szs", "EntIcoLHUB^s", "EntIcoLHUB.bc4"),
        ];

        private static bool Exists(IFileSystem fs, string path) =>
            fs.GetEntryType(out DirectoryEntryType type, path.ToU8Span()).IsSuccess() && type == DirectoryEntryType.File;

        // Puts new texture data (already in the GPU layout of the original, same size) in place of
        // a texture of the BNTX archives of a SARC layout. True when done.
        private static bool ReplaceTexture(byte[] sarc, string name, byte[] data)
        {
            if (sarc.Length < 0x20 || Encoding.ASCII.GetString(sarc, 0, 4) != "SARC")
            {
                return false;
            }

            int dataOffset = BitConverter.ToInt32(sarc, 0x0C);
            int sfat = 0x14;
            int nodes = BitConverter.ToUInt16(sarc, sfat + 6);
            int names = sfat + 12 + 16 * nodes + 8;

            for (int i = 0; i < nodes; i++)
            {
                int node = sfat + 12 + 16 * i;
                int nameOffset = (BitConverter.ToInt32(sarc, node + 4) & 0xFFFF) * 4;
                int start = dataOffset + BitConverter.ToInt32(sarc, node + 8);
                int end = dataOffset + BitConverter.ToInt32(sarc, node + 12);
                int nameEnd = Array.IndexOf(sarc, (byte)0, names + nameOffset);
                string file = Encoding.ASCII.GetString(sarc, names + nameOffset, nameEnd - (names + nameOffset));

                if (!file.EndsWith(".bntx", StringComparison.Ordinal))
                {
                    continue;
                }

                Span<byte> bntx = sarc.AsSpan(start, end - start);
                int nx = bntx.IndexOf("NX  "u8);

                if (nx < 0)
                {
                    continue;
                }

                int count = BitConverter.ToInt32(bntx[(nx + 4)..]);
                int infoPointers = (int)BitConverter.ToInt64(bntx[(nx + 8)..]);

                for (int t = 0; t < count; t++)
                {
                    int brti = (int)BitConverter.ToInt64(bntx[(infoPointers + 8 * t)..]);
                    int nameAddress = (int)BitConverter.ToInt64(bntx[(brti + 0x60)..]);
                    int nameLength = BitConverter.ToUInt16(bntx[nameAddress..]);
                    string texture = Encoding.ASCII.GetString(bntx.Slice(nameAddress + 2, nameLength));

                    if (texture != name)
                    {
                        continue;
                    }

                    int size = BitConverter.ToInt32(bntx[(brti + 0x50)..]);
                    int mips = (int)BitConverter.ToInt64(bntx[(brti + 0x70)..]);
                    int mip0 = (int)BitConverter.ToInt64(bntx[mips..]);

                    if (size != data.Length)
                    {
                        Logger.Warning?.Print(LogClass.ModLoader, $"[Nextendo] texture {name}: 0x{size:X} bytes, 0x{data.Length:X} given, left as it is");

                        return false;
                    }

                    data.CopyTo(bntx.Slice(mip0, size));

                    return true;
                }
            }

            return false;
        }

        // "/message/EUfr/x.msbt.szs" -> "fr"; Chinese keeps its script.
        private static string LanguageOf(string path)
        {
            string folder = path.Split('/')[2];

            return folder switch
            {
                "CNzh" => "zh-CN",
                "TWzh" => "zh-TW",
                _ => folder.Length >= 4 ? folder[2..4] : "en",
            };
        }

        public static string Rebrand(string text, string language)
        {
            string website = _website.TryGetValue(language, out string name) ? name : _website["en"];

            text = text.Replace("Nintendo Switch Online", website)
                       .Replace("Nintendo Switch&nbsp;Online", website);
            text = _address.Replace(text, m => (m.Groups[1].Success ? m.Groups[1].Value : "") + "nextendo.network");
            text = text.Replace("Nintendo Co., Ltd.", "Nextendo Network");

            // The names of Nintendo's consoles and shop stay (a "Nextendo Switch" means nothing);
            // the account, the online service, the support site become Nextendo's. A "Nintendo"
            // at the end of a run is followed by a formatting tag holding "Switch 2": kept too.
            text = language switch
            {
                "fr" => _consoleFr.Replace(text, Emulator),
                "en" => _consoleEn.Replace(text, Emulator),
                _ => text,
            };

            text = _brand.Replace(text, "Nextendo");
            text = _brandUpper.Replace(text, "NEXTENDO");
            text = _katakana.Replace(text, "ネクステンドー");
            text = _kanji.Replace(text, "Nextendo");
            text = _hangul.Replace(text, "넥스텐도");

            return text;
        }

        private static byte[] Read(IFileSystem fs, string path)
        {
            using UniqueRef<IFile> file = new();
            fs.OpenFile(ref file.Ref, path.ToU8Span(), OpenMode.Read).ThrowIfFailure();
            file.Get.GetSize(out long size).ThrowIfFailure();
            byte[] data = new byte[size];
            file.Get.Read(out _, 0, data).ThrowIfFailure();

            return data;
        }

        // ---- Yaz0 ----

        public static byte[] Yaz0Decode(byte[] src)
        {
            if (src.Length < 16 || src[0] != 'Y' || src[1] != 'a' || src[2] != 'z' || src[3] != '0')
            {
                return src;
            }

            int size = (src[4] << 24) | (src[5] << 16) | (src[6] << 8) | src[7];
            byte[] dst = new byte[size];
            int s = 16, d = 0;

            while (d < size)
            {
                byte flags = src[s++];

                for (int bit = 0; bit < 8 && d < size; bit++)
                {
                    if ((flags & (0x80 >> bit)) != 0)
                    {
                        dst[d++] = src[s++];
                    }
                    else
                    {
                        int b1 = src[s++], b2 = src[s++];
                        int dist = ((b1 & 0xF) << 8 | b2) + 1;
                        int n = b1 >> 4;

                        n = n == 0 ? src[s++] + 0x12 : n + 2;

                        for (int k = 0; k < n && d < size; k++, d++)
                        {
                            dst[d] = dst[d - dist];
                        }
                    }
                }
            }

            return dst;
        }

        // Stored as literals only: valid Yaz0, read the same way.
        public static byte[] Yaz0Encode(byte[] src)
        {
            using MemoryStream output = new();

            output.Write("Yaz0"u8);
            output.WriteByte((byte)(src.Length >> 24));
            output.WriteByte((byte)(src.Length >> 16));
            output.WriteByte((byte)(src.Length >> 8));
            output.WriteByte((byte)src.Length);
            output.Write(new byte[8]);

            for (int i = 0; i < src.Length; i += 8)
            {
                output.WriteByte(0xFF);
                output.Write(src, i, Math.Min(8, src.Length - i));
            }

            return output.ToArray();
        }

        // ---- MSBT ----

        private delegate string Rewriter(string text);

        private static byte[] PatchMsbt(byte[] msbt, Rewriter rewrite, out int count)
        {
            count = 0;

            if (msbt.Length < 0x20 || Encoding.ASCII.GetString(msbt, 0, 8) != "MsgStdBn")
            {
                return msbt;
            }

            bool little = msbt[8] == 0xFF && msbt[9] == 0xFE;
            int sections = U16(msbt, 0x0E, little);
            int offset = 0x20;
            List<byte[]> parts = new() { msbt[..0x20] };

            for (int i = 0; i < sections && offset + 16 <= msbt.Length; i++)
            {
                string magic = Encoding.ASCII.GetString(msbt, offset, 4);
                int size = (int)U32(msbt, offset + 4, little);
                int padded = (size + 15) & ~15;
                byte[] header = msbt[offset..(offset + 16)];
                byte[] body = msbt[(offset + 16)..(offset + 16 + size)];

                if (magic == "TXT2" && msbt[0x0C] == 1)
                {
                    body = PatchTxt2(body, little, rewrite, ref count);
                    WriteU32(header, 4, (uint)body.Length, little);
                    padded = (body.Length + 15) & ~15;
                }

                byte[] section = new byte[16 + padded];

                header.CopyTo(section, 0);
                body.CopyTo(section, 16);
                section.AsSpan(16 + body.Length).Fill(0xAB);
                parts.Add(section);

                offset += 16 + ((size + 15) & ~15);
            }

            if (count == 0)
            {
                return msbt;
            }

            byte[] result = parts.SelectMany(p => p).ToArray();

            WriteU32(result, 0x12, (uint)result.Length, little);

            return result;
        }

        private static byte[] PatchTxt2(byte[] body, bool little, Rewriter rewrite, ref int count)
        {
            int entries = (int)U32(body, 0, little);
            List<byte[]> strings = new();

            for (int k = 0; k < entries; k++)
            {
                int start = (int)U32(body, 4 + 4 * k, little);
                int end = k + 1 < entries ? (int)U32(body, 8 + 4 * k, little) : body.Length;
                byte[] original = body[start..end];
                byte[] patched = PatchString(original, little, rewrite, out bool changed);

                if (changed)
                {
                    count++;
                }

                strings.Add(patched);
            }

            using MemoryStream output = new();
            byte[] table = new byte[4 + 4 * entries];
            int at = table.Length;

            WriteU32(table, 0, (uint)entries, little);

            for (int k = 0; k < entries; k++)
            {
                WriteU32(table, 4 + 4 * k, (uint)at, little);
                at += strings[k].Length;
            }

            output.Write(table);

            foreach (byte[] s in strings)
            {
                output.Write(s);
            }

            return output.ToArray();
        }

        // A string: text runs rewritten, tags (0x0E ... / 0x0F ...) copied as they are.
        private static byte[] PatchString(byte[] raw, bool little, Rewriter rewrite, out bool changed)
        {
            changed = false;

            using MemoryStream output = new();
            StringBuilder run = new();
            int i = 0;

            void Flush(ref bool c)
            {
                if (run.Length == 0)
                {
                    return;
                }

                string before = run.ToString();
                string after = rewrite(before);

                if (after != before)
                {
                    c = true;
                }

                foreach (char ch in after)
                {
                    WriteChar(output, ch, little);
                }

                run.Clear();
            }

            while (i + 1 < raw.Length)
            {
                int c = U16(raw, i, little);

                if (c == 0x0E && i + 8 <= raw.Length)
                {
                    Flush(ref changed);
                    int length = 8 + U16(raw, i + 6, little);
                    output.Write(raw, i, Math.Min(length, raw.Length - i));
                    i += length;
                }
                else if (c == 0x0F && i + 6 <= raw.Length)
                {
                    Flush(ref changed);
                    output.Write(raw, i, 6);
                    i += 6;
                }
                else if (c == 0)
                {
                    Flush(ref changed);
                    output.Write(raw, i, raw.Length - i);
                    i = raw.Length;
                }
                else
                {
                    run.Append((char)c);
                    i += 2;
                }
            }

            Flush(ref changed);

            return output.ToArray();
        }

        private static void WriteChar(Stream output, char c, bool little)
        {
            if (little)
            {
                output.WriteByte((byte)c);
                output.WriteByte((byte)(c >> 8));
            }
            else
            {
                output.WriteByte((byte)(c >> 8));
                output.WriteByte((byte)c);
            }
        }

        private static int U16(byte[] b, int o, bool little) => little ? b[o] | b[o + 1] << 8 : b[o] << 8 | b[o + 1];

        private static uint U32(byte[] b, int o, bool little) => little
            ? (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24)
            : (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

        private static void WriteU32(byte[] b, int o, uint v, bool little)
        {
            if (little)
            {
                b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
            }
            else
            {
                b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
            }
        }
    }
}
