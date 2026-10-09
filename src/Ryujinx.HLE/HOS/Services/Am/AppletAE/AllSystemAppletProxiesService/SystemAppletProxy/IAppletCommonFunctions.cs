namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.SystemAppletProxy
{
    class IAppletCommonFunctions : IpcService
    {
        // [Nextendo, HOME Menu work]
        [CommandCmif(51)] // 8.0.0+
        // GetHomeButtonDoubleClickEnabled() -> bool
        public ResultCode GetHomeButtonDoubleClickEnabled(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(70)] // 11.0.0+
        // SetCpuBoostRequestPriority(s32)
        public ResultCode SetCpuBoostRequestPriority(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        public IAppletCommonFunctions() { }
    }
}
