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

        public static bool Enabled => Environment.GetEnvironmentVariable("NEXTENDO_BRANDING") != "0";

        public static IStorage Appliquer(ulong programId, IStorage baseStorage)
        {
            if (!Enabled || baseStorage == null || programId < 0x0100000000001000 || programId > 0x0100000000001FFF)
            {
                return baseStorage;
            }

            try
            {
                return Patch(programId, baseStorage);
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.ModLoader, $"[Nextendo] texts of {programId:X16} left as they are ({e.GetType().Name}: {e.Message})");

                return baseStorage;
            }
        }

        private static IStorage Patch(ulong programId, IStorage baseStorage)
        {
            RomFsFileSystem baseRom = new(baseStorage);
            Dictionary<string, byte[]> changed = new();
            int strings = 0;

            foreach (DirectoryEntryEx entry in baseRom.EnumerateEntries().Where(f => f.Type == DirectoryEntryType.File &&
                         f.FullPath.StartsWith("/message/", StringComparison.Ordinal) && f.FullPath.EndsWith(".msbt.szs", StringComparison.Ordinal)))
            {
                string language = LanguageOf(entry.FullPath);
                byte[] packed = Read(baseRom, entry.FullPath);
                byte[] msbt = Yaz0Decode(packed);
                byte[] patched = PatchMsbt(msbt, text => Rebrand(text, language), out int count);

                if (count > 0)
                {
                    changed[entry.FullPath] = Yaz0Encode(patched);
                    strings += count;
                }
            }

            if (changed.Count == 0)
            {
                return baseStorage;
            }

            RomFsBuilder builder = new();

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
            text = text.Replace("Nintendo Co., Ltd.", "Nextendo Network")
                       .Replace("Nintendo", "Nextendo")
                       .Replace("NINTENDO", "NEXTENDO")
                       .Replace("ニンテンドー", "ネクステンドー")
                       .Replace("任天堂", "Nextendo")
                       .Replace("닌텐도", "넥스텐도");

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
