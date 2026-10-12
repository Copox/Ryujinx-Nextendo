using Ryujinx.Common.Logging;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
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

        private static readonly ConcurrentQueue<(string Title, string Text, byte[] Icon)> _queue = new();
        private static Thread _thread;
        private static readonly object _lock = new();

        /// <summary>The window of the GPU to lay the card over (the running emulation's), or null.</summary>
        public static Func<Ryujinx.Graphics.Gpu.Window> Target { get; set; }

        public static void Show(string title, string text, byte[] icon = null)
        {
            _queue.Enqueue((title, text, icon));

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
            while (_queue.TryDequeue(out (string Title, string Text, byte[] Icon) toast))
            {
                try
                {
                    Play(toast.Title, toast.Text, toast.Icon);
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo][notif] notification not shown: {e.Message}");
                }
            }
        }

        private static void Play(string title, string text, byte[] icon)
        {
            if (Target?.Invoke() is not { } window)
            {
                return;
            }

            window.SetNotificationPicture(Draw(title, text, icon), CardWidth, CardHeight);

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

        private static double EaseOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);

        private static double EaseIn(double t) => Math.Pow(Math.Clamp(t, 0, 1), 3);

        // The card: the console's dark grey, a thin light edge, the icon in a circle, the text in white.
        private static byte[] Draw(string title, string text, byte[] icon)
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

            if (icon != null && SKBitmap.Decode(icon) is { } picture)
            {
                using (picture)
                using (SKPath circle = new())
                {
                    circle.AddOval(iconRect);
                    canvas.Save();
                    canvas.ClipPath(circle, SKClipOperation.Intersect, true);
                    canvas.DrawBitmap(picture, iconRect, new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true });
                    canvas.Restore();
                }

                using SKPaint ring = new() { Color = new SKColor(0x60, 0x60, 0x60), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
                canvas.DrawOval(iconRect, ring);
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
