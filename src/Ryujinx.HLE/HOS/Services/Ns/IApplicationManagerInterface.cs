using LibHac.Ns;
using Ryujinx.Common.Utilities;

namespace Ryujinx.HLE.HOS.Services.Ns
{
    [Service("ns:am")]
    class IApplicationManagerInterface : IpcService
    {
        private readonly StubEvent _recordUpdateEvent;
        private readonly StubEvent _sdCardMountStatusEvent;
        private readonly StubEvent _gameCardUpdateEvent;
        private readonly StubEvent _gameCardMountFailureEvent;

        public IApplicationManagerInterface(ServiceCtx context)
        {
            _recordUpdateEvent = new StubEvent(context);
            Ryujinx.HLE.HOS.HomeMenuLibrary.Changed += _recordUpdateEvent.Signal;
            _sdCardMountStatusEvent = new StubEvent(context);
            _gameCardUpdateEvent = new StubEvent(context);
            _gameCardMountFailureEvent = new StubEvent(context);
        }

        [CommandCmif(0)]
        // ListApplicationRecord(s32 offset) -> (s32 count, buffer<nn::ns::ApplicationRecord, 6>)
        public ResultCode ListApplicationRecord(ServiceCtx context)
        {
            // No installed application is reported to the HOME Menu yet.
            context.ResponseData.Write(0);

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // GetApplicationRecordUpdateSystemEvent() -> handle<copy>
        public ResultCode GetApplicationRecordUpdateSystemEvent(ServiceCtx context) => _recordUpdateEvent.ReplyWithHandle(context);

        [CommandCmif(44)]
        // GetSdCardMountStatusChangedEvent() -> handle<copy>
        public ResultCode GetSdCardMountStatusChangedEvent(ServiceCtx context) => _sdCardMountStatusEvent.ReplyWithHandle(context);

        [CommandCmif(52)]
        // GetGameCardUpdateDetectionEvent() -> handle<copy>
        public ResultCode GetGameCardUpdateDetectionEvent(ServiceCtx context) => _gameCardUpdateEvent.ReplyWithHandle(context);

        [CommandCmif(505)]
        // GetGameCardMountFailureEvent() -> handle<copy>
        public ResultCode GetGameCardMountFailureEvent(ServiceCtx context) => _gameCardMountFailureEvent.ReplyWithHandle(context);

        [CommandCmif(400)]
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
        [CommandCmif(1706)] // 20.0.0+
        // GetApplicationView(buffer<nn::ncm::ApplicationId, 5>) -> buffer<nn::ns::ApplicationView, 6>
        public ResultCode GetApplicationView(ServiceCtx context)
        {
            const int ViewSize = 0x58;

            ulong inPosition = context.Request.SendBuff[0].Position;
            int count = (int)(context.Request.SendBuff[0].Size / 8);
            ulong outPosition = context.Request.ReceiveBuff[0].Position;
            int capacity = (int)(context.Request.ReceiveBuff[0].Size / ViewSize);

            for (int i = 0; i < count && i < capacity; i++)
            {
                ulong applicationId = context.Memory.Read<ulong>(inPosition + (ulong)(i * 8));
                ulong view = outPosition + (ulong)(i * ViewSize);

                // A title that is fully on the console: nothing downloading, nothing to update.
                context.Memory.Write(view, new byte[ViewSize]);
                context.Memory.Write(view + 0x00, applicationId);
                context.Memory.Write(view + 0x08, 0u);         // version
                context.Memory.Write(view + 0x0C, 0x401f17u);  // can be launched, has all its contents
            }

            return ResultCode.Success;
        }
        [CommandCmif(1704)] // 20.0.0+
        // GetApplicationViewWithPromotionInfo(buffer<nn::ncm::ApplicationId, 5>) -> buffer<nn::ns::ApplicationViewWithPromotionInfo, 6>
        public ResultCode GetApplicationViewWithPromotionInfo(ServiceCtx context)
        {
            const int EntrySize = 0x78; // the 0x58 bytes view, then 0x20 bytes of promotion info

            ulong inPosition = context.Request.SendBuff[0].Position;
            int count = (int)(context.Request.SendBuff[0].Size / 8);
            ulong outPosition = context.Request.ReceiveBuff[0].Position;
            int capacity = (int)(context.Request.ReceiveBuff[0].Size / EntrySize);

            for (int i = 0; i < count && i < capacity; i++)
            {
                ulong applicationId = context.Memory.Read<ulong>(inPosition + (ulong)(i * 8));
                ulong entry = outPosition + (ulong)(i * EntrySize);

                context.Memory.Write(entry, new byte[EntrySize]);
                context.Memory.Write(entry + 0x00, applicationId);
                context.Memory.Write(entry + 0x08, 0u);
                context.Memory.Write(entry + 0x0C, 0x401f17u);
            }

            return ResultCode.Success;
        }

        [CommandCmif(2050)] // 6.0.0+
        // GetApplicationRightsOnClient(u32 flags, nn::ncm::ApplicationId, nn::account::Uid) -> (s32 count, buffer<nn::ns::ApplicationRightsOnClient, 6>)
        public ResultCode GetApplicationRightsOnClient(ServiceCtx context)
        {
            context.RequestData.ReadUInt64(); // flags
            ulong applicationId = context.RequestData.ReadUInt64();
            ulong uidLow = context.RequestData.ReadUInt64();
            ulong uidHigh = context.RequestData.ReadUInt64();

            ulong position = context.Request.ReceiveBuff[0].Position;

            // One entry: the title belongs to this console, for every user.
            context.Memory.Write(position, new byte[0x20]);
            context.Memory.Write(position + 0x00, applicationId);
            context.Memory.Write(position + 0x08, uidLow);
            context.Memory.Write(position + 0x10, uidHigh);
            context.Memory.Write(position + 0x18, (byte)(int.TryParse(System.Environment.GetEnvironmentVariable("NEXTENDO_NS_RIGHTS"), out int rights) ? rights : 1));
            context.Memory.Write(position + 0x19, (byte)0);

            context.ResponseData.Write(1);

            return ResultCode.Success;
        }
        private StubEvent _controlDataDownloadEvent;

        [CommandCmif(412)] // 20.0.0+
        // RequestDownloadApplicationControlData(u8, nn::ncm::ApplicationId, u64) -> (handle<copy>, object<nn::ns::detail::IAsyncResult>)
        public ResultCode RequestDownloadApplicationControlData(ServiceCtx context)
        {
            // Nothing is downloaded: what is known of a title is already served by the control
            // data interface. The request completes at once.
            _controlDataDownloadEvent ??= new StubEvent(context);
            _controlDataDownloadEvent.Signal();
            _controlDataDownloadEvent.ReplyWithHandle(context);

            MakeObject(context, new IAsyncResult());

            return ResultCode.Success;
        }
    }
}
