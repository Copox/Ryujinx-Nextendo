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
