using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Memory;
using Ryujinx.HLE.HOS.Kernel.Process;
using System;
using System.Collections.Generic;
using System.Text;

namespace Ryujinx.HLE.HOS
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The glyphs the HOME Menu (22.5.0) has drawn: its font texture
    /// cache (nn::font::TextureCache) keeps each character drawn once in a texture. Read here to
    /// find where the button symbols are.
    /// </summary>
    public static class NextendoButtonGlyphs
    {

        /// <summary>The addresses of 8-byte values in the writable memory of a program.</summary>
        internal static List<ulong> Find(KProcess process, ulong value, int most = 16, bool everywhere = false)
        {
            return Find(process, BitConverter.GetBytes(value), 8, most, everywhere);
        }

        private static readonly Dictionary<ulong, List<ulong>> _caches = [];

        /// <summary>
        /// The font texture caches of a program, whatever the program (HOME Menu, overlay applet,
        /// applets): found from the name of their class in the program, then its type record,
        /// then its function table, then the objects that start with it. Looked for once.
        /// </summary>
        internal static List<ulong> Caches(KProcess process)
        {
            lock (_caches)
            {
                if (_caches.TryGetValue(process.Pid, out List<ulong> known) && known.Count > 0)
                {
                    return known;
                }
            }

            List<ulong> caches = [];

            foreach (ulong name in Find(process, Encoding.ASCII.GetBytes("N2nn4font12TextureCacheE\0"), 1, 4, true))
            {
                foreach (ulong inType in Find(process, name, 4, true))
                {
                    foreach (ulong inTable in Find(process, inType - 8, 4, true))
                    {
                        foreach (ulong cache in Find(process, inTable + 8))
                        {
                            if (!caches.Contains(cache))
                            {
                                caches.Add(cache);
                            }
                        }
                    }
                }
            }

            lock (_caches)
            {
                _caches[process.Pid] = caches;
            }

            return caches;
        }

        internal static List<ulong> Find(KProcess process, byte[] pattern, int alignment, int most = 16, bool everywhere = false)
        {
            const int Chunk = 0x400000;

            List<ulong> found = [];
            byte[] buffer = new byte[Chunk];
            ulong address = process.MemoryManager.AddrSpaceStart;

            while (address < process.MemoryManager.AddrSpaceEnd && found.Count < most)
            {
                KMemoryInfo info = process.MemoryManager.QueryMemory(address);
                ulong start = info.Address;
                ulong size = info.Size;
                bool data = info.State != MemoryState.Unmapped && info.State != MemoryState.Io &&
                    (info.Permission == KMemoryPermission.ReadAndWrite || (everywhere && (info.Permission & KMemoryPermission.Read) != 0));

                if (size == 0 || start + size <= address)
                {
                    break;
                }

                address = start + size;

                if (!data)
                {
                    continue;
                }

                try
                {
                    for (ulong at = start; at < start + size && found.Count < most; at += Chunk)
                    {
                        int count = (int)Math.Min((ulong)Chunk, start + size - at);
                        Span<byte> memory = buffer.AsSpan(0, count);

                        process.CpuMemory.Read(at, memory);

                        int from = 0;

                        while (from <= count - pattern.Length && memory[from..].IndexOf(pattern) is >= 0 and int hit)
                        {
                            from += hit;

                            if (((at + (ulong)from) % (ulong)alignment) == 0)
                            {
                                found.Add(at + (ulong)from);
                            }

                            from++;
                        }
                    }
                }
                catch (Exception)
                {
                    // memory given back meanwhile
                }
            }

            return found;
        }

        /// <summary>
        /// The button symbols the HOME Menu has already drawn, drawn again for another kind of
        /// controller, in place: the texts on screen change at once. A character of the cache is
        /// a record in the list of its line (0x100 lists of 0x18 bytes from 0x80, linked at 0x10
        /// of the record): code at 0x30, size at 0x36, place in the texture at 0x40/0x42, width
        /// and height at 0x44/0x46, left and baseline at 0x4E/0x50. The texture (one byte a
        /// pixel, lines from the bottom, pointer at 0x20, size at 0x28/0x2C) is the memory the
        /// graphics processor reads.
        /// </summary>
        public static void Redraw(Switch device, Services.Sdb.Pl.ControllerStyle before, Services.Sdb.Pl.ControllerStyle now)
        {
            byte[] oldFont = Services.Sdb.Pl.ControllerButtonFont.Variant(before);
            byte[] newFont = Services.Sdb.Pl.ControllerButtonFont.Variant(now);

            if (oldFont == null || newFont == null)
            {
                return;
            }

            using SkiaSharp.SKTypeface oldFace = SkiaSharp.SKTypeface.FromData(SkiaSharp.SKData.CreateCopy(oldFont));
            using SkiaSharp.SKTypeface newFace = SkiaSharp.SKTypeface.FromData(SkiaSharp.SKData.CreateCopy(newFont));

            if (oldFace == null || newFace == null)
            {
                return;
            }

            int drawn = 0;

            foreach (KProcess process in device.System.KernelContext.Processes.Values)
            {
                if (!Services.Am.AppletAE.RealAppletManager.IsSystemAppletProgram(process.TitleId))
                {
                    continue;
                }

                foreach (ulong cache in Caches(process))
                {
                    ulong bitmap = process.CpuMemory.Read<ulong>(cache + 0x20);
                    int width = process.CpuMemory.Read<int>(cache + 0x28);
                    int height = process.CpuMemory.Read<int>(cache + 0x2C);

                    if (bitmap == 0 || width <= 0 || height <= 0 || width > 0x4000 || height > 0x4000)
                    {
                        continue;
                    }

                    bool changed = false;

                    for (int line = 0; line < 0x100; line++)
                    {
                        ulong headAt = cache + 0x80 + (ulong)line * 0x18;
                        ulong link = process.CpuMemory.Read<ulong>(headAt + 8);

                        for (int guard = 0; link != headAt && link != 0 && guard < 4096; guard++)
                        {
                            byte[] node = new byte[0x58];

                            process.CpuMemory.Read(link - 0x10, node);
                            link = process.CpuMemory.Read<ulong>(link + 8);

                            uint code = BitConverter.ToUInt32(node, 0x30);
                            int size = BitConverter.ToUInt16(node, 0x36);
                            int x = BitConverter.ToUInt16(node, 0x40), y = BitConverter.ToUInt16(node, 0x42);
                            int w = BitConverter.ToUInt16(node, 0x44), h = BitConverter.ToUInt16(node, 0x46);
                            int left = BitConverter.ToInt16(node, 0x4E), baseline = BitConverter.ToInt16(node, 0x50);

                            if (code < 0xE000 || code > 0xF8FF || size == 0 || w == 0 || h == 0 || w > 512 || h > 512 || x + w + 2 > width || y + h > height)
                            {
                                continue;
                            }

                            string text = char.ConvertFromUtf32((int)code);
                            byte[] was = Draw(oldFace, text, size, w, h, -left, baseline);
                            byte[] becomes = Draw(newFace, text, size, w, h, -left, baseline);

                            // a symbol that is the same for both controllers stays as the console drew it
                            if (was.AsSpan().SequenceEqual(becomes))
                            {
                                continue;
                            }

                            // Where the console put it in its cell: the place where our drawing of the
                            // symbol it has matches best what is there.
                            byte[] there = new byte[w * h];

                            for (int row = 0; row < h; row++)
                            {
                                process.CpuMemory.Read(bitmap + (ulong)((height - 1 - (y + row)) * width + x + 1), there.AsSpan(row * w, w));
                            }

                            int bestX = 0, bestY = 0;
                            long best = long.MaxValue;

                            for (int dy = -3; dy <= 3; dy++)
                            {
                                for (int dx = -2; dx <= 2; dx++)
                                {
                                    byte[] guess = dx == 0 && dy == 0 ? was : Draw(oldFace, text, size, w, h, dx - left, baseline + dy);
                                    long difference = 0;

                                    for (int i = 0; i < guess.Length; i++)
                                    {
                                        difference += Math.Abs(guess[i] - there[i]);
                                    }

                                    if (difference < best)
                                    {
                                        best = difference;
                                        bestX = dx;
                                        bestY = dy;
                                    }
                                }
                            }

                            if (bestX != 0 || bestY != 0)
                            {
                                becomes = Draw(newFace, text, size, w, h, bestX - left, baseline + bestY);
                            }

                            for (int row = 0; row < h; row++)
                            {
                                process.CpuMemory.Write(bitmap + (ulong)((height - 1 - (y + row)) * width + x + 1), becomes.AsSpan(row * w, w));
                            }

                            drawn++;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        // The HOME Menu only sends this texture to the graphics processor when it
                        // draws a character itself: sent for it.
                        byte[] all = new byte[width * height];

                        process.CpuMemory.Read(bitmap, all);
                        device.Gpu.NextendoUploadTexture(process.Pid, width, height, all);
                    }
                }
            }

            Logger.Notice.Print(LogClass.Application, $"[Nextendo] button symbols: {now}, {drawn} drawn again");
        }

        // A character of a font, as the texture holds it: one byte a pixel, w by h.
        private static byte[] Draw(SkiaSharp.SKTypeface face, string text, int size, int w, int h, int x, int baseline)
        {
            using SkiaSharp.SKBitmap picture = new(new SkiaSharp.SKImageInfo(w, h, SkiaSharp.SKColorType.Alpha8));
            using SkiaSharp.SKCanvas canvas = new(picture);
            using SkiaSharp.SKPaint paint = new() { Typeface = face, TextSize = size, IsAntialias = true, Color = SkiaSharp.SKColors.White };

            canvas.Clear(SkiaSharp.SKColors.Transparent);
            canvas.DrawText(text, x, baseline, paint);
            canvas.Flush();

            byte[] pixels = new byte[w * h];
            ReadOnlySpan<byte> all = picture.GetPixelSpan();

            for (int row = 0; row < h; row++)
            {
                all.Slice(row * picture.RowBytes, w).CopyTo(pixels.AsSpan(row * w, w));
            }

            return pixels;
        }

    }
}
