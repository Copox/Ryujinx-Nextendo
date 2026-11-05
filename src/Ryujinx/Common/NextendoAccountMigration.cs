using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Common
{
    internal static class NextendoAccountMigration
    {
        private static Timer _retry;
        internal static event Action Completed;

        internal static void Start()
        {
            if (_retry != null) return;
            NextendoAccount.ActiveChanged += Run;
            // Only pending legacy records are checked. Protected credentials are not revalidated.
            _retry = new Timer(_ => Run(), null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        }

        internal static void Stop()
        {
            _retry?.Dispose();
            _retry = null;
            NextendoAccount.ActiveChanged -= Run;
        }

        private static async void Run()
        {
            try
            {
                await NextendoAccount.MigrateLegacyAccountsAsync().ConfigureAwait(false);
                Completed?.Invoke();
            }
            catch (Exception)
            {
                Logger.Warning?.Print(LogClass.Application, "[Nextendo] Automatic account storage migration could not finish. Saved credentials were retained.");
            }
        }
    }
}
