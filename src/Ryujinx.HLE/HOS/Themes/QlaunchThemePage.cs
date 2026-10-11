using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Memory;
using Ryujinx.HLE.HOS.Kernel.Process;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>Where the picture of an added entry is in the HOME Menu's files (as it names its own).</summary>
        public static string PicturePath(int entry) => $"theme/NxThemeIcon_{entry:D2}.jpg";

        // The pictures, one place per entry, of a fixed size (a JPEG followed by zeros): their
        // bytes change when the themes do, the HOME Menu's files stay as they are.
        private const int PictureSize = 0x8000;
        private static readonly byte[][] _pictures = new byte[MaxEntries][];

        /// <summary>The places of the pictures of the added entries, to add to the HOME Menu's files.</summary>
        public static IEnumerable<(string Path, byte[] Data)> Pictures()
        {
            for (int i = 0; i < MaxEntries; i++)
            {
                _pictures[i] ??= new byte[PictureSize];

                yield return ("/" + PicturePath(i), _pictures[i]);
            }
        }

        /// <summary>The debug string the added code sends when the page is built.</summary>
        public const string RefreshSignal = "NxThemes";

        // The pages of a theme, shown in place of the themes (X on a theme): its parts, which of
        // them are applied, and the entry whose deletion waits for a second press.
        private static string _menuPack;
        private static List<(string Target, string File)> _menuParts = [];
        private static bool[] _menuOn = [];
        private static int _confirm = -1;
        private static bool _french = true;

        private static readonly Dictionary<string, (string Fr, string En)> _targetNames = new()
        {
            ["home"] = ("Menu HOME", "HOME Menu"),
            ["lock"] = ("Écran de verrouillage", "Lock screen"),
            ["apps"] = ("Tous les logiciels", "All software"),
            ["set"] = ("Paramètres de la console", "System Settings"),
            ["news"] = ("Actualités", "News"),
            ["user"] = ("Page utilisateur", "User page"),
            ["psl"] = ("Choix de l'utilisateur", "User selection"),
        };

        /// <summary>The language of the system, for the texts of the pages of a theme.</summary>
        public static void SetLanguage(SystemState.SystemLanguage language) =>
            _french = language is SystemState.SystemLanguage.French or SystemState.SystemLanguage.CanadianFrench;

        private static string Text(string fr, string en) => _french ? fr : en;

        private static string TargetName(string target) =>
            _targetNames.TryGetValue(target ?? "", out var name) ? Text(name.Fr, name.En) : target;

        // The entries of the pages of a theme after its parts (added entry numbers).
        private static int DeleteOffRow => _menuParts.Count;
        private static int DeleteAllRow => _menuParts.Count + 1;

        /// <summary>
        /// The themes page is being built: the themes, their names and pictures as they are now
        /// (the parts the code uses for the entries it made are left as they are). Built by the
        /// system (not rebuilt for the pages of a theme): the themes again.
        /// </summary>
        internal static bool Refresh(KProcess process)
        {
            if (process != _process)
            {
                return false;
            }

            // Built by the system: the themes.
            _menuPack = null;
            _confirm = -1;

            try
            {
                byte[] data = new byte[DataSize];

                WriteData(data);

                ulong at = _base + Cave + CodeSize;

                process.CpuMemory.Write(at, data.AsSpan(0, 8));
                process.CpuMemory.Write(at + 0x3DC, data.AsSpan(0x3DC, 4));
                process.CpuMemory.Write(at + 0x3E4, data.AsSpan(0x3E4, 4));
                process.CpuMemory.Write(at + 0x400, data.AsSpan(0x400, 0x1000));
                process.CpuMemory.Write(at + 0x1800, data.AsSpan(0x1800, 0x20 * MaxEntries));
                process.CpuMemory.Write(at + 0x2400, data.AsSpan(0x2400, 0x200));
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.Loader, $"[theme] themes page not refreshed: {e.Message}");
            }

            return true;
        }

        private static bool _xDown;

        /// <summary>
        /// The pad's X button, as the emulator reads it: pressed on a theme of the page (the
        /// entry whose key node, button+0x828, is focused: flags +0x148 bits 0 and 1), its pages
        /// are shown in place of the themes.
        /// </summary>
        public static void OnPadX(bool down)
        {
            bool pressed = down && !_xDown;

            _xDown = down;

            KProcess process = _process;

            if (!pressed || process == null || _menuPack != null)
            {
                return;
            }

            try
            {
                ulong data = _base + Cave + CodeSize;

                if (process.CpuMemory.Read<ulong>(data + 0x38) == 0)
                {
                    return; // the page is not built
                }

                int count = Math.Min(process.CpuMemory.Read<int>(data), MaxEntries);

                for (int i = 0; i < count; i++)
                {
                    ulong button = process.CpuMemory.Read<ulong>(data + 0x100 + 0x20 * (ulong)i + 0x10);

                    if (Focused(process, button))
                    {
                        OpenMenu(process, i);

                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.ServiceSet, $"[theme] X on the themes page: {e.Message}");
            }
        }

        private static bool _bDown;
        private static bool _bSwallowed;

        /// <summary>
        /// The pad's B button, as the emulator reads it: in the pages of a theme (focus on the
        /// page), back to the themes; the HOME Menu does not get it (it would leave the page).
        /// True while it is kept from the HOME Menu.
        /// </summary>
        public static bool OnPadB(bool down)
        {
            if (_bSwallowed)
            {
                _bSwallowed = down;

                return down;
            }

            bool pressed = down && !_bDown;

            _bDown = down;

            KProcess process = _process;

            if (!pressed || process == null || _menuPack == null)
            {
                return false;
            }

            try
            {
                if (!PageFocused(process))
                {
                    return false;
                }

                BackToThemes(process);
                _bSwallowed = true;

                return true;
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.ServiceSet, $"[theme] B on the themes page: {e.Message}");

                return false;
            }
        }

        // The focus on an entry of the page (its key node, button+0x828: flags +0x148 bits 0 and 1).
        private static bool Focused(KProcess process, ulong button) =>
            button != 0 && (process.CpuMemory.Read<byte>(button + 0x828 + 0x148) & 3) == 3;

        private static bool PageFocused(KProcess process)
        {
            ulong data = _base + Cave + CodeSize;
            ulong page = process.CpuMemory.Read<ulong>(data + 0x38);

            if (page == 0)
            {
                return false;
            }

            if (Focused(process, process.CpuMemory.Read<ulong>(page + 0xCD0)) || Focused(process, process.CpuMemory.Read<ulong>(page + 0xCF0)))
            {
                return true;
            }

            int count = Math.Min(process.CpuMemory.Read<int>(data + 0x3EC), MaxEntries);

            for (int i = 0; i < count; i++)
            {
                if (Focused(process, process.CpuMemory.Read<ulong>(data + 0x100 + 0x20 * (ulong)i + 0x10)))
                {
                    return true;
                }
            }

            return false;
        }

        // From the pages of a theme back to the themes, the focus on that theme.
        private static void BackToThemes(KProcess process)
        {
            int index = _packs.IndexOf(_menuPack);

            _menuPack = null;
            _confirm = -1;
            Rebuild(process, index >= 0 ? index + 2 : 0);
        }

        // The pages of a theme shown in place of the themes.
        private static void OpenMenu(KProcess process, int entry)
        {
            if (entry < 0 || entry >= _packs.Count)
            {
                return;
            }

            _menuPack = _packs[entry];
            _menuParts = NextendoThemes.PartsOf(_menuPack);

            HashSet<string> off = _menuPack == NextendoThemes.Chosen ? NextendoThemes.ExcludedTargets : [];

            _menuOn = _menuParts.Select(p => !off.Contains(p.Target)).ToArray();
            _confirm = -1;

            Logger.Info?.Print(LogClass.ServiceSet, $"[theme] pages of {_menuPack}: {string.Join(", ", _menuParts.Select(p => p.Target))}");

            Rebuild(process, 2);
        }

        // The page shown again (by the added code, once its keys are handled) with the entries as
        // they are now: the themes, or the pages of one.
        private static void Rebuild(KProcess process, int focus)
        {
            ulong at = _base + Cave + CodeSize;
            byte[] data = new byte[DataSize];

            WriteData(data);

            process.CpuMemory.Write(at, data.AsSpan(0, 8));
            process.CpuMemory.Write(at + 0x3DC, data.AsSpan(0x3DC, 4));
            process.CpuMemory.Write(at + 0x400, data.AsSpan(0x400, 0x1000));
            process.CpuMemory.Write(at + 0x2400, data.AsSpan(0x2400, 0x200));
            process.CpuMemory.Write(at + 0x3E8, focus);
            process.CpuMemory.Write(at + 0x3D4, 1);
        }

        // An entry of the pages of a theme picked (0: apply, 1: back, then the parts and the deletions).
        private static void TakeMenuPick(KProcess process, int row)
        {
            int part = row - 2;

            if (row == 0)
            {
                string pack = _menuPack;
                HashSet<string> off = _menuParts.Where((p, i) => !_menuOn[i]).Select(p => p.Target).ToHashSet();

                _menuPack = null;

                if (_menuOn.Any(on => on))
                {
                    NextendoThemes.SetChoice(pack, off);
                    Logger.Info?.Print(LogClass.ServiceSet, $"[theme] HOME Menu theme: {pack} (pages left out: {string.Join(", ", off)}), the HOME Menu starts again");
                }
                else
                {
                    NextendoThemes.Chosen = null;
                    Logger.Info?.Print(LogClass.ServiceSet, "[theme] HOME Menu theme: no page kept, the system's, the HOME Menu starts again");
                }

                HomeMenuStatus.RequestPower(true);

                return;
            }

            if (row == 1)
            {
                BackToThemes(process);

                return;
            }

            if (part >= 0 && part < _menuParts.Count)
            {
                _menuOn[part] = !_menuOn[part];
                _confirm = -1;
                Rebuild(process, row);

                return;
            }

            if (part == DeleteOffRow || part == DeleteAllRow)
            {
                if (_confirm != part)
                {
                    _confirm = part;
                    Rebuild(process, row);

                    return;
                }

                _confirm = -1;

                bool all = part == DeleteAllRow || _menuOn.All(on => !on);
                List<string> files = _menuParts.Where((p, i) => all || !_menuOn[i]).Select(p => p.File).ToList();
                string pack = _menuPack;
                bool inUse = pack == NextendoThemes.Current;

                NextendoThemes.Delete(pack, files);

                Logger.Info?.Print(LogClass.ServiceSet, $"[theme] {pack}: {files.Count} page(s) deleted");

                _menuParts = NextendoThemes.PartsOf(pack);

                if (all || _menuParts.Count == 0)
                {
                    _menuPack = null;

                    if (inUse)
                    {
                        NextendoThemes.Chosen = null;
                        HomeMenuStatus.RequestPower(true);

                        return;
                    }

                    Rebuild(process, 0);

                    return;
                }

                _menuOn = _menuParts.Select(_ => true).ToArray();

                if (inUse)
                {
                    HomeMenuStatus.RequestPower(true);

                    return;
                }

                Rebuild(process, 2);
            }
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

            if (_menuPack != null)
            {
                try
                {
                    TakeMenuPick(process, process.CpuMemory.Read<int>(_base + Cave + CodeSize + 0x3D8));
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ServiceSet, $"[theme] pages of a theme: {e.Message}");
                }

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

            if (theme == NextendoThemes.Current)
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

            if (_menuPack != null)
            {
                WriteMenu(data);

                return;
            }

            int count = Math.Min(packs.Count, MaxEntries);

            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), count);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), packs.IndexOf(NextendoThemes.Current ?? "") is int chosen and < MaxEntries ? chosen : -1);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x30), -2); // nothing picked yet
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3E4), EntriesToMake(count));
            Encoding.ASCII.GetBytes(RefreshSignal).CopyTo(data.AsSpan(0x3F0));
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3E8), -1); // no entry to focus

            for (int i = 0; i < count; i++)
            {
                byte[] name = Encoding.Unicode.GetBytes(DisplayName(packs[i]));

                name.AsSpan(0, Math.Min(name.Length, 0xFE)).CopyTo(data.AsSpan(0x400 + 0x100 * i));

                byte[] picture = NextendoThemes.Picture(packs[i]);

                _pictures[i] ??= new byte[PictureSize];

                lock (_pictures[i])
                {
                    Array.Clear(_pictures[i]);

                    if (picture != null && picture.Length <= PictureSize)
                    {
                        picture.CopyTo(_pictures[i], 0);
                        Encoding.ASCII.GetBytes(PicturePath(i)).CopyTo(data.AsSpan(0x1800 + 0x20 * i));
                    }
                }
            }

            Logger.Info?.Print(LogClass.Loader, $"[theme] themes for the HOME Menu page: {packs.Count} ({string.Join(", ", packs)})");
        }

        // The entries made with the page: one per theme, and enough for the pages of any theme
        // (7 parts and the 2 deletions) and one never shown, checked while they are; those not
        // needed are not shown.
        private static int EntriesToMake(int themes) => Math.Min(Math.Max(themes, 10), MaxEntries);

        private static void WriteName(byte[] data, int at, string name)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(name);

            bytes.AsSpan(0, Math.Min(bytes.Length, 0xFE)).CopyTo(data.AsSpan(at));
        }

        // The pages of a theme: entry 0 applies them, 1 goes back, then the parts (A: applied or
        // not) and the deletions (a second press confirms).
        private static void WriteMenu(byte[] data)
        {
            int count = Math.Min(_menuParts.Count + 2, MaxEntries);

            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), count);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), -1);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x30), -2);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3DC), 1);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3E4), EntriesToMake(Math.Min(_packs.Count, MaxEntries)));
            Encoding.ASCII.GetBytes(RefreshSignal).CopyTo(data.AsSpan(0x3F0));
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3E8), -1);

            WriteName(data, 0x2400, Text("Appliquer ce thème", "Apply this theme"));
            WriteName(data, 0x2500, Text("Retour aux thèmes", "Back to themes"));

            for (int i = 0; i < _menuParts.Count && i < count; i++)
            {
                string state = _menuOn[i] ? Text("oui", "on") : Text("non", "off");

                WriteName(data, 0x400 + 0x100 * i, $"{TargetName(_menuParts[i].Target)} : {state}");
            }

            if (DeleteOffRow < count)
            {
                WriteName(data, 0x400 + 0x100 * DeleteOffRow, _confirm == DeleteOffRow
                    ? Text("Confirmer la suppression", "Confirm deletion")
                    : Text("Supprimer les pages « non »", "Delete the pages set off"));
            }

            if (DeleteAllRow < count)
            {
                WriteName(data, 0x400 + 0x100 * DeleteAllRow, _confirm == DeleteAllRow
                    ? Text("Confirmer la suppression", "Confirm deletion")
                    : Text("Supprimer tout le thème", "Delete the whole theme"));
            }

            for (int i = 0; i < MaxEntries; i++)
            {
                _pictures[i] ??= new byte[PictureSize];

                lock (_pictures[i])
                {
                    Array.Clear(_pictures[i]);
                }
            }
        }
    }
}
