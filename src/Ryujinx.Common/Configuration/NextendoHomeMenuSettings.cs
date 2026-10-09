using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The settings of the HOME Menu (qLaunch), set from the
    /// "Nextendo > qLaunch" window. Kept in nextendo_home_menu.json next to the other data. The two
    /// the emulated system reads itself (the menu of the album, the colour theme) are also written
    /// to the files it reads.
    /// </summary>
    public static class NextendoHomeMenuSettings
    {
        private static readonly object _lock = new();
        private static bool _loaded;

        private static bool _autoStart;
        private static bool _loadInBackground = true;
        private static bool _fullscreen;
        private static string _albumMenu = "sphaira";
        private static string _theme = "auto";

        private static string FilePath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_home_menu.json");
        private static string AlbumMenuPath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_album_menu.txt");
        private static string ColorSetPath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_color_set.txt");

        /// <summary>The HOME Menu starts with the emulator.</summary>
        public static bool AutoStart { get { Load(); return _autoStart; } set { Load(); _autoStart = value; Save(); } }

        /// <summary>At start, the window stays hidden while the menu loads, and shows with it.</summary>
        public static bool LoadInBackground { get { Load(); return _loadInBackground; } set { Load(); _loadInBackground = value; Save(); } }

        /// <summary>The HOME Menu opens in full screen.</summary>
        public static bool Fullscreen { get { Load(); return _fullscreen; } set { Load(); _fullscreen = value; Save(); } }

        /// <summary>The homebrew menu of the album: "sphaira" or "hbmenu".</summary>
        public static string AlbumMenu
        {
            get { Load(); return _albumMenu; }
            set
            {
                Load();
                _albumMenu = value == "hbmenu" ? "hbmenu" : "sphaira";
                Save();

                try
                {
                    File.WriteAllText(AlbumMenuPath, _albumMenu);
                }
                catch (Exception)
                {
                    // the menu keeps its default
                }
            }
        }

        /// <summary>The colour theme of the menu: "auto" (black with a custom theme), "white" or "black".</summary>
        public static string Theme
        {
            get { Load(); return _theme; }
            set
            {
                Load();
                _theme = value is "white" or "black" ? value : "auto";
                Save();

                try
                {
                    if (_theme == "auto")
                    {
                        File.Delete(ColorSetPath);
                    }
                    else
                    {
                        File.WriteAllText(ColorSetPath, _theme == "black" ? "1" : "0");
                    }
                }
                catch (Exception)
                {
                    // the menu keeps its theme
                }
            }
        }

        private static void Load()
        {
            lock (_lock)
            {
                if (_loaded)
                {
                    return;
                }

                _loaded = true;

                try
                {
                    if (File.Exists(AlbumMenuPath))
                    {
                        _albumMenu = File.ReadAllText(AlbumMenuPath).Trim() == "hbmenu" ? "hbmenu" : "sphaira";
                    }

                    if (File.Exists(ColorSetPath))
                    {
                        _theme = File.ReadAllText(ColorSetPath).Trim() == "1" ? "black" : "white";
                    }

                    if (!File.Exists(FilePath))
                    {
                        return;
                    }

                    JsonNode root = JsonNode.Parse(File.ReadAllText(FilePath));

                    _autoStart = root?["auto_start"]?.GetValue<bool>() ?? false;
                    _loadInBackground = root?["load_in_background"]?.GetValue<bool>() ?? true;
                    _fullscreen = root?["fullscreen"]?.GetValue<bool>() ?? false;
                }
                catch (Exception)
                {
                    // defaults
                }
            }
        }

        private static void Save()
        {
            lock (_lock)
            {
                try
                {
                    JsonObject root = new()
                    {
                        ["auto_start"] = _autoStart,
                        ["load_in_background"] = _loadInBackground,
                        ["fullscreen"] = _fullscreen,
                    };

                    File.WriteAllText(FilePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception)
                {
                    // kept for this session
                }
            }
        }
    }
}
