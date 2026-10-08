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
    }
}
