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
    }

    // The emulator's game library, handed to the HOME Menu as the console's installed titles.
    // The interface fills it; the ns services read it.
    public static class HomeMenuLibrary
    {
        private static Func<IReadOnlyList<HomeMenuTitle>> _provider;

        // Raised when the list changes: the HOME Menu then lists its titles again.
        public static event Action Changed;

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
