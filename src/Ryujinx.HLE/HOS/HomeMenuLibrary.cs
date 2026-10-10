using System;
using System.Collections.Generic;

namespace Ryujinx.HLE.HOS
{
    // One game of the emulator's library, as the HOME Menu gets to see it.
    public class HomeMenuTitle
    {
        public ulong Id;
        public string Name;
        public string Path;

        // The 256x256 JPEG and the 0x4000 bytes control property of the title, when known.
        public byte[] Icon;
        public byte[] Nacp;

        // The update selected and the add-on content enabled for the title in the emulator.
        public string UpdatePath;
        public string[] DlcPaths = [];

        private static long SizeOf(string path)
        {
            try
            {
                return !string.IsNullOrEmpty(path) && System.IO.File.Exists(path) ? new System.IO.FileInfo(path).Length : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public long ApplicationSize => SizeOf(Path);
        public long UpdateSize => SizeOf(UpdatePath);
        public long DlcSize => System.Linq.Enumerable.Sum(DlcPaths ?? [], SizeOf);
    }

    // The emulator's game library, handed to the HOME Menu as the console's installed titles.
    // The interface fills it; the ns services read it.
    public static class HomeMenuLibrary
    {
        private static Func<IReadOnlyList<HomeMenuTitle>> _provider;

        // Raised when the list changes: the HOME Menu then lists its titles again.
        public static event Action Changed;

        /// <summary>
        /// "Delete Software" of the HOME Menu: removes the files of the title from the computer
        /// (set by the interface, which then lists its games again). True when done.
        /// </summary>
        public static Func<HomeMenuTitle, bool> Delete { get; set; }

        public static void SetProvider(Func<IReadOnlyList<HomeMenuTitle>> provider)
        {
            _provider = provider;
        }

        public static void NotifyChanged()
        {
            Ryujinx.Common.Logging.Logger.Notice.Print(Ryujinx.Common.Logging.LogClass.Service, $"[home] library changed, {Titles().Count} title(s)");

            try
            {
                Changed?.Invoke();
            }
            catch (Exception)
            {
                // A listener that is gone must not stop the others.
            }
        }

        public static IReadOnlyList<HomeMenuTitle> Titles()
        {
            try
            {
                return _provider?.Invoke() ?? [];
            }
            catch (Exception)
            {
                return [];
            }
        }

        public static HomeMenuTitle Find(ulong id)
        {
            foreach (HomeMenuTitle title in Titles())
            {
                if (title.Id == id)
                {
                    return title;
                }
            }

            return null;
        }
    }
}
