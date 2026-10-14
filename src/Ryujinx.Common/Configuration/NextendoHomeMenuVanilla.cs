using System;
using System.IO;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The HOME Menu as the console has it: none of the emulator's own
    /// changes (the Nextendo logo and texts, the themes page and the "Ryujinx | Nextendo" section,
    /// the controller pictures and buttons, the profile banners and badges, the Discord avatar
    /// decorations, the rounded pictures of the profile pages, the larger friend picture of the
    /// notifications, the emulator's notifications, the virtual game cards). The online service
    /// (friends, presence) stays. Off unless the player turns it on; read when the HOME Menu starts.
    ///
    /// The changes are made for one version of the HOME Menu only (<see cref="SupportedFirmware"/>):
    /// with any other firmware they are all off, whatever the player chose, and the choice comes
    /// back as it was once that firmware is installed again.
    /// </summary>
    public static class NextendoHomeMenuVanilla
    {
        /// <summary>The firmware whose HOME Menu the Nextendo additions are made for.</summary>
        public const string SupportedFirmware = "22.5.0";

        private static bool? _choice;

        private static string FilePath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_home_vanilla.txt");

        /// <summary>
        /// The firmware installed ("" = none), set by the interface when it reads it; null while
        /// unknown (then the player's choice alone counts).
        /// </summary>
        public static string Firmware { get; set; }

        /// <summary>The additions cannot run: a firmware other than the supported one is installed.</summary>
        public static bool Forced => !string.IsNullOrEmpty(Firmware) && Firmware != SupportedFirmware;

        /// <summary>The player's own choice, kept even while it is forced.</summary>
        public static bool Choice
        {
            get
            {
                if (_choice == null)
                {
                    try
                    {
                        _choice = File.Exists(FilePath) && File.ReadAllText(FilePath).Trim() == "1";
                    }
                    catch (Exception)
                    {
                        _choice = false;
                    }
                }

                return _choice.Value;
            }
        }

        /// <summary>The HOME Menu without the additions: by choice, or because the firmware is another one.</summary>
        public static bool Enabled
        {
            get => Forced || Choice;
            set
            {
                // a forced state is never saved as the choice of the player
                if (Forced)
                {
                    return;
                }

                _choice = value;

                try
                {
                    File.WriteAllText(FilePath, value ? "1" : "0");
                }
                catch (Exception)
                {
                    // kept for this session only
                }
            }
        }
    }
}
