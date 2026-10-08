namespace Ryujinx.HLE.HOS.Services.Ldn.Lp2p
{
    [Service("lp2p:m")] // 9.0.0+
    class IMonitorServiceCreator : IpcService
    {
        public IMonitorServiceCreator(ServiceCtx context) { }

        [CommandCmif(0)]
        // CreateMonitorService(pid, u64) -> object<nn::lp2p::monitor::detail::IMonitorService>
        public ResultCode CreateMonitorService(ServiceCtx context)
        {
            MakeObject(context, new ISfServiceMonitor(context));

            return ResultCode.Success;
        }
    }
}
