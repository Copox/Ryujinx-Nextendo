using Ryujinx.Common.Logging;
using Ryujinx.Horizon.Ovln.Ipc;
using System;
using System.Collections.Generic;

namespace Ryujinx.Horizon.Ovln
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The overlay notifications of the system: what a sender (the HOME
    /// Menu, the friends service) sends reaches every receiver (the overlay applet that draws them).
    /// </summary>
    static class OvlnQueue
    {
        private static readonly object _lock = new();
        private static readonly List<Receiver> _receivers = [];

        // "A friend is online" (the user, the friend, the time away): its frame is painted for that friend.
        private const uint FriendOnlineTag = 0x1B5A;

        /// <summary>
        /// The system starts (again, after a restart of the emulation): the receivers of the one
        /// before are gone, their events with them.
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _receivers.Clear();
            }
        }

        public static void Add(Receiver receiver)
        {
            lock (_lock)
            {
                _receivers.Add(receiver);
            }
        }

        public static void Send(ReadOnlySpan<byte> message, bool front)
        {
            byte[] copy = message.ToArray();

            Logger.Info?.Print(LogClass.Service, $"[ovln] message tag 0x{BitConverter.ToUInt32(copy, 0):X} size {BitConverter.ToUInt32(copy, 4)} : {Convert.ToHexString(copy, 8, Math.Min(0x40, copy.Length - 8))}");

            // [Nextendo] Every notification shares one frame, that the emulator paints for a friend
            // coming online (their nameplate, the icon of their game): any other notification gets
            // the plain frame back ("Friends online: 7" showed the icon of the friend before).
            if (BitConverter.ToUInt32(copy, 0) != FriendOnlineTag)
            {
                Ryujinx.Common.Configuration.NextendoFriends.TellFriendNotificationShown(0);
            }
            else if (copy.Length >= 0x20)
            {
                // the frame of THIS friend, now that the overlay gets the notification: painted when
                // it was posted, the second of two friends coming online together got the frame of
                // the first (seconds before its turn), without the icon of its game
                Ryujinx.Common.Configuration.NextendoFriends.TellFriendNotificationShown(BitConverter.ToUInt64(copy, 8 + 0x10));
            }

            Receiver[] receivers;

            lock (_lock)
            {
                receivers = [.. _receivers];
            }

            foreach (Receiver receiver in receivers)
            {
                receiver.Push(copy, front);
            }
        }

        private static readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _pending = new();

        /// <summary>
        /// A notification the emulator makes itself (tag and data as the overlay reads them). Kept
        /// until a thread of the system sends it (OvlnNotifierMain): the receivers' events can only
        /// be signalled from one.
        /// </summary>
        public static void Notify(uint tag, ReadOnlySpan<byte> data)
        {
            byte[] message = new byte[0x80];

            BitConverter.TryWriteBytes(message.AsSpan(0, 4), tag);
            BitConverter.TryWriteBytes(message.AsSpan(4, 4), (uint)Math.Min(data.Length, 0x78));
            data[..Math.Min(data.Length, 0x78)].CopyTo(message.AsSpan(8));

            _pending.Enqueue(message);
        }

        /// <summary>The notifications made by the emulator, sent (from a thread of the system).</summary>
        public static void SendPending()
        {

            while (_pending.TryDequeue(out byte[] message))
            {
                Send(message, false);
            }
        }
    }
}
