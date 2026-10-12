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

        /// <summary>The emulation is the HOME Menu (and what it starts): the system draws its own notifications.</summary>
        public static bool Running { get; internal set; }

        /// <summary>The console is asked to restart (true) or to turn off (false).</summary>
        public static event Action<bool> PowerRequested;

        /// <summary>
        /// The emulator is asked to start again from scratch, on the HOME Menu (after the initial
        /// setup: a restart inside the same run does not get through the end of the emulation).
        /// </summary>
        public static event Action RelaunchRequested;

        public static void RequestRelaunch()
        {
            try
            {
                RelaunchRequested?.Invoke();
            }
            catch (Exception)
            {
                // the request is lost, nothing else
            }
        }

        /// <summary>The console is asked to sleep (power menu, "Sleep Mode").</summary>
        public static event Action SleepRequested;

        public static void RequestSleep()
        {
            try
            {
                SleepRequested?.Invoke();
            }
            catch (Exception)
            {
                // the request is lost, nothing else
            }
        }

        public static void RequestPower(bool reboot)
        {
            try
            {
                PowerRequested?.Invoke(reboot);
            }
            catch (Exception)
            {
                // the request is lost, nothing else
            }
        }

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
