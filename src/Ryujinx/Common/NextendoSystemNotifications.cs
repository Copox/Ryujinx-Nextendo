using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.Horizon.Ovln;
using System;
using System.Globalization;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The notifications of the console itself, drawn by its overlay
    /// applet over the HOME Menu and the games (the messages it reads, found in overlayDisp 22.5.0):
    /// for the user bound to the Nextendo account.
    /// </summary>
    public static class NextendoSystemNotifications
    {
        // "Friend request received." (the user).
        private const uint FriendRequestTag = 0x1B5B;

        // "Friends online: N" (the user, then the number of friends online).
        private const uint FriendsOnlineTag = 0x1B59;

        // A friend online, with the game being played (the user, the friend's id, then how long
        // the friend was away in seconds: under 30 minutes the console says nothing).
        private const uint FriendOnlineTag = 0x1B5A;

        public static void FriendOnline(ulong friendId)
        {
            if (UserBytes() is not { } user)
            {
                return;
            }

            byte[] data = new byte[0x20];

            user.CopyTo(data, 0);
            BitConverter.TryWriteBytes(data.AsSpan(0x10, 8), friendId);
            BitConverter.TryWriteBytes(data.AsSpan(0x18, 4), 3600u);

            OvlnNotifications.Post(FriendOnlineTag, data);

            Logger.Info?.Print(LogClass.Application, $"[Nextendo][notif] console notification: friend {friendId:X} online");
        }

        public static void FriendRequestReceived()
        {
            if (UserBytes() is { } user)
            {
                OvlnNotifications.Post(FriendRequestTag, user);

                Logger.Info?.Print(LogClass.Application, "[Nextendo][notif] console notification: friend request received");
            }
        }

        public static void FriendsOnline(int count)
        {
            if (UserBytes() is not { } user || count <= 0)
            {
                return;
            }

            byte[] data = new byte[0x20];

            user.CopyTo(data, 0);
            BitConverter.TryWriteBytes(data.AsSpan(0x10, 8), (ulong)count);

            OvlnNotifications.Post(FriendsOnlineTag, data);

            Logger.Info?.Print(LogClass.Application, $"[Nextendo][notif] console notification: {count} friend(s) online");
        }

        // The user bound to the Nextendo account, as the console writes a user (high half, then
        // low half, each little-endian).
        private static byte[] UserBytes()
        {
            string id = NextendoAccount.ProfileUserId;

            if (string.IsNullOrEmpty(id) || id.Length != 32 ||
                !ulong.TryParse(id.AsSpan(0, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong high) ||
                !ulong.TryParse(id.AsSpan(16, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong low))
            {
                return null;
            }

            byte[] user = new byte[0x10];

            BitConverter.TryWriteBytes(user.AsSpan(0, 8), high);
            BitConverter.TryWriteBytes(user.AsSpan(8, 8), low);

            return user;
        }
    }
}
