namespace Ryujinx.HLE.HOS.Services.Npns
{
    [Service("npns:a")]
    [Service("npns:s")]
    class INpnsSystem : IpcService
    {
        public INpnsSystem(ServiceCtx context) { }
        private Ryujinx.HLE.HOS.Services.StubEvent _receiveEvent;

        [CommandCmif(5)]
        // GetReceiveEvent() -> handle<copy>
        public ResultCode GetReceiveEvent(ServiceCtx context)
        {
            _receiveEvent ??= new Ryujinx.HLE.HOS.Services.StubEvent(context);
            _receiveEvent.ReplyWithHandle(context);

            return ResultCode.Success;
        }

        private Ryujinx.HLE.HOS.Services.StubEvent _stateChangeEvent;

        [CommandCmif(7)]
        // GetStateChangeEvent() -> handle<copy>
        public ResultCode GetStateChangeEvent(ServiceCtx context)
        {
            _stateChangeEvent ??= new Ryujinx.HLE.HOS.Services.StubEvent(context);
            _stateChangeEvent.ReplyWithHandle(context);

            return ResultCode.Success;
        }

        [CommandCmif(8)]
        // ListenToByName(buffer<bytes, 0x19> name)
        public ResultCode ListenToByName(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(103)]
        // GetState() -> u32
        public ResultCode GetState(ServiceCtx context)
        {
            context.ResponseData.Write(0);

            return ResultCode.Success;
        }

        private Ryujinx.HLE.HOS.Services.StubEvent _cancelEvent;

        [CommandCmif(161)]
        // GetRequestChangeStateCancelEvent() -> handle<copy>
        public ResultCode GetRequestChangeStateCancelEvent(ServiceCtx context)
        {
            // The caller raises this event itself to cancel a pending state change.
            _cancelEvent ??= new Ryujinx.HLE.HOS.Services.StubEvent(context);
            _cancelEvent.ReplyWithWritableHandle(context);

            return ResultCode.Success;
        }

        [CommandCmif(162)]
        // RequestChangeStateForceTimedWithCancelEvent(u32, u32)
        public ResultCode RequestChangeStateForceTimedWithCancelEvent(ServiceCtx context)
        {

            return ResultCode.Success;
        }
    }
}
