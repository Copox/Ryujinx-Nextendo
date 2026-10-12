using System;
using System.Threading;

namespace Ryujinx.Horizon.Ovln
{
    /// <summary>
    /// [Nextendo, HOME Menu notifications] One notification on screen at a time, as on the console:
    /// those of the overlay applet and the emulator's own cards wait for each other's turn.
    /// </summary>
    public static class NotificationTurn
    {
        // How long the overlay applet shows a notification, slides included (measured).
        public const double OverlaySeconds = 5.0;

        private static readonly object _lock = new();
        private static DateTime _freeAt = DateTime.MinValue;

        /// <summary>Waits for the screen to be free, then holds it for the given time.</summary>
        public static void Take(double seconds)
        {
            while (true)
            {
                TimeSpan wait;

                lock (_lock)
                {
                    DateTime now = DateTime.UtcNow;

                    if (now >= _freeAt)
                    {
                        _freeAt = now.AddSeconds(seconds);

                        return;
                    }

                    wait = _freeAt - now;
                }

                Thread.Sleep(wait);
            }
        }

        /// <summary>The notification has gone before its time: the next one may come.</summary>
        public static void Release()
        {
            lock (_lock)
            {
                _freeAt = DateTime.UtcNow.AddSeconds(0.3);
            }
        }
    }
}
