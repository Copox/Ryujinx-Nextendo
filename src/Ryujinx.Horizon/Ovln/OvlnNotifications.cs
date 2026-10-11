using System;

namespace Ryujinx.Horizon.Ovln
{
    /// <summary>[Nextendo, HOME Menu notifications] The overlay notifications, for the emulator.</summary>
    public static class OvlnNotifications
    {
        /// <summary>A notification to the overlay applet (tag and data as it reads them).</summary>
        public static void Post(uint tag, ReadOnlySpan<byte> data) => OvlnQueue.Notify(tag, data);
    }
}
