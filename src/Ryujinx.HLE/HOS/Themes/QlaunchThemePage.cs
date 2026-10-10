using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Memory;
using Ryujinx.HLE.HOS.Kernel.Process;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Ryujinx.HLE.HOS.Themes
{
    /// <summary>
    /// [Nextendo, HOME Menu themes] The themes installed on the SD card listed in "System Settings >
    /// Themes" of the HOME Menu, after Basic White and Basic Black. The HOME Menu (qlaunch 22.5.0)
    /// only builds those two entries; code added after its image (Qlaunch/theme_page.s, assembled
    /// by Qlaunch/build.py) builds one more per theme. The emulator puts that code there when the
    /// program is loaded, with a data page holding the themes, and points the page at it.
    /// </summary>
    public static class QlaunchThemePage
    {
        private const string BuildId = "26CC3BC5AC534AE990B410769736EE81B919C999";

        public const uint Cave = 0x425F000;
        public const uint CodeSize = 0x8000;
        public const uint DataSize = 0x8000;
        public const uint Size = CodeSize + DataSize;

        // The page's "build" method in its vtable (relocated at start: the base is added to it).
        private const uint BuildSlot = 0x10D4020;

        /// <summary>Room to add after the image of that program (0 when nothing is added).</summary>
        public static uint ExtraSize(string buildId, uint imageSize)
        {
            if (!Enabled(buildId))
            {
                return 0;
            }

            if (imageSize != Cave)
            {
                Logger.Warning?.Print(LogClass.Loader, $"[theme] HOME Menu image of 0x{imageSize:X} bytes, 0x{Cave:X} expected: themes page left as it is");

                return 0;
            }

            return Size;
        }

        private static bool Enabled(string buildId) =>
            buildId != null && buildId.StartsWith(BuildId, StringComparison.OrdinalIgnoreCase) &&
            Environment.GetEnvironmentVariable("NEXTENDO_THEME_PAGE") != "0";

        /// <summary>Puts the code and the data after the image, and points the page at them.</summary>
        internal static void Apply(KProcess process, ulong baseAddress)
        {
            byte[] code = Ryujinx.Common.EmbeddedResources.Read("Ryujinx.HLE/HOS/Themes/Qlaunch/theme_page.bin");
            byte[] data = new byte[DataSize];

            WriteData(data);

            process.CpuMemory.Write(baseAddress + Cave, code);
            process.CpuMemory.Write(baseAddress + Cave + CodeSize, data);

            // Not executable yet: at start the program takes every executable region for a module
            // of its own (and stops on one that is not). It becomes so once the program runs.
            // Read only, a block of its own (next to the program's writable data it would be taken
            // for a part of it).
            process.MemoryManager.SetProcessMemoryPermission(baseAddress + Cave, CodeSize, KMemoryPermission.Read);
            process.MemoryManager.SetProcessMemoryPermission(baseAddress + Cave + CodeSize, DataSize, KMemoryPermission.ReadAndWrite);

            lock (_pending)
            {
                _pending[process] = baseAddress;
            }

            // The slot holds an offset the program relocates itself (the base is added to it).
            byte[] slot = new byte[8];

            BinaryPrimitives.WriteUInt64LittleEndian(slot, Cave + 0); // entry 0: build
            process.CpuMemory.Write(baseAddress + BuildSlot, slot);

            Logger.Info?.Print(LogClass.Loader, $"[theme] HOME Menu themes page: {code.Length} bytes of code added");
        }

        private static readonly Dictionary<KProcess, ulong> _pending = [];

        /// <summary>The program has started (its modules are known): the added code may run.</summary>
        internal static void Activate(KProcess process)
        {
            ulong baseAddress;

            lock (_pending)
            {
                if (!_pending.Remove(process, out baseAddress))
                {
                    return;
                }
            }

            process.MemoryManager.SetProcessMemoryPermission(baseAddress + Cave, CodeSize, KMemoryPermission.ReadAndExecute);

            Logger.Info?.Print(LogClass.Loader, "[theme] HOME Menu themes page: code active");
        }

        // The data page: what the code reads (see theme_page.s).
        private static void WriteData(byte[] data)
        {
            List<string> packs = NextendoThemes.Packs();

            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), packs.Count);
        }
    }
}
