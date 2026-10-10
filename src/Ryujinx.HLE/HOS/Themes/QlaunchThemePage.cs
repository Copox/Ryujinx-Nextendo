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

        private const int MaxEntries = 16;

        // "ThemezerNX\Purple Skies ANIMATED by Hsushi (186B6)" -> "Purple Skies ANIMATED by Hsushi".
        private static string DisplayName(string pack)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(pack.TrimEnd('\\', '/'));
            int id = name.LastIndexOf(" (", StringComparison.Ordinal);

            return id > 0 && name.EndsWith(')') ? name[..id] : name;
        }

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

            // The page's methods to ours (entry points at the start of the code, 4 bytes each). The
            // slots hold offsets the program relocates itself (the base is added to them).
            uint[] slots = [BuildSlot, BuildSlot + 0x08, BuildSlot + 0x40, BuildSlot + 0x48]; // build, refresh, deactivate, destroy

            for (int i = 0; i < slots.Length; i++)
            {
                byte[] slot = new byte[8];

                BinaryPrimitives.WriteUInt64LittleEndian(slot, Cave + (uint)(4 * i));
                process.CpuMemory.Write(baseAddress + slots[i], slot);
            }

            // The page's buttons: room for the added entries (the page reserves 2).
            WriteCode(process, baseAddress + 0x98CEE0, 0x52800241); // mov w1, #18

            Logger.Info?.Print(LogClass.Loader, $"[theme] HOME Menu themes page: {code.Length} bytes of code added");
        }

        private static readonly Dictionary<KProcess, ulong> _pending = [];

        private static void WriteCode(KProcess process, ulong address, uint instruction)
        {
            byte[] bytes = new byte[4];

            BinaryPrimitives.WriteUInt32LittleEndian(bytes, instruction);
            process.CpuMemory.Write(address, bytes);
        }

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

            int count = Math.Min(packs.Count, MaxEntries);

            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), count);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), packs.IndexOf(NextendoThemes.Chosen ?? "") is int chosen and < MaxEntries ? chosen : -1);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x30), -2); // nothing picked yet

            for (int i = 0; i < count; i++)
            {
                byte[] name = Encoding.Unicode.GetBytes(DisplayName(packs[i]));

                name.AsSpan(0, Math.Min(name.Length, 0xFE)).CopyTo(data.AsSpan(0x400 + 0x100 * i));
            }

            Logger.Info?.Print(LogClass.Loader, $"[theme] themes for the HOME Menu page: {packs.Count} ({string.Join(", ", packs)})");
        }
    }
}
