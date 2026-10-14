using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Tools.Fs;
using LibHac.Tools.FsSystem;
using LibHac.Tools.FsSystem.RomFs;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Ryujinx.HLE.HOS
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The texts of the system (HOME Menu, user page, user selection...)
    /// name Nextendo: "Nintendo Switch Online" becomes the Nextendo website, every other
    /// "Nintendo" becomes "Nextendo", and the addresses of Nintendo's sites point to Nextendo's.
    /// Done on the message files (/message/&lt;language&gt;/*.msbt.szs: MSBT in Yaz0) when a system
    /// program is loaded; the formatting tags of the texts are kept as they are.
    /// </summary>
    public static class NextendoBranding
    {
        // "Nextendo Website" in each language of the console, by the folder of its messages.
        private static readonly Dictionary<string, string> _website = new()
        {
            ["fr"] = "Site Nextendo",
            ["en"] = "Nextendo Website",
            ["es"] = "Sitio web de Nextendo",
            ["de"] = "Nextendo-Website",
            ["it"] = "Sito web di Nextendo",
            ["nl"] = "Nextendo-website",
            ["pt"] = "Site da Nextendo",
            ["ru"] = "Сайт Nextendo",
            ["ja"] = "Nextendoウェブサイト",
            ["ko"] = "Nextendo 웹사이트",
            ["zh-CN"] = "Nextendo 网站",
            ["zh-TW"] = "Nextendo 網站",
            ["pl"] = "Strona Nextendo",
            ["th"] = "เว็บไซต์ Nextendo",
        };

        private static readonly Regex _address = new(
            @"(https?://)?([a-z0-9-]+\.)*(nintendo|nintendoswitch)\.(com\.cn|co\.jp|com|net|jp)(/[^\s<""]*)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const string Kept = @"(?!\s*(Switch|eShop|3DS|2DS|DS(?![a-z])|Entertainment|64|Game\s?Boy|GameCube|Wii|Labo|Ring|Super\s?(NES|Nintendo)|\s+$))";
        private static readonly Regex _brand = new("Nintendo" + Kept, RegexOptions.Compiled);
        private static readonly Regex _brandUpper = new("NINTENDO" + Kept, RegexOptions.Compiled);
        private static readonly Regex _katakana = new(@"ニンテンドー(?!\s*(スイッチ|3DS|イーショップ|$))", RegexOptions.Compiled);
        private static readonly Regex _kanji = new(@"任天堂(?!\s*(Switch|eShop|$))", RegexOptions.Compiled);
        private static readonly Regex _hangul = new(@"닌텐도(?!\s*(스위치|Switch|e숍|eShop|$))", RegexOptions.Compiled);

        // "The console" is the emulator: "Ryujinx | Nextendo". Not when it names another console
        // (the previous one, the one data comes from, a Nintendo Switch 2...).
        private const string Emulator = "Ryujinx | Nextendo";
        private static readonly Regex _consoleFr = new(@"\b(?:[Ll]a|[Cc]ette) console(?!\s+(?:Nintendo|Nextendo|source|précédente|cible|d'origine|de destination|Switch|\d)|\s+(?:depuis|sur|dans|avec|à partir de|vers) laquelle)", RegexOptions.Compiled);
        private static readonly Regex _consoleEn = new(@"\b(?:[Tt]he|[Tt]his) console(?!\s+(?:you|from|to which|Nintendo|Nextendo|Switch|\d))", RegexOptions.Compiled);

        // The label before the system version on the System Update page, in each language: the
        // version of the emulator is put in front of it.
        private static readonly HashSet<string> _systemVersionLabels =
        [
            "Logiciel système : version ", "System version ", "Systemversion ", "Versione di sistema: ", "Versión: ", "Versie: ",
            "Versão da consola: ", "Версия системы ", "システムバージョン ", "시스템 버전 ", "系统版本 ", "系統版本 ",
        ];

        private static string EmulatorVersion
        {
            get
            {
                string version = Ryujinx.Common.ReleaseInformation.Version ?? "";
                int end = version.IndexOfAny(['+', '-']);

                return end > 0 ? version[..end] : version;
            }
        }

        public static bool Enabled => true;

        // [Nextendo] the HOME Menu as the console has it: only the theme chosen is applied
        private static bool Vanilla => Ryujinx.Common.Configuration.NextendoHomeMenuVanilla.Enabled;

        public static IStorage Appliquer(ulong programId, IStorage baseStorage, IStorage originalStorage = null)
        {
            if (!Enabled || baseStorage == null || programId < 0x0100000000001000 || programId > 0x0100000000001FFF)
            {
                return baseStorage;
            }

            try
            {
                return Patch(programId, baseStorage, originalStorage);
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.ModLoader, $"[Nextendo] texts of {programId:X16} left as they are ({e.GetType().Name}: {e.Message})");

                return baseStorage;
            }
        }

        private static IStorage Patch(ulong programId, IStorage baseStorage, IStorage originalStorage)
        {
            RomFsFileSystem baseRom = new(baseStorage);

            // [Nextendo, HOME Menu themes] With themes for the themes page, the layouts are the
            // system's own: a theme installed on top of them (NXThemes, in atmosphere/contents)
            // would be mixed with the one chosen, or stay with Basic White.
            // A theme installed there after the choice made in the page wins: shown as it is, and
            // the page follows it.
            bool installedWins = Themes.NextendoThemes.InstalledWins;
            RomFsFileSystem originalRom = originalStorage != null && originalStorage != baseStorage ? new(originalStorage) : null;
            RomFsFileSystem layoutRom = originalRom != null && !installedWins && Themes.NextendoThemes.Packs().Count > 0 ? originalRom : null;

            if (installedWins && originalRom != null && programId == 0x0100000000001000)
            {
                try
                {
                    Themes.NextendoThemes.Identify(programId,
                        name => Exists(originalRom, "/lyt/" + name) ? Yaz0Decode(Read(originalRom, "/lyt/" + name)) : null,
                        name => Exists(baseRom, "/lyt/" + name) && Exists(originalRom, "/lyt/" + name) &&
                                !Read(baseRom, "/lyt/" + name).AsSpan().SequenceEqual(Read(originalRom, "/lyt/" + name)) ? Yaz0Decode(Read(baseRom, "/lyt/" + name)) : null);
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ModLoader, $"[theme] installed theme not identified: {e.Message}");
                }
            }
            Dictionary<string, byte[]> changed = new();

            int strings = 0;

            foreach (DirectoryEntryEx entry in baseRom.EnumerateEntries().Where(f => !Vanilla && f.Type == DirectoryEntryType.File &&
                         f.FullPath.StartsWith("/message/", StringComparison.Ordinal) && f.FullPath.EndsWith(".msbt.szs", StringComparison.Ordinal)))
            {
                string language = LanguageOf(entry.FullPath);
                byte[] packed = Read(baseRom, entry.FullPath);
                byte[] msbt = Yaz0Decode(packed);
                byte[] patched = PatchMsbt(msbt, text =>
                {

                    if (entry.FullPath.EndsWith("/setting.msbt.szs", StringComparison.Ordinal) && _systemVersionLabels.Contains(text))
                    {
                        text = $"{Emulator} {EmulatorVersion} · {text}";
                    }

                    return Rebrand(text, language);
                }, out int count);

                // [Nextendo, HOME Menu themes] The footer text of the themes page's X button.
                if (programId == 0x0100000000001000 && entry.FullPath.EndsWith("/common.msbt.szs", StringComparison.Ordinal) &&
                    AddMsbtLabel(patched, Themes.QlaunchThemePage.CustomizeLabel, CustomizeText(language), "Cmn_FooterText_Back") is { } withCustomize)
                {
                    patched = withCustomize;
                    count++;
                }

                // [Nextendo, HOME Menu work] The "Ryujinx | Nextendo" section of the settings, in
                // the place of Bluetooth Audio: its button's name.
                if (programId == 0x0100000000001000 && entry.FullPath.EndsWith("/setting.msbt.szs", StringComparison.Ordinal) &&
                    AddMsbtLabel(patched, "Nx_SetNav_Ryujinx", $"{Emulator}", "SetNav_BtnSound") is { } withSettings)
                {
                    patched = withSettings;
                    count++;
                }

                // ... and the titles and choices of its select boxes (as Region's: a title, a
                // label per choice), in the language of that file.
                if (programId == 0x0100000000001000 && entry.FullPath.EndsWith("/setting.msbt.szs", StringComparison.Ordinal))
                {
                    foreach (Ryujinx.Common.Configuration.NextendoEmulatorSetting setting in Ryujinx.Common.Configuration.NextendoEmulatorSettings.List)
                    {
                        if (AddMsbtLabel(patched, Themes.QlaunchSettingsPage.TitleLabel(setting.Key), Themes.QlaunchSettingsPage.Text(setting.Key, language), "SetCntHardware_BtnSelRegion") is { } withTitle)
                        {
                            patched = withTitle;
                            count++;
                        }

                        for (int choice = 0; choice < setting.Values.Length; choice++)
                        {
                            if (AddMsbtLabel(patched, Themes.QlaunchSettingsPage.ChoiceLabel(setting.Key, choice), Themes.QlaunchSettingsPage.Shown(setting.Values[choice], language), "SetArea_Japan") is { } withChoice)
                            {
                                patched = withChoice;
                                count++;
                            }
                        }
                    }

                    // the restart dialog's bodies (the buttons of the language's: Cancel, Restart)
                    foreach ((string label, string text) in Themes.QlaunchSettingsPage.RestartBodies(language))
                    {
                        if (AddMsbtLabel(patched, label, text, "SetCntHardware_DlgBodyLanguageRestartConfirm") is { } withBody)
                        {
                            patched = withBody;
                            count++;
                        }
                    }
                }

                if (count > 0)
                {
                    changed[entry.FullPath] = Yaz0Encode(patched);
                    strings += count;
                }
            }

            // [Nextendo, HOME Menu themes] The theme chosen changes the layout archives of its parts.
            foreach (DirectoryEntryEx entry in baseRom.EnumerateEntries().Where(f => f.Type == DirectoryEntryType.File &&
                         f.FullPath.StartsWith("/lyt/", StringComparison.Ordinal) && f.FullPath.EndsWith(".szs", StringComparison.Ordinal)).ToList())
            {
                try
                {
                    byte[] packed = Read(baseRom, entry.FullPath);
                    byte[] own = layoutRom != null && Exists(layoutRom, entry.FullPath) ? Read(layoutRom, entry.FullPath) : packed;
                    byte[] themed = Themes.NextendoThemes.Apply(programId, entry.Name, Yaz0Decode(own));

                    if (themed != null)
                    {
                        changed[entry.FullPath] = Yaz0Encode(themed);
                    }
                    else if (installedWins && entry.Name == "ResidentMenu.szs" && !own.AsSpan().SequenceEqual(Read(originalRom ?? baseRom, entry.FullPath)) &&
                             Themes.NextendoThemes.FixInstalledHome(Yaz0Decode(own)) is { } fixedHome)
                    {
                        changed[entry.FullPath] = Yaz0Encode(fixedHome);
                    }
                    else if (!own.AsSpan().SequenceEqual(packed))
                    {
                        changed[entry.FullPath] = own;
                        Logger.Info?.Print(LogClass.ModLoader, $"[theme] {entry.FullPath}: the system's own (a theme installed in atmosphere/contents left aside)");
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ModLoader, $"[theme] {entry.FullPath} left as it is: {e.Message}");
                }
            }

            // The "Nintendo Switch Online" button of the HOME Menu (and its picture on the screen
            // that resumes a system applet): the Nextendo logo instead, as single-channel masks the
            // layouts colour, of the size and GPU layout of the originals.
            if (programId == 0x0100000000001000 && !Vanilla)
            {
                foreach ((string path, string texture, string asset) in _logos)
                {
                    if (!Exists(baseRom, path))
                    {
                        continue;
                    }

                    byte[] layout = Yaz0Decode(changed.TryGetValue(path, out byte[] themedArchive) ? themedArchive : Read(baseRom, path));
                    bool replaced = ReplaceTexture(layout, texture, Ryujinx.Common.EmbeddedResources.Read("Ryujinx.HLE/HOS/NextendoAssets/" + asset));

                    if (replaced)
                    {
                        changed[path] = Yaz0Encode(layout);
                    }

                    Logger.Info?.Print(LogClass.ModLoader, $"[Nextendo] HOME Menu: Nextendo logo in {path} ({texture}) {(replaced ? "done" : "not done")}");
                }
            }

            // [Nextendo] The Controllers page: its Pro Controller picture follows the controller of
            // player 1 (PlayStation, Xbox), its layers drawn from Kenney's Input Prompts (CC0).
            // [Nextendo, HOME Menu work] The entries of the themes page get a value on the right,
            // hidden (the "Ryujinx | Nextendo" section shows it), and room for longer names.
            const string SettingsLayout = "/lyt/Set.szs";

            if (programId == 0x0100000000001000 && !Vanilla && Exists(baseRom, SettingsLayout) &&
                AddEntryValueText(Yaz0Decode(changed.TryGetValue(SettingsLayout, out byte[] themedSettings) ? themedSettings : Read(baseRom, SettingsLayout))) is { } withValues)
            {
                changed[SettingsLayout] = Yaz0Encode(withValues);
            }

            // [Nextendo, HOME Menu work] "A friend is online": the friend's picture over both lines.
            if (programId == 0x010000000000100C && !Vanilla && Exists(baseRom, NextendoFriendPage.NotificationArchive))
            {
                try
                {

                    byte[] notification = NextendoFriendPage.PatchNotification(Yaz0Decode(changed.TryGetValue(NextendoFriendPage.NotificationArchive, out byte[] themedNotification) ? themedNotification : Read(baseRom, NextendoFriendPage.NotificationArchive)));

                    if (notification != null)
                    {
                        changed[NextendoFriendPage.NotificationArchive] = Yaz0Encode(notification);
                    }

                    Logger.Info?.Print(LogClass.ModLoader, $"[Nextendo] friend notification: picture {(notification != null ? "enlarged" : "left as it is")}");
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ModLoader, $"[Nextendo] friend notification left as it is: {e.Message}");
                }
            }

            // [Nextendo, HOME Menu work] The page of a friend: a banner and the friend's badges.
            if (programId == 0x0100000000001013 && !Vanilla && Exists(baseRom, NextendoFriendPage.Archive))
            {
                try
                {
                    byte[] ownBanner = NextendoFriendProfile.PaintOwn();
                    byte[] original = Yaz0Decode(changed.TryGetValue(NextendoFriendPage.Archive, out byte[] themedPage) ? themedPage : Read(baseRom, NextendoFriendPage.Archive));
                    byte[] page = NextendoFriendPage.Patch(ownBanner, original);

                    if (page != null)
                    {
                        changed[NextendoFriendPage.Archive] = Yaz0Encode(page);
                    }

                    Logger.Info?.Print(LogClass.ModLoader, $"[Nextendo] friend page: banner and badges {(page != null ? "added" : "not added")}");
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ModLoader, $"[Nextendo] friend page left as it is: {e.Message}");
                }
            }

            const string ControllerLayout = "/lyt/Controller.szs";

            if (programId == 0x0100000000001003 && !Vanilla && ControllerPagePictures() is { } pictures && Exists(baseRom, ControllerLayout))
            {
                byte[] layout = Yaz0Decode(changed.TryGetValue(ControllerLayout, out byte[] earlier) ? earlier : Read(baseRom, ControllerLayout));
                int replaced = pictures.Count(p => ReplaceTexture(layout, p.Name, p.Data));

                if (replaced > 0)
                {
                    changed[ControllerLayout] = Yaz0Encode(layout);
                }

                Logger.Info?.Print(LogClass.ModLoader, $"[Nextendo] Controllers page: {replaced}/{pictures.Count} controller layer(s)");
            }

            // [Nextendo, HOME Menu themes] The pictures of the themes added to its themes page.
            List<(string Path, byte[] Data)> added = programId == 0x0100000000001000 ? Themes.QlaunchThemePage.Pictures().ToList() : [];

            if (changed.Count == 0 && added.Count == 0)
            {
                return baseStorage;
            }

            RomFsBuilder builder = new();

            foreach ((string path, byte[] data) in added)
            {
                builder.AddFile(path, new StorageFile(new MemoryStorage(data), OpenMode.Read));
            }

            if (added.Count > 0)
            {
                Logger.Info?.Print(LogClass.ModLoader, $"[theme] {added.Count} picture place(s) for the themes page (system one present: {Exists(baseRom, "/theme/ThemeIcon_00.jpg")})");
            }

            foreach (DirectoryEntryEx entry in baseRom.EnumerateEntries().Where(f => f.Type == DirectoryEntryType.File).OrderBy(f => f.FullPath, StringComparer.Ordinal))
            {
                if (changed.TryGetValue(entry.FullPath, out byte[] data))
                {
                    builder.AddFile(entry.FullPath, new StorageFile(new MemoryStorage(data), OpenMode.Read));
                }
                else
                {
                    using UniqueRef<IFile> file = new();
                    baseRom.OpenFile(ref file.Ref, entry.FullPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();
                    builder.AddFile(entry.FullPath, file.Release());
                }
            }

            Logger.Info?.Print(LogClass.ModLoader, $"[Nextendo] texts of {programId:X16}: {strings} string(s) in {changed.Count} file(s) name Nextendo, {Interlocked.Exchange(ref _systemUpdateHits, 0)} of the System Update page");

            return builder.Build();
        }

        // The online button of the HOME Menu, then its picture on the screen that resumes a system applet.
        private static readonly (string Path, string Texture, string Asset)[] _logos =
        [
            ("/lyt/ResidentMenu.szs", "RdtIcoLR_00^s", "RdtIcoLR_00.bc4"),
            ("/lyt/Entrance.szs", "EntIcoLHUB^s", "EntIcoLHUB.bc4"),
        ];

        private static bool Exists(IFileSystem fs, string path) =>
            fs.GetEntryType(out DirectoryEntryType type, path.ToU8Span()).IsSuccess() && type == DirectoryEntryType.File;

        // Puts new texture data (already in the GPU layout of the original, same size) in place of
        // a texture of the BNTX archives of a SARC layout. True when done.
        private static bool ReplaceTexture(byte[] sarc, string name, byte[] data)
        {
            if (sarc.Length < 0x20 || Encoding.ASCII.GetString(sarc, 0, 4) != "SARC")
            {
                return false;
            }

            int dataOffset = BitConverter.ToInt32(sarc, 0x0C);
            int sfat = 0x14;
            int nodes = BitConverter.ToUInt16(sarc, sfat + 6);
            int names = sfat + 12 + 16 * nodes + 8;

            for (int i = 0; i < nodes; i++)
            {
                int node = sfat + 12 + 16 * i;
                int nameOffset = (BitConverter.ToInt32(sarc, node + 4) & 0xFFFF) * 4;
                int start = dataOffset + BitConverter.ToInt32(sarc, node + 8);
                int end = dataOffset + BitConverter.ToInt32(sarc, node + 12);
                int nameEnd = Array.IndexOf(sarc, (byte)0, names + nameOffset);
                string file = Encoding.ASCII.GetString(sarc, names + nameOffset, nameEnd - (names + nameOffset));

                if (!file.EndsWith(".bntx", StringComparison.Ordinal))
                {
                    continue;
                }

                if (ReplaceBntxTexture(sarc.AsSpan(start, end - start), name, data))
                {
                    return true;
                }
            }

            return false;
        }

        // The theme chosen in the HOME Menu (nextendo_color_set.txt: 0 Basic White, 1 Basic Black,
        // the latter when none was chosen).
        private static bool WhiteTheme()
        {
            try
            {
                string saved = System.IO.Path.Combine(Ryujinx.Common.Configuration.AppDataManager.BaseDirPath, "nextendo_color_set.txt");

                return File.Exists(saved) && File.ReadAllText(saved).Trim() == "0";
            }
            catch (Exception)
            {
                return false;
            }
        }

        // The entry of the themes page (SetBtnListCheckTheme_) with the value text of System's lists
        // (T_Value of BtnListSelBox, "Language ..... English") under its right side, hidden, and
        // room for 63 characters in it and in the name. Null when it cannot be done.
        private static byte[] AddEntryValueText(byte[] settings)
        {
            try
            {
                Themes.SarcArchive archive = Themes.SarcArchive.Read(settings);

                if (!archive.Files.TryGetValue("blyt/SetBtnListCheckTheme_.bflyt", out byte[] entryData) ||
                    !archive.Files.TryGetValue("blyt/BtnListSelBox.bflyt", out byte[] selectData))
                {
                    return null;
                }

                Themes.LayoutFile entry = new(entryData);
                Themes.LayoutFile select = new(selectData);
                Themes.LayoutFile.Section value = select.Pane("T_Value");
                int right = entry.FindPane("N_R");

                if (entry.FindPane("T_Value") >= 0 || value == null || right < 0 || right + 1 >= entry.Sections.Count || entry.Sections[right + 1].Magic != "pas1")
                {
                    return null;
                }

                List<byte[]> materials = entry.Materials();

                materials.Add(select.Materials()[BitConverter.ToUInt16(value.Data, 0x58)]);
                entry.SetMaterials(materials);

                byte[] text = (byte[])value.Data.Clone();

                BitConverter.TryWriteBytes(text.AsSpan(0x54), (ushort)0x80);                 // room
                BitConverter.TryWriteBytes(text.AsSpan(0x58), (ushort)(materials.Count - 1)); // its material
                BitConverter.TryWriteBytes(text.AsSpan(0x5A), (ushort)0);                     // the entry's font
                text[0x08] &= 0xFE;                                                           // hidden

                // The colour of System's values: the accent of the theme in use (Basic White:
                // blue, Basic Black: teal), as the HOME Menu starts again when it changes.
                string accent = WhiteTheme() ? "FFFF5032" : "FFC8FF00";

                Themes.LayoutFile.SetColor(text, 0x68, accent); // font, top
                Themes.LayoutFile.SetColor(text, 0x6C, accent); // font, bottom

                entry.Sections.Insert(right + 2, new Themes.LayoutFile.Section("txt1", text));

                if (entry.Pane("T_00") is { } name && BitConverter.ToUInt16(name.Data, 0x54) < 0x80)
                {
                    BitConverter.TryWriteBytes(name.Data.AsSpan(0x54), (ushort)0x80);
                }

                archive.Files["blyt/SetBtnListCheckTheme_.bflyt"] = entry.Write();

                // "Ryujinx | Nextendo" (the Bluetooth Audio entry, N_Sound) under System: the menu
                // shows its entries in the order of its layout.
                if (archive.Files.TryGetValue("blyt/SetBaseNav.bflyt", out byte[] navData))
                {
                    Themes.LayoutFile nav = new(navData);
                    int sound = nav.FindPane("N_Sound");

                    if (sound >= 0 && nav.FindPane("N_Btm") > sound)
                    {
                        (int first, int end) = nav.Subtree(sound);
                        List<Themes.LayoutFile.Section> moved = nav.Sections.GetRange(first, end - first);

                        nav.Sections.RemoveRange(first, end - first);
                        nav.Sections.InsertRange(nav.FindPane("N_Btm"), moved);
                        archive.Files["blyt/SetBaseNav.bflyt"] = nav.Write();
                    }
                }

                return archive.Write();
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.ModLoader, $"[Nextendo] themes page entries left as they are: {e.Message}");

                return null;
            }
        }

        // Puts new texture data (GPU layout of the original, same size) in place of a texture of a
        // BNTX archive. True when done.
        private static bool ReplaceBntxTexture(Span<byte> bntx, string name, byte[] data, Action<byte[]> before = null)
        {
            int nx = bntx.IndexOf("NX  "u8);

            if (nx < 0)
            {
                return false;
            }

            int count = BitConverter.ToInt32(bntx[(nx + 4)..]);
            int infoPointers = (int)BitConverter.ToInt64(bntx[(nx + 8)..]);

            for (int t = 0; t < count; t++)
            {
                int brti = (int)BitConverter.ToInt64(bntx[(infoPointers + 8 * t)..]);
                int nameAddress = (int)BitConverter.ToInt64(bntx[(brti + 0x60)..]);
                int nameLength = BitConverter.ToUInt16(bntx[nameAddress..]);
                string texture = Encoding.ASCII.GetString(bntx.Slice(nameAddress + 2, nameLength));

                if (texture != name)
                {
                    continue;
                }

                int size = BitConverter.ToInt32(bntx[(brti + 0x50)..]);
                int mips = (int)BitConverter.ToInt64(bntx[(brti + 0x70)..]);
                int mip0 = (int)BitConverter.ToInt64(bntx[mips..]);

                before?.Invoke(bntx.Slice(mip0, size).ToArray());

                if (data == null)
                {
                    return false;
                }

                if (size != data.Length)
                {
                    Logger.Warning?.Print(LogClass.ModLoader, $"[Nextendo] texture {name}: 0x{size:X} bytes, 0x{data.Length:X} given, left as it is");

                    return false;
                }

                data.CopyTo(bntx.Slice(mip0, size));

                return true;
            }

            return false;
        }

        // The kind of controller of player 1, for its pictures ("ps4", "ps5", "xbox"), or null.
        private static string ControllerStyleName() => Services.Sdb.Pl.ControllerButtonFont.Style switch
        {
            Services.Sdb.Pl.ControllerStyle.PlayStation4 => "ps4",
            Services.Sdb.Pl.ControllerStyle.PlayStation5 => "ps5",
            Services.Sdb.Pl.ControllerStyle.Xbox => "xbox",
            _ => null,
        };

        // The layers of the Controllers page's controller picture for that controller
        // (ControllerPage_*.bin: count, then per texture its name and data), or null.
        private static List<(string Name, byte[] Data)> ControllerPagePictures()
        {
            if (ControllerStyleName() is not { } style)
            {
                return null;
            }

            using BinaryReader reader = new(new MemoryStream(Ryujinx.Common.EmbeddedResources.Read($"Ryujinx.HLE/HOS/NextendoAssets/ControllerPage_{style}.bin")));
            List<(string, byte[])> layers = [];
            int count = reader.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                string name = Encoding.ASCII.GetString(reader.ReadBytes(reader.ReadUInt16()));

                layers.Add((name, reader.ReadBytes(reader.ReadInt32())));
            }

            return layers;
        }

        /// <summary>
        /// [Nextendo] The system's controller pictures (ControllerIcon archive, used by the HOME
        /// Menu footer): with a PlayStation or Xbox controller for player 1, the Pro Controller one
        /// ("0C-00") is that controller (Kenney's Input Prompts, CC0).
        /// </summary>
        public static IStorage ControllerIcons(IStorage storage)
        {
            if (Ryujinx.Common.Configuration.NextendoHomeMenuVanilla.Enabled)
            {
                return storage;
            }

            string style = ControllerStyleName();

            // The Pro Controller's own picture is kept: a controller plugged in later changes
            // the picture shown (NextendoControllerPictures).
            if (style == null && NextendoControllerPictures.Original != null)
            {
                return storage;
            }

            try
            {
                byte[] picture = style == null ? null : Ryujinx.Common.EmbeddedResources.Read($"Ryujinx.HLE/HOS/NextendoAssets/ControllerIcon_{style}.bc4");
                RomFsFileSystem rom = new(storage);
                RomFsBuilder builder = new();
                int replaced = 0;

                foreach (DirectoryEntryEx entry in rom.EnumerateEntries().Where(f => f.Type == DirectoryEntryType.File).OrderBy(f => f.FullPath, StringComparer.Ordinal))
                {
                    if (entry.FullPath.EndsWith(".bntx", StringComparison.Ordinal))
                    {
                        byte[] bntx = Read(rom, entry.FullPath);

                        if (ReplaceBntxTexture(bntx, "0C-00", picture, NextendoControllerPictures.Keep))
                        {
                            replaced++;
                        }

                        builder.AddFile(entry.FullPath, new StorageFile(new MemoryStorage(bntx), OpenMode.Read));
                    }
                    else
                    {
                        using UniqueRef<IFile> file = new();
                        rom.OpenFile(ref file.Ref, entry.FullPath.ToU8Span(), OpenMode.Read).ThrowIfFailure();
                        builder.AddFile(entry.FullPath, file.Release());
                    }
                }

                if (style == null)
                {
                    return storage;
                }

                Logger.Info?.Print(LogClass.ModLoader, $"[Nextendo] controller pictures: {style} in {replaced} archive(s)");

                return replaced > 0 ? builder.Build() : storage;
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.ModLoader, $"[Nextendo] controller pictures left as they are: {e.Message}");

                return storage;
            }
        }

        // "/message/EUfr/x.msbt.szs" -> "fr"; Chinese keeps its script.
        private static string LanguageOf(string path)
        {
            string folder = path.Split('/')[2];

            return folder switch
            {
                "CNzh" => "zh-CN",
                "TWzh" => "zh-TW",
                _ => folder.Length >= 4 ? folder[2..4] : "en",
            };
        }

        // The title of the System Settings, which names the console: the emulator's (as "Paramètres de
        // la console" -> "Paramètres de Ryujinx | Nextendo"). Japanese, Korean and Chinese only say
        // "Settings".
        private static readonly Dictionary<string, string> _settingsTitles = new()
        {
            ["System Settings"] = "Ryujinx | Nextendo Settings",
            ["Systemeinstellungen"] = "Ryujinx | Nextendo-Einstellungen",
            ["Configuración de la consola"] = "Configuración de Ryujinx | Nextendo",
            ["Impostazioni di sistema"] = "Impostazioni di Ryujinx | Nextendo",
            ["Systeeminstellingen"] = "Ryujinx | Nextendo-instellingen",
            ["Definições da consola"] = "Definições de Ryujinx | Nextendo",
        };

        // The System Update page updates the emulator: its texts name it, in every language (French
        // already says "la console", which becomes the emulator's name). The generic "Update"
        // buttons stay: they are used elsewhere too.
        private static int _systemUpdateHits;

        private static readonly Dictionary<string, string> _systemUpdateTexts = new()
        {
            ["System Update"] = "Ryujinx | Nextendo Update",
            ["A system update is available. Select Update to start the process."] = "A Ryujinx | Nextendo update is available. Select Update to start the process.",
            ["A system update is available. Update now?"] = "A Ryujinx | Nextendo update is available. Update now?",
            ["The download will continue even if you exit this screen. You will need to update the system once downloading finishes."] = "The download will continue even if you exit this screen. You will need to update Ryujinx | Nextendo once downloading finishes.",
            ["Download will continue even if you leave this screen. Update your console after the download is complete."] = "Download will continue even if you leave this screen. Update Ryujinx | Nextendo after the download is complete.",

            ["System-Update"] = "Ryujinx | Nextendo-Update",
            ["Ein System-Update ist verfügbar. Wähle „Aktualisieren“, um den Prozess zu starten."] = "Ein Update für Ryujinx | Nextendo ist verfügbar. Wähle „Aktualisieren“, um den Prozess zu starten.",
            ["Der Download wird auch dann fortgesetzt, wenn du diesen Bildschirm verlässt. Führe das System-Update durch, sobald der Download beendet ist."] = "Der Download wird auch dann fortgesetzt, wenn du diesen Bildschirm verlässt. Führe das Update von Ryujinx | Nextendo durch, sobald der Download beendet ist.",

            ["Actualización de la consola"] = "Actualización de Ryujinx | Nextendo",
            ["Hay una actualización disponible para la consola. Selecciona \"Actualizar\" para iniciar el proceso."] = "Hay una actualización disponible para Ryujinx | Nextendo. Selecciona \"Actualizar\" para iniciar el proceso.",
            ["Hay una actualización disponible para la consola. ¿Quieres descargarla ahora?"] = "Hay una actualización disponible para Ryujinx | Nextendo. ¿Quieres descargarla ahora?",
            ["La descarga no se detendrá aunque salgas de esta pantalla. Cuando finalice, tendrás que actualizar la consola."] = "La descarga no se detendrá aunque salgas de esta pantalla. Cuando finalice, tendrás que actualizar Ryujinx | Nextendo.",
            ["La descarga continuará aun cuando salgas de esta pantalla. Actualiza la consola una vez que haya terminado la descarga."] = "La descarga continuará aun cuando salgas de esta pantalla. Actualiza Ryujinx | Nextendo una vez que haya terminado la descarga.",

            ["Aggiornamento della console"] = "Aggiornamento di Ryujinx | Nextendo",
            ["È disponibile un aggiornamento della console. Per scaricarlo, seleziona Aggiorna."] = "È disponibile un aggiornamento di Ryujinx | Nextendo. Per scaricarlo, seleziona Aggiorna.",
            ["Il download proseguirà anche dopo aver abbandonato questo schermo. Una volta terminato il download, sarà necessario aggiornare la console."] = "Il download proseguirà anche dopo aver abbandonato questo schermo. Una volta terminato il download, sarà necessario aggiornare Ryujinx | Nextendo.",

            ["Systeemupdate"] = "Ryujinx | Nextendo-update",
            ["Er is een systeemupdate beschikbaar. Wil je de update nu uitvoeren?"] = "Er is een update voor Ryujinx | Nextendo beschikbaar. Wil je de update nu uitvoeren?",
            ["De download wordt voortgezet, ook al verlaat je dit scherm. Nadat het downloaden is voltooid, moet je de systeemupdate installeren."] = "De download wordt voortgezet, ook al verlaat je dit scherm. Nadat het downloaden is voltooid, moet je de update van Ryujinx | Nextendo installeren.",

            ["Atualização da consola"] = "Atualização do Ryujinx | Nextendo",
            ["Está disponível uma atualização. Seleciona \"Atualizar\" para iniciar o processo."] = "Está disponível uma atualização do Ryujinx | Nextendo. Seleciona \"Atualizar\" para iniciar o processo.",
            ["O download continuará em curso, mesmo que saias deste ecrã. Assim que este terminar, será necessário atualizar a consola."] = "O download continuará em curso, mesmo que saias deste ecrã. Assim que este terminar, será necessário atualizar o Ryujinx | Nextendo.",
            ["Atualização do console"] = "Atualização do Ryujinx | Nextendo",
            ["Há uma atualização disponível para o console. Selecione \"Atualizar\" para iniciar o processo."] = "Há uma atualização disponível para o Ryujinx | Nextendo. Selecione \"Atualizar\" para iniciar o processo.",
            ["O download continuará mesmo se você sair desta tela. Atualize o console quando o download tiver sido concluído."] = "O download continuará mesmo se você sair desta tela. Atualize o Ryujinx | Nextendo quando o download tiver sido concluído.",

            ["Обновление системы"] = "Обновление Ryujinx | Nextendo",
            ["Доступно обновление системы. Выберите Обновить, чтобы запустить процесс."] = "Доступно обновление Ryujinx | Nextendo. Выберите Обновить, чтобы запустить процесс.",
            ["Загрузка продолжится, даже если вы выйдете с этого экрана. После завершения загрузки будет необходимо обновить систему."] = "Загрузка продолжится, даже если вы выйдете с этого экрана. После завершения загрузки будет необходимо обновить Ryujinx | Nextendo.",

            ["本体の更新"] = "Ryujinx | Nextendoの更新",
            ["本体の更新があります。更新しますか？"] = "Ryujinx | Nextendoの更新があります。更新しますか？",
            ["本体を更新する"] = "Ryujinx | Nextendoを更新する",
            ["この画面から抜けても、ダウンロードはつづきます。ダウンロードがおわったら本体を更新してください｡"] = "この画面から抜けても、ダウンロードはつづきます。ダウンロードがおわったらRyujinx | Nextendoを更新してください｡",

            ["본체 업데이트"] = "Ryujinx | Nextendo 업데이트",
            ["본체 업데이트가 있습니다. 업데이트하겠습니까?"] = "Ryujinx | Nextendo 업데이트가 있습니다. 업데이트하겠습니까?",
            ["이 화면에서 나가더라도 다운로드는 계속됩니다. 다운로드가 완료되면 본체를 업데이트해 주십시오."] = "이 화면에서 나가더라도 다운로드는 계속됩니다. 다운로드가 완료되면 Ryujinx | Nextendo를 업데이트해 주십시오.",

            ["更新主机"] = "更新Ryujinx | Nextendo",
            ["发现主机可以更新。是否开始更新？"] = "发现Ryujinx | Nextendo可以更新。是否开始更新？",
            ["退出此画面后也将继续下载。下载完成后，请更新主机｡"] = "退出此画面后也将继续下载。下载完成后，请更新Ryujinx | Nextendo｡",
            ["更新主機"] = "更新Ryujinx | Nextendo",
            ["有主機的更新檔。是否要更新？"] = "有Ryujinx | Nextendo的更新檔。是否要更新？",
            ["即使離開此畫面，下載仍會繼續進行。下載結束後請更新主機。"] = "即使離開此畫面，下載仍會繼續進行。下載結束後請更新Ryujinx | Nextendo。",
        };

        // Nintendo's postal address (after the name of the provider of the online services): ours
        // is the website. "Nintendo Co., Ltd.[,] Kyoto 601-8501, Japan" in each language.
        private static readonly Regex _nintendoProducts = new(
            @"(produits|products|productos de|prodotti|produtos|продукты|Nintendo-(Produkte|producten))\s*Nintendo|Nintendo(?=-Produkte|-producten|\s+products)",
            RegexOptions.Compiled);

        private static readonly Regex _postalAddress = new(@"Nintendo Co\., Ltd\.,?\s+\S+\s+601-8501,\s*[^\s<|]+", RegexOptions.Compiled);

        public static string Rebrand(string text, string language)
        {
            string website = _website.TryGetValue(language, out string name) ? name : _website["en"];

            if (_settingsTitles.TryGetValue(text, out string title))
            {
                return title;
            }

            if (_systemUpdateTexts.TryGetValue(text, out string update))
            {
                Interlocked.Increment(ref _systemUpdateHits);

                return update;
            }

            text = _postalAddress.Replace(text, "Nextendo Network, nextendo.network");

            // "Nintendo products" stay Nintendo's (the products, not the service).
            text = _nintendoProducts.Replace(text, m => m.Value.Replace("Nintendo", ""));

            text = text.Replace("Nintendo Switch Online", website)
                       .Replace("Nintendo Switch&nbsp;Online", website);
            text = _address.Replace(text, m => (m.Groups[1].Success ? m.Groups[1].Value : "") + "nextendo.network");
            text = text.Replace("Nintendo Co., Ltd.", "Nextendo Network");

            // The names of Nintendo's consoles and shop stay (a "Nextendo Switch" means nothing);
            // the account, the online service, the support site become Nextendo's. A "Nintendo"
            // at the end of a run is followed by a formatting tag holding "Switch 2": kept too.
            text = language switch
            {
                "fr" => _consoleFr.Replace(text, Emulator),
                "en" => _consoleEn.Replace(text, Emulator),
                _ => text,
            };

            text = _brand.Replace(text, "Nextendo");
            text = _brandUpper.Replace(text, "NEXTENDO");
            text = _katakana.Replace(text, "ネクステンドー");
            text = _kanji.Replace(text, "Nextendo");
            text = _hangul.Replace(text, "넥스텐도");
            text = text.Replace(''.ToString(), "Nintendo");

            return text;
        }

        private static byte[] Read(IFileSystem fs, string path)
        {
            using UniqueRef<IFile> file = new();
            fs.OpenFile(ref file.Ref, path.ToU8Span(), OpenMode.Read).ThrowIfFailure();
            file.Get.GetSize(out long size).ThrowIfFailure();
            byte[] data = new byte[size];
            file.Get.Read(out _, 0, data).ThrowIfFailure();

            return data;
        }

        // ---- Yaz0 ----

        public static byte[] Yaz0Decode(byte[] src)
        {
            if (src.Length < 16 || src[0] != 'Y' || src[1] != 'a' || src[2] != 'z' || src[3] != '0')
            {
                return src;
            }

            int size = (src[4] << 24) | (src[5] << 16) | (src[6] << 8) | src[7];
            byte[] dst = new byte[size];
            int s = 16, d = 0;

            while (d < size)
            {
                byte flags = src[s++];

                for (int bit = 0; bit < 8 && d < size; bit++)
                {
                    if ((flags & (0x80 >> bit)) != 0)
                    {
                        dst[d++] = src[s++];
                    }
                    else
                    {
                        int b1 = src[s++], b2 = src[s++];
                        int dist = ((b1 & 0xF) << 8 | b2) + 1;
                        int n = b1 >> 4;

                        n = n == 0 ? src[s++] + 0x12 : n + 2;

                        for (int k = 0; k < n && d < size; k++, d++)
                        {
                            dst[d] = dst[d - dist];
                        }
                    }
                }
            }

            return dst;
        }

        // Stored as literals only: valid Yaz0, read the same way.
        public static byte[] Yaz0Encode(byte[] src)
        {
            using MemoryStream output = new();

            output.Write("Yaz0"u8);
            output.WriteByte((byte)(src.Length >> 24));
            output.WriteByte((byte)(src.Length >> 16));
            output.WriteByte((byte)(src.Length >> 8));
            output.WriteByte((byte)src.Length);
            output.Write(new byte[8]);

            for (int i = 0; i < src.Length; i += 8)
            {
                output.WriteByte(0xFF);
                output.Write(src, i, Math.Min(8, src.Length - i));
            }

            return output.ToArray();
        }

        // ---- MSBT ----

        private delegate string Rewriter(string text);

        private static byte[] PatchMsbt(byte[] msbt, Rewriter rewrite, out int count)
        {
            count = 0;

            if (msbt.Length < 0x20 || Encoding.ASCII.GetString(msbt, 0, 8) != "MsgStdBn")
            {
                return msbt;
            }

            bool little = msbt[8] == 0xFF && msbt[9] == 0xFE;
            int sections = U16(msbt, 0x0E, little);
            int offset = 0x20;
            List<byte[]> parts = new() { msbt[..0x20] };

            for (int i = 0; i < sections && offset + 16 <= msbt.Length; i++)
            {
                string magic = Encoding.ASCII.GetString(msbt, offset, 4);
                int size = (int)U32(msbt, offset + 4, little);
                int padded = (size + 15) & ~15;
                byte[] header = msbt[offset..(offset + 16)];
                byte[] body = msbt[(offset + 16)..(offset + 16 + size)];

                if (magic == "TXT2" && msbt[0x0C] == 1)
                {
                    body = PatchTxt2(body, little, rewrite, ref count);
                    WriteU32(header, 4, (uint)body.Length, little);
                    padded = (body.Length + 15) & ~15;
                }

                byte[] section = new byte[16 + padded];

                header.CopyTo(section, 0);
                body.CopyTo(section, 16);
                section.AsSpan(16 + body.Length).Fill(0xAB);
                parts.Add(section);

                offset += 16 + ((size + 15) & ~15);
            }

            if (count == 0)
            {
                return msbt;
            }

            byte[] result = parts.SelectMany(p => p).ToArray();

            WriteU32(result, 0x12, (uint)result.Length, little);

            return result;
        }

        private static string CustomizeText(string language) => language switch
        {
            "fr" => "Personnaliser",
            "de" => "Anpassen",
            "es" => "Personalizar",
            "it" => "Personalizza",
            "nl" => "Aanpassen",
            "pt" => "Personalizar",
            "ru" => "Настроить",
            "ja" => "カスタマイズ",
            "ko" => "사용자 지정",
            "zh-CN" => "自定义",
            "zh-TW" => "自訂",
            "pl" => "Dostosuj",
            "th" => "ปรับแต่ง",
            _ => "Customize",
        };

        /// <summary>
        /// A message added to an MSBT (UTF-16): its label in the label table (LBL1, by the hash
        /// the format uses), its attributes copied from another message (ATR1), its text at the
        /// end (TXT2). Null when it cannot be added (or is there already).
        /// </summary>
        private static byte[] AddMsbtLabel(byte[] msbt, string label, string text, string attributesOf)
        {
            if (msbt.Length < 0x20 || Encoding.ASCII.GetString(msbt, 0, 8) != "MsgStdBn" || msbt[0x0C] != 1)
            {
                return null;
            }

            bool little = msbt[8] == 0xFF && msbt[9] == 0xFE;
            int sections = U16(msbt, 0x0E, little);
            int offset = 0x20;
            List<(string Magic, byte[] Header, byte[] Body)> parts = [];

            for (int i = 0; i < sections && offset + 16 <= msbt.Length; i++)
            {
                int size = (int)U32(msbt, offset + 4, little);

                parts.Add((Encoding.ASCII.GetString(msbt, offset, 4), msbt[offset..(offset + 16)], msbt[(offset + 16)..(offset + 16 + size)]));
                offset += 16 + ((size + 15) & ~15);
            }

            int lbl = parts.FindIndex(x => x.Magic == "LBL1");
            int atr = parts.FindIndex(x => x.Magic == "ATR1");
            int txt = parts.FindIndex(x => x.Magic == "TXT2");

            if (lbl < 0 || txt < 0)
            {
                return null;
            }

            // The labels, by bucket.
            byte[] lb = parts[lbl].Body;
            int buckets = (int)U32(lb, 0, little);
            List<List<(string Name, uint Index)>> table = [];

            for (int k = 0; k < buckets; k++)
            {
                int count = (int)U32(lb, 4 + 8 * k, little);
                int at = (int)U32(lb, 8 + 8 * k, little);
                List<(string, uint)> bucket = [];

                for (int e = 0; e < count; e++)
                {
                    int length = lb[at];
                    string name = Encoding.ASCII.GetString(lb, at + 1, length);

                    bucket.Add((name, U32(lb, at + 1 + length, little)));
                    at += 5 + length;
                }

                table.Add(bucket);
            }

            if (table.Any(b => b.Any(x => x.Name == label)))
            {
                return null;
            }

            byte[] tb = parts[txt].Body;
            uint messages = U32(tb, 0, little);
            uint hash = 0;

            foreach (byte c in Encoding.ASCII.GetBytes(label))
            {
                hash = hash * 0x492 + c;
            }

            table[(int)(hash % (uint)buckets)].Add((label, messages));

            using (MemoryStream o = new())
            {
                byte[] head = new byte[4 + 8 * buckets];
                int at = head.Length;

                WriteU32(head, 0, (uint)buckets, little);

                for (int k = 0; k < buckets; k++)
                {
                    WriteU32(head, 4 + 8 * k, (uint)table[k].Count, little);
                    WriteU32(head, 8 + 8 * k, (uint)at, little);
                    at += table[k].Sum(x => 5 + x.Name.Length);
                }

                o.Write(head);

                foreach ((string name, uint index) in table.SelectMany(b => b))
                {
                    byte[] id = new byte[4];

                    WriteU32(id, 0, index, little);
                    o.WriteByte((byte)name.Length);
                    o.Write(Encoding.ASCII.GetBytes(name));
                    o.Write(id);
                }

                parts[lbl] = (parts[lbl].Magic, parts[lbl].Header, o.ToArray());
            }

            // The attributes: those of another message.
            if (atr >= 0)
            {
                byte[] ab = parts[atr].Body;
                uint size = U32(ab, 4, little);
                uint source = table.SelectMany(b => b).FirstOrDefault(x => x.Name == attributesOf).Index;
                byte[] attributes = new byte[size];

                if (8 + (source + 1) * size <= ab.Length)
                {
                    ab.AsSpan((int)(8 + source * size), (int)size).CopyTo(attributes);
                }

                byte[] nb = new byte[8 + (messages + 1) * size];

                ab.AsSpan(0, (int)Math.Min(ab.Length, 8 + messages * size)).CopyTo(nb);
                WriteU32(nb, 0, messages + 1, little);
                attributes.CopyTo(nb, (int)(8 + messages * size));
                parts[atr] = (parts[atr].Magic, parts[atr].Header, nb);
            }

            // The text, at the end: the offsets move by the new one's 4 bytes.
            {
                byte[] value = (little ? Encoding.Unicode : Encoding.BigEndianUnicode).GetBytes(text + "\0");
                byte[] nb = new byte[tb.Length + 4 + value.Length];

                WriteU32(nb, 0, messages + 1, little);

                for (uint m = 0; m < messages; m++)
                {
                    WriteU32(nb, (int)(4 + 4 * m), U32(tb, (int)(4 + 4 * m), little) + 4, little);
                }

                WriteU32(nb, (int)(4 + 4 * messages), (uint)(tb.Length + 4), little);
                tb.AsSpan((int)(4 + 4 * messages)).CopyTo(nb.AsSpan((int)(8 + 4 * messages)));
                value.CopyTo(nb, tb.Length + 4);
                parts[txt] = (parts[txt].Magic, parts[txt].Header, nb);
            }

            using MemoryStream result = new();

            result.Write(msbt.AsSpan(0, 0x20));

            foreach ((string _, byte[] header, byte[] body) in parts)
            {
                byte[] h = (byte[])header.Clone();
                int padded = (body.Length + 15) & ~15;

                WriteU32(h, 4, (uint)body.Length, little);
                result.Write(h);
                result.Write(body);

                for (int i = body.Length; i < padded; i++)
                {
                    result.WriteByte(0xAB);
                }
            }

            byte[] bytes = result.ToArray();

            WriteU32(bytes, 0x12, (uint)bytes.Length, little);

            return bytes;
        }

        private static byte[] PatchTxt2(byte[] body, bool little, Rewriter rewrite, ref int count)
        {
            int entries = (int)U32(body, 0, little);
            List<byte[]> strings = new();

            for (int k = 0; k < entries; k++)
            {
                int start = (int)U32(body, 4 + 4 * k, little);
                int end = k + 1 < entries ? (int)U32(body, 8 + 4 * k, little) : body.Length;
                byte[] original = body[start..end];
                byte[] patched = PatchString(original, little, rewrite, out bool changed);

                if (changed)
                {
                    count++;
                }

                strings.Add(patched);
            }

            using MemoryStream output = new();
            byte[] table = new byte[4 + 4 * entries];
            int at = table.Length;

            WriteU32(table, 0, (uint)entries, little);

            for (int k = 0; k < entries; k++)
            {
                WriteU32(table, 4 + 4 * k, (uint)at, little);
                at += strings[k].Length;
            }

            output.Write(table);

            foreach (byte[] s in strings)
            {
                output.Write(s);
            }

            return output.ToArray();
        }

        // A string: text runs rewritten, tags (0x0E ... / 0x0F ...) copied as they are.
        private static byte[] PatchString(byte[] raw, bool little, Rewriter rewrite, out bool changed)
        {
            changed = false;

            using MemoryStream output = new();
            StringBuilder run = new();
            int i = 0;

            void Flush(ref bool c)
            {
                if (run.Length == 0)
                {
                    return;
                }

                string before = run.ToString();
                string after = rewrite(before);

                if (after != before)
                {
                    c = true;
                }

                foreach (char ch in after)
                {
                    WriteChar(output, ch, little);
                }

                run.Clear();
            }

            while (i + 1 < raw.Length)
            {
                int c = U16(raw, i, little);

                if (c == 0x0E && i + 8 <= raw.Length)
                {
                    Flush(ref changed);
                    int length = 8 + U16(raw, i + 6, little);
                    output.Write(raw, i, Math.Min(length, raw.Length - i));
                    i += length;
                }
                else if (c == 0x0F && i + 6 <= raw.Length)
                {
                    Flush(ref changed);
                    output.Write(raw, i, 6);
                    i += 6;
                }
                else if (c == 0)
                {
                    Flush(ref changed);
                    output.Write(raw, i, raw.Length - i);
                    i = raw.Length;
                }
                else
                {
                    run.Append((char)c);
                    i += 2;
                }
            }

            Flush(ref changed);

            return output.ToArray();
        }

        private static void WriteChar(Stream output, char c, bool little)
        {
            if (little)
            {
                output.WriteByte((byte)c);
                output.WriteByte((byte)(c >> 8));
            }
            else
            {
                output.WriteByte((byte)(c >> 8));
                output.WriteByte((byte)c);
            }
        }

        private static int U16(byte[] b, int o, bool little) => little ? b[o] | b[o + 1] << 8 : b[o] << 8 | b[o + 1];

        private static uint U32(byte[] b, int o, bool little) => little
            ? (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24)
            : (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

        private static void WriteU32(byte[] b, int o, uint v, bool little)
        {
            if (little)
            {
                b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
            }
            else
            {
                b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
            }
        }
    }
}
