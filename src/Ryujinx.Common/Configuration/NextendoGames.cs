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

        public const ulong HomeMenuId = 0x0100000000001000;

        /// <summary>
        /// A program of the system (HOME Menu, applets: 0100000000001000 to 0100000000001FFF),
        /// by its id in hexadecimal: never a game played, neither in the presence nor in the history.
        /// </summary>
        public static bool IsSystemProgram(string titleId)
        {
            return ulong.TryParse(titleId, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ulong id)
                && id >= 0x0100000000001000 && id <= 0x0100000000001FFF;
        }

        /// <summary>
        /// What the user of this console is in right now, when the HOME Menu runs: the game
        /// started from it, or the menu itself.
        /// </summary>
        public static ulong Playing { get; set; } = HomeMenuId;

        private static readonly ConcurrentDictionary<ulong, Game> _games = new();

        public static void Remember(ulong titleId, string name, byte[] icon)
        {
            if (titleId == 0 || string.IsNullOrEmpty(name))
            {
                return;
            }

            _games.AddOrUpdate(titleId, new Game(name, icon), (_, known) => new Game(name, icon ?? known.Icon));
        }

        private static readonly System.Net.Http.HttpClient _http = new() { Timeout = System.TimeSpan.FromSeconds(3) };

        public static bool Knows(ulong titleId) => _games.ContainsKey(titleId);

        public static bool TryGet(ulong titleId, out Game game)
        {
            if (!_games.TryGetValue(titleId, out game))
            {
                return false;
            }

            // A history entry may come without its icon (the player's own record never had
            // one): the shared catalogue of the account server has it, by title id.
            if (game.Icon == null)
            {
                byte[] icon = System.Array.Empty<byte>();

                try
                {
                    string url = NextendoEndpoint.BaseUrl().TrimEnd('/') + "/api/game-icon/" + titleId.ToString("X16");
                    byte[] body = _http.GetByteArrayAsync(url).GetAwaiter().GetResult();

                    if (body.Length > 2 && body[0] == 0xFF && body[1] == 0xD8)
                    {
                        icon = body;
                    }
                }
                catch (System.Exception)
                {
                    // out of reach: asked again next time
                    return true;
                }

                game = new Game(game.Name, icon);
                _games[titleId] = game;
            }

            return true;
        }
    }
}
