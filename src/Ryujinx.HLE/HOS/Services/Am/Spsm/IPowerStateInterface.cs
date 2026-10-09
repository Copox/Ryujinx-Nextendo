namespace Ryujinx.HLE.HOS.Services.Am.Spsm
{
    [Service("spsm")]
    class IPowerStateInterface : IpcService
    {
        public IPowerStateInterface(ServiceCtx context) { }

        [CommandCmif(3)]
        // Shutdown(b8 reboot)
        public ResultCode Shutdown(ServiceCtx context)
        {
            HomeMenuStatus.RequestPower(reboot: context.RequestData.ReadBoolean());

            return ResultCode.Success;
        }
    }
}
