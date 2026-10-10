using System;
using System.Threading.Tasks;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo, HOME Menu work] "System Update" of the HOME Menu settings updates the emulator:
    /// the check reads the Nextendo releases on GitHub, the install downloads the new build,
    /// replaces the files and starts the HOME Menu again (set by the emulator's updater).
    /// </summary>
    public static class NextendoSystemUpdate
    {
        /// <summary>The newer version available, or null when up to date (or unknown).</summary>
        public static Func<Task<string>> Check { get; set; }

        /// <summary>Installs the newer version found by <see cref="Check"/> and restarts.</summary>
        public static Action Install { get; set; }
    }
}
