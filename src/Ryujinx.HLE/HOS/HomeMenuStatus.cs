using System;

namespace Ryujinx.HLE.HOS
{
    // What the HOME Menu has on screen, for whoever wants to tell (the Discord presence).
    public static class HomeMenuStatus
    {
        public const string ProgramIdText = "0100000000001000";

        // Name of the library applet in the foreground ("MyPage", "PhotoViewer"...), empty on the menu itself.
        public static string Applet { get; private set; } = "";

        public static event Action Changed;

        internal static void Set(string applet)
        {
            if (Applet != applet)
            {
                Applet = applet;

                // Another screen: what was looked at inside the previous one no longer holds.
                Ryujinx.Common.Configuration.NextendoActivity.Set("");

                try
                {
                    Changed?.Invoke();
                }
                catch (Exception)
                {
                    // A listener's failure is not the applet's business.
                }
            }
        }
    }
}
