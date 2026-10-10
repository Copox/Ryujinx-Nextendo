using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Process;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Ryujinx.HLE.HOS
{
    /// <summary>
    /// [Nextendo, HOME Menu work] Development: reads objects of a system program while it runs.
    /// NEXTENDO_MEMPROBE="titleId:vtableOffset:fromHex-toHex" finds, every 3 seconds, the objects
    /// of that program whose first word is the address of the vtable (offset in one of its
    /// images), and logs their words from..to when they change.
    /// </summary>
    static class NextendoMemProbe
    {
        private static readonly ConcurrentDictionary<KProcess, ulong[]> _images = new();
        private static Thread _thread;

        public static void Register(KProcess process, ulong[] bases)
        {
            string config = Environment.GetEnvironmentVariable("NEXTENDO_MEMPROBE");

            if (config == null)
            {
                return;
            }

            _images[process] = bases;

            Logger.Notice.Print(LogClass.Application, $"[memprobe] process {process.Pid} title {process.TitleId:X16} images {string.Join(",", Array.ConvertAll(bases, b => b.ToString("X")))}");

            if (_thread == null)
            {
                _thread = new Thread(() => Run(config)) { IsBackground = true, Name = "Nextendo.MemProbe" };
                _thread.Start();
            }
        }

        private static void Run(string config)
        {
            string[] parts = config.Split(':');
            ulong titleId = Convert.ToUInt64(parts[0], 16);
            ulong[] vtables = Array.ConvertAll(parts[1].Split(','), v => Convert.ToUInt64(v, 16));
            // "from..to" (hex, signed: "-B38..-B00"), several separated by "/"; "a-b" still works.
            static int Hex(string v) => v.StartsWith('-') ? -Convert.ToInt32(v[1..], 16) : Convert.ToInt32(v, 16);
            (int From, int To)[] ranges = Array.ConvertAll(parts[2].Split('/'), r => r.Contains("..")
                ? (Hex(r[..r.IndexOf("..")]), Hex(r[(r.IndexOf("..") + 2)..]))
                : (Hex(r.Split('-')[0]), Hex(r.Split('-')[1])));
            Dictionary<ulong, string> last = new();
            int round = 0;

            while (true)
            {
                round++;

                Thread.Sleep(int.TryParse(Environment.GetEnvironmentVariable("NEXTENDO_MEMPROBE_MS"), out int ms) ? ms : 3000);

                foreach ((KProcess process, ulong[] bases) in _images)
                {
                    if (process.TitleId != titleId || process.State == ProcessState.Exited)
                    {
                        continue;
                    }

                    try
                    {
                        if (!last.ContainsKey(0))
                        {
                            last[0] = "";
                            byte[] probe = new byte[32];
                            process.CpuMemory.Read(bases[0] + vtables[0], probe);
                            Logger.Notice.Print(LogClass.Application, $"[memprobe] at image+0x{vtables[0]:X}: {Convert.ToHexString(probe)} {System.Text.Encoding.ASCII.GetString(probe)}");
                        }

                        if (round % 30 == 1)
                        {
                            Scan(process, bases, vtables, ranges, last);
                        }
                        else
                        {
                            // Between two searches, the objects already found are read again.
                            foreach (ulong address in new List<ulong>(last.Keys))
                            {
                                if (address == 0)
                                {
                                    continue;
                                }

                                string text = Describe(process, address, ranges);

                                if (last[address] != text)
                                {
                                    last[address] = text;
                                    Logger.Notice.Print(LogClass.Application, $"[memprobe] object 0x{address:X}:{text}");
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Warning?.Print(LogClass.Application, $"[memprobe] {e.Message}");
                    }
                }
            }
        }

        private static string Describe(KProcess process, ulong address, (int From, int To)[] ranges)
        {
            System.Text.StringBuilder sb = new();

            foreach ((int from, int to) in ranges)
            {
                byte[] words = new byte[to - from];

                process.CpuMemory.Read((ulong)((long)address + from), words);

                for (int w = 0; w + 8 <= words.Length; w += 8)
                {
                    sb.Append($" {(from + w < 0 ? "-" : "+")}{Math.Abs(from + w):X}={BitConverter.ToUInt64(words, w):X}");
                }
            }

            return sb.ToString();
        }

        private static void Scan(KProcess process, ulong[] bases, ulong[] vtables, (int From, int To)[] ranges, Dictionary<ulong, string> last)
        {
            ulong start = process.MemoryManager.HeapRegionStart;
            ulong size = process.MemoryManager.GetTotalHeapSize();
            HashSet<ulong> wanted = new();

            foreach (ulong b in bases)
            {
                foreach (ulong vtable in vtables)
                {
                    wanted.Add(b + vtable);
                }
            }

            byte[] chunk = new byte[0x1000];
            List<(ulong Start, ulong End)> regions = [(start, start + size)];

            foreach (ulong b in bases)
            {
                regions.Add((b, b + 0x2000000)); // the images and their data
            }

            foreach ((ulong regionStart, ulong regionEnd) in regions)
            for (ulong at = regionStart; at < regionEnd; at += (ulong)chunk.Length)
            {
                int length = (int)Math.Min((ulong)chunk.Length, regionEnd - at);

                if (!process.CpuMemory.IsRangeMapped(at, (ulong)length))
                {
                    continue;
                }

                process.CpuMemory.Read(at, chunk.AsSpan(0, length));

                for (int i = 0; i + 8 <= length; i += 8)
                {
                    if (!wanted.Contains(BitConverter.ToUInt64(chunk, i)))
                    {
                        continue;
                    }

                    ulong address = at + (ulong)i;
                    System.Text.StringBuilder sb = new();

                    foreach ((int from, int to) in ranges)
                    {
                        byte[] words = new byte[to - from];

                        process.CpuMemory.Read((ulong)((long)address + from), words);

                        for (int w = 0; w + 8 <= words.Length; w += 8)
                        {
                            sb.Append($" {(from + w < 0 ? "-" : "+")}{Math.Abs(from + w):X}={BitConverter.ToUInt64(words, w):X}");
                        }
                    }

                    string text = sb.ToString();

                    if (!last.TryGetValue(address, out string previous) || previous != text)
                    {
                        last[address] = text;

                        Logger.Notice.Print(LogClass.Application, $"[memprobe] object 0x{address:X} vtable 0x{BitConverter.ToUInt64(chunk, i):X}:{sb}");
                    }
                }
            }
        }
    }
}
