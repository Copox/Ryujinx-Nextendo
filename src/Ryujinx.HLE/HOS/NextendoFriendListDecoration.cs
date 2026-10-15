using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Process;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Ryujinx.HLE.HOS
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The Discord avatar decorations of friends on their small round
    /// pictures (the friend list), as on Discord: moving, the photo smaller in the circle with the
    /// decoration around it, nothing behind. The applet gets a still JPEG with a mark of its own for
    /// each decorated friend (a grey in its top left corner, hidden by the round mask), and the
    /// pictures of the animation then go to the textures that carry that mark, as for the user's
    /// own picture (NextendoOwnDecoration).
    /// </summary>
    public static class NextendoFriendListDecoration
    {
        // The programs that show friends' pictures: the HOME Menu and the user page.
        private static readonly ulong[] _programs = [0x0100000000001000, 0x0100000000001013];
        private const ulong OverlayProgram = 0x010000000000100C;

        // The mark of a friend: a light neutral grey, then the friend's own neutral grey, in whole
        // blocks of JPEG (kept almost exactly), checked closely. Not the user's own mark (dark
        // then light): a dark corner of a photo passed for one, and the photo got another friend's
        // pictures. More decorated friends than greys: still pictures.
        public const byte Light = 232;
        private static readonly byte[] _greys = [40, 88, 136, 184];
        private const int Close = 10;

        private static readonly Dictionary<ulong, byte> _marks = [];
        private static readonly Dictionary<ulong, int> _turns = [];

        private static Switch _device;

        // The mark of the pictures without a decoration: made round, once.
        private const byte Round = 64;

        private static int _rounding;

        public static void Attach(Switch device)
        {
            _device = device;
            NextendoImages.MarkRound ??= MarkRound;

            // round as soon as read: the pictures without a decoration whole, the decorated ones
            // down to their photo (the decoration around it comes with the animation)
            Ryujinx.Graphics.Gpu.GpuContext.NextendoRoundOnLoad ??= first =>
                IsMarked(first, Round) ? 1f : _greys.Any(grey => IsMarked(first, grey)) ? 1f / 1.2f : 0f;

            if (System.Threading.Interlocked.Exchange(ref _rounding, 1) == 0)
            {
                Task.Run(RoundLoop);
            }
        }

        // A friend's picture with the mark that has it made round.
        private static byte[] MarkRound(byte[] jpeg)
        {
            using SkiaSharp.SKBitmap picture = SkiaSharp.SKBitmap.Decode(jpeg);

            if (picture == null)
            {
                return null;
            }

            using (SkiaSharp.SKCanvas canvas = new(picture))
            using (SkiaSharp.SKPaint flat = new())
            {
                flat.Color = new SkiaSharp.SKColor(Light, Light, Light);
                canvas.DrawRect(SkiaSharp.SKRect.Create(0, 0, 8, 16), flat);
                flat.Color = new SkiaSharp.SKColor(Round, Round, Round);
                canvas.DrawRect(SkiaSharp.SKRect.Create(8, 0, 24, 16), flat);
                canvas.Flush();
            }

            using SkiaSharp.SKImage image = SkiaSharp.SKImage.FromBitmap(picture);
            using SkiaSharp.SKData data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 95);

            return data?.ToArray();
        }

        // The marked pictures made round as soon as a program has them (a tenth of a second
        // square at most), in the programs that show friends.
        private static async Task RoundLoop()
        {
            while (true)
            {
                try
                {
                    Switch device = _device;

                    if (device != null && !NextendoHomeMenuVanilla.Enabled)
                    {
                        foreach (KProcess process in device.System.KernelContext.Processes.Values)
                        {
                            if ((Array.IndexOf(_programs, process.TitleId) >= 0 || process.TitleId == OverlayProgram) && process.State != ProcessState.Exited)
                            {
                                device.Gpu.NextendoRoundMarked(process.Pid, first => IsMarked(first, Round));
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // the list of the programs changed meanwhile: next time
                }

                await Task.Delay(80);
            }
        }

        /// <summary>The still picture of a decorated friend (see NextendoImages.Decorate), its animation started.</summary>
        public static byte[] Decorate(ulong pid, byte[] picture, byte[] decoration)
        {
            byte mark;

            lock (_marks)
            {
                if (!_marks.TryGetValue(pid, out mark))
                {
                    mark = _marks.Count < _greys.Length ? _greys[_marks.Count] : (byte)0;
                    _marks[pid] = mark;
                }
            }

            byte[] made = NextendoFriendProfile.DecorateRound(picture, decoration, mark);

            if (made != null && mark != 0)
            {
                Animate(pid, picture, decoration, mark);
            }

            return made;
        }

        // The pictures given in turn to the textures with the friend's mark, for as long as this
        // picture of the friend is the one the applet has.
        private static void Animate(ulong pid, byte[] picture, byte[] still, byte mark)
        {
            int turn;

            lock (_turns)
            {
                turn = _turns.TryGetValue(pid, out int last) ? last + 1 : 1;
                _turns[pid] = turn;
            }

            Task.Run(async () =>
            {
                try
                {
                    string address = NextendoFriends.Get().FirstOrDefault(f => f.Pid == pid).Decoration ?? "";
                    byte[] moving = address.Length > 0 ? NextendoFriendProfile.Picture(address.Replace("passthrough=false", "passthrough=true")) : null;
                    List<(byte[] Pixels, int Milliseconds)> frames = NextendoFriendProfile.RoundFrames(picture, moving ?? still, out int side) ??
                                                                     NextendoFriendProfile.RoundFrames(picture, still, out side);

                    if (frames == null)
                    {
                        return;
                    }

                    Logger.Info?.Print(LogClass.Application, $"[Nextendo] friend list: decoration of 0x{pid:X} ({frames.Count} picture(s), mark {mark})");

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

                    for (int at = 0; Current(pid, turn); at = (at + 1) % frames.Count)
                    {
                        Switch device = _device;

                        if (device != null && !NextendoHomeMenuVanilla.Enabled)
                        {
                            foreach (KProcess process in device.System.KernelContext.Processes.Values)
                            {
                                if (Array.IndexOf(_programs, process.TitleId) >= 0 && process.State != ProcessState.Exited)
                                {
                                    int now = at;

                                    device.Gpu.NextendoUploadMarked(process.Pid, (width, height) => PixelsFor(now, width, height), first => IsMarked(first, mark));
                                }
                            }
                        }

                        // a still decoration: given again now and then (the texture may be read
                        // again from the applet's memory, with the dark corners of the JPEG)
                        await Task.Delay(frames.Count > 1 ? frames[at].Milliseconds : 100);
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] friend list: decoration of 0x{pid:X} not animated: {e.Message}");
                }
            });
        }

        // The mark in the first 16 pixels of a texture (made smaller with the picture: 8 + 24
        // pixels at 256, 2 + 6 at 64; the edge between the two greys may be a blend of them).
        private static bool IsMarked(byte[] first, byte mark)
        {
            int light = 0;

            while (light < 8 && Neutral(first, light, Light))
            {
                light++;
            }

            if (light == 0)
            {
                return false;
            }

            int from = Neutral(first, light, mark) ? light : light + 1;
            int end = Math.Min(16, light * 4 - 1);

            if (end - from < 1)
            {
                return false;
            }

            for (int pixel = from; pixel < end; pixel++)
            {
                if (!Neutral(first, pixel, mark))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Neutral(byte[] first, int pixel, int want)
        {
            int r = first[pixel * 4], g = first[pixel * 4 + 1], b = first[pixel * 4 + 2];

            return Math.Abs(r - want) <= Close && Math.Abs(g - want) <= Close && Math.Abs(b - want) <= Close && first[pixel * 4 + 3] == 255;
        }

        private static bool Current(ulong pid, int turn)
        {
            lock (_turns)
            {
                return _turns.TryGetValue(pid, out int last) && last == turn;
            }
        }
    }
}
