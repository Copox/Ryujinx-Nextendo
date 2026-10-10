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
            int from = Convert.ToInt32(parts[2].Split('-')[0], 16);
            int to = Convert.ToInt32(parts[2].Split('-')[1], 16);
            Dictionary<ulong, string> last = new();

            while (true)
            {
                Thread.Sleep(3000);

                foreach ((KProcess process, ulong[] bases) in _images)
                {
                    if (process.TitleId != titleId || process.State == ProcessState.Exited)
                    {
                        continue;
                    }

                    try
                    {
                        Scan(process, bases, vtables, from, to, last);
                    }
                    catch (Exception e)
                    {
                        Logger.Warning?.Print(LogClass.Application, $"[memprobe] {e.Message}");
                    }
                }
            }
        }

        private static void Scan(KProcess process, ulong[] bases, ulong[] vtables, int from, int to, Dictionary<ulong, string> last)
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

            byte[] chunk = new byte[0x100000];

            for (ulong at = start; at < start + size; at += (ulong)chunk.Length)
            {
                int length = (int)Math.Min((ulong)chunk.Length, start + size - at);

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
                    byte[] words = new byte[to - from];

                    process.CpuMemory.Read(address + (ulong)from, words);

                    string text = Convert.ToHexString(words);

                    if (!last.TryGetValue(address, out string previous) || previous != text)
                    {
                        last[address] = text;

                        System.Text.StringBuilder sb = new();

                        for (int w = 0; w + 8 <= words.Length; w += 8)
                        {
                            sb.Append($" +{from + w:X}={BitConverter.ToUInt64(words, w):X}");
                        }

                        Logger.Notice.Print(LogClass.Application, $"[memprobe] object 0x{address:X} vtable 0x{BitConverter.ToUInt64(chunk, i):X}:{sb}");
                    }
                }
            }
        }
    }
}
