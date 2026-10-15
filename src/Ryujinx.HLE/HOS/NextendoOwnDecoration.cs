using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Process;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.HLE.HOS
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The user's own picture with their Discord avatar decoration, as
    /// their friends see it (staff and donors only: the account server gives the decoration to
    /// them alone). Asked once when the emulator starts, so that the HOME Menu, which asks for the
    /// picture first and keeps it, has it decorated. The decoration moves as on Discord: the
    /// programs get the still picture, with a mark in its top left corner (hidden by the rounded
    /// corners), and the pictures of the animation then go to the textures that carry the mark.
    /// </summary>
    public static class NextendoOwnDecoration
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
        private static readonly object _lock = new();
        private static readonly ManualResetEventSlim _known = new(false);

        // The programs that show the user's own picture: the HOME Menu and the user page.
        private static readonly ulong[] _programs = [0x0100000000001000, 0x0100000000001013];

        // The grey of the mark (left half dark, right half this).
        private const byte Mark = 240;

        private static Switch _device;
        private static string _address;          // the decoration's address ("" = none)
        private static byte[] _decoration;       // its still picture
        private static byte[] _animation;        // its animated picture (APNG), or null
        private static long _askedAt = -1;       // -1 = never asked
        private static int _asking;

        private static byte[] _plain;            // the last picture decorated, and the result
        private static byte[] _decorated;
        private static int _turn;                // the animation running (a new one stops the last)

        /// <summary>Asks for the decoration as soon as the emulator starts.</summary>
        public static void Attach(Switch device)
        {
            _device = device;
            Refresh();
        }

        /// <summary>The picture to show for the user's own profile: decorated when it can be.</summary>
        public static byte[] Apply(byte[] picture)
        {
            if (picture == null || picture.Length == 0)
            {
                return picture;
            }

            Refresh();

            // the first time, the answer of the server is worth a short wait: the HOME Menu keeps
            // the first picture it gets
            _known.Wait(2500);

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

                byte[] made = NextendoFriendProfile.Decorate(picture, _decoration, Mark);

                _plain = picture;
                _decorated = made;

                if (made != null && _animation != null)
                {
                    Animate(picture, _animation);
                }

                if (made != null)
                {
                    Overlay(picture, _decoration, _animation);
                }

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
                    byte[] moving = null;

                    if (Uri.TryCreate(address, UriKind.Absolute, out Uri uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "cdn.discordapp.com" &&
                        uri.AbsolutePath.StartsWith("/avatar-decoration-presets/", StringComparison.Ordinal))
                    {
                        still = _http.GetByteArrayAsync(uri.AbsoluteUri.Replace("passthrough=true", "passthrough=false")).GetAwaiter().GetResult();

                        try
                        {
                            moving = _http.GetByteArrayAsync(uri.AbsoluteUri.Contains("passthrough=") ? uri.AbsoluteUri.Replace("passthrough=false", "passthrough=true") : uri.AbsoluteUri + "&passthrough=true").GetAwaiter().GetResult();
                        }
                        catch (Exception)
                        {
                            moving = null; // still, then
                        }
                    }

                    lock (_lock)
                    {
                        _address = address;
                        _decoration = still;
                        _animation = moving;
                        _plain = null;
                        _decorated = null;

                        if (still == null)
                        {
                            Interlocked.Increment(ref _turn); // no decoration any more: no animation either
                            Interlocked.Increment(ref _overlayTurn);
                        }
                    }

                    Logger.Info?.Print(LogClass.Application, $"[Nextendo] own picture: {(still != null ? "Discord avatar decoration" : "no decoration")}");
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] own picture: decoration not fetched: {e.Message}");
                }
                finally
                {
                    if (_askedAt >= 0)
                    {
                        _known.Set(); // an answer (none without the account: the next ask waits for it)
                    }

                    Interlocked.Exchange(ref _asking, 0);
                }
            });
        }

        // The pictures of the animation, given in turn to the textures of the programs that carry
        // the mark, for as long as this animation is the one to show.
        private static void Animate(byte[] picture, byte[] animation)
        {
            int turn = Interlocked.Increment(ref _turn);

            Task.Run(async () =>
            {
                try
                {
                    List<(byte[] Pixels, int Milliseconds)> frames = NextendoFriendProfile.DecorateFrames(picture, animation, out int side);

                    if (frames == null || frames.Count < 2)
                    {
                        return;
                    }

                    Logger.Info?.Print(LogClass.Application, $"[Nextendo] own picture: decoration animated ({frames.Count} pictures, {side}x{side})");

                    int found = 0;

                    // the pictures at the size each program keeps its own (made once a size)
                    Dictionary<(int, int, int), byte[]> sized = [];

                    byte[] PixelsFor(int at, int width, int height)
                    {
                        if (width == side && height == side)
                        {
                            return frames[at].Pixels;
                        }

                        lock (sized)
                        {
                            if (!sized.TryGetValue((at, width, height), out byte[] pixels))
                            {
                                sized[(at, width, height)] = pixels = NextendoFriendProfile.Resize(frames[at].Pixels, side, width, height);
                            }

                            return pixels;
                        }
                    }

                    for (int at = 0; turn == _turn; at = (at + 1) % frames.Count)
                    {
                        Switch device = _device;

                        if (device != null && !NextendoHomeMenuVanilla.Enabled)
                        {
                            foreach (KProcess process in device.System.KernelContext.Processes.Values)
                            {
                                if (Array.IndexOf(_programs, process.TitleId) >= 0 && process.State != ProcessState.Exited)
                                {
                                    int now = at;

                                    device.Gpu.NextendoUploadMarked(process.Pid, (width, height) => PixelsFor(now, width, height), Marked, count =>
                                    {
                                        if (count > 0 && Interlocked.Exchange(ref found, 1) == 0)
                                        {
                                            Logger.Info?.Print(LogClass.Application, "[Nextendo] own picture: animated decoration shown");
                                        }
                                    });
                                }
                            }
                        }

                        await Task.Delay(frames[at].Milliseconds);
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] own picture: decoration not animated: {e.Message}");
                }
            });
        }

        // The decoration on the user's own page, as on Discord: moving, past the edge of the round
        // picture (a pane of its own, see NextendoFriendPage), given while the page's applet runs.
        private static int _overlayTurn;

        private static void Overlay(byte[] picture, byte[] still, byte[] animation)
        {
            int turn = Interlocked.Increment(ref _overlayTurn);

            Task.Run(async () =>
            {
                try
                {
                    const int Side = NextendoFriendPage.OwnDecorationSide;
                    List<(byte[] Pixels, int Milliseconds)> frames = NextendoFriendProfile.DecorationFrames(picture, animation ?? still, Side) ??
                                                                     NextendoFriendProfile.DecorationFrames(picture, still, Side);

                    if (frames == null)
                    {
                        return;
                    }

                    // a still one: given again now and then, kept in the applet's memory, for the
                    // page opened later; a moving one: its pictures in turn
                    for (int at = 0; turn == _overlayTurn; at = (at + 1) % frames.Count)
                    {
                        Switch device = _device;

                        if (device != null && !NextendoHomeMenuVanilla.Enabled)
                        {
                            foreach (KProcess process in device.System.KernelContext.Processes.Values)
                            {
                                if (process.TitleId == _programs[1] && process.State != ProcessState.Exited)
                                {
                                    device.Gpu.NextendoUploadTexture(process.Pid, Side, Side, frames[at].Pixels, 4);
                                }
                            }
                        }

                        await Task.Delay(frames.Count > 1 ? frames[at].Milliseconds : 500);
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] own picture: decoration of the page not painted: {e.Message}");
                }
            });
        }

        // The mark, in the first 16 pixels of the texture (its first row in any layout): a dark
        // grey stripe 8 pixels wide, then 24 of the grey of the mark, both made smaller when the
        // program keeps the picture smaller (64 x 64: 2 then 6). JPEG keeps flat grey almost exactly.
        private static bool Marked(byte[] first) => IsMarked(first, Mark);

        /// <summary>Whether the first 16 pixels of a texture carry the mark of this grey (see Marked).</summary>
        public static bool IsMarked(byte[] first, int mark)
        {
            int dark = 0;

            while (dark < 8 && Grey(first, dark, 16))
            {
                dark++;
            }

            if (dark == 0)
            {
                return false;
            }

            // the edge between the two greys may be a blend of them, when the picture was made smaller
            int light = Grey(first, dark, mark) ? dark : dark + 1;
            int end = Math.Min(16, dark * 4 - 1);

            if (end - light < 1)
            {
                return false;
            }

            for (int pixel = light; pixel < end; pixel++)
            {
                if (!Grey(first, pixel, mark))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Grey(byte[] first, int pixel, int want)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                if (Math.Abs(first[pixel * 4 + channel] - want) > 28)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
