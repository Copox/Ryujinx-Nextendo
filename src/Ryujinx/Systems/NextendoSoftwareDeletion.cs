using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Ryujinx.Ava.Systems
{
    /// <summary>
    /// [Nextendo, HOME Menu work] "Delete Software" of the HOME Menu: the files of the game (the
    /// game, its update, its add-on content) leave the computer. On Windows they go to the
    /// recycle bin, so a wrong choice can still be undone.
    /// </summary>
    internal static class NextendoSoftwareDeletion
    {
        public static bool Delete(Ryujinx.HLE.HOS.HomeMenuTitle title)
        {
            List<string> files = new[] { title.Path, title.UpdatePath }
                .Concat(title.DlcPaths ?? [])
                .Where(path => !string.IsNullOrEmpty(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                return false;
            }

            bool all = true;

            foreach (string file in files)
            {
                bool done = OperatingSystem.IsWindows() ? ToRecycleBin(file) : TryDelete(file);

                all &= done;

                Logger.Info?.Print(LogClass.Application, $"[home] {title.Name}: {file} {(done ? (OperatingSystem.IsWindows() ? "sent to the recycle bin" : "deleted") : "NOT deleted")}");
            }

            return all;
        }

        private static bool TryDelete(string file)
        {
            try
            {
                File.Delete(file);

                return !File.Exists(file);
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.Application, $"[home] {file}: {e.Message}");

                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShFileOpStruct
        {
            public IntPtr Hwnd;
            public uint Func;
            public string From;
            public string To;
            public ushort Flags;
            public int AnyOperationsAborted;
            public IntPtr NameMappings;
            public string ProgressTitle;
        }

        private const uint FoDelete = 3;
        private const ushort FofSilent = 0x0004, FofNoConfirmation = 0x0010, FofAllowUndo = 0x0040, FofNoErrorUi = 0x0400;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref ShFileOpStruct op);

        private static bool ToRecycleBin(string file)
        {
            ShFileOpStruct op = new()
            {
                Func = FoDelete,
                From = Path.GetFullPath(file) + "\0\0",
                Flags = (ushort)(FofSilent | FofNoConfirmation | FofAllowUndo | FofNoErrorUi),
            };

            return SHFileOperation(ref op) == 0 && op.AnyOperationsAborted == 0 && !File.Exists(file);
        }
    }
}
