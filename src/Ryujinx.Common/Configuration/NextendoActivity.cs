using System;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo] What the player is looking at inside the screen they are on (a friend page,
    /// the friend list, the news...), told by the services that answer that screen, for whoever
    /// wants to show it (the Discord presence).
    /// </summary>
    public static class NextendoActivity
    {
        /// <summary>"Friend:name", "FriendList", "Profile", "AddFriend", "News", "NewsArticle", or empty.</summary>
        public static string Detail { get; private set; } = "";

        public static event Action Changed;

        public static void Set(string detail)
        {
            detail ??= "";

            if (Detail == detail)
            {
                return;
            }

            Detail = detail;

            try
            {
                Changed?.Invoke();
            }
            catch (Exception)
            {
                // A failure of a listener is not the business of the service that tells.
            }
        }
    }
}
