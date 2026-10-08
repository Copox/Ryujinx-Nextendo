namespace Ryujinx.HLE.HOS.Services.Ldn
{
    // nn::ldn::detail::IMonitorService: read-only view of the local wireless state, used by the
    // HOME Menu. No local network is ever active here, so the state is always None.
    class IMonitorService : IpcService
    {
        public IMonitorService() { }

        [CommandCmif(0)]
        // GetStateForMonitor() -> u32
        public ResultCode GetStateForMonitor(ServiceCtx context)
        {
            context.ResponseData.Write(0u); // State_None

            return ResultCode.Success;
        }

        [CommandCmif(100)]
        // InitializeMonitor()
        public ResultCode InitializeMonitor(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(101)]
        // FinalizeMonitor()
        public ResultCode FinalizeMonitor(ServiceCtx context)
        {
            return ResultCode.Success;
        }
    }
}
