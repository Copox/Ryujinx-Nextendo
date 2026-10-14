using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Applets.Browser;
using Ryujinx.HLE.HOS.Services.Am.AppletAE;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Ryujinx.HLE.HOS.Applets
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The sign-in page a console opens to link a Nintendo Account
    /// (applet 0x36 on recent firmware, started by the user import). Here the sign-in is the
    /// Nextendo one, in the browser of the computer; when it is over the applet answers with the
    /// address the procedure waits for, as the web page would.
    /// </summary>
    internal class NextendoLoginApplet : IApplet
    {
        private const string CallbackUri = "nintendo://nextendo.link/callback";

        private readonly Horizon _system;
        private AppletSession _normalSession;
        private bool _ok;

        public event EventHandler AppletStateChanged;

        public NextendoLoginApplet(Horizon system)
        {
            _system = system;
        }

        public ResultCode Start(AppletSession normalSession, AppletSession interactiveSession)
        {
            _normalSession = normalSession;

            while (_normalSession.TryPop(out byte[] data))
            {
                Logger.Info?.Print(LogClass.ServiceAm, $"[login] in-data of {data.Length} bytes");
            }

            Task.Run(async () =>
            {
                try
                {
                    if (NextendoAccountLink.SignIn != null)
                    {
                        (bool ok, string error) = await NextendoAccountLink.SignIn();

                        _ok = ok && NextendoAccount.IsLinked;

                        Logger.Info?.Print(LogClass.ServiceAm, $"[login] Nextendo sign-in {(_ok ? "done, " + NextendoAccount.Username : "failed: " + error)}");
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ServiceAm, $"[login] sign-in failed: {e.Message}");
                }

                NextendoAccountLink.SignedIn = _ok;

                // The answer the user import reads (0x1010 bytes): the nickname of the account at
                // 0x8 (found by the import page showing what sat there).
                byte[] output = new byte[0x1010];

                if (_ok)
                {
                    BitConverter.TryWriteBytes(output.AsSpan(0, 4), (uint)WebExitReason.LastUrl);

                    byte[] nick = Encoding.UTF8.GetBytes(NextendoAccount.Username ?? "");

                    nick.AsSpan(0, Math.Min(nick.Length, 0x20)).CopyTo(output.AsSpan(0x8));
                }
                else
                {
                    BitConverter.TryWriteBytes(output.AsSpan(0, 4), (uint)WebExitReason.BackButton);
                }

                _normalSession.Push(output);

                AppletStateChanged?.Invoke(this, null);
            });

            return ResultCode.Success;
        }

        public ResultCode GetResult()
        {
            return ResultCode.Success;
        }
    }
}
