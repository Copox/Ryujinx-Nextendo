namespace Ryujinx.HLE.HOS.Services.Npns
{
    [Service("npns:a")]
    [Service("npns:s")]
    class INpnsSystem : IpcService
    {
        public INpnsSystem(ServiceCtx context) { }
    }
}
