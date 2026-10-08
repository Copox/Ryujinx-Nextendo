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
    }
}
