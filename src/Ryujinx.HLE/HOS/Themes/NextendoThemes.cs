using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ryujinx.HLE.HOS.Themes
{
    /// <summary>
    /// [Nextendo, HOME Menu themes] The themes of the HOME Menu: packs of .nxtheme files (as
    /// Themezer gives them) found on the SD card of the emulator, under /Themes. The pack chosen
    /// is applied to the layouts of the system when they are loaded, every part of it (HOME
    /// Menu, lock screen, settings, all software, news, user page, user selection); nothing is
    /// written on the SD card.
    /// </summary>
    public static class NextendoThemes
    {
        /// <summary>One part of the system a theme can change.</summary>
        private sealed record Part(string Target, ulong ProgramId, string Archive, string MainLayout, string Texture, string PaneName, string[] BackgroundPanes, string FallbackTexture);

        private static readonly Part[] _parts =
        [
            new("home", 0x0100000000001000, "ResidentMenu.szs", "blyt/BgNml.bflyt", "White1x1A128^s", "exelixBG", ["P_Bg_00"], "White1x1A64^t"),
            new("lock", 0x0100000000001000, "Entrance.szs", "blyt/EntMain.bflyt", "White1x1^s", "exelixLK", ["P_BgL", "P_BgR"], "White1x1^r"),
            new("apps", 0x0100000000001000, "Flaunch.szs", "blyt/BgNml.bflyt", "NavBg_03^d", "exelixFBG", ["P_Bg_00"], "White1x1^r"),
            new("set", 0x0100000000001000, "Set.szs", "blyt/BgNml.bflyt", "NavBg_03^d", "exelixSET", ["P_Bg_00"], "White1x1A0^t"),
            new("news", 0x0100000000001000, "Notification.szs", "blyt/BgNml.bflyt", "NavBg_03^d", "exelixNEW", ["P_Bg_00"], "White1x1^r"),
            new("user", 0x0100000000001013, "MyPage.szs", "blyt/BgNml.bflyt", "NavBg_03^d", "exelixMY", ["P_Bg_00"], "White1x1A0^t"),
            new("psl", 0x0100000000001007, "Psl.szs", "blyt/PslSelectSinglePlayer.bflyt", "PselTopUserIcon_Bg^s", "exelixPSL", ["P_Bg"], "White1x1^r"),
        ];

        /// <summary>
        /// An icon a theme can replace (name.png or name.dds in the part): the texture, the
        /// picture pane that shows it (resized, its texture coordinates reset) and the panes
        /// drawn over it in the system's look, hidden.
        /// </summary>
        private sealed record Icon(string Target, string Name, string[] Textures, string Layout, string Pane, int Width, int Height, uint Channels, bool Tinted, float[] Position, string[] Hidden);

        private static readonly Icon[] _icons =
        [
            new("home", "album", ["RdtIcoPvr_00^s"], "blyt/RdtBtnPvr.bflyt", "P_Pict_00", 64, 56, 0x05050505, true, [22, 13, 0], ["N_02", "N_01", "P_Pict_01", "P_Color"]),
            new("home", "news", ["RdtIcoNews_00_Home^s", "RdtIcoNews_00^s"], "blyt/RdtBtnNtf.bflyt", "P_PictNtf_00", 64, 56, 0x05050505, true, null, ["P_PictNtf_01"]),
            new("home", "shop", ["RdtIcoShop^s"], "blyt/RdtBtnShop.bflyt", "P_Pict", 64, 56, 0x05050505, true, null, []),
            new("home", "controller", ["RdtIcoCtrl_00^s"], "blyt/RdtBtnCtrl.bflyt", "P_Form", 64, 56, 0x05050505, true, null, ["P_Stick", "P_Y", "P_X", "P_A", "P_B"]),
            new("home", "settings", ["RdtIcoSet^s"], "blyt/RdtBtnSet.bflyt", "P_Pict", 64, 56, 0x05050505, true, null, []),
            new("home", "power", ["RdtIcoPwrForm^s"], "blyt/RdtBtnPow.bflyt", "P_Pict_00", 64, 56, 0x05050505, true, null, []),
            new("home", "nso", ["RdtIcoLR_00^s"], "blyt/RdtBtnLR.bflyt", "P_LR_00", 64, 56, 0x05050505, false, null, ["P_LR_01"]),
            new("home", "card", ["RdtIcoHomeVgc^s"], "blyt/RdtBtnVgc.bflyt", "P_Pict_00", 64, 56, 0x05050505, true, null, ["P_00", "P_01"]),
            new("home", "share", ["RdtIcoHomeSplayFrame^s"], "blyt/RdtBtnSplay.bflyt", "P_Pict_00", 64, 56, 0x05050505, true, null, ["N_Wave", "P_Pict_01", "P_Pict_02", "P_Pict_03"]),
            new("lock", "lock", ["EntIcoHome^s"], "blyt/EntBtnResumeSystemApplet.bflyt", "P_PictHome", 184, 168, 0x05040302, false, [0, 0, 0], []),
        ];

        private static string ThemesFolder => Path.Combine(AppDataManager.BaseDirPath, "sdcard", "Themes");

        private static string ChoicePath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_home_theme.txt");

        /// <summary>The packs found: a folder holding .nxtheme files, or a lone .nxtheme file.</summary>
        public static List<string> Packs()
        {
            List<string> packs = [];

            if (!Directory.Exists(ThemesFolder))
            {
                return packs;
            }

            foreach (string folder in Directory.EnumerateDirectories(ThemesFolder, "*", SearchOption.AllDirectories).Prepend(ThemesFolder))
            {
                string[] parts = Directory.GetFiles(folder, "*.nxtheme");

                if (parts.Length == 0)
                {
                    continue;
                }

                if (folder == ThemesFolder)
                {
                    packs.AddRange(parts);
                }
                else
                {
                    packs.Add(folder);
                }
            }

            return packs.Select(p => Path.GetRelativePath(ThemesFolder, p)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static readonly Dictionary<string, byte[]> _pictures = [];

        /// <summary>
        /// The picture of a pack in the HOME Menu's themes page: its HOME Menu wallpaper (or the
        /// first one it has), at the size of the system's own (92x54, JPEG). Null when none.
        /// </summary>
        public static byte[] Picture(string pack)
        {
            lock (_pictures)
            {
                if (_pictures.TryGetValue(pack, out byte[] known))
                {
                    return known;
                }

                byte[] picture = null;

                try
                {
                    string path = Path.Combine(ThemesFolder, pack);
                    IEnumerable<string> files = Directory.Exists(path) ? Directory.GetFiles(path, "*.nxtheme") : File.Exists(path) ? [path] : [];
                    List<ThemePart> parts = [];

                    foreach (string file in files)
                    {
                        try
                        {
                            parts.Add(ThemePart.Load(file));
                        }
                        catch (Exception)
                        {
                        }
                    }

                    foreach (ThemePart part in parts.OrderBy(p => p.Target == "home" ? 0 : p.Target == "lock" ? 1 : 2))
                    {
                        if (part.Wallpaper == null)
                        {
                            continue;
                        }

                        using SkiaSharp.SKBitmap image = SkiaSharp.SKBitmap.Decode(part.Wallpaper);

                        if (image == null)
                        {
                            continue;
                        }

                        using SkiaSharp.SKBitmap small = image.Resize(new SkiaSharp.SKImageInfo(92, 54, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Opaque), SkiaSharp.SKFilterQuality.High);
                        using SkiaSharp.SKData jpeg = small?.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);

                        picture = jpeg?.ToArray();

                        if (picture != null)
                        {
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ModLoader, $"[theme] no picture for {pack}: {e.Message}");
                }

                picture = PadThumbnail(picture);
                _pictures[pack] = picture;

                return picture;
            }
        }

        // qlaunch n'affiche pas les tout petits JPEG (constate : < ~500 o -> case
        // noire, ex. test-theme 495 o, Switch 2 320-481 o) : on bourre apres le
        // marqueur de fin (ignore par les decodeurs) pour rester au-dessus.
        private const int MinThumbnail = 2048;

        private static byte[] PadThumbnail(byte[] jpeg)
        {
            if (jpeg == null || jpeg.Length >= MinThumbnail)
            {
                return jpeg;
            }

            byte[] padded = new byte[MinThumbnail];
            Array.Copy(jpeg, padded, jpeg.Length);

            return padded;
        }

        /// <summary>The wallpaper of one part file, small (92x54 JPEG), or null.</summary>
        public static byte[] PartThumbnail(string file)
        {
            try
            {
                byte[] wallpaper = ThemePart.Load(file).Wallpaper;

                if (wallpaper == null)
                {
                    return null;
                }

                using SkiaSharp.SKBitmap image = SkiaSharp.SKBitmap.Decode(wallpaper);

                if (image == null)
                {
                    return null;
                }

                using SkiaSharp.SKBitmap small = image.Resize(new SkiaSharp.SKImageInfo(92, 54, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Opaque), SkiaSharp.SKFilterQuality.High);
                using SkiaSharp.SKData jpeg = small?.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);

                return PadThumbnail(jpeg?.ToArray());
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The parts of a pack: their target and file.</summary>
        public static List<(string Target, string File)> PartsOf(string pack)
        {
            List<(string Target, string File)> parts = [];
            string path = Path.Combine(ThemesFolder, pack);
            IEnumerable<string> files = Directory.Exists(path) ? Directory.GetFiles(path, "*.nxtheme") : File.Exists(path) ? [path] : [];
            string[] order = ["home", "lock", "apps", "set", "news", "user", "psl"];

            foreach (string file in files)
            {
                try
                {
                    parts.Add((ThemePart.Load(file).Target, file));
                }
                catch (Exception)
                {
                }
            }

            return parts.OrderBy(p => Array.IndexOf(order, p.Target) is int i and >= 0 ? i : order.Length).ToList();
        }

        /// <summary>The parts of the pack chosen left out (their targets), from the themes page.</summary>
        public static HashSet<string> ExcludedTargets
        {
            get
            {
                try
                {
                    string[] lines = File.Exists(ChoicePath) ? File.ReadAllLines(ChoicePath) : [];

                    return lines.Length > 1 && lines[1].StartsWith("exclude=", StringComparison.Ordinal)
                        ? lines[1]["exclude=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet()
                        : [];
                }
                catch (Exception)
                {
                    return [];
                }
            }
        }

        /// <summary>A pack chosen with some of its parts left out.</summary>
        public static void SetChoice(string pack, IEnumerable<string> excluded)
        {
            string left = string.Join(",", excluded ?? []);

            Chosen = left.Length > 0 ? pack + "\nexclude=" + left : pack;
        }

        /// <summary>Files of a pack deleted (the pack's folder too once empty).</summary>
        public static void Delete(string pack, IEnumerable<string> files)
        {
            string root = Path.GetFullPath(ThemesFolder);

            foreach (string file in files)
            {
                string full = Path.GetFullPath(file);

                if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                {
                    File.Delete(full);
                }
            }

            string folder = Path.GetFullPath(Path.Combine(ThemesFolder, pack));

            if (folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }

            lock (_pictures)
            {
                _pictures.Remove(pack);
            }
        }

        /// <summary>The pack chosen (relative to /Themes), or null for the system's own look.</summary>
        public static string Chosen
        {
            get
            {
                try
                {
                    string chosen = File.Exists(ChoicePath) ? File.ReadAllLines(ChoicePath).FirstOrDefault()?.Trim() ?? "" : "";

                    return chosen.Length > 0 ? chosen : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
            set
            {
                try
                {
                    File.WriteAllText(ChoicePath, value ?? "");
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ModLoader, $"[theme] choice not kept: {e.Message}");
                }

                _loaded = default;
            }
        }

        private static (string Pack, List<ThemePart> Parts) _loaded;

        // The programs a theme changes, and where NXThemes Installer puts their layouts.
        private static readonly ulong[] _themedPrograms = [0x0100000000001000, 0x0100000000001007, 0x0100000000001013];

        private static string InstalledFolder(ulong programId) =>
            Path.Combine(AppDataManager.BaseDirPath, "sdcard", "atmosphere", "contents", programId.ToString("X16"), "romfs", "lyt");

        /// <summary>When a theme was last installed in atmosphere/contents (NXThemes Installer), or never.</summary>
        private static DateTime InstalledTime()
        {
            DateTime last = DateTime.MinValue;

            try
            {
                foreach (ulong programId in _themedPrograms)
                {
                    string folder = InstalledFolder(programId);

                    if (!Directory.Exists(folder))
                    {
                        continue;
                    }

                    foreach (string file in Directory.GetFiles(folder, "*.szs"))
                    {
                        DateTime time = File.GetLastWriteTimeUtc(file);

                        if (time > last)
                        {
                            last = time;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return last;
        }

        /// <summary>
        /// True when the theme installed in atmosphere/contents (NXThemes Installer) is newer than
        /// the choice made in the themes page: it is shown as it is, and the page follows it.
        /// </summary>
        public static bool InstalledWins => InstalledTime() > (File.Exists(ChoicePath) ? File.GetLastWriteTimeUtc(ChoicePath) : DateTime.MinValue);

        private static string _identified;

        /// <summary>The pack shown now (null: the system's own look).</summary>
        public static string Current => InstalledWins ? _identified : Chosen;

        /// <summary>
        /// The pack the theme installed in atmosphere/contents comes from: the one whose parts,
        /// applied here to the system's layouts, give the layouts installed (most of them: the
        /// installer may have left some out).
        /// </summary>
        public static void Identify(ulong programId, Func<string, byte[]> system, Func<string, byte[]> installed)
        {
            string best = null;
            double bestScore = 0;

            foreach (string archiveName in new[] { "ResidentMenu.szs", "Entrance.szs", "Flaunch.szs", "Set.szs", "Notification.szs" })
            {
                byte[] stock = system(archiveName);
                byte[] mods = installed(archiveName);

                if (stock == null || mods == null)
                {
                    continue;
                }

                SarcArchive stockArchive = SarcArchive.Read(stock);
                SarcArchive modsArchive = SarcArchive.Read(mods);
                List<string> changed = modsArchive.Files
                    .Where(f => f.Key.EndsWith(".bflyt", StringComparison.Ordinal) && (!stockArchive.Files.TryGetValue(f.Key, out byte[] o) || !o.AsSpan().SequenceEqual(f.Value)))
                    .Select(f => f.Key)
                    .ToList();

                if (changed.Count == 0)
                {
                    continue;
                }

                foreach (string pack in Packs())
                {
                    foreach (ThemePart part in LoadParts(pack))
                    {
                        byte[] mine;

                        try
                        {
                            mine = Apply(programId, archiveName, stock, [part]);
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        if (mine == null)
                        {
                            continue;
                        }

                        SarcArchive mineArchive = SarcArchive.Read(mine);
                        double score = (double)changed.Count(n => mineArchive.Files.TryGetValue(n, out byte[] m) && m.AsSpan().SequenceEqual(modsArchive.Files[n])) / changed.Count;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = pack;
                        }
                    }
                }

                if (best != null)
                {
                    break;
                }
            }

            _identified = bestScore >= 0.5 ? best : null;

            Logger.Info?.Print(LogClass.ModLoader, $"[theme] theme installed in atmosphere/contents: {_identified ?? "none of /Themes"} ({bestScore:P0} of its layouts)");
        }

        private static List<ThemePart> LoadParts(string pack)
        {
            string path = Path.Combine(ThemesFolder, pack);
            IEnumerable<string> files = Directory.Exists(path) ? Directory.GetFiles(path, "*.nxtheme") : File.Exists(path) ? [path] : [];
            List<ThemePart> parts = [];

            foreach (string file in files)
            {
                try
                {
                    parts.Add(ThemePart.Load(file));
                }
                catch (Exception)
                {
                }
            }

            return parts;
        }

        private static List<ThemePart> ChosenParts()
        {
            string chosen = Chosen;

            // A theme installed by NXThemes Installer after the choice: shown as it is.
            if (chosen == null || InstalledWins)
            {
                return [];
            }

            HashSet<string> excluded = ExcludedTargets;

            if (_loaded.Pack == chosen)
            {
                return _loaded.Parts;
            }

            string path = Path.Combine(ThemesFolder, chosen);
            IEnumerable<string> files = Directory.Exists(path) ? Directory.GetFiles(path, "*.nxtheme") : File.Exists(path) ? [path] : [];
            List<ThemePart> parts = [];

            foreach (string file in files)
            {
                try
                {
                    parts.Add(ThemePart.Load(file));
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ModLoader, $"[theme] {Path.GetFileName(file)} not read: {e.Message}");
                }
            }

            // The pages left out in the themes page.
            parts = parts.Where(p => !excluded.Contains(p.Target)).ToList();

            _loaded = (chosen, parts);

            return parts;
        }

        /// <summary>
        /// The theme's version of a layout archive of a system program (/lyt/name.szs, as SARC
        /// bytes), or null when the chosen theme leaves it as it is.
        /// </summary>
        public static byte[] Apply(ulong programId, string archiveName, byte[] sarc) => Apply(programId, archiveName, sarc, ChosenParts());

        /// <summary>The parts given applied to a layout archive (null when none concerns it).</summary>
        public static byte[] Apply(ulong programId, string archiveName, byte[] sarc, List<ThemePart> parts)
        {
            if (parts.Count == 0)
            {
                return null;
            }

            List<(ThemePart Theme, Part Part)> main = parts
                .Select(t => (t, _parts.FirstOrDefault(p => p.Target == t.Target && p.ProgramId == programId && p.Archive == archiveName)))
                .Where(x => x.Item2 != null)
                .ToList();

            // common.szs takes the common.json of the parts of the same program.
            List<ThemePart> common = archiveName == "common.szs"
                ? parts.Where(t => t.CommonLayout != null && _parts.Any(p => p.Target == t.Target && p.ProgramId == programId)).ToList()
                : [];

            if (main.Count == 0 && common.Count == 0)
            {
                return null;
            }

            SarcArchive archive = SarcArchive.Read(sarc);

            foreach ((ThemePart theme, Part part) in main)
            {
                try
                {

                    if (theme.Wallpaper != null)
                    {
                        ApplyWallpaper(archive, part, theme.Wallpaper);
                    }

                    foreach (Icon icon in _icons.Where(i => i.Target == theme.Target))
                    {
                        byte[] image = theme.Files.GetValueOrDefault(icon.Name + ".png") ?? theme.Files.GetValueOrDefault(icon.Name + ".dds");

                        if (image != null)
                        {
                            ApplyIcon(archive, icon, image);
                        }
                    }

                    // A HOME Menu layout made before 20.0 places the applet buttons of its time.
                    if (theme.Target == "home" && theme.Layout != null && (theme.Layout.TargetFirmware ?? 1100) < 2000)
                    {
                        ApplyLayout(archive, new LayoutPatch { Files = [LegacyAppletRow()] });
                    }

                    // A theme drawn without the online button (the default of older themes) hides it.
                    if (theme.Target == "home" && theme.Layout != null && (theme.Layout.HideOnlineBtn ?? true))
                    {
                        ApplyLayout(archive, new LayoutPatch
                        {
                            Files = [new LayoutFilePatch { FileName = "blyt/RdtBase.bflyt", Patches = [new PanePatch { PaneName = "L_BtnLR", Visible = false }] }],
                        });
                    }

                    if (theme.Layout != null)
                    {
                        ApplyLayout(archive, theme.Layout, true);
                    }

                    // The "Clear Lock" lock screen layout, made before 9.0, moved the Continue button
                    // and the status bar where they are not seen since: back in place.
                    if (theme.Target == "lock" && theme.Layout?.ID == "builtin_ClearLock")
                    {
                        ApplyLayout(archive, new LayoutPatch
                        {
                            Files =
                            [
                                new LayoutFilePatch
                                {
                                    FileName = "blyt/EntMain.bflyt",
                                    Patches =
                                    [
                                        new PanePatch { PaneName = "L_BtnResume", Position = new Vector { X = -180, Y = 0 } },
                                        new PanePatch { PaneName = "N_CntHud", Position = new Vector { X = 0, Y = 0 } },
                                    ],
                                },
                            ],
                        });
                    }

                    // The online button (the Nextendo button here) is placed by the system where the
                    // theme put something else (made for a firmware where it was not there, or not
                    // there): what the theme put there is seen, the button hidden.
                    if (theme.Target == "home")
                    {
                        HideCoveringOnlineButton(archive);
                    }

                    Logger.Info?.Print(LogClass.ModLoader, $"[theme] {theme.Name} ({theme.Target}) applied to {archiveName}");
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ModLoader, $"[theme] {theme.Name} ({theme.Target}) not applied to {archiveName}: {e.Message}");
                }
            }

            foreach (ThemePart theme in common)
            {
                try
                {
                    ApplyLayout(archive, theme.CommonLayout);
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ModLoader, $"[theme] common layout of {theme.Name} not applied: {e.Message}");
                }
            }

            return archive.Write();
        }

        private static void ApplyWallpaper(SarcArchive archive, Part part, byte[] image)
        {
            const string Textures = "timg/__Combined.bntx";

            if (!archive.Files.TryGetValue(part.MainLayout, out byte[] mainData) || !archive.Files.TryGetValue(Textures, out byte[] bntx))
            {
                return;
            }

            LayoutFile main = new(mainData);

            if (!LayoutPatcher.AddWallpaperPane(main, part.PaneName, part.Texture, part.BackgroundPanes))
            {
                return;
            }

            byte[] replaced = TextureArchive.ReplaceWithImage(bntx, part.Texture, image);

            if (replaced == null)
            {
                return;
            }

            archive.Files[part.MainLayout] = main.Write();
            archive.Files[Textures] = replaced;

            // The other layouts that drew with that texture now draw with a plain one.
            string fallback = TextureArchive.Has(replaced, part.FallbackTexture) ? part.FallbackTexture : null;

            if (fallback == null)
            {
                return;
            }

            foreach (string name in archive.Files.Keys.Where(n => n.StartsWith("blyt/", StringComparison.Ordinal) && n.EndsWith(".bflyt", StringComparison.Ordinal) && n != part.MainLayout).ToList())
            {
                LayoutFile other = new(archive.Files[name]);

                if (LayoutPatcher.RenameTexture(other, part.Texture, fallback))
                {
                    archive.Files[name] = other.Write();
                }
            }
        }

        /// <summary>
        /// The HOME Menu layouts of a theme installed in atmosphere/contents (NXThemes Installer),
        /// with the corrections made to the themes applied here (null when none is needed).
        /// </summary>
        public static byte[] FixInstalledHome(byte[] sarc)
        {
            SarcArchive archive = SarcArchive.Read(sarc);

            return HideCoveringOnlineButton(archive) ? archive.Write() : null;
        }

        private static bool HideCoveringOnlineButton(SarcArchive archive)
        {
            const string Base = "blyt/RdtBase.bflyt";

            if (!archive.Files.TryGetValue(Base, out byte[] data))
            {
                return false;
            }

            LayoutFile layout = new(data);

            if (LayoutPatcher.ScreenPlace(layout, "L_BtnLR") is not { Visible: true } button)
            {
                return false;
            }

            string[] others = ["N_MyPage", "L_BtnNoti", "L_BtnShop", "L_BtnPvr", "L_BtnCtrl", "L_BtnSet", "L_BtnPow", "L_BtnSplay", "L_BtnVgc", "L_BtnFlc"];

            foreach (string name in others)
            {
                if (LayoutPatcher.ScreenPlace(layout, name) is { Visible: true } other &&
                    Math.Abs(other.X - button.X) < 40 && Math.Abs(other.Y - button.Y) < 40)
                {
                    LayoutPatcher.Hide(layout, "L_BtnLR");
                    archive.Files[Base] = layout.Write();

                    Logger.Info?.Print(LogClass.ModLoader, $"[theme] online button hidden: the theme put {name} where it is");

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The applet buttons as before 20.0: the two buttons 20.0 added (game card, sharing)
        /// hidden, the seven others 108 apart around the middle of the screen, the height they
        /// had.
        /// </summary>
        private static LayoutFilePatch LegacyAppletRow()
        {
            string[] buttons = ["L_BtnLR", "L_BtnNoti", "L_BtnShop", "L_BtnPvr", "L_BtnCtrl", "L_BtnSet", "L_BtnPow"];
            List<PanePatch> panes =
            [
                new PanePatch { PaneName = "L_BtnSplay", Visible = false },
                new PanePatch { PaneName = "L_BtnVgc", Visible = false },
                new PanePatch { PaneName = "N_System", Position = new Vector { X = 54, Y = -186 } },
            ];

            for (int i = 0; i < buttons.Length; i++)
            {
                panes.Add(new PanePatch { PaneName = buttons[i], Position = new Vector { X = -378 + 108 * i }, Size = new Vector { X = 508, Y = 180 } });
            }

            return new LayoutFilePatch { FileName = "blyt/RdtBase.bflyt", Patches = [.. panes] };
        }

        private static void ApplyIcon(SarcArchive archive, Icon icon, byte[] image)
        {
            const string Textures = "timg/__Combined.bntx";

            if (!archive.Files.TryGetValue(icon.Layout, out byte[] layoutData) || !archive.Files.TryGetValue(Textures, out byte[] bntx))
            {
                return;
            }

            string texture = icon.Textures.FirstOrDefault(t => TextureArchive.Has(bntx, t));
            byte[] replaced = texture == null ? null : TextureArchive.ReplaceWithIcon(bntx, texture, image, icon.Width, icon.Height, icon.Channels);

            if (replaced == null)
            {
                return;
            }

            LayoutFile layout = new(layoutData);
            List<PanePatch> panes =
            [
                new PanePatch
                {
                    PaneName = icon.Pane,
                    Size = new Vector { X = icon.Width, Y = icon.Height },
                    Position = icon.Position == null ? null : new Vector { X = icon.Position[0], Y = icon.Position[1], Z = icon.Position[2] },
                    // The colour the layout gives the icon (a near white, whatever the theme of the console).
                    UsdPatches = icon.Tinted ? [new UserDataPatch { PropName = "C_W", PropValues = ["100", "100", "100", "100"], Type = 1 }] : null,
                },
            ];

            panes.AddRange(icon.Hidden.Select(name => new PanePatch { PaneName = name, Visible = false }));

            LayoutPatcher.Apply(layout, new LayoutFilePatch { FileName = icon.Layout, Patches = [.. panes] });
            LayoutPatcher.ResetTextureCoordinates(layout, icon.Pane);

            archive.Files[icon.Layout] = layout.Write();
            archive.Files[Textures] = replaced;
        }

        private static void ApplyLayout(SarcArchive archive, LayoutPatch patch, bool animations = true)
        {
            foreach (LayoutFilePatch file in patch.Files ?? [])
            {
                if (file?.FileName == null || !archive.Files.TryGetValue(file.FileName, out byte[] data))
                {
                    continue;
                }

                LayoutFile layout = new(data);

                if (LayoutPatcher.Apply(layout, file))
                {
                    archive.Files[file.FileName] = layout.Write();
                }
            }

            foreach (AnimFilePatch animation in animations ? patch.Anims ?? [] : [])
            {
                if (animation?.FileName == null || animation.AnimJson == null || !archive.Files.TryGetValue(animation.FileName, out byte[] existing))
                {
                    continue;
                }

                archive.Files[animation.FileName] = AnimationWriter.Write(animation.AnimJson, AnimationWriter.VersionOf(existing));
            }
        }
    }
}
