using Avalonia.Media;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Helper;
using System;
using System.IO;
using System.Linq;

namespace Ryujinx.Ava.UI.Windows
{
    /// <summary>
    /// [Nextendo, HOME Menu work] "Nextendo > qLaunch": the settings of the HOME Menu. Each change
    /// is saved at once (NextendoHomeMenuSettings).
    /// </summary>
    public partial class NextendoHomeMenuWindow : StyleableAppWindow
    {
        private static NextendoHomeMenuWindow _open;

        private static string SdCard => Path.Combine(AppDataManager.BaseDirPath, AppDataManager.DefaultSdcardDir);

        public static void Open()
        {
            if (_open != null)
            {
                _open.Activate();

                return;
            }

            _open = new NextendoHomeMenuWindow();
            _open.Closed += (_, _) => _open = null;
            _open.Show(RyujinxApp.MainWindow);
        }

        public NextendoHomeMenuWindow() : base(useCustomTitleBar: true, 37)
        {
            Title = "qLaunch";

            InitializeComponent();

            AutoStartCheck.IsChecked = NextendoHomeMenuSettings.AutoStart;
            BackgroundCheck.IsChecked = NextendoHomeMenuSettings.LoadInBackground;
            BackgroundCheck.IsEnabled = NextendoHomeMenuSettings.AutoStart;
            FullscreenCheck.IsChecked = NextendoHomeMenuSettings.Fullscreen;

            SphairaRadio.IsChecked = NextendoHomeMenuSettings.AlbumMenu != "hbmenu";
            HbmenuRadio.IsChecked = NextendoHomeMenuSettings.AlbumMenu == "hbmenu";

            ThemeAutoRadio.IsChecked = NextendoHomeMenuSettings.Theme == "auto";
            ThemeWhiteRadio.IsChecked = NextendoHomeMenuSettings.Theme == "white";
            ThemeBlackRadio.IsChecked = NextendoHomeMenuSettings.Theme == "black";

            ShowAlbumHint();
            FillHomeThemes();

            AutoStartCheck.IsCheckedChanged += (_, _) =>
            {
                NextendoHomeMenuSettings.AutoStart = AutoStartCheck.IsChecked == true;
                BackgroundCheck.IsEnabled = AutoStartCheck.IsChecked == true;
            };
            BackgroundCheck.IsCheckedChanged += (_, _) => NextendoHomeMenuSettings.LoadInBackground = BackgroundCheck.IsChecked == true;
            FullscreenCheck.IsCheckedChanged += (_, _) => NextendoHomeMenuSettings.Fullscreen = FullscreenCheck.IsChecked == true;

            SphairaRadio.IsCheckedChanged += (_, _) =>
            {
                if (SphairaRadio.IsChecked == true)
                {
                    NextendoHomeMenuSettings.AlbumMenu = "sphaira";
                    ShowAlbumHint();
                }
            };
            HbmenuRadio.IsCheckedChanged += (_, _) =>
            {
                if (HbmenuRadio.IsChecked == true)
                {
                    NextendoHomeMenuSettings.AlbumMenu = "hbmenu";
                    ShowAlbumHint();
                }
            };

            ThemeAutoRadio.IsCheckedChanged += (_, _) => { if (ThemeAutoRadio.IsChecked == true) NextendoHomeMenuSettings.Theme = "auto"; };
            ThemeWhiteRadio.IsCheckedChanged += (_, _) => { if (ThemeWhiteRadio.IsChecked == true) NextendoHomeMenuSettings.Theme = "white"; };
            ThemeBlackRadio.IsCheckedChanged += (_, _) => { if (ThemeBlackRadio.IsChecked == true) NextendoHomeMenuSettings.Theme = "black"; };

            OpenSdButton.Click += (_, _) => OpenFolder(SdCard);
            OpenThemesButton.Click += (_, _) => OpenFolder(Path.Combine(SdCard, "themes"));
            ResetThemeButton.Click += (_, _) => PutThemeAside();

            LaunchButton.Click += (_, _) =>
            {
                RyujinxApp.MainWindow?.LaunchHomeMenu();
                Close();
            };
            CloseButton.Click += (_, _) => Close();
        }

        // [Nextendo, HOME Menu themes] The packs of the SD card; the one chosen applies to every part.
        private void FillHomeThemes()
        {
            const string None = "Aucun (l'apparence de la Switch)";
            System.Collections.Generic.List<string> packs = Ryujinx.HLE.HOS.Themes.NextendoThemes.Packs();
            string chosen = Ryujinx.HLE.HOS.Themes.NextendoThemes.Chosen;

            HomeThemeCombo.ItemsSource = new[] { None }.Concat(packs).ToList();
            HomeThemeCombo.SelectedIndex = chosen != null && packs.Contains(chosen) ? packs.IndexOf(chosen) + 1 : 0;

            HomeThemeCombo.SelectionChanged += (_, _) =>
            {
                string pick = HomeThemeCombo.SelectedItem as string;

                Ryujinx.HLE.HOS.Themes.NextendoThemes.Chosen = pick == None ? null : pick;

                bool installed = Directory.Exists(Path.Combine(SdCard, "atmosphere", "contents", "0100000000001000", "romfs", "lyt"));

                ShowStatus(pick == None
                    ? "Le menu HOME reprendra l'apparence de la Switch au prochain démarrage."
                    : "« " + pick + " » s'appliquera au prochain démarrage du menu HOME." +
                      (installed ? " Un thème installé par NXThemes est aussi présent : retire-le pour que les deux ne se mélangent pas." : ""), ok: true);
            };
        }

        private void ShowAlbumHint()
        {
            string sd = SdCard;
            bool sphaira = File.Exists(Path.Combine(sd, "switch", "sphaira", "sphaira.nro"));
            bool hbmenu = File.Exists(Path.Combine(sd, "hbmenu.nro"));
            string wanted = NextendoHomeMenuSettings.AlbumMenu;

            AlbumHint.Text = wanted == "hbmenu"
                ? (hbmenu ? "hbmenu.nro est présent à la racine de la carte SD." : "hbmenu.nro est absent de la carte SD : l'album ouvrira Sphaira.")
                : (sphaira ? "Sphaira est présent (switch/sphaira/sphaira.nro)." : "Sphaira est absent de la carte SD : l'album ouvrira hbmenu.");
        }

        private static void OpenFolder(string path)
        {
            Directory.CreateDirectory(path);
            OpenHelper.OpenFolder(path);
        }

        // The theme is put aside, not deleted: its files go to "atmosphere/themes_retires/<date>".
        private void PutThemeAside()
        {
            string sd = SdCard;
            string aside = Path.Combine(sd, "atmosphere", "themes_retires", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            int moved = 0;

            try
            {
                foreach (string programId in new[] { "0100000000001000", "0100000000001013", "0100000000001007" })
                {
                    string layouts = Path.Combine(sd, "atmosphere", "contents", programId, "romfs", "lyt");

                    if (Directory.Exists(layouts))
                    {
                        Directory.CreateDirectory(Path.Combine(aside, programId));
                        Directory.Move(layouts, Path.Combine(aside, programId, "lyt"));
                        moved++;
                    }
                }

                string patches = Path.Combine(sd, "atmosphere", "exefs_patches", "NxThemesInstaller");

                if (Directory.Exists(patches))
                {
                    Directory.Move(patches, Path.Combine(aside, "NxThemesInstaller"));
                    moved++;
                }

                ShowStatus(moved == 0
                    ? "Aucun thème installé."
                    : "Thème retiré (mis de côté dans atmosphere/themes_retires). Redémarre le menu HOME pour retrouver le thème d'origine.", ok: true);
            }
            catch (Exception ex)
            {
                ShowStatus("Impossible de retirer le thème : " + ex.Message, ok: false);
            }
        }

        private void ShowStatus(string text, bool ok)
        {
            StatusText.Text = text;
            StatusText.Foreground = ok ? Brushes.MediumSeaGreen : Brushes.IndianRed;
            StatusText.IsVisible = true;
        }
    }
}
