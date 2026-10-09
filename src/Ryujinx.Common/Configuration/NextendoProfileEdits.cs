using System;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo, HOME Menu work] A profile edited from the console itself (the user page of the
    /// HOME Menu: nickname, icon). The emulator side sends the change to the Nextendo account when
    /// the profile is the one bound to it; the account is the source of truth, so the edit is kept
    /// only when the account took it.
    /// </summary>
    public static class NextendoProfileEdits
    {
        /// <summary>(profile user id, new nickname or null, new icon JPEG or null) -> accepted.</summary>
        public static Func<string, string, byte[], bool> Push { get; set; }

        public static bool TryPush(string userId, string name, byte[] image)
        {
            try
            {
                return Push?.Invoke(userId, name, image) ?? true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
