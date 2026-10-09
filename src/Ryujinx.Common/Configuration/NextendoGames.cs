using System.Collections.Concurrent;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// The games seen on Nextendo (what a friend plays, what a friend played): their name and
    /// their icon, as the account server gives them. The HOME Menu asks the system for the
    /// title card of such a game by its id; this is where the answer comes from when the game
    /// is not in the library.
    /// </summary>
    public static class NextendoGames
    {
        public readonly record struct Game(string Name, byte[] Icon);

        private static readonly ConcurrentDictionary<ulong, Game> _games = new();

        public static void Remember(ulong titleId, string name, byte[] icon)
        {
            if (titleId == 0 || string.IsNullOrEmpty(name))
            {
                return;
            }

            _games.AddOrUpdate(titleId, new Game(name, icon), (_, known) => new Game(name, icon ?? known.Icon));
        }

        public static bool TryGet(ulong titleId, out Game game) => _games.TryGetValue(titleId, out game);
    }
}
