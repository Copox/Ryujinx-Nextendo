namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.SystemAppletProxy
{
    class IGlobalStateController : IpcService
    {
        // [Nextendo, HOME Menu work] Power off and restart, from the power menu.
        [CommandCmif(2)]
        // StartSleepSequence()
        public ResultCode StartSleepSequence(ServiceCtx context)
        {
            Ryujinx.HLE.HOS.HomeMenuStatus.RequestSleep();

            return ResultCode.Success;
        }

        [CommandCmif(3)]
        // StartShutdownSequence()
        public ResultCode StartShutdownSequence(ServiceCtx context)
        {
            Ryujinx.HLE.HOS.HomeMenuStatus.RequestPower(false);

            return ResultCode.Success;
        }

        [CommandCmif(4)]
        // StartRebootSequence()
        public ResultCode StartRebootSequence(ServiceCtx context)
        {
            Ryujinx.HLE.HOS.HomeMenuStatus.RequestPower(true);

            return ResultCode.Success;
        }

        public IGlobalStateController() { }

        [CommandCmif(30)]
        // OpenCradleFirmwareUpdater() -> object<nn::am::service::ICradleFirmwareUpdater>
        public ResultCode OpenCradleFirmwareUpdater(ServiceCtx context)
        {
            MakeObject(context, new ICradleFirmwareUpdater());

            return ResultCode.Success;
        }
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
