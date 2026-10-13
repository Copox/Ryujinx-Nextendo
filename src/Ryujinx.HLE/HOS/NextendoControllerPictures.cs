using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Memory;
using Ryujinx.HLE.HOS.Kernel.Process;
using Ryujinx.HLE.HOS.Services.Sdb.Pl;
using System;
using System.Collections.Generic;

namespace Ryujinx.HLE.HOS
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The controller picture of the HOME Menu (the Pro Controller one
    /// of the system's ControllerIcon archive) follows the controller in use while the console
    /// runs: a PlayStation or Xbox controller plugged in shows as itself at once. The picture is
    /// a texture the HOME Menu keeps in its memory: it is rewritten there, and the renderer draws
    /// the new one.
    /// </summary>
    public static class NextendoControllerPictures
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<ControllerStyle, byte[]> _pictures = [];

        /// <summary>The Pro Controller's own picture, as the system has it.</summary>
        public static byte[] Original { get; private set; }

        public static void Keep(byte[] original)
        {
            Original ??= original;
        }

        private static byte[] Picture(ControllerStyle style)
        {
            if (style == ControllerStyle.Nintendo)
            {
                return Original;
            }

            if (!_pictures.TryGetValue(style, out byte[] picture))
            {
                string name = style switch
                {
                    ControllerStyle.PlayStation4 => "ps4",
                    ControllerStyle.PlayStation5 => "ps5",
                    _ => "xbox",
                };

                _pictures[style] = picture = Ryujinx.Common.EmbeddedResources.Read($"Ryujinx.HLE/HOS/NextendoAssets/ControllerIcon_{name}.bc4");
            }

            return picture;
        }

        /// <summary>The controller in use is of another kind: its picture, everywhere it shows.</summary>
        public static void Show(Switch device, ControllerStyle style)
        {
            lock (_lock)
            {
                if (ControllerButtonFont.Style == style)
                {
                    return;
                }

                ControllerStyle before = ControllerButtonFont.Style;

                ControllerButtonFont.Style = style;

                // The button symbols: the font the programs read, then the ones already drawn.
                if (device != null && !Ryujinx.Common.Configuration.NextendoHomeMenuVanilla.Enabled && ControllerButtonFont.Variant(style) is { } font)
                {
                    try
                    {
                        ControllerButtonFont.Rewrite?.Invoke(font);
                        NextendoButtonGlyphs.Redraw(device, before, style);
                    }
                    catch (Exception e)
                    {
                        Logger.Warning?.Print(LogClass.Application, $"[Nextendo] button symbols left as they are: {e.Message}");
                    }
                }

                if (device == null || Ryujinx.Common.Configuration.NextendoHomeMenuVanilla.Enabled || Original == null)
                {
                    return;
                }

                byte[] shown = Picture(style);
                List<byte[]> others = [];

                foreach (ControllerStyle other in Enum.GetValues<ControllerStyle>())
                {
                    if (other != style && Picture(other) is { } picture && !picture.AsSpan().SequenceEqual(shown))
                    {
                        others.Add(picture);
                    }
                }

                int written = 0;

                foreach (KProcess process in device.System.KernelContext.Processes.Values)
                {
                    if (Services.Am.AppletAE.RealAppletManager.IsSystemAppletProgram(process.TitleId))
                    {
                        written += Rewrite(process, others, shown);
                    }
                }

                Logger.Notice.Print(LogClass.Application, $"[Nextendo] controller picture: {style}, {written} texture(s) rewritten");
            }
        }

        /// <summary>
        /// The overlay applet (22.5.0) draws the "Joy-Con attached" effect on the sides of the
        /// screen only out of the dock: it holds it back while the console is docked. The check
        /// is taken out (both sides), for the effect to play when a controller is plugged in.
        /// </summary>
        internal static void AllowRailEffect(KProcess process, ulong imageBase)
        {
            // cmp w8, #1 ; b.ne (not docked: go on) -> b (go on), right side then left side
            (ulong At, uint Was, uint Becomes)[] checks =
            [
                (0x31E438, 0x54000181, 0x1400000C),
                (0x31E458, 0x54000121, 0x14000009),
            ];

            // not in the HOME Menu as the console has it
            if (Ryujinx.Common.Configuration.NextendoHomeMenuVanilla.Enabled)
            {
                return;
            }

            try
            {
                foreach ((ulong at, uint was, _) in checks)
                {
                    if (process.CpuMemory.Read<uint>(imageBase + at - 4) != 0x7100051F || process.CpuMemory.Read<uint>(imageBase + at) != was)
                    {
                        return;
                    }
                }

                foreach ((ulong at, _, uint becomes) in checks)
                {
                    process.CpuMemory.Write(imageBase + at, becomes);
                }

                Logger.Info?.Print(LogClass.Loader, "[Nextendo] overlay applet: \"Joy-Con attached\" effect allowed in the dock");
            }
            catch (Exception)
            {
                // another program, shorter than that: not the overlay applet
            }
        }

        private const int Window = 16;

        // Where the bytes of a picture vary the most.
        private static int Mark(byte[] picture)
        {
            int best = 0;
            int most = 0;

            for (int i = 0; i + Window <= picture.Length; i += 8)
            {
                int kinds = new HashSet<byte>(picture[i..(i + Window)]).Count;

                if (kinds > most)
                {
                    most = kinds;
                    best = i;
                }
            }

            return best;
        }

        // Every copy of the other pictures in the memory of a program becomes the one shown.
        private static int Rewrite(KProcess process, List<byte[]> others, byte[] shown)
        {
            const int Chunk = 0x400000;

            int written = 0;
            int length = shown.Length;
            byte[] buffer = new byte[Chunk + length];
            ulong address = process.MemoryManager.AddrSpaceStart;

            while (address < process.MemoryManager.AddrSpaceEnd)
            {
                KMemoryInfo info = process.MemoryManager.QueryMemory(address);
                ulong start = info.Address;
                ulong size = info.Size;
                bool data = info.State != MemoryState.Unmapped && info.State != MemoryState.Io && info.Permission == KMemoryPermission.ReadAndWrite;

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
                    for (ulong at = start; at < start + size; at += Chunk)
                    {
                        int count = (int)Math.Min((ulong)buffer.Length, start + size - at);

                        if (count < length)
                        {
                            break;
                        }

                        Span<byte> memory = buffer.AsSpan(0, count);

                        process.CpuMemory.Read(at, memory);

                        foreach (byte[] other in others)
                        {
                            // looked for by its most varied bytes (the first ones are blank)
                            int mark = Mark(other);
                            int from = 0;

                            while (from <= count - Window && memory[from..].IndexOf(other.AsSpan(mark, Window)) is >= 0 and int found)
                            {
                                from += found;

                                int first = from - mark;

                                // a copy that starts in this chunk (the next one reads the rest over)
                                if (first >= 0 && first < Chunk && first + length <= count && memory.Slice(first, length).SequenceEqual(other))
                                {
                                    process.CpuMemory.Write(at + (ulong)first, shown);
                                    written++;
                                }

                                from++;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // memory given back meanwhile: nothing of ours there
                }
            }

            return written;
        }
    }
}
