namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.SystemAppletProxy
{
    // [Nextendo, HOME Menu work] The dock firmware updater the HOME Menu opens when the console
    // is docked: the emulated dock is always up to date.
    class ICradleFirmwareUpdater : IpcService
    {
        private StubEvent _deviceInfoChangeEvent;

        [CommandCmif(0)]
        // StartUpdate()
        public ResultCode StartUpdate(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(1)]
        // FinishUpdate()
        public ResultCode FinishUpdate(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(2)]
        // GetCradleDeviceInfo() -> nn::am::service::CradleDeviceInfo
        public ResultCode GetCradleDeviceInfo(ServiceCtx context)
        {
            context.ResponseData.Write(new byte[0x10]);

            return ResultCode.Success;
        }

        [CommandCmif(3)]
        // GetCradleDeviceInfoChangeEvent() -> handle<copy>
        public ResultCode GetCradleDeviceInfoChangeEvent(ServiceCtx context)
        {
            _deviceInfoChangeEvent ??= new StubEvent(context);
            _deviceInfoChangeEvent.ReplyWithHandle(context);

            return ResultCode.Success;
        }

        [CommandCmif(4)]
        // GetUpdateProgressInfo() -> nn::am::service::CradleUpdateProgressInfo
        public ResultCode GetUpdateProgressInfo(ServiceCtx context)
        {
            context.ResponseData.Write(new byte[0x8]);

            return ResultCode.Success;
        }

        [CommandCmif(5)]
        // GetLastInternalResult()
        public ResultCode GetLastInternalResult(ServiceCtx context) => ResultCode.Success;
    }
}
