using System;
using System.Collections.Concurrent;
using static SDL.SDL3;
using SDL;

namespace Ryujinx.Input.SDL3
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The colours of a Joy-Con, as it keeps them in its memory (body
    /// at 0x6050, buttons at 0x6053): read once when it connects, for the console's "Joy-Con
    /// attached" effect to be in its colour.
    /// </summary>
    public unsafe class JoyConColors
    {
        private static readonly ConcurrentDictionary<string, JoyConColors> _known = new();

        public uint Body { get; private set; }
        public uint Buttons { get; private set; }

        /// <summary>The colours read for the Joy-Con of that name, if they were.</summary>
        public static JoyConColors Of(string name) => name != null && _known.TryGetValue(name, out JoyConColors colors) ? colors : null;

        public static void Read(string name, string path)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
            {
                return;
            }

            SDL_hid_device* device = null;

            try
            {
                SDL_hid_init();
                device = SDL_hid_open_path(path);

                if (device == null)
                {
                    return;
                }

                // Subcommand 0x10 (read the memory): 6 bytes at 0x6050, with a neutral rumble.
                byte[] request = new byte[49];

                request[0] = 0x01;
                request[1] = 0x0F;
                new byte[] { 0x00, 0x01, 0x40, 0x40, 0x00, 0x01, 0x40, 0x40 }.CopyTo(request, 2);
                request[10] = 0x10;
                request[11] = 0x50;
                request[12] = 0x60;
                request[15] = 0x06;

                fixed (byte* data = request)
                {
                    if (SDL_hid_write(device, data, (nuint)request.Length) < 0)
                    {
                        return;
                    }
                }

                byte[] reply = new byte[64];
                long until = Environment.TickCount64 + 400;

                while (Environment.TickCount64 < until)
                {
                    int read;

                    fixed (byte* data = reply)
                    {
                        read = SDL_hid_read_timeout(device, data, (nuint)reply.Length, 50);
                    }

                    // The answer: report 0x21, subcommand 0x10, the address and the size asked.
                    if (read >= 26 && reply[0] == 0x21 && reply[14] == 0x10 && reply[15] == 0x50 && reply[16] == 0x60 && reply[19] == 0x06)
                    {
                        _known[name] = new JoyConColors
                        {
                            Body = (uint)(reply[20] << 16 | reply[21] << 8 | reply[22]),
                            Buttons = (uint)(reply[23] << 16 | reply[24] << 8 | reply[25]),
                        };

                        return;
                    }
                }
            }
            catch (Exception)
            {
                // the colours of a Joy-Con as sold are used
            }
            finally
            {
                if (device != null)
                {
                    SDL_hid_close(device);
                }
            }
        }
    }
}
