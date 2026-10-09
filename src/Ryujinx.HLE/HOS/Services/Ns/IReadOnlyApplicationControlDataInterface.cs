using LibHac.Common;
using LibHac.Ns;

namespace Ryujinx.HLE.HOS.Services.Ns
{
    class IReadOnlyApplicationControlDataInterface : IpcService
    {
        public IReadOnlyApplicationControlDataInterface(ServiceCtx context) { }

        [CommandCmif(0)]
        // GetApplicationControlData(u8, u64) -> (unknown<4>, buffer<unknown, 6>)
        public ResultCode GetApplicationControlData(ServiceCtx context)
        {
#pragma warning disable IDE0059 // Remove unnecessary value assignment
            byte source = (byte)context.RequestData.ReadInt64();
            ulong titleId = context.RequestData.ReadUInt64();
#pragma warning restore IDE0059

            ulong position = context.Request.ReceiveBuff[0].Position;

            ApplicationControlProperty nacp = context.Device.Processes.GetProcess(context.ClientProcessId).ApplicationControlProperties;

            context.Memory.Write(position, SpanHelpers.AsByteSpan(ref nacp).ToArray());

            return ResultCode.Success;
        }
        [CommandCmif(1)]
        // GetApplicationDesiredLanguage(u32 supportedLanguages) -> u8
        public ResultCode GetApplicationDesiredLanguage(ServiceCtx context)
        {
            uint supported = context.RequestData.ReadUInt32();

            // Index in the control property's title table: the console language when the title
            // has it, American English otherwise, else the first one it has.
            int wanted = (int)context.Device.System.State.DesiredTitleLanguage;
            int language = 0;

            if (wanted is >= 0 and < 32 && (supported & (1u << wanted)) != 0)
            {
                language = wanted;
            }
            else if ((supported & 1) == 0 && supported != 0)
            {
                language = System.Numerics.BitOperations.TrailingZeroCount(supported);
            }

            context.ResponseData.Write((byte)language);

            return ResultCode.Success;
        }

        [CommandCmif(19)] // 19.0.0+
        [CommandCmif(20)] // 20.0.0+, asked for titles that are not on the console (a friend's game)
        // GetApplicationControlData(u8 source, u8, u8, u64 applicationId) -> (u64, buffer<bytes, 6>)
        public ResultCode GetApplicationControlDataWithIcon(ServiceCtx context)
        {
            const int NacpSize = 0x4000;

            // source, then two flags: the first asks for the icon already decoded.
            ulong options = context.RequestData.ReadUInt64();
            bool decoded = ((options >> 8) & 0xFF) != 0;
            ulong applicationId = context.RequestData.ReadUInt64();

            ulong position = context.Request.ReceiveBuff[0].Position;
            ulong capacity = context.Request.ReceiveBuff[0].Size;

            Ryujinx.HLE.HOS.HomeMenuTitle title = Ryujinx.HLE.HOS.HomeMenuLibrary.Find(applicationId);

            if (title == null && capacity >= NacpSize)
            {
                // Not in the library: a game known from Nextendo (what a friend plays or played).
                string name = null;
                byte[] picture = null;

                if (Ryujinx.Common.Configuration.NextendoGames.TryGet(applicationId, out Ryujinx.Common.Configuration.NextendoGames.Game game))
                {
                    name = game.Name;
                    picture = game.Icon;
                }
                else if (System.Environment.GetEnvironmentVariable("NEXTENDO_FRIEND_DETAIL_PROBE") != null)
                {
                    name = applicationId.ToString("X16"); // probing aid: shows which field held the id
                    picture = Ryujinx.HLE.HOS.HomeMenuLibrary.Titles().Count > 0 ? Ryujinx.HLE.HOS.HomeMenuLibrary.Titles()[0].Icon : null;
                }

                if (name != null)
                {
                    title = new Ryujinx.HLE.HOS.HomeMenuTitle { Id = applicationId, Name = name, Icon = picture, Nacp = BuildNacp(name) };
                }
            }

            if (title == null || capacity < NacpSize)
            {
                // Not one of ours: no data.
                context.ResponseData.Write(0UL);

                return ResultCode.Success;
            }

            byte[] nacp = new byte[NacpSize];

            if (title.Nacp != null)
            {
                System.Array.Copy(title.Nacp, nacp, System.Math.Min(title.Nacp.Length, NacpSize));
            }

            context.Memory.Write(position, nacp);

            // The icon follows the control property, as the title carries it (a JPEG).
            string layout = System.Environment.GetEnvironmentVariable("NEXTENDO_NS_CONTROL_LAYOUT") ?? "j";
            byte[] icon = title.Icon;

            if (decoded && icon != null && capacity > NacpSize && System.Environment.GetEnvironmentVariable("NEXTENDO_NS20_RAW") == "1")
            {
                // What is left of the buffer after the control property is exactly a square of
                // four-byte pixels (128x128 for a title that is not on the console).
                int side = (int)System.Math.Sqrt((capacity - NacpSize) / 4);

                icon = DecodeIcon(icon, side);
            }

            int iconSize = 0;

            // The buffer for a title that is not on the console is smaller: the picture is
            // compressed again until it fits.
            if (decoded && icon != null)
            {
                // Asked this way (the friend page, for a game that is not on the console), the
                // picture is drawn in a 174x174 frame and has to come at that size, under 64 KiB:
                // at 256 or 128 the page says the loading failed.
                icon = ResizeIcon(icon, 174);
            }

            if (decoded && icon == null)
            {
                // A game the server names but has no picture of: a plain tile, so that its name
                // shows instead of "loading failed".
                using SkiaSharp.SKBitmap blank = new(174, 174);
                blank.Erase(new SkiaSharp.SKColor(0xC8, 0xC8, 0xC8));
                using SkiaSharp.SKData plain = blank.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);

                icon = plain?.ToArray();
            }

            if (icon != null && System.Environment.GetEnvironmentVariable("NEXTENDO_FORCE_REENCODE") == "1")
            {
                icon = ShrinkIcon(icon, 60000); // test: are re-encoded pictures readable by the menu?
            }

            if (icon != null && (ulong)(NacpSize + icon.Length) > capacity && capacity > NacpSize)
            {
                icon = ShrinkIcon(icon, (int)(capacity - NacpSize));
            }

            if (icon != null && (ulong)(NacpSize + icon.Length) <= capacity)
            {
                context.Memory.Write(position + NacpSize, icon);

                iconSize = icon.Length;
            }

            // Three values come back (seen in the HOME Menu's client code): a u32 it drops, a
            // flag, then the size of what was written, control property included.
            // The flag says the card is there (seen in the HOME Menu's code: it must read 1 when
            // the decoded form was asked for, or a download is requested and the card refused).
            string variant = decoded ? "a" : "";

            context.ResponseData.Write(variant is "b" or "c" ? 1u : 0u);
            context.ResponseData.Write(variant is "a" or "c" ? 1u : 0u);
            context.ResponseData.Write((uint)(NacpSize + iconSize));

            Ryujinx.Common.Logging.Logger.Notice.Print(Ryujinx.Common.Logging.LogClass.Service, $"[home] control data of {applicationId:X16}: icon {iconSize} bytes, layout {layout}");

            return ResultCode.Success;
        }
        // A control property that only names the title, in every language slot.
        private static byte[] BuildNacp(string name)
        {
            byte[] nacp = new byte[0x4000];
            byte[] text = System.Text.Encoding.UTF8.GetBytes(name);
            int length = System.Math.Min(text.Length, 0x1FF);

            for (int language = 0; language < 16; language++)
            {
                System.Array.Copy(text, 0, nacp, language * 0x300, length);
            }

            // SupportedLanguageFlag: all of them.
            System.BitConverter.TryWriteBytes(new System.Span<byte>(nacp, 0x302C, 4), 0xFFFFu);

            return nacp;
        }

        // The picture as a JPEG of `side` pixels a side.
        private static byte[] ResizeIcon(byte[] image, int side)
        {
            try
            {
                using SkiaSharp.SKBitmap source = SkiaSharp.SKBitmap.Decode(image);

                if (source == null)
                {
                    return null;
                }

                using SkiaSharp.SKBitmap scaled = source.Resize(new SkiaSharp.SKImageInfo(side, side), SkiaSharp.SKFilterQuality.High);
                using SkiaSharp.SKData encoded = scaled?.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);

                return encoded?.ToArray();
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static byte[] ShrinkIcon(byte[] image, int maxBytes)
        {
            try
            {
                using SkiaSharp.SKBitmap source = SkiaSharp.SKBitmap.Decode(image);

                if (source == null)
                {
                    return null;
                }

                for (int quality = 85; quality >= 35; quality -= 10)
                {
                    using SkiaSharp.SKData encoded = source.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, quality);

                    if (encoded != null && encoded.Size <= maxBytes)
                    {
                        return encoded.ToArray();
                    }
                }
            }
            catch (System.Exception)
            {
            }

            return null;
        }

        private static byte[] DecodeIcon(byte[] image, int side)
        {
            try
            {
                using SkiaSharp.SKBitmap source = SkiaSharp.SKBitmap.Decode(image);

                if (source == null)
                {
                    return null;
                }

                SkiaSharp.SKImageInfo info = new(side, side, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);

                using SkiaSharp.SKBitmap scaled = new(info);

                if (!source.ScalePixels(scaled, SkiaSharp.SKFilterQuality.High))
                {
                    return null;
                }

                return scaled.Bytes;
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
