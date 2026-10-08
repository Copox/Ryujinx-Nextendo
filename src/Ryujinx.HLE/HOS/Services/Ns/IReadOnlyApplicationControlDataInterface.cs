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
        // GetApplicationControlData(u8 source, u8, u8, u64 applicationId) -> (u64, buffer<bytes, 6>)
        public ResultCode GetApplicationControlDataWithIcon(ServiceCtx context)
        {
            const int NacpSize = 0x4000;

            context.RequestData.ReadUInt64(); // source and two flags
            ulong applicationId = context.RequestData.ReadUInt64();

            ulong position = context.Request.ReceiveBuff[0].Position;
            ulong capacity = context.Request.ReceiveBuff[0].Size;

            Ryujinx.HLE.HOS.HomeMenuTitle title = Ryujinx.HLE.HOS.HomeMenuLibrary.Find(applicationId);

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
            byte[] icon = layout == "r" && title.Icon != null ? DecodeIcon(title.Icon, 160) : title.Icon;

            int iconSize = 0;

            if (icon != null && (ulong)(NacpSize + icon.Length) <= capacity)
            {
                context.Memory.Write(position + NacpSize, icon);

                iconSize = icon.Length;
            }

            // Three values come back (seen in the HOME Menu's client code): a u32 it drops, a
            // flag, then the size of what was written, control property included.
            context.ResponseData.Write(0u);
            context.ResponseData.Write(0u);
            context.ResponseData.Write((uint)(NacpSize + iconSize));

            Ryujinx.Common.Logging.Logger.Notice.Print(Ryujinx.Common.Logging.LogClass.Service, $"[home] control data of {applicationId:X16}: icon {iconSize} bytes, layout {layout}");

            return ResultCode.Success;
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
