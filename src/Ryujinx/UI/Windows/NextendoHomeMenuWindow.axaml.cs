using Avalonia.Media;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Helper;
using System;
using System.IO;

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

            // [Nextendo] the HOME Menu as the console has it (taken when it starts again)
            // With another firmware than the one the additions are made for, they are off and the
            // option is locked; with none, the HOME Menu cannot start at all.
            Ryujinx.HLE.FileSystem.SystemVersion firmware = RyujinxApp.MainWindow?.ViewModel.ContentManager.GetCurrentFirmwareVersion();

            NextendoHomeMenuVanilla.Firmware = firmware == null ? "" : $"{firmware.Major}.{firmware.Minor}.{firmware.Micro}";

            VanillaCheck.IsChecked = NextendoHomeMenuVanilla.Enabled;
            VanillaCheck.IsEnabled = !NextendoHomeMenuVanilla.Forced;
            VanillaCheck.IsCheckedChanged += (_, _) => NextendoHomeMenuVanilla.Enabled = VanillaCheck.IsChecked == true;

            if (firmware == null)
            {
                FirmwareHint.Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_HomeMenuNoFirmware];
                FirmwareHint.IsVisible = true;
            }
            else if (NextendoHomeMenuVanilla.Forced)
            {
                FirmwareHint.Text = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_QLaunchOtherFirmwareHint]
                    .Replace("{0}", NextendoHomeMenuVanilla.Firmware)
                    .Replace("{1}", NextendoHomeMenuVanilla.SupportedFirmware);
                FirmwareHint.IsVisible = true;
            }

            // [Nextendo] the game cards (.xci) as virtual game cards, or as games of the HOME Menu
            XciCardsCheck.IsChecked = Ryujinx.HLE.HOS.NextendoVirtualCards.Option;
            XciCardsCheck.IsCheckedChanged += (_, _) => Ryujinx.HLE.HOS.NextendoVirtualCards.Enabled = XciCardsCheck.IsChecked == true;

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

        private void ShowAlbumHint()
        {
            string sd = SdCard;
            bool sphaira = File.Exists(Path.Combine(sd, "switch", "sphaira", "sphaira.nro"));
            bool hbmenu = File.Exists(Path.Combine(sd, "hbmenu.nro"));
            string wanted = NextendoHomeMenuSettings.AlbumMenu;

            AlbumHint.Text = wanted == "hbmenu"
                ? (hbmenu ? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_QLaunchHbmenuPresent] : LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_QLaunchHbmenuMissing])
                : (sphaira ? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_QLaunchSphairaPresent] : LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_QLaunchSphairaMissing]);
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
                    ? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_QLaunchNoTheme]
                    : LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_QLaunchThemeRemoved], ok: true);
            }
            catch (Exception ex)
            {
                ShowStatus(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_QLaunchThemeRemoveFailed] + ex.Message, ok: false);
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
