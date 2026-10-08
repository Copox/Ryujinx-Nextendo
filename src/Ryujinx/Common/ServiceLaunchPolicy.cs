using System;
namespace Ryujinx.Ava.Common
{
    public sealed record ServiceLaunchDecision(bool Managed, bool OnlineAllowed, bool VersionMismatch, string RequiredVersions, string Message)
    {
        public static ServiceLaunchDecision Unmanaged { get; } = new(false, true, false, "", "");
        public static ServiceLaunchDecision Unavailable { get; } = new(true, false, false, "", "Service requirements could not be verified. Refresh service information or continue offline.");
    }
    public static class ServiceLaunchPolicy
    {
        public static void ResetOnlineBlock()
        {
            Ryujinx.Common.Configuration.NextendoAccount.OnlineBlocked = false;
        }

        public static void UpdateOnlineBlock(bool serviceBlocked, bool versionMismatch, ServiceLaunchDecision decision)
        {
            Ryujinx.Common.Configuration.NextendoAccount.OnlineBlocked =
                serviceBlocked || versionMismatch || (decision.Managed && !decision.OnlineAllowed);
        }

        public static ServiceLaunchDecision Evaluate(bool known, bool fresh, ServiceListing listing, string installed)
        {
            if (!fresh)
                return ServiceLaunchDecision.Unavailable;
            if (!known)
                return ServiceLaunchDecision.Unmanaged;
            if (listing == null)
                return ServiceLaunchDecision.Unavailable;
            if (!listing.Enabled)
                return new(true, false, false, "", "This title is currently disabled on Nextendo. You can continue offline.");
            ServiceVersionStatus version = listing.Evaluate(installed);
            if (version == ServiceVersionStatus.NotAccepted)
                return new(true, false, true, string.Join(" / ", listing.Accepted), "");
            if (!listing.CanEvaluate)
                return new(true, false, false, "", "This client cannot verify all mandatory service requirements (including any required DLC). Update the client if a compatible release is available, or continue offline.");
            if (version != ServiceVersionStatus.Accepted)
                return new(true, false, false, "", "The installed game version could not be verified. You can continue offline.");
            return new(true, true, false, "", "");
        }
    }
}
