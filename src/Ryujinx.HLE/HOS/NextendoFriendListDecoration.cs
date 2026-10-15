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

        // The greys of the marks, one for each decorated friend, far enough from each other and
        // from the user's own (240) for a JPEG to keep them apart. More friends: still pictures.
        private static readonly byte[] _greys = [48, 96, 144, 192];

        private static readonly Dictionary<ulong, byte> _marks = [];
        private static readonly Dictionary<ulong, int> _turns = [];

        private static Switch _device;

        public static void Attach(Switch device) => _device = device;

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

                                    device.Gpu.NextendoUploadMarked(process.Pid, (width, height) => PixelsFor(now, width, height), first => NextendoOwnDecoration.IsMarked(first, mark));
                                }
                            }
                        }

                        // a still decoration: given again now and then (the texture may be read
                        // again from the applet's memory, with the dark corners of the JPEG)
                        await Task.Delay(frames.Count > 1 ? frames[at].Milliseconds : 500);
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] friend list: decoration of 0x{pid:X} not animated: {e.Message}");
                }
            });
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
