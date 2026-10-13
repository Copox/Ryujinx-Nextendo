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
        private static readonly int[] _delays = [0, 120, 250, 400, 600, 900, 1300, 1800, 2600, 4000];

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };
        private static readonly ConcurrentDictionary<string, SKBitmap> _badges = new();

        private static Switch _device;
        private static bool _listening;
        private static int _shown;

        public static void Attach(Switch device)
        {
            _device = device;

            if (!_listening)
            {
                _listening = true;
                NextendoFriends.FriendPageShown += Shown;
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

            Task.Run(async () =>
            {
                try
                {
                    NextendoFriends.Entry friend = NextendoFriends.Get().FirstOrDefault(f => f.Pid == pid);
                    // NEXTENDO_FRIEND_BADGES="admin,donor,...": those on every page, to see them drawn
                    string forced = Environment.GetEnvironmentVariable("NEXTENDO_FRIEND_BADGES");
                    byte[] banner = Paint(pid, friend.Color, string.IsNullOrEmpty(forced) ? friend.Badges ?? [] : forced.Split(','));

                    Logger.Info?.Print(LogClass.Application, $"[Nextendo] friend page: banner of 0x{pid:X} ({friend.Badges?.Length ?? 0} badge(s), colour \"{friend.Color}\")");

                    foreach (int delay in _delays)
                    {
                        await Task.Delay(delay);

                        // another friend's page meanwhile: theirs is the one to show
                        if (turn != _shown || device != _device)
                        {
                            return;
                        }

                        foreach (KProcess process in device.System.KernelContext.Processes.Values)
                        {
                            if (process.TitleId == MyPageProgram)
                            {
                                device.Gpu.NextendoUploadTexture(process.Pid, NextendoFriendPage.BannerWidth, NextendoFriendPage.BannerHeight, banner, 4);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] friend page: banner not painted: {e.Message}");
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

                return Paint(NextendoAccount.Pid, colour, badges);
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] own page: banner not painted: {e.Message}");

                return null;
            }
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

        /// <summary>The banner of a profile: red, green, blue and opacity of each pixel, the top row first.</summary>
        public static byte[] Paint(ulong pid, string colour, IReadOnlyList<string> badges)
        {
            const int Width = NextendoFriendPage.BannerWidth, Height = NextendoFriendPage.BannerHeight;
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

            paint.BlendMode = SKBlendMode.SrcOver;

            // darker at the bottom, where the badges are
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, Height * 0.45f), new SKPoint(0, Height), [SKColors.Transparent, SKColors.Black.WithAlpha(90)], SKShaderTileMode.Clamp);
            canvas.DrawPaint(paint);
            paint.Shader = null;

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
    }
}
