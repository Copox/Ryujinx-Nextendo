using System.Runtime.InteropServices;

namespace Ryujinx.Horizon.Sdk.Friends.Detail
{
    // What a friend is doing right now, as the friend page of SDK 20+ asks for it (20111).
    [StructLayout(LayoutKind.Sequential, Size = Size)]
    struct FriendPresenceV2Impl
    {
        public const int Size = 0x80;
    }
}
