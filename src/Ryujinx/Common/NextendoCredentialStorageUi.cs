using Ryujinx.Ava.Common.Locale;
using Ryujinx.Common.Configuration;

namespace Ryujinx.Ava.Common
{
    internal static class NextendoCredentialStorageUi
    {
        private static bool NeedsAttention(NextendoCredentialStorageState state) => state is
                    NextendoCredentialStorageState.Conflict or NextendoCredentialStorageState.IdentityMismatch or
                    NextendoCredentialStorageState.Rejected or NextendoCredentialStorageState.RecoveryRequired or NextendoCredentialStorageState.Failed;

        private static (NextendoCredentialStorageState State, bool OtherAccount) Notice
        {
            get
            {
                NextendoCredentialStorageState active = NextendoAccount.StorageState;
                NextendoCredentialStorageState background = NextendoAccount.BackgroundMigrationIssue;
                // An inactive record must not be described as disabling the active session.
                return NeedsAttention(active) ? (active, false) : NeedsAttention(background) ? (background, true) : (active, false);
            }
        }

        internal static bool RequiresAttention => NeedsAttention(Notice.State);

        internal static string Message
        {
            get
            {
                var notice = Notice;
                if (notice.OtherAccount)
                {
                    return LocaleManager.Instance[notice.State == NextendoCredentialStorageState.IdentityMismatch
                        ? LocaleKeys.Dialog_Nextendo_StorageOtherAccountMismatch : LocaleKeys.Dialog_Nextendo_StorageOtherAccountAttention];
                }
                return LocaleManager.Instance[notice.State switch
                {
                    NextendoCredentialStorageState.Protected => LocaleKeys.Dialog_Nextendo_StorageProtected,
                    NextendoCredentialStorageState.LegacyPending => LocaleKeys.Dialog_Nextendo_StoragePending,
                    NextendoCredentialStorageState.Unsupported => LocaleKeys.Dialog_Nextendo_StorageUnsupported,
                    NextendoCredentialStorageState.Conflict => LocaleKeys.Dialog_Nextendo_StorageConflict,
                    NextendoCredentialStorageState.IdentityMismatch => LocaleKeys.Dialog_Nextendo_StorageMismatch,
                    NextendoCredentialStorageState.Rejected => LocaleKeys.Dialog_Nextendo_StorageRejected,
                    NextendoCredentialStorageState.Unavailable => LocaleKeys.Dialog_Nextendo_StorageUnavailable,
                    NextendoCredentialStorageState.RecoveryRequired => LocaleKeys.Dialog_Nextendo_StorageRecovery,
                    _ => LocaleKeys.Dialog_Nextendo_StorageFailed,
                }];
            }
        }
    }
}
