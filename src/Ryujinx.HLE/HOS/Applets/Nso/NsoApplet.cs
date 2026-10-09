using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Am.AppletAE;
using System;
using System.Diagnostics;

namespace Ryujinx.HLE.HOS.Applets.Nso
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The "Nintendo Switch Online" entry of the HOME Menu. On a
    /// console it is a web page (the place where the profile picture is chosen from the icon
    /// collection); here it opens the Nextendo account page, where the same picture is
    /// composed, in the computer's browser.
    /// </summary>
    internal class NsoApplet : IApplet
    {
        // The menu reads back a web return value of exactly this size.
        private const int ReturnValueSize = 0x2000;

        private readonly Horizon _system;

        public event EventHandler AppletStateChanged;

        public NsoApplet(Horizon system)
        {
            _system = system;
        }

        public ResultCode Start(AppletSession normalSession, AppletSession interactiveSession)
        {
            string url = NextendoEndpoint.Canonical + "/compte";

            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

                Logger.Info?.Print(LogClass.ServiceAm, $"[Nextendo] Nintendo Switch Online entry: opened {url}");
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.ServiceAm, $"[Nextendo] Nintendo Switch Online entry: could not open {url}: {e.Message}");
            }

            normalSession.Push(new byte[ReturnValueSize]);
            AppletStateChanged?.Invoke(this, null);
            _system.ReturnFocus();

            return ResultCode.Success;
        }
    }
}
