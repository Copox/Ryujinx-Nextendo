using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.HLE.HOS
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The user's own picture with their Discord avatar decoration, as
    /// their friends see it (staff and donors only: the account server gives the decoration to
    /// them alone). Never waits on the network: the picture shown is the plain one until the
    /// decoration is known, then the decorated one from the next time it is asked for.
    /// </summary>
    public static class NextendoOwnDecoration
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
        private static readonly object _lock = new();

        private static string _address;          // the decoration's address ("" = none)
        private static byte[] _decoration;       // its still picture
        private static long _askedAt = -1; // -1 = never asked
        private static int _asking;

        private static byte[] _plain;            // the last picture decorated, and the result
        private static byte[] _decorated;

        /// <summary>The picture to show for the user's own profile: decorated when it can be.</summary>
        public static byte[] Apply(byte[] picture)
        {
            if (picture == null || picture.Length == 0)
            {
                return picture;
            }

            Refresh();

            lock (_lock)
            {
                if (_decoration == null)
                {
                    return picture;
                }

                if (_plain != null && _plain.AsSpan().SequenceEqual(picture))
                {
                    return _decorated ?? picture;
                }

                byte[] made = NextendoFriendProfile.Decorate(picture, _decoration);

                _plain = picture;
                _decorated = made;

                return made ?? picture;
            }
        }

        // The decoration of the account, asked again every ten minutes, in the background.
        private static void Refresh()
        {
            if ((_askedAt >= 0 && Environment.TickCount64 - _askedAt < 10 * 60 * 1000) || Interlocked.Exchange(ref _asking, 1) == 1)
            {
                return;
            }

            _askedAt = Environment.TickCount64;

            Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(NextendoAccount.NexToken))
                    {
                        Logger.Info?.Print(LogClass.Application, "[Nextendo] own picture: no account token yet");
                        _askedAt = -1; // asked again next time
                        return;
                    }

                    using HttpRequestMessage request = new(HttpMethod.Get, NextendoEndpoint.BaseUrl().TrimEnd('/') + "/api/me");

                    request.Headers.Add("Authorization", "Bearer " + NextendoAccount.NexToken);

                    using HttpResponseMessage response = _http.Send(request);
                    using System.Text.Json.JsonDocument answer = System.Text.Json.JsonDocument.Parse(response.Content.ReadAsStream());

                    string address = answer.RootElement.TryGetProperty("account", out System.Text.Json.JsonElement account) &&
                                     account.TryGetProperty("avatar_decoration", out System.Text.Json.JsonElement found) &&
                                     found.ValueKind == System.Text.Json.JsonValueKind.String ? found.GetString() ?? "" : "";

                    if (address == _address)
                    {
                        return;
                    }

                    byte[] still = null;

                    if (Uri.TryCreate(address, UriKind.Absolute, out Uri uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "cdn.discordapp.com" &&
                        uri.AbsolutePath.StartsWith("/avatar-decoration-presets/", StringComparison.Ordinal))
                    {
                        still = _http.GetByteArrayAsync(uri.AbsoluteUri.Replace("passthrough=true", "passthrough=false")).GetAwaiter().GetResult();
                    }

                    lock (_lock)
                    {
                        _address = address;
                        _decoration = still;
                        _plain = null;
                        _decorated = null;
                    }

                    Logger.Info?.Print(LogClass.Application, $"[Nextendo] own picture: {(still != null ? "Discord avatar decoration" : "no decoration")}");
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] own picture: decoration not fetched: {e.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _asking, 0);
                }
            });
        }
    }
}
