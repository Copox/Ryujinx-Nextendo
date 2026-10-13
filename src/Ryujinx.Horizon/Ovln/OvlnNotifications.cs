using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Ryujinx.Horizon.Ovln
{
    /// <summary>[Nextendo, HOME Menu notifications] The overlay notifications, for the emulator.</summary>
    public static class OvlnNotifications
    {
        private static readonly BlockingCollection<(uint Tag, byte[] Data)> _pending = new();
        private static Thread _thread;

        /// <summary>
        /// A notification to the overlay applet (tag and data as it reads them), sent in its turn:
        /// one on screen at a time with the emulator's own (see <see cref="NotificationTurn"/>).
        /// </summary>
        public static void Post(uint tag, ReadOnlySpan<byte> data)
        {
            lock (_pending)
            {
                _thread ??= new Thread(Run) { IsBackground = true, Name = "Nextendo.OvlnNotifications" };

                if (!_thread.IsAlive)
                {
                    _thread.Start();
                }
            }

            _pending.Add((tag, data.ToArray()));
        }

        /// <summary>A message to the overlay applet that is no notification: sent at once.</summary>
        public static void PostNow(uint tag, ReadOnlySpan<byte> data)
        {
            OvlnQueue.Notify(tag, data);
        }

        private static void Run()
        {
            foreach ((uint tag, byte[] data) in _pending.GetConsumingEnumerable())
            {
                NotificationTurn.Take(NotificationTurn.OverlaySeconds);
                OvlnQueue.Notify(tag, data);
            }
        }
    }
}
