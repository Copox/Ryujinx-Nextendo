namespace Ryujinx.HLE.HOS.Services.Sockets.Bsd
{
    internal static class BsdPollSchedulingPolicy
    {
        internal const ulong MarioKart8Deluxe = 0x0100152000022000;

        internal static bool ShouldDefer(bool hasEventFd, int timeout, ulong programId)
        {
            // Keep gRPC eventfd scheduling and the finite-timeout optimization for
            // other games. MK8's ordinary NEX polls retain the 1.9.9 blocking path.
            return hasEventFd || (timeout > 0 && programId != MarioKart8Deluxe);
        }
    }
}
