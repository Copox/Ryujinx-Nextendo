using System;
using System.Threading.Tasks;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo, HOME Menu work] What the console's "link an account" needs from the emulator:
    /// the Nextendo sign-in in the browser, and the account's picture. Set by the emulator side.
    /// </summary>
    public static class NextendoAccountLink
    {
        /// <summary>Signs in to Nextendo in the browser; (ok, error).</summary>
        public static Func<Task<(bool ok, string error)>> SignIn { get; set; }

        /// <summary>The picture of the signed-in account (JPEG), or null.</summary>
        public static Func<byte[]> Picture { get; set; }

        /// <summary>The sign-in page of the console was answered by a Nextendo sign-in.</summary>
        public static bool SignedIn { get; set; }

        /// <summary>Set by the last import: the account already had its user on this emulator.</summary>
        public static bool AlreadyRegistered { get; set; }
    }
}
