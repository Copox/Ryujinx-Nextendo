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
        [CommandCmif(22)] // 20.0.0+, the virtual game cards page
        // GetApplicationControlData(u8 source, u8, u8, u64 applicationId) -> (u64, buffer<bytes, 6>)
        public ResultCode GetApplicationControlDataWithIcon(ServiceCtx context)
        {
            const int NacpSize = 0x4000;

            // source, then two flags: the first asks for the icon already decoded.
            ulong options = context.RequestData.ReadUInt64();
            bool decoded = ((options >> 8) & 0xFF) != 0;
            ulong applicationId = context.RequestData.ReadUInt64();

            Ryujinx.Common.Logging.Logger.Notice.Print(Ryujinx.Common.Logging.LogClass.Service, $"[home] title card asked: {applicationId:X16} options={options:X} capacity=0x{context.Request.ReceiveBuff[0].Size:X} cmd={context.RequestData.BaseStream.Length}");

            ulong position = context.Request.ReceiveBuff[0].Position;
            ulong capacity = context.Request.ReceiveBuff[0].Size;

            long command = System.BitConverter.ToInt64(((System.IO.MemoryStream)context.RequestData.BaseStream).GetBuffer(), 8);

            // The virtual game cards page (22) has the friend page's room: the picture at that size.
            decoded |= command == 22;

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

                if (name != null)
                {
                    title = new Ryujinx.HLE.HOS.HomeMenuTitle { Id = applicationId, Name = name, Icon = picture, Nacp = BuildNacp(name, applicationId) };
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
            byte[] icon = title.Icon;

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

            // the virtual game cards page asks for the picture decoded: the state says it is (bit 16)
            context.ResponseData.Write(command == 22 && iconSize > 0 ? 0x10000u : variant is "b" or "c" ? 1u : 0u);
            context.ResponseData.Write(variant is "a" or "c" ? 1u : 0u);
            context.ResponseData.Write((uint)(NacpSize + iconSize));

            Ryujinx.Common.Logging.Logger.Notice.Print(Ryujinx.Common.Logging.LogClass.Service, $"[home] control data of {applicationId:X16}: icon {iconSize} bytes, startup user account {nacp[0x3025]} option {nacp[0x3026]} NACP3000 {System.Convert.ToHexString(nacp, 0x3000, 0x400)}");

            return ResultCode.Success;
        }
        // A control property that only names the title, in every language slot.
        // The HOME Menu (the card of a friend on a console without a game) in the console's
        // languages, in the order of the slots of a control property (Nintendo's own names).
        private static readonly string[] _homeMenuNames =
        [
            "HOME Menu", "HOME Menu", "HOMEメニュー", "Menu HOME", "HOME-Menü", "Menú HOME", "Menú HOME", "Menu HOME",
            "HOME-menu", "Menu HOME", "Menu HOME", "Меню HOME", "HOME 메뉴", "HOME選單", "HOME菜单", "Menu HOME",
        ];

        private static byte[] BuildNacp(string name, ulong applicationId = 0)
        {
            byte[] nacp = new byte[0x4000];

            for (int language = 0; language < 16; language++)
            {
                string named = applicationId == Ryujinx.Common.Configuration.NextendoGames.HomeMenuId ? _homeMenuNames[language] : name;
                byte[] text = System.Text.Encoding.UTF8.GetBytes(named);

                System.Array.Copy(text, 0, nacp, language * 0x300, System.Math.Min(text.Length, 0x1FF));
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
