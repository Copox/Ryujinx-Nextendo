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

        [CommandCmif(350)]
        // Called by the HOME Menu at start on some machines, with a 32-bit value (15 seen in a
        // player's log), nothing given back: accepted (it used to stop the emulator).
        public ResultCode NextendoCommand350(ServiceCtx context)
        {
            uint value = context.RequestData.BaseStream.Length - context.RequestData.BaseStream.Position >= 4 ? context.RequestData.ReadUInt32() : 0;

            Ryujinx.Common.Logging.Logger.Stub?.PrintStub(Ryujinx.Common.Logging.LogClass.ServiceAm, new { value });

            return ResultCode.Success;
        }

        public IAppletCommonFunctions() { }
    }
}
