using Ryujinx.Common.Logging;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Notifications of the emulator drawn like the console's (a dark card at the top
    /// left, an icon in a circle, one or two lines), laid over the picture by the GPU and sliding in
    /// and out like the console's: free text and icon (update of the emulator...).
    /// </summary>
    public static class NextendoConsoleToasts
    {
        // The card, in pixels of a 1920x1080 picture (the size of the console's notifications).
        private const int CardWidth = 560;
        private const int CardHeight = 114;
        private const double SlideSeconds = 0.25;
        private const double HoldSeconds = 4.5;

        private static readonly ConcurrentQueue<(string Title, string Text, byte[] Icon, string Glyph)> _queue = new();
        private static Thread _thread;
        private static readonly object _lock = new();

        /// <summary>The window of the GPU to lay the card over (the running emulation's), or null.</summary>
        public static Func<Ryujinx.Graphics.Gpu.Window> Target { get; set; }

        public static void Show(string title, string text, byte[] icon = null, string glyph = null)
        {
            _queue.Enqueue((title, text, icon, glyph));

            lock (_lock)
            {
                if (_thread is { IsAlive: true })
                {
                    return;
                }

                _thread = new Thread(Run) { IsBackground = true, Name = "Nextendo.ConsoleToasts" };
                _thread.Start();
            }
        }

        private static void Run()
        {
            while (_queue.TryDequeue(out (string Title, string Text, byte[] Icon, string Glyph) toast))
            {
                try
                {
                    Play(toast.Title, toast.Text, toast.Icon, toast.Glyph);
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo][notif] notification not shown: {e.Message}");
                }
            }
        }

        private static void Play(string title, string text, byte[] icon, string glyph)
        {
            if (Target?.Invoke() is not { } window)
            {
                return;
            }

            window.SetNotificationPicture(Draw(title, text, icon, glyph), CardWidth, CardHeight);

            float width = CardWidth / 1920f;
            float height = CardHeight / 1080f;
            Stopwatch clock = Stopwatch.StartNew();
            double total = SlideSeconds * 2 + HoldSeconds;

            while (clock.Elapsed.TotalSeconds < total)
            {
                double t = clock.Elapsed.TotalSeconds;
                double shown;

                if (t < SlideSeconds)
                {
                    shown = EaseOut(t / SlideSeconds);
                }
                else if (t > SlideSeconds + HoldSeconds)
                {
                    shown = 1 - EaseIn((t - SlideSeconds - HoldSeconds) / SlideSeconds);
                }
                else
                {
                    shown = 1;
                }

                window.SetNotificationPlace(true, (float)((shown - 1) * width), 0, width, height);

                Thread.Sleep(8);
            }

            window.SetNotificationPlace(false, 0, 0, 0, 0);
        }

        private static readonly Regex _svgPath = new(@"<path[^>]*? fill=""#([0-9A-Fa-f]{6})""[^>]*? d=""([^""]+)""", RegexOptions.Compiled);

        // The paths of an SVG of the assets (fill, then even-odd outline: holes are cut out).
        private static void DrawPictogram(SKCanvas canvas, string name, SKRect place)
        {
            string svg;

            try
            {
                using Stream stream = typeof(NextendoConsoleToasts).Assembly.GetManifestResourceStream($"Ryujinx.Assets.ControllerIcons.{name}.svg");

                if (stream == null)
                {
                    return;
                }

                using StreamReader reader = new(stream);
                svg = reader.ReadToEnd();
            }
            catch (Exception)
            {
                return;
            }

            canvas.Save();
            canvas.Translate(place.Left, place.Top);
            canvas.Scale(place.Width / 64f, place.Height / 64f);

            foreach (Match match in _svgPath.Matches(svg))
            {
                using SKPath path = SKPath.ParseSvgPathData(match.Groups[2].Value);

                if (path == null)
                {
                    continue;
                }


                using SKPaint paint = new() { Color = SKColor.Parse(match.Groups[1].Value), IsAntialias = true };
                canvas.DrawPath(path, paint);
            }

            canvas.Restore();
        }

        private static double EaseOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);

        private static double EaseIn(double t) => Math.Pow(Math.Clamp(t, 0, 1), 3);

        // The card: the console's dark grey, a thin light edge, the icon in a circle, the text in white.
        private static byte[] Draw(string title, string text, byte[] icon, string glyph)
        {
            SKImageInfo info = new(CardWidth, CardHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);

            using SKBitmap bitmap = new(info);
            using SKCanvas canvas = new(bitmap);

            canvas.Clear(new SKColor(0x0F, 0x0F, 0x0F, 0xF0));

            using (SKPaint edge = new() { Color = new SKColor(0x3A, 0x3A, 0x3A), Style = SKPaintStyle.Stroke, StrokeWidth = 2 })
            {
                canvas.DrawRect(1, 1, CardWidth - 2, CardHeight - 2, edge);
            }

            const float iconSize = 76;
            SKRect iconRect = SKRect.Create(26, (CardHeight - iconSize) / 2, iconSize, iconSize);
            SKBitmap decoded = icon != null ? SKBitmap.Decode(icon) : null;

            if (decoded != null && decoded.Width > decoded.Height * 1.3f)
            {
                // A screenshot: a 16:9 frame with rounded corners.
                iconRect = SKRect.Create(22, (CardHeight - 72) / 2, 128, 72);

                using (decoded)
                using (SKPath frame = new())
                {
                    frame.AddRoundRect(iconRect, 6, 6);
                    canvas.Save();
                    canvas.ClipPath(frame, SKClipOperation.Intersect, true);
                    canvas.DrawBitmap(decoded, iconRect, new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true });
                    canvas.Restore();
                }

                decoded = null;
            }
            else if (decoded is { } picture)
            {
                // A game icon or a badge: a square with slightly rounded corners.
                using (picture)
                using (SKPath square = new())
                {
                    square.AddRoundRect(iconRect, 6, 6);
                    canvas.Save();
                    canvas.ClipPath(square, SKClipOperation.Intersect, true);
                    canvas.DrawBitmap(picture, iconRect, new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true });
                    canvas.Restore();
                }
            }
            else if (!string.IsNullOrEmpty(glyph))
            {
                // A pictogram of Assets/ControllerIcons (Kenney's Input Prompts, 64x64), white like
                // the HOME Menu's own.
                iconRect = SKRect.Create(18, (CardHeight - 92) / 2f, 92, 92);
                // "controller+flair": the controller a little smaller at the top left, the flair
                // (battery, disconnected) over its bottom right corner, as Kenney composes them.
                string[] parts = glyph.Split('+');

                if (parts.Length > 1)
                {
                    DrawPictogram(canvas, parts[0], SKRect.Create(iconRect.Left, iconRect.Top, iconRect.Width * 0.86f, iconRect.Height * 0.86f));
                    DrawPictogram(canvas, parts[1], iconRect);
                }
                else
                {
                    DrawPictogram(canvas, glyph, iconRect);
                }
            }

            float textLeft = iconRect.Right + 28;
            float textWidth = CardWidth - textLeft - 20;

            using SKTypeface typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal) ?? SKTypeface.Default;
            using SKPaint white = new() { Color = SKColors.White, IsAntialias = true, Typeface = typeface, TextSize = 27 };
            using SKPaint grey = new() { Color = new SKColor(0xC8, 0xC8, 0xC8), IsAntialias = true, Typeface = typeface, TextSize = 23 };

            if (string.IsNullOrEmpty(text))
            {
                canvas.DrawText(Fit(title, white, textWidth), textLeft, CardHeight / 2f + 10, white);
            }
            else
            {
                canvas.DrawText(Fit(title, white, textWidth), textLeft, CardHeight / 2f - 6, white);
                canvas.DrawText(Fit(text, grey, textWidth), textLeft, CardHeight / 2f + 26, grey);
            }

            canvas.Flush();

            return bitmap.Bytes;
        }

        // A line too long for the card ends in "...".
        private static string Fit(string line, SKPaint paint, float width)
        {
            if (paint.MeasureText(line) <= width)
            {
                return line;
            }

            while (line.Length > 1 && paint.MeasureText(line + "…") > width)
            {
                line = line[..^1];
            }

            return line + "…";
        }
    }
}
