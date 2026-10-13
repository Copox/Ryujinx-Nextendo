using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.IO;

namespace Ryujinx.HLE.HOS
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The game cards of the library (.xci) as the HOME Menu's virtual
    /// game cards: they are listed in its "Virtual Game Cards" page, one of them is loaded to the
    /// console (as a game card inserted) and only that one shows on the HOME Menu; loading another
    /// one takes the first one out. An option (on unless turned off), not in the HOME Menu as the
    /// console has it.
    /// </summary>
    public static class NextendoVirtualCards
    {
        // The layout of an entry of the list (ns 4008, 0x40 bytes), as the HOME Menu reads it.
        private const int EntrySize = 0x40;
        private const uint Compatible = 1u << 14;      // software for this console
        private const uint LoadedHere = 1;             // +0x20: the console it is loaded to

        private static bool? _enabled;
        private static ulong? _inserted;

        private static string EnabledPath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_xci_cards.txt");
        private static string InsertedPath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_xci_inserted.txt");

        /// <summary>The option (the HOME Menu's "Ryujinx | Nextendo" settings).</summary>
        public static bool Enabled
        {
            get
            {
                _enabled ??= Read(EnabledPath) != "0";

                return _enabled.Value && !NextendoHomeMenuVanilla.Enabled;
            }
            set
            {
                _enabled = value;
                Write(EnabledPath, value ? "1" : "0");
                HomeMenuLibrary.NotifyChanged();
            }
        }

        /// <summary>The value of the option, whatever the HOME Menu as the console has it says.</summary>
        public static bool Option => _enabled ??= Read(EnabledPath) != "0";

        /// <summary>The game card loaded to the console (0: none).</summary>
        public static ulong Inserted
        {
            get
            {
                if (_inserted == null)
                {
                    _inserted = ulong.TryParse(Read(InsertedPath), System.Globalization.NumberStyles.HexNumber, null, out ulong id) ? id : 0;
                }

                return _inserted.Value;
            }
            private set
            {
                _inserted = value;
                Write(InsertedPath, value.ToString("X16"));
            }
        }

        /// <summary>A title of the library that is a virtual game card (a game card, the option on).</summary>
        public static bool IsCard(HomeMenuTitle title) =>
            Enabled && title?.Path != null && title.Path.EndsWith(".xci", StringComparison.OrdinalIgnoreCase);

        /// <summary>A title the HOME Menu shows: not a virtual game card, or the one loaded.</summary>
        public static bool Shown(HomeMenuTitle title) => !IsCard(title) || title.Id == Inserted;

        /// <summary>The list of the virtual game cards (ns 4008), as the HOME Menu reads it.</summary>
        public static byte[] Entries()
        {
            List<HomeMenuTitle> cards = [];

            foreach (HomeMenuTitle title in HomeMenuLibrary.Titles())
            {
                if (IsCard(title))
                {
                    cards.Add(title);
                }
            }

            byte[] entries = new byte[EntrySize * cards.Count];

            for (int i = 0; i < cards.Count; i++)
            {
                Span<byte> entry = entries.AsSpan(EntrySize * i, EntrySize);
                HomeMenuTitle card = cards[i];

                BitConverter.TryWriteBytes(entry[0x00..], card.Id);
                BitConverter.TryWriteBytes(entry[0x08..], card.Id);
                BitConverter.TryWriteBytes(entry[0x10..], card.Id);
                entry[0x18] = 1;
                BitConverter.TryWriteBytes(entry[0x20..], card.Id == Inserted ? LoadedHere : 0u);
                BitConverter.TryWriteBytes(entry[0x24..], Compatible);
                BitConverter.TryWriteBytes(entry[0x38..], Obtained(card.Path));
            }

            return entries;
        }

        /// <summary>
        /// Virtual game cards loaded to this console (ns 4076: their entries' first 0x20 bytes):
        /// the last one is the game card inserted, the one before is taken out.
        /// </summary>
        public static void Load(ReadOnlySpan<byte> entries, int count)
        {
            for (int i = 0; i < count && (i + 1) * 0x20 <= entries.Length; i++)
            {
                ulong id = BitConverter.ToUInt64(entries[(0x20 * i)..]);
                HomeMenuTitle title = HomeMenuLibrary.Find(id);

                if (title == null || !IsCard(title))
                {
                    continue;
                }

                Logger.Notice.Print(LogClass.ServiceNs, $"[vgc] game card inserted: {title.Name} ({id:X16}), {(Inserted != 0 && Inserted != id ? $"{Inserted:X16} taken out" : "none before")}");

                Inserted = id;
            }

            HomeMenuLibrary.NotifyChanged();
        }

        private static string WelcomePath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_vgc_welcome.txt");

        /// <summary>The welcome of the virtual game cards page was seen (it shows only once).</summary>
        public static bool WelcomeSeen
        {
            get => Read(WelcomePath) == "1";
            set
            {
                Write(WelcomePath, value ? "1" : "0");

                if (value)
                {
                    Themes.QlaunchThemePage.SetVgcWelcomeSeen();
                }
            }
        }

        /// <summary>The game card loaded to this console taken out (ns 4038): none inserted.</summary>
        public static void Eject()
        {
            if (Inserted != 0)
            {
                Logger.Notice.Print(LogClass.ServiceNs, $"[vgc] game card ejected: {Inserted:X16}");

                Inserted = 0;
                HomeMenuLibrary.NotifyChanged();
            }
        }

        // The date the card was obtained: the file's (the page sorts by it).
        private static long Obtained(string path)
        {
            try
            {
                return new DateTimeOffset(File.GetCreationTimeUtc(path)).ToUnixTimeSeconds();
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static string Read(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Write(string path, string text)
        {
            try
            {
                File.WriteAllText(path, text);
            }
            catch (Exception)
            {
                // kept for this session only
            }
        }
    }
}
