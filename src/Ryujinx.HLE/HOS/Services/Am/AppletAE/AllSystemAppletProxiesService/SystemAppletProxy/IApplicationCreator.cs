namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.SystemAppletProxy
{
    class IApplicationCreator : IpcService
    {
        private readonly ulong _pid;

        public IApplicationCreator(ulong pid = 0)
        {
            _pid = pid;
        }

        [CommandCmif(0)]
        // CreateApplication(nn::ncm::ApplicationId) -> object<nn::am::service::IApplicationAccessor>
        public ResultCode CreateApplication(ServiceCtx context)
        {
            ulong applicationId = context.RequestData.ReadUInt64();

            MakeObject(context, new IApplicationAccessor(context, applicationId, _pid));

            return ResultCode.Success;
        }

        [CommandCmif(10)]
        // CreateSystemApplication(nn::ncm::SystemApplicationId) -> object<nn::am::service::IApplicationAccessor>
        public ResultCode CreateSystemApplication(ServiceCtx context)
        {
            // [Nextendo, HOME Menu work] The HOME Menu starts a program of the firmware in the place
            // of a game: the initial setup (starter, 0100000000001012) on the first launch.
            ulong programId = context.RequestData.ReadUInt64();

            MakeObject(context, new IApplicationAccessor(context, programId, _pid, systemProgram: true));

            return ResultCode.Success;
        }
    }
}
