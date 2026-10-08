namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.SystemAppletProxy
{
    class IGlobalStateController : IpcService
    {
        public IGlobalStateController() { }
        [CommandCmif(14)]
        // ShouldSleepOnBoot() -> bool
        public ResultCode ShouldSleepOnBoot(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        private Ryujinx.HLE.HOS.Services.StubEvent _hdcpAuthenticationFailedEvent;

        [CommandCmif(15)]
        // GetHdcpAuthenticationFailedEvent() -> handle<copy>
        public ResultCode GetHdcpAuthenticationFailedEvent(ServiceCtx context)
        {
            _hdcpAuthenticationFailedEvent ??= new Ryujinx.HLE.HOS.Services.StubEvent(context);
            _hdcpAuthenticationFailedEvent.ReplyWithHandle(context);

            return ResultCode.Success;
        }
    }
}
