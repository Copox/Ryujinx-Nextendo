using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Process;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.HLE.HOS
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The banner of a friend's page in the HOME Menu (see
    /// NextendoFriendPage): painted when the page of a friend opens, and given to the renderer
    /// in place of the blank texture the page has. Nextendo Network has no banners yet: the one
    /// painted is made up from the colour of the friend's profile (or from their id), with their
    /// badges on it.
    /// </summary>
    public static class NextendoFriendProfile
    {
        private const ulong MyPageProgram = 0x0100000000001013;

        // The page draws its textures a moment after it asks for the friend: given again meanwhile.

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };
        private static readonly ConcurrentDictionary<string, SKBitmap> _badges = new();

        // An animated banner: no more pictures than this (each one is a megabyte), none shown
        // for less than this long.
        private const int MostFrames = 60;
        private const int ShortestFrame = 40;

        private static Switch _device;
        private static bool _listening;
        private static int _shown;
        private static ulong _lastShownPid;
        private static int _own;

        public static void Attach(Switch device)
        {
            _device = device;
            NextendoImages.Decorate ??= NextendoFriendListDecoration.Decorate;
            NextendoOwnDecoration.Attach(device);
            NextendoFriendListDecoration.Attach(device);

            if (!_listening)
            {
                _listening = true;
                NextendoFriends.FriendPageShown += Shown;
                NextendoFriends.FriendNotificationShown += NotificationShown;
            }
        }

        private static void Shown(ulong pid)
        {
            Switch device = _device;

            if (device == null || NextendoHomeMenuVanilla.Enabled)
            {
                return;
            }

            int turn = Interlocked.Increment(ref _shown);

            // the page of another friend: the pictures of the last one are not given again to its
            // textures (read again before this friend's come, they would show the other friend's)
            if (Interlocked.Exchange(ref _lastShownPid, pid) != pid)
            {
                foreach (KProcess process in device.System.KernelContext.Processes.Values)
                {
                    if (process.TitleId == MyPageProgram && process.State != ProcessState.Exited)
                    {
                        device.Gpu.NextendoForget(process.Pid, NextendoFriendPage.BannerWidth, NextendoFriendPage.BannerHeight);
                        device.Gpu.NextendoForget(process.Pid, NextendoFriendPage.DetailWidth, NextendoFriendPage.DetailHeight);
                        device.Gpu.NextendoForget(process.Pid, NextendoFriendPage.DecorationSide, NextendoFriendPage.DecorationSide);
                    }
                }
            }

            Task.Run(async () =>
            {
                try
                {
                    NextendoFriends.Entry friend = NextendoFriends.Get().FirstOrDefault(f => f.Pid == pid);
                    List<(byte[] Pixels, int Milliseconds)> banner = Banner(pid, friend.Color, friend.Badges ?? [], NextendoFriendPage.BannerWidth, friend.BannerUrl, friend.BannerCrop);

                    ShowDetail(device, turn, friend.Status > 0 ? friend.Detail : "");
                    ShowDecoration(device, pid, friend.Decoration);

                    Logger.Info?.Print(LogClass.Application, $"[Nextendo] friend page: banner of 0x{pid:X} ({friend.Badges?.Length ?? 0} badge(s), colour \"{friend.Color}\", {banner.Count} picture(s))");

                    // A still one: given until the applet has made its texture (it makes it only
                    // when it draws the page: later on a slow machine, after the 12 seconds the
                    // banner used to be given for, which left it empty "sometimes"), then twice
                    // more. Kept in the applet's memory too: read again from there, the texture
                    // keeps it. An animated one is given all the time.
                    if (banner.Count == 1)
                    {
                        long until = Environment.TickCount64 + 20000;
                        int given = 0;

                        while (given < 3 && Environment.TickCount64 < until)
                        {
                            // the page of another friend meanwhile: theirs is the one to show
                            if (turn != _shown || device != _device)
                            {
                                return;
                            }

                            int found = await UploadCounted(device, NextendoFriendPage.BannerWidth, banner[0].Pixels);

                            if (found < 0)
                            {
                                return; // the applet is closed
                            }

                            given += found > 0 ? 1 : 0;

                            await Task.Delay(given == 0 ? 200 : 700);
                        }

                        if (given == 0)
                        {
                            Logger.Warning?.Print(LogClass.Application, $"[Nextendo] friend page: the banner texture of 0x{pid:X} never appeared");
                        }
                    }

                    // an animated one: its pictures in turn, as long as this page is the one shown
                    for (int at = 0; banner.Count > 1 && turn == _shown && device == _device; at = (at + 1) % banner.Count)
                    {
                        if (!Upload(device, NextendoFriendPage.BannerWidth, banner[at].Pixels))
                        {
                            return; // the applet is closed
                        }

                        await Task.Delay(banner[at].Milliseconds);
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] friend page: banner not painted: {e.Message}");
                }
            });
        }

        // The decoration of the friend over their picture, moving and past its edge (nothing when
        // they have none: the one of the last page must go), as long as this page is the one shown.
        // The "online" notification of a friend with a Discord nameplate: the nameplate, moving,
        // in the frame of the notification while it shows, then the plain frame again.
        private const ulong OverlayProgram = 0x010000000000100C;
        private const int NotificationMilliseconds = 12000; // at most: the next notification ends it
        private static int _notificationTurn;
        private static readonly ConcurrentDictionary<string, List<byte[]>> _nameplates = new();

        private static void NotificationShown(ulong pid)
        {
            Switch device = _device;
            NextendoFriends.Entry friend = NextendoFriends.Get().FirstOrDefault(f => f.Pid == pid);

            string address = friend.Nameplate ?? "";
            SKColor? colour = SKColor.TryParse(friend.NameplateColor ?? "", out SKColor parsed) ? parsed : null;

            if (device == null || NextendoHomeMenuVanilla.Enabled)
            {
                return;
            }

            int turn = Interlocked.Increment(ref _notificationTurn);
            // the title shown: the game, the Nextendo App, or the HOME Menu for a friend online without one
            ulong title = friend.AppId != 0 ? friend.AppId : friend.Status > 0 ? NextendoGames.HomeMenuId : 0;
            byte[] gameIcon = title != 0 && NextendoGames.TryGet(title, out NextendoGames.Game played) ? played.Icon : null;

            // the icon of the game and the friend's picture, sharp, in their own textures; cleared
            // once the notification has gone
            if (NextendoFriendPage.NotificationGameApart)
            {
                using SKBitmap game = gameIcon == null ? null : SKBitmap.Decode(gameIcon);
                byte[] photo = NextendoFriends.ProfileImage(pid);
                using SKBitmap face = photo is { Length: > 0 } ? SKBitmap.Decode(photo) : null;

                UploadOverlay(device, NextendoFriendPage.NotificationGameSide, NextendoFriendPage.NotificationGamePixels(game));
                UploadOverlay(device, NextendoFriendPage.NotificationPictureSide, NextendoFriendPage.NotificationPicturePixels(face));

                Task.Delay(NotificationMilliseconds).ContinueWith(_ =>
                {
                    if (turn == _notificationTurn)
                    {
                        UploadOverlay(device, NextendoFriendPage.NotificationGameSide, NextendoFriendPage.NotificationGamePixels(null));
                        UploadOverlay(device, NextendoFriendPage.NotificationPictureSide, NextendoFriendPage.NotificationPicturePixels(null));
                    }
                });
            }

            // a friend without a nameplate: the plain frame at once (the last nameplate may still
            // be on it), with the icon of their game
            if (address.Length == 0)
            {
                using SKBitmap icon = gameIcon == null ? null : SKBitmap.Decode(gameIcon);

                UploadOverlay(device, NextendoFriendPage.NotificationBasePixels(game: icon));

                // the plain frame again once it has gone (for the notifications that come next)
                if (icon != null)
                {
                    Task.Delay(NotificationMilliseconds).ContinueWith(_ =>
                    {
                        if (turn == _notificationTurn)
                        {
                            UploadOverlay(device, NextendoFriendPage.NotificationBasePixels());
                        }
                    });
                }

                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    string key = address + "|" + friend.NameplateColor + "|" + friend.AppId;

                    if (!_nameplates.TryGetValue(key, out List<byte[]> frames))
                    {
                        frames = NameplateFrames(Picture(address), colour, gameIcon, out List<int> times);

                        if (frames == null)
                        {
                            return;
                        }

                        _nameplates[key] = frames;
                        _nameplateTimes[key] = times;
                    }

                    List<int> durations = _nameplateTimes.TryGetValue(key, out List<int> t) ? t : [];
                    long until = Environment.TickCount64 + NotificationMilliseconds;

                    for (int at = 0; turn == _notificationTurn && Environment.TickCount64 < until; at = (at + 1) % frames.Count)
                    {
                        UploadOverlay(device, frames[at]);
                        await Task.Delay(at < durations.Count ? durations[at] : 80);
                    }

                    if (turn == _notificationTurn)
                    {
                        UploadOverlay(device, NextendoFriendPage.NotificationBasePixels());
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] notification: nameplate of 0x{pid:X} not shown: {e.Message}");
                }
            });
        }

        private static readonly ConcurrentDictionary<string, List<int>> _nameplateTimes = new();

        // The pictures of a nameplate (animated PNG) in the frame of the notification.
        private static List<byte[]> NameplateFrames(byte[] png, SKColor? colour, byte[] gameIcon, out List<int> times)
        {
            times = [];
            List<(SKBitmap Picture, int Milliseconds)> pictures = NextendoApng.Frames(png, 90);

            if (pictures == null || pictures.Count == 0)
            {
                SKBitmap still = png == null ? null : SKBitmap.Decode(png);

                if (still == null)
                {
                    return null;
                }

                pictures = [(still, 0)];
            }

            try
            {
                List<byte[]> frames = [];
                using SKBitmap icon = gameIcon == null ? null : SKBitmap.Decode(gameIcon);

                foreach ((SKBitmap picture, int milliseconds) in pictures)
                {
                    frames.Add(NextendoFriendPage.NotificationBasePixels(picture, colour, icon));
                    times.Add(Math.Max(milliseconds, ShortestFrame));
                }

                return frames;
            }
            finally
            {
                foreach ((SKBitmap picture, _) in pictures)
                {
                    picture.Dispose();
                }
            }
        }

        // A square texture of the overlay applet (found by its size, which no other texture has).
        private static void UploadOverlay(Switch device, int side, byte[] pixels)
        {
            foreach (KProcess process in device.System.KernelContext.Processes.Values)
            {
                if (process.TitleId == OverlayProgram && process.State != ProcessState.Exited)
                {
                    device.Gpu.NextendoUploadTexture(process.Pid, side, side, pixels, 4);
                }
            }
        }

        private static void UploadOverlay(Switch device, byte[] pixels)
        {
            foreach (KProcess process in device.System.KernelContext.Processes.Values)
            {
                if (process.TitleId == OverlayProgram && process.State != ProcessState.Exited)
                {
                    device.Gpu.NextendoUploadTexture(process.Pid, NextendoFriendPage.NotificationBaseWidth, NextendoFriendPage.NotificationBaseHeight, pixels, 4);
                }
            }
        }

        // The decoration shown, and its pictures (made once: the applet asks for the friend's
        // status every few seconds while the page shows, the decoration keeps going meanwhile).
        private static ulong _decorationPid;
        private static string _decorationAddress;
        private static int _decorationTurn;
        private static int _decorationRunning;
        private static readonly ConcurrentDictionary<string, List<(byte[] Pixels, int Milliseconds)>> _decorationFrames = new();

        private static void ShowDecoration(Switch device, ulong pid, string address)
        {
            address ??= "";

            if (pid == _decorationPid && address == _decorationAddress && Volatile.Read(ref _decorationRunning) == 1)
            {
                return;
            }

            _decorationPid = pid;
            _decorationAddress = address;
            Volatile.Write(ref _decorationRunning, 1);

            int turn = Interlocked.Increment(ref _decorationTurn);

            Task.Run(async () =>
            {
                try
                {
                    const int Side = NextendoFriendPage.DecorationSide;
                    List<(byte[] Pixels, int Milliseconds)> frames = null;

                    if (Uri.TryCreate(address, UriKind.Absolute, out Uri uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "cdn.discordapp.com" &&
                        uri.AbsolutePath.StartsWith("/avatar-decoration-presets/", StringComparison.Ordinal))
                    {
                        // the picture of the friend comes with the list: a few seconds at most
                        byte[] photo = NextendoFriends.PlainImage(pid);

                        for (int wait = 0; photo == null && wait < 50 && turn == _decorationTurn; wait++)
                        {
                            await Task.Delay(100);
                            photo = NextendoFriends.PlainImage(pid);
                        }

                        string key = $"{pid:X}|{address}|{photo?.Length}";

                        if (photo != null && !_decorationFrames.TryGetValue(key, out frames))
                        {
                            frames = DecorationFrames(photo, Picture(uri.AbsoluteUri.Replace("passthrough=false", "passthrough=true")), Side);

                            if (frames != null)
                            {
                                if (_decorationFrames.Count >= 4)
                                {
                                    _decorationFrames.Clear();
                                }

                                _decorationFrames[key] = frames;
                            }
                        }
                    }

                    frames ??= [(new byte[Side * Side * 4], 0)];

                    Logger.Info?.Print(LogClass.Application, $"[Nextendo] friend page: decoration of 0x{pid:X}: {(address.Length == 0 ? "none" : $"{frames.Count} picture(s)")}");

                    // the first one until the applet has made its texture (kept in its memory),
                    // then the animation straight away
                    long until = Environment.TickCount64 + 20000;

                    while (true)
                    {
                        if (turn != _decorationTurn || device != _device)
                        {
                            return;
                        }

                        int found = await UploadCounted(device, Side, frames[0].Pixels, Side);

                        if (found < 0)
                        {
                            return;
                        }

                        if (found > 0)
                        {
                            break;
                        }

                        if (Environment.TickCount64 >= until)
                        {
                            Logger.Warning?.Print(LogClass.Application, $"[Nextendo] friend page: the decoration texture of 0x{pid:X} never appeared");
                            return;
                        }

                        await Task.Delay(100);
                    }

                    for (int at = 0; frames.Count > 1 && turn == _decorationTurn && device == _device; at = (at + 1) % frames.Count)
                    {
                        if (!Upload(device, Side, frames[at].Pixels, Side))
                        {
                            return;
                        }

                        await Task.Delay(frames[at].Milliseconds);
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] friend page: decoration not painted: {e.Message}");
                }
                finally
                {
                    if (turn == _decorationTurn)
                    {
                        Volatile.Write(ref _decorationRunning, 0);
                    }
                }
            });
        }

        // A banner given to the renderer, for the applet to show: false when the applet does not run.
        private static bool Upload(Switch device, int width, byte[] pixels, int height = NextendoFriendPage.BannerHeight)
        {
            bool running = false;

            foreach (KProcess process in device.System.KernelContext.Processes.Values)
            {
                if (process.TitleId == MyPageProgram && process.State != ProcessState.Exited)
                {
                    device.Gpu.NextendoUploadTexture(process.Pid, width, height, pixels, 4);
                    running = true;
                }
            }

            return running;
        }

        // What the friend is doing in the game ("Offline · VS · 150cc"), painted under the game of
        // their status, in the console's own letters and in the grey of the theme; nothing when
        // the game says nothing (a transparent picture, given all the same: the page of the
        // friend before may have left theirs).
        private static void ShowDetail(Switch device, int turn, string detail)
        {
            Task.Run(async () =>
            {
                try
                {
                    byte[] pixels = PaintDetail(detail);
                    long until = Environment.TickCount64 + 20000;
                    int given = 0;

                    while (given < 2 && Environment.TickCount64 < until && turn == _shown && device == _device)
                    {
                        int found = await UploadCounted(device, NextendoFriendPage.DetailWidth, pixels, NextendoFriendPage.DetailHeight);

                        if (found < 0)
                        {
                            return;
                        }

                        given += found > 0 ? 1 : 0;

                        await Task.Delay(given == 0 ? 200 : 700);
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] friend page: detail not painted: {e.Message}");
                }
            });
        }

        private static SKTypeface _console;

        private static byte[] PaintDetail(string detail)
        {
            int width = NextendoFriendPage.DetailWidth, height = NextendoFriendPage.DetailHeight;

            using SKBitmap picture = new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using SKCanvas canvas = new(picture);

            canvas.Clear(SKColors.Transparent);

            if (!string.IsNullOrWhiteSpace(detail))
            {
                if (_console == null && Services.Sdb.Pl.SharedFontManager.StandardFont is { } font)
                {
                    _console = SKTypeface.FromData(SKData.CreateCopy(font));
                }

                bool dark = NextendoHomeMenuSettings.Theme == "black" || Themes.NextendoThemes.Chosen != null;

                using SKPaint paint = new()
                {
                    IsAntialias = true,
                    Typeface = _console ?? SKTypeface.Default,
                    TextSize = 38f,
                    Color = dark ? new SKColor(0xB4, 0xB4, 0xB4) : new SKColor(0x6E, 0x6E, 0x6E),
                };

                // under the name of the game: same left edge (124 of the 574 of the row, twice here)
                const float Left = 248f;
                string text = detail.Trim();

                while (text.Length > 1 && paint.MeasureText(text) > width - Left - 16)
                {
                    text = text[..^2].TrimEnd() + "…";
                }

                float baseline = height / 2f - (paint.FontMetrics.Ascent + paint.FontMetrics.Descent) / 2f;

                canvas.DrawText(text, Left, baseline, paint);
            }

            canvas.Flush();

            return picture.Bytes;
        }

        // A still banner given to the applet and kept in its memory: how many textures took it
        // (0 while the applet has not made it yet), -1 when the applet does not run.
        private static Task<int> UploadCounted(Switch device, int width, byte[] pixels, int height = NextendoFriendPage.BannerHeight)
        {
            KProcess page = device.System.KernelContext.Processes.Values.FirstOrDefault(p => p.TitleId == MyPageProgram && p.State != ProcessState.Exited);

            if (page == null)
            {
                return Task.FromResult(-1);
            }

            TaskCompletionSource<int> found = new(TaskCreationOptions.RunContinuationsAsynchronously);

            device.Gpu.NextendoUploadTexture(page.Pid, width, height, pixels, 4, n => found.TrySetResult(n));

            // the renderer gives its answer at its next frame; none within a second: try again
            return Task.WhenAny(found.Task, Task.Delay(1000)).ContinueWith(t => found.Task.IsCompleted ? found.Task.Result : 0);
        }

        // The own banner, when it is an animated one: its pictures in turn while the applet runs
        // (the first one is in the textures of the applet from the start).
        private static void AnimateOwn(List<(byte[] Pixels, int Milliseconds)> banner)
        {
            Switch device = _device;
            int turn = Interlocked.Increment(ref _own);

            if (device == null || banner.Count < 2)
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    // the applet is only being loaded when its textures are made
                    long until = Environment.TickCount64 + 20000;
                    bool seen = false;

                    for (int at = 0; turn == _own && device == _device; at = (at + 1) % banner.Count)
                    {
                        if (Upload(device, NextendoFriendPage.OwnBannerWidth, banner[at].Pixels))
                        {
                            seen = true;
                        }
                        else if (seen || Environment.TickCount64 > until)
                        {
                            return;
                        }

                        await Task.Delay(banner[at].Milliseconds);
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] own page: banner not animated: {e.Message}");
                }
            });
        }

        /// <summary>The banner of the own profile of the user (their badges, from the account server), or null when not signed in.</summary>
        public static byte[] PaintOwn()
        {
            try
            {
                if (NextendoHomeMenuVanilla.Enabled || string.IsNullOrEmpty(NextendoAccount.NexToken))
                {
                    return null;
                }

                using HttpRequestMessage request = new(HttpMethod.Get, NextendoEndpoint.BaseUrl().TrimEnd('/') + "/api/me");

                request.Headers.Add("Authorization", "Bearer " + NextendoAccount.NexToken);

                using HttpResponseMessage response = _http.Send(request);
                using System.Text.Json.JsonDocument answer = System.Text.Json.JsonDocument.Parse(response.Content.ReadAsStream());

                if (!answer.RootElement.TryGetProperty("account", out System.Text.Json.JsonElement account))
                {
                    return null;
                }

                string[] badges = account.TryGetProperty("badges", out System.Text.Json.JsonElement list) && list.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? [.. list.EnumerateArray().Select(b => b.GetString() ?? "").Where(b => b.Length > 0)]
                    : [];
                string colour = account.TryGetProperty("color", out System.Text.Json.JsonElement chosen) && chosen.ValueKind == System.Text.Json.JsonValueKind.String ? chosen.GetString() : "";

                string address = NextendoFriends.BannerOf(account, out float[] frame);
                List<(byte[] Pixels, int Milliseconds)> banner = Banner(NextendoAccount.Pid, colour, badges, NextendoFriendPage.OwnBannerWidth, address, frame);

                AnimateOwn(banner);

                return banner[0].Pixels;
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] own page: banner not painted: {e.Message}");

                return null;
            }
        }

        /// <summary>
        /// A profile picture with a Discord avatar decoration, as Discord draws it: the picture in a
        /// circle, the decoration 20 % larger around it. The corners, outside the circle, take the
        /// picture itself, blurred and darker (the HOME Menu shows square pictures).
        /// </summary>
        public static byte[] Decorate(byte[] jpeg, byte[] decoration, byte mark = 0)
        {
            using SKBitmap photo = SKBitmap.Decode(jpeg);
            using SKBitmap ring = SKBitmap.Decode(decoration);

            if (photo == null || ring == null)
            {
                return null;
            }

            int side = Math.Max(photo.Width, photo.Height);

            using SKBitmap picture = Backdrop(photo, side);
            using SKCanvas canvas = new(picture);
            using SKPaint smooth = new() { IsAntialias = true, FilterQuality = SKFilterQuality.High };

            canvas.DrawBitmap(ring, RingPlace(side), smooth);

            if (mark != 0)
            {
                // the mark that finds the picture in the textures of the programs (see
                // NextendoOwnDecoration): whole blocks of JPEG, in a corner the rounding hides
                using SKPaint flat = new();

                flat.Color = new SKColor(16, 16, 16);
                canvas.DrawRect(SKRect.Create(0, 0, 8, 16), flat);
                flat.Color = new SKColor(mark, mark, mark);
                canvas.DrawRect(SKRect.Create(8, 0, 24, 16), flat);
            }

            canvas.Flush();

            using SKImage image = SKImage.FromBitmap(picture);
            using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 95);

            return data?.ToArray();
        }

        /// <summary>
        /// The pictures of an animated decoration (APNG) over a profile picture, as RGBA pixels of
        /// the side of the picture, with how long each one shows; null when it does not move.
        /// </summary>
        public static List<(byte[] Pixels, int Milliseconds)> DecorateFrames(byte[] jpeg, byte[] animation, out int side)
        {
            side = 0;

            using SKBitmap photo = SKBitmap.Decode(jpeg);

            if (photo == null)
            {
                return null;
            }

            List<(SKBitmap Picture, int Milliseconds)> rings = NextendoApng.Frames(animation, MostFrames);

            try
            {
                if (rings == null || rings.Count < 2)
                {
                    return null;
                }

                side = Math.Max(photo.Width, photo.Height);

                using SKBitmap under = Backdrop(photo, side);
                using SKPaint smooth = new() { IsAntialias = true, FilterQuality = SKFilterQuality.High };
                List<(byte[] Pixels, int Milliseconds)> frames = [];

                foreach ((SKBitmap ring, int milliseconds) in rings)
                {
                    using SKBitmap frame = under.Copy();
                    using SKCanvas canvas = new(frame);

                    canvas.DrawBitmap(ring, RingPlace(side), smooth);
                    canvas.Flush();
                    frames.Add((frame.Bytes, Math.Max(milliseconds, ShortestFrame)));
                }

                return frames;
            }
            finally
            {
                foreach ((SKBitmap ring, _) in rings ?? [])
                {
                    ring.Dispose();
                }
            }
        }

        /// <summary>
        /// The pictures of the decoration pane (see NextendoFriendPage.DecorationTexture), RGBA,
        /// <paramref name="texture"/> pixels a side: the profile picture as the applet shows it
        /// (round, covering the applet's own), the decoration over it, past its edge, each picture
        /// of the animation with how long it shows. Null when the decoration cannot be read.
        /// </summary>
        public static List<(byte[] Pixels, int Milliseconds)> DecorationFrames(byte[] jpeg, byte[] decoration, int texture)
        {
            using SKBitmap photo = SKBitmap.Decode(jpeg);

            if (photo == null || decoration == null)
            {
                return null;
            }

            List<(SKBitmap Picture, int Milliseconds)> rings = NextendoApng.Frames(decoration, MostFrames);

            if (rings == null || rings.Count == 0)
            {
                SKBitmap still = SKBitmap.Decode(decoration);

                if (still == null)
                {
                    return null;
                }

                rings = [(still, 0)];
            }

            try
            {
                int side = (int)MathF.Round(texture / NextendoFriendPage.DecorationScale);
                float at = (texture - side) / 2f;

                using SKBitmap round = new(new SKImageInfo(texture, texture, SKColorType.Rgba8888, SKAlphaType.Premul));
                using SKPaint smooth = new() { IsAntialias = true, FilterQuality = SKFilterQuality.High };

                // as on Discord: the photo fills the round picture of the applet (a pixel larger,
                // none of the applet's own shows at its edge), the decoration 1.2 times as large
                // around it, past its edge
                using (SKCanvas canvas = new(round))
                using (SKPath circle = new())
                {
                    SKRect place = SKRect.Create(at - 1, at - 1, side + 2, side + 2);
                    float scale = Math.Max((side + 2f) / photo.Width, (side + 2f) / photo.Height);
                    SKRect fill = SKRect.Create(place.MidX - photo.Width * scale / 2, place.MidY - photo.Height * scale / 2, photo.Width * scale, photo.Height * scale);

                    canvas.Clear(SKColors.Transparent);
                    circle.AddOval(place);
                    canvas.ClipPath(circle, antialias: true);
                    canvas.DrawBitmap(photo, fill, smooth);
                    canvas.Flush();
                }

                float size = side * 1.2f;
                SKRect ring = SKRect.Create((texture - size) / 2, (texture - size) / 2, size, size);

                List<(byte[] Pixels, int Milliseconds)> frames = [];

                foreach ((SKBitmap picture, int milliseconds) in rings)
                {
                    using SKBitmap frame = round.Copy();
                    using SKCanvas canvas = new(frame);

                    canvas.DrawBitmap(picture, ring, smooth);
                    canvas.Flush();
                    frames.Add((frame.Bytes, Math.Max(milliseconds, ShortestFrame)));
                }

                return frames;
            }
            finally
            {
                foreach ((SKBitmap picture, _) in rings)
                {
                    picture.Dispose();
                }
            }
        }

        /// <summary>
        /// A small round picture of a friend with their decoration, as Discord draws it: the photo
        /// in a circle at 1/1.2 of the square, the decoration filling the square around it. The
        /// still one the applet gets (JPEG, the corners dark), with the mark that finds its
        /// texture, the pictures then given to that texture having transparent corners.
        /// </summary>
        public static byte[] DecorateRound(byte[] jpeg, byte[] decoration, byte mark)
        {
            using SKBitmap photo = SKBitmap.Decode(jpeg);
            using SKBitmap ring = SKBitmap.Decode(decoration);

            if (photo == null || ring == null)
            {
                return null;
            }

            int side = Math.Max(photo.Width, photo.Height);

            using SKBitmap picture = RoundUnder(photo, side, opaque: true);
            using SKCanvas canvas = new(picture);
            using SKPaint smooth = new() { IsAntialias = true, FilterQuality = SKFilterQuality.High };

            canvas.DrawBitmap(ring, SKRect.Create(0, 0, side, side), smooth);

            if (mark != 0)
            {
                using SKPaint flat = new();

                flat.Color = new SKColor(NextendoFriendListDecoration.Light, NextendoFriendListDecoration.Light, NextendoFriendListDecoration.Light);
                canvas.DrawRect(SKRect.Create(0, 0, 8, 16), flat);
                flat.Color = new SKColor(mark, mark, mark);
                canvas.DrawRect(SKRect.Create(8, 0, 24, 16), flat);
            }

            canvas.Flush();

            using SKImage image = SKImage.FromBitmap(picture);
            using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 95);

            return data?.ToArray();
        }

        /// <summary>The pictures of DecorateRound as RGBA with transparent corners, each with how long it shows.</summary>
        public static List<(byte[] Pixels, int Milliseconds)> RoundFrames(byte[] jpeg, byte[] decoration, out int side)
        {
            side = 0;

            using SKBitmap photo = SKBitmap.Decode(jpeg);

            if (photo == null || decoration == null)
            {
                return null;
            }

            List<(SKBitmap Picture, int Milliseconds)> rings = NextendoApng.Frames(decoration, MostFrames);

            if (rings == null || rings.Count == 0)
            {
                SKBitmap still = SKBitmap.Decode(decoration);

                if (still == null)
                {
                    return null;
                }

                rings = [(still, 0)];
            }

            try
            {
                side = Math.Max(photo.Width, photo.Height);

                using SKBitmap under = RoundUnder(photo, side, opaque: false);
                using SKPaint smooth = new() { IsAntialias = true, FilterQuality = SKFilterQuality.High };
                List<(byte[] Pixels, int Milliseconds)> frames = [];

                foreach ((SKBitmap ring, int milliseconds) in rings)
                {
                    using SKBitmap frame = under.Copy();
                    using SKCanvas canvas = new(frame);

                    canvas.DrawBitmap(ring, SKRect.Create(0, 0, side, side), smooth);
                    canvas.Flush();
                    frames.Add((frame.Bytes, Math.Max(milliseconds, ShortestFrame)));
                }

                return frames;
            }
            finally
            {
                foreach ((SKBitmap ring, _) in rings)
                {
                    ring.Dispose();
                }
            }
        }

        // The photo in a circle at 1/1.2 of the square (the decoration fills the square), on
        // nothing, or on the photo blurred and darker for a JPEG.
        private static SKBitmap RoundUnder(SKBitmap photo, int side, bool opaque)
        {
            float inner = side / 1.2f;
            SKRect circle = SKRect.Create((side - inner) / 2, (side - inner) / 2, inner, inner);

            SKBitmap picture = new(new SKImageInfo(side, side, SKColorType.Rgba8888, SKAlphaType.Premul));
            using SKCanvas canvas = new(picture);
            using SKPaint smooth = new() { IsAntialias = true, FilterQuality = SKFilterQuality.High };

            canvas.Clear(opaque ? SKColors.Black : SKColors.Transparent);

            if (opaque)
            {
                using SKPaint backdrop = new()
                {
                    ImageFilter = SKImageFilter.CreateBlur(side / 18f, side / 18f),
                    ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(0, 0, 0, 110), SKBlendMode.SrcOver),
                    FilterQuality = SKFilterQuality.Medium,
                };

                canvas.DrawBitmap(photo, SKRect.Create(-side / 10f, -side / 10f, side * 1.2f, side * 1.2f), backdrop);
            }

            canvas.Save();

            using (SKPath round = new())
            {
                round.AddOval(circle);
                canvas.ClipPath(round, antialias: true);
            }

            canvas.DrawBitmap(photo, circle, smooth);
            canvas.Restore();
            canvas.Flush();

            return picture;
        }

        /// <summary>RGBA pixels of a square picture, at another size.</summary>
        public static byte[] Resize(byte[] pixels, int side, int width, int height)
        {
            using SKBitmap from = new(new SKImageInfo(side, side, SKColorType.Rgba8888, SKAlphaType.Premul));
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, from.GetPixels(), pixels.Length);

            using SKBitmap to = from.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), SKFilterQuality.High);

            return to?.Bytes;
        }

        // The pictures of people are round in the HOME Menu (a round mask on a square picture):
        // the photo fills most of that circle, and the decoration keeps Discord's proportion to it
        // (20 % larger), its outer edge a little past the circle. A photo at 1/1.2 of the square,
        // the decoration at the square, was too small once the mask made it round.
        private const float PhotoShrink = 1.12f;

        private static SKRect RingPlace(int side)
        {
            float size = side / PhotoShrink * 1.2f;

            return SKRect.Create((side - size) / 2, (side - size) / 2, size, size);
        }

        // The picture under a decoration, as Discord draws it: the photo in a circle, the
        // decoration 20 % larger around it. The corners, outside the circle, take the photo
        // itself, blurred and darker (the HOME Menu shows square pictures).
        private static SKBitmap Backdrop(SKBitmap photo, int side)
        {
            float inner = side / PhotoShrink;
            SKRect circle = SKRect.Create((side - inner) / 2, (side - inner) / 2, inner, inner);

            SKBitmap picture = new(new SKImageInfo(side, side, SKColorType.Rgba8888, SKAlphaType.Premul));
            using SKCanvas canvas = new(picture);
            using SKPaint smooth = new() { IsAntialias = true, FilterQuality = SKFilterQuality.High };

            canvas.Clear(SKColors.Black);

            using (SKPaint backdrop = new()
            {
                ImageFilter = SKImageFilter.CreateBlur(side / 18f, side / 18f),
                ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(0, 0, 0, 110), SKBlendMode.SrcOver),
                FilterQuality = SKFilterQuality.Medium,
            })
            {
                canvas.DrawBitmap(photo, SKRect.Create(-side / 10f, -side / 10f, side * 1.2f, side * 1.2f), backdrop);
            }

            canvas.Save();

            using (SKPath round = new())
            {
                round.AddOval(circle);
                canvas.ClipPath(round, antialias: true);
            }

            canvas.DrawBitmap(photo, circle, smooth);
            canvas.Restore();
            canvas.Flush();

            return picture;
        }

        // The icon of a badge on the website.
        private static string Icon(string badge)
        {
            if (badge.StartsWith("booster", StringComparison.Ordinal))
            {
                return "boost_" + (badge.Contains('_') ? badge[(badge.IndexOf('_') + 1)..] : "1");
            }

            return badge switch
            {
                "admin" => "admin",
                "team" => "team",
                "verified_dev" => "dev_verifie",
                "donor" => "donateur",
                "anniversary_1" => "anniversaire",
                _ => "membre",
            };
        }

        private static SKBitmap Badge(string badge)
        {
            return _badges.GetOrAdd(Icon(badge), icon =>
            {
                try
                {
                    return SKBitmap.Decode(_http.GetByteArrayAsync($"{NextendoEndpoint.BaseUrl().TrimEnd('/')}/assets/img/badges/{icon}.png").GetAwaiter().GetResult());
                }
                catch (Exception)
                {
                    return null; // the banner goes without it
                }
            });
        }

        private static SKColor Colour(ulong pid, string chosen)
        {
            if (chosen is { Length: 7 } && chosen[0] == '#' && uint.TryParse(chosen.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            {
                return new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            }

            // none chosen: one of their own, always the same
            return SKColor.FromHsv((pid * 2654435761u >> 8) % 360, 62, 86);
        }

        // Where a GIF picked at KLIPY comes from: KLIPY asks for its media to be loaded from
        // there by the program that shows it, never copied elsewhere.
        private static readonly string[] _klipyHosts = ["static.klipy.com", "static1.klipy.com", "static2.klipy.com"];

        private const int LargestPicture = 12 << 20;

        // The pictures read lately, by address (an address changes when the banner does).
        private static readonly ConcurrentDictionary<string, byte[]> _pictures = new();

        // The picture a profile has for its banner (a still one or an animated one, as a file), or
        // null when it has none.
        internal static byte[] Picture(string address)
        {

            if (string.IsNullOrEmpty(address))
            {
                return null;
            }

            string full;

            if (address.StartsWith("/api/banner?", StringComparison.Ordinal))
            {
                full = NextendoEndpoint.BaseUrl().TrimEnd('/') + address;
            }
            else if (Uri.TryCreate(address, UriKind.Absolute, out Uri uri) && uri.Scheme == Uri.UriSchemeHttps &&
                     (_klipyHosts.Contains(uri.Host) || (uri.Host == "cdn.discordapp.com" && uri.AbsolutePath.StartsWith("/avatar-decoration-presets/", StringComparison.Ordinal)) ||
                      (uri.Host == "nextendo.network" && uri.AbsolutePath.StartsWith("/assets/nameplates/", StringComparison.Ordinal))))
            {
                full = uri.AbsoluteUri;
            }
            else
            {
                return null; // an address of another kind: not followed
            }

            if (_pictures.TryGetValue(full, out byte[] known))
            {
                return known;
            }

            try
            {
                using HttpResponseMessage response = _http.Send(new HttpRequestMessage(HttpMethod.Get, full), HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > LargestPicture)
                {
                    return null;
                }

                using System.IO.Stream body = response.Content.ReadAsStream();
                using System.IO.MemoryStream file = new();
                byte[] part = new byte[81920];

                for (int read; (read = body.Read(part, 0, part.Length)) > 0;)
                {
                    if (file.Length + read > LargestPicture)
                    {
                        return null;
                    }

                    file.Write(part, 0, read);
                }

                if (_pictures.Count >= 24)
                {
                    _pictures.Clear();
                }

                return _pictures[full] = file.ToArray();
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] banner picture not fetched: {e.Message}");

                return null;
            }
        }

        /// <summary>The banner of a profile, one picture or the pictures of an animation with how long each shows.</summary>
        public static List<(byte[] Pixels, int Milliseconds)> Banner(ulong pid, string colour, IReadOnlyList<string> badges, int width, string address = null, float[] crop = null)
        {
            byte[] file = Picture(address);

            if (file != null)
            {
                try
                {
                    using SKCodec codec = SKCodec.Create(new System.IO.MemoryStream(file));

                    if (codec != null)
                    {
                        SKImageInfo info = new(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                        using SKBitmap frame = new(info);
                        int count = Math.Max(1, codec.FrameCount);
                        int step = (count + MostFrames - 1) / MostFrames;
                        List<(byte[], int)> pictures = [];
                        int waited = 0;

                        for (int i = 0; i < count; i++)
                        {
                            // each picture of an animation is drawn over the one before
                            SKCodecResult result = count > 1
                                ? codec.GetPixels(info, frame.GetPixels(), new SKCodecOptions(i, i > 0 ? i - 1 : -1))
                                : codec.GetPixels(info, frame.GetPixels());

                            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                            {
                                break;
                            }

                            waited += count > 1 ? Math.Max(ShortestFrame, codec.FrameInfo[i].Duration) : 0;

                            if (i % step == step - 1 || i == count - 1)
                            {
                                pictures.Add((Paint(pid, colour, badges, width, frame, crop), waited));
                                waited = 0;
                            }
                        }

                        if (pictures.Count > 0)
                        {
                            return pictures;
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] banner picture not read: {e.Message}");
                }
            }

            return [(Paint(pid, colour, badges, width, null, null), 0)];
        }

        /// <summary>
        /// A banner: red, green, blue and opacity of each pixel, the top row first. Made of the
        /// picture given (the frame chosen in it fills the banner; without one, its centre does), or
        /// made up when there is none.
        /// </summary>
        public static byte[] Paint(ulong pid, string colour, IReadOnlyList<string> badges, int width, SKBitmap background, float[] crop)
        {
            int Width = width;
            const int Height = NextendoFriendPage.BannerHeight;
            const float Corner = 28f;

            SKColor main = Colour(pid, colour);

            main.ToHsv(out float hue, out float saturation, out float value);

            SKColor dark = SKColor.FromHsv((hue + 335) % 360, Math.Min(100, saturation + 18), value * 0.34f);
            SKColor light = SKColor.FromHsv((hue + 38) % 360, Math.Max(35, saturation - 8), Math.Min(100, value + 14));

            using SKBitmap picture = new(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using SKCanvas canvas = new(picture);
            using SKPaint paint = new() { IsAntialias = true };

            canvas.Clear(SKColors.Transparent);
            canvas.ClipRoundRect(new SKRoundRect(new SKRect(0, 0, Width, Height), Corner), antialias: true);

            if (background != null)
            {
                // the frame chosen on the website (the whole picture when there is none, or a wrong one)
                float fx = 0, fy = 0, fw = 1, fh = 1;

                if (crop is { Length: 4 } && crop[2] > 0.01f && crop[3] > 0.01f && crop[0] >= 0 && crop[1] >= 0 && crop[0] + crop[2] <= 1.001f && crop[1] + crop[3] <= 1.001f)
                {
                    (fx, fy, fw, fh) = (crop[0], crop[1], crop[2], crop[3]);
                }

                // it covers the banner, centred on its own centre, the picture never leaving a gap
                float scale = Math.Max(Width / (fw * background.Width), Height / (fh * background.Height));
                float w = background.Width * scale, h = background.Height * scale;
                float left = Math.Min(0, Math.Max(Width - w, Width / 2f - (fx + fw / 2) * w));
                float top = Math.Min(0, Math.Max(Height - h, Height / 2f - (fy + fh / 2) * h));

                using SKPaint filling = new() { IsAntialias = true, FilterQuality = SKFilterQuality.High };

                canvas.DrawBitmap(background, SKRect.Create(left, top, w, h), filling);
            }
            else
            {
                PaintMadeUp(canvas, paint, pid, Width, Height, dark, main, light);
            }

            paint.BlendMode = SKBlendMode.SrcOver;
            paint.Style = SKPaintStyle.Fill;

            // darker at the bottom, where the badges are
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, Height * 0.45f), new SKPoint(0, Height), [SKColors.Transparent, SKColors.Black.WithAlpha(90)], SKShaderTileMode.Clamp);
            canvas.DrawPaint(paint);
            paint.Shader = null;

            PaintBadges(canvas, paint, badges, Width, Height);
            canvas.Flush();

            // as the page blends it: colours not multiplied by their opacity
            byte[] pixels = picture.Bytes;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                int opacity = pixels[i + 3];

                if (opacity is > 0 and < 255)
                {
                    pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / opacity);
                    pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / opacity);
                    pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / opacity);
                }
            }

            return pixels;
        }

        private static void PaintMadeUp(SKCanvas canvas, SKPaint paint, ulong pid, int Width, int Height, SKColor dark, SKColor main, SKColor light)
        {
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, Height), new SKPoint(Width, 0), [dark, main, light], [0f, 0.55f, 1f], SKShaderTileMode.Clamp);
            canvas.DrawPaint(paint);
            paint.Shader = null;

            // made-up shapes, the same ones each time for a friend: slanted bands, rings and dots
            Random random = new(unchecked((int)(pid * 40503u + 17)));

            paint.BlendMode = SKBlendMode.Plus;

            for (int i = 0; i < 5; i++)
            {
                float x = random.Next(-100, Width);
                float width = random.Next(50, 190);

                using SKPath band = new();

                band.MoveTo(x, Height);
                band.LineTo(x + width, Height);
                band.LineTo(x + width + Height * 0.8f, 0);
                band.LineTo(x + Height * 0.8f, 0);
                band.Close();

                paint.Color = light.WithAlpha((byte)random.Next(14, 40));
                canvas.DrawPath(band, paint);
            }

            for (int i = 0; i < 7; i++)
            {
                float x = random.Next(0, Width), y = random.Next(0, Height), radius = random.Next(26, 120);

                paint.Style = i % 2 == 0 ? SKPaintStyle.Stroke : SKPaintStyle.Fill;
                paint.StrokeWidth = random.Next(6, 18);
                paint.Color = (i % 3 == 0 ? SKColors.White : light).WithAlpha((byte)random.Next(16, 46));
                canvas.DrawCircle(x, y, radius, paint);
            }

            paint.Style = SKPaintStyle.Fill;

            for (int i = 0; i < 26; i++)
            {
                paint.Color = SKColors.White.WithAlpha((byte)random.Next(30, 90));
                canvas.DrawCircle(random.Next(0, Width), random.Next(0, Height), random.Next(2, 7), paint);
            }

        }

        private static void PaintBadges(SKCanvas canvas, SKPaint paint, IReadOnlyList<string> badges, int Width, int Height)
        {
            // the badges: in a dark pill at the bottom right
            List<SKBitmap> icons = [.. badges.Take(6).Select(Badge).Where(b => b != null)];

            if (icons.Count > 0)
            {
                const float Size = 52f, Gap = 12f, Inside = 14f, Margin = 18f;

                float pillWidth = icons.Count * Size + (icons.Count - 1) * Gap + 2 * Inside;
                float pillHeight = Size + 2 * 9f;
                SKRect pill = SKRect.Create(Width - Margin - pillWidth, Height - Margin - pillHeight, pillWidth, pillHeight);

                paint.Color = new SKColor(12, 14, 20, 170);
                canvas.DrawRoundRect(pill, pillHeight / 2, pillHeight / 2, paint);

                using SKPaint drawn = new() { IsAntialias = true, FilterQuality = SKFilterQuality.High };

                for (int i = 0; i < icons.Count; i++)
                {
                    canvas.DrawBitmap(icons[i], SKRect.Create(pill.Left + Inside + i * (Size + Gap), pill.Top + 9f, Size, Size), drawn);
                }
            }

        }
    }
}
