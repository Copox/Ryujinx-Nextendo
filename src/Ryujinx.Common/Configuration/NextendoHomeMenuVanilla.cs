using System;
using System.IO;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The HOME Menu as the console has it: none of the emulator's own
    /// changes (the Nextendo logo and texts, the themes page and the "Ryujinx | Nextendo" section,
    /// the controller pictures and buttons, the profile banners and badges, the rounded pictures of
    /// the profile pages, the larger friend picture of the notifications, the emulator's
    /// notifications). The online service
    /// (friends, presence) stays. Off unless the player turns it on; read when the HOME Menu starts.
    /// </summary>
    public static class NextendoHomeMenuVanilla
    {
        private static bool? _enabled;

        private static string FilePath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_home_vanilla.txt");

        public static bool Enabled
        {
            get
            {
                if (_enabled == null)
                {
                    try
                    {
                        _enabled = File.Exists(FilePath) && File.ReadAllText(FilePath).Trim() == "1";
                    }
                    catch (Exception)
                    {
                        _enabled = false;
                    }
                }

                return _enabled.Value;
            }
            set
            {
                _enabled = value;

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
