using System;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// Pictures coming from Nextendo, put in the form the console software reads: a square
    /// baseline JPEG of a given side. The HOME Menu shows a "?" for a profile picture that is
    /// not 256x256 (accounts keep them up to 512x512).
    /// The conversion itself lives where an image library is available and is plugged in here.
    /// </summary>
    public static class NextendoImages
    {
        public static Func<byte[], int, byte[]> SquareJpeg { get; set; }

        /// <summary>
        /// [Nextendo] A profile picture (JPEG) with a Discord avatar decoration (PNG) over it, as a
        /// JPEG of the same size, or null. Set by the layer that draws pictures (HLE).
        /// </summary>
        public static Func<ulong, byte[], byte[], byte[]> Decorate { get; set; }

        /// <summary>
        /// [Nextendo] A friend's picture (JPEG) with the mark that has the emulator make it round
        /// in the textures of the HOME Menu, or null. Set by the layer that draws pictures (HLE).
        /// </summary>
        public static Func<byte[], byte[]> MarkRound { get; set; }

        /// <summary>
        /// The picture of a player who never chose one: the default avatar of the account
        /// server (head and shoulders on a diagonal gradient), tinted from the PID exactly as
        /// the Nextendo apps and the site do. Takes (pid, side), returns a JPEG.
        /// </summary>
        public static Func<ulong, int, byte[]> Silhouette { get; set; }

        public static byte[] Default(ulong pid, int side)
        {
            try
            {
                return Silhouette?.Invoke(pid, side);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static byte[] Fit(byte[] image, int side)
        {
            if (image == null || image.Length == 0 || SquareJpeg == null)
            {
                return image;
            }

            try
            {
                return SquareJpeg(image, side) ?? image;
            }
            catch (Exception)
            {
                return image;
            }
        }
    }
}
