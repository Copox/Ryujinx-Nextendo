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

            // The page's own code, changed to know the added entries (Qlaunch/build.py).
            byte[] patches = Ryujinx.Common.EmbeddedResources.Read("Ryujinx.HLE/HOS/Themes/Qlaunch/theme_page.patches");
            int count = BinaryPrimitives.ReadInt32LittleEndian(patches);

            for (int i = 0, at = 4; i < count; i++)
            {
                uint address = BinaryPrimitives.ReadUInt32LittleEndian(patches.AsSpan(at));
                int length = BinaryPrimitives.ReadInt32LittleEndian(patches.AsSpan(at + 4));

                process.CpuMemory.Write(baseAddress + address, patches.AsSpan(at + 8, length));
                at += 8 + length;
            }

            _process = process;
            _base = baseAddress;

            Logger.Info?.Print(LogClass.Loader, $"[theme] HOME Menu themes page: {code.Length} bytes of code added");
        }

        private static readonly Dictionary<KProcess, ulong> _pending = [];

        private static KProcess _process;
        private static ulong _base;
        private static List<string> _packs = [];

        /// <summary>
        /// The HOME Menu has set its colour set: an entry of its themes page was picked. A theme
        /// different from the one in use is kept, and the HOME Menu starts again with it (its
        /// layouts are made when it starts). True when that happens.
        /// </summary>
        public static bool TakePick()
        {
            KProcess process = _process;

            if (process == null)
            {
                return false;
            }

            ulong at = _base + Cave + CodeSize + 0x30;
            int picked;

            try
            {
                picked = process.CpuMemory.Read<int>(at);
                process.CpuMemory.Write(at, -2);
            }
            catch (Exception)
            {
                return false;
            }

            if (picked < -1 || picked >= _packs.Count)
            {
                return false;
            }

            string theme = picked < 0 ? null : _packs[picked];

            if (theme == NextendoThemes.Chosen)
            {
                return false;
            }

            NextendoThemes.Chosen = theme;

            Logger.Info?.Print(LogClass.ServiceSet, $"[theme] HOME Menu theme: {theme ?? "the system's"}, the HOME Menu starts again");

            HomeMenuStatus.RequestPower(true);

            return true;
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

            _packs = packs;

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
