namespace Ryujinx.HLE.HOS.Services.Npns
{
    [Service("npns:u")]
    class INpnsUser : IpcService
    {
        private StubEvent _receiveEvent;
        private StubEvent _stateChangeEvent;

        public INpnsUser(ServiceCtx context) { }

        [CommandCmif(5)]
        // GetReceiveEvent() -> handle<copy>
        // [Nextendo, HOME Menu work] The overlay applet waits on it for the push notifications.
        public ResultCode GetReceiveEvent(ServiceCtx context)
        {
            _receiveEvent ??= new StubEvent(context);
            _receiveEvent.ReplyWithHandle(context);

            return ResultCode.Success;
        }

        [CommandCmif(7)]
        // GetStateChangeEvent() -> handle<copy>
        public ResultCode GetStateChangeEvent(ServiceCtx context)
        {
            _stateChangeEvent ??= new StubEvent(context);
            _stateChangeEvent.ReplyWithHandle(context);

            return ResultCode.Success;
        }

        [CommandCmif(8)]
        // ListenToByName(buffer<bytes, 0x19> name)
        public ResultCode ListenToByName(ServiceCtx context)
        {
            return ResultCode.Success;
        }
    }
}
