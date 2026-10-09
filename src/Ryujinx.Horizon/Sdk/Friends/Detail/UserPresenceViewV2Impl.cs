using System.Runtime.InteropServices;

namespace Ryujinx.Horizon.Sdk.Friends.Detail
{
    // The presence of the user as SDK 19+ returns it (20601): 8 bytes more than the first form.
    [StructLayout(LayoutKind.Sequential, Size = Size)]
    struct UserPresenceViewV2Impl
    {
        public const int Size = 0xE8;
    }
}
