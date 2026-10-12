using Ryujinx.Ava.Common;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Common;
using Ryujinx.Common.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Systems
{
    /// <summary>
    /// [Nextendo, HOME Menu work] "Update available" as a notification of the console, while the
    /// HOME Menu runs: checked when it starts and every half hour, said once per new version.
    /// </summary>
    internal static class NextendoUpdateNotifier
    {
        private static readonly TimeSpan _first = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan _every = TimeSpan.FromMinutes(30);

        private static Timer _timer;
        private static string _told;

        public static void Start()
        {
            _timer ??= new Timer(_ => _ = CheckAsync(), null, _first, _every);
        }

        private static async Task CheckAsync()
        {
            if (!Ryujinx.HLE.HOS.HomeMenuStatus.Running)
            {
                return;
            }

            string version = await Updater.NextendoNewerVersionAsync();

            if (version == null || version == _told)
            {
                return;
            }

            _told = version;

            Logger.Info?.Print(LogClass.Application, $"[Nextendo][notif] update available: {version}");

            NextendoConsoleToasts.Show(
                LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_ConsoleUpdateTitle],
                $"Ryujinx | Nextendo {version}",
                EmbeddedResources.Read("Ryujinx/Assets.UIImages.Logo_Nextendo.png"));
        }
    }
}
