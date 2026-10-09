using Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService;
using Ryujinx.HLE.HOS.Services.Am.AppletOE.ApplicationProxyService;

namespace Ryujinx.HLE.HOS.Services.Am.AppletAE
{
    [Service("appletAE")]
    class IAllSystemAppletProxiesService : IpcService
    {
        public IAllSystemAppletProxiesService(ServiceCtx context) { }

        [CommandCmif(100)]
        [CommandCmif(110)] // [Nextendo] 19.0.0+ refactor: qlaunch (Home Menu) opens its system applet proxy via 110
        // OpenSystemAppletProxy(u64, pid, handle<copy>) -> object<nn::am::service::ISystemAppletProxy>
        public ResultCode OpenSystemAppletProxy(ServiceCtx context)
        {
            RealAppletManager.RegisterSystemApplet(context.Device.System, context.Request.HandleDesc.PId);

            MakeObject(context, new ISystemAppletProxy(context.Request.HandleDesc.PId));

            return ResultCode.Success;
        }

        [CommandCmif(200)]
        [CommandCmif(201)] // 3.0.0+
        // OpenLibraryAppletProxy(u64, pid, handle<copy>) -> object<nn::am::service::ILibraryAppletProxy>
        public ResultCode OpenLibraryAppletProxy(ServiceCtx context)
        {
            MakeObject(context, new ILibraryAppletProxy(context.Request.HandleDesc.PId));

            return ResultCode.Success;
        }

        [CommandCmif(300)]
        // OpenOverlayAppletProxy(u64, pid, handle<copy>) -> object<nn::am::service::IOverlayAppletProxy>
        public ResultCode OpenOverlayAppletProxy(ServiceCtx context)
        {
            // [Nextendo, HOME Menu work] The overlay applet (overlayDisp): a system applet of its own.
            RealAppletManager.RegisterOverlayApplet(context.Device.System, context.Request.HandleDesc.PId);

            MakeObject(context, new IOverlayAppletProxy(context.Request.HandleDesc.PId));

            return ResultCode.Success;
        }

        [CommandCmif(450)] // 19.0.0+
        // GetSystemProcessCommonFunctions() -> object<nn::am::service::ISystemProcessCommonFunctions>
        public ResultCode GetSystemProcessCommonFunctions(ServiceCtx context)
        {
            // qlaunch asks for this object right after opening its proxy and aborts without
            // one; its commands are answered as they turn up.
            MakeObject(context, new ISystemProcessCommonFunctions());

            return ResultCode.Success;
        }

        [CommandCmif(460)] // 20.0.0+
        // () -> object<nn::am::service::IAppletAlternativeFunctions>
        public ResultCode GetAppletAlternativeFunctions(ServiceCtx context)
        {
            MakeObject(context, new IAppletAlternativeFunctions());

            return ResultCode.Success;
        }

        [CommandCmif(350)]
        // OpenSystemApplicationProxy(u64, pid, handle<copy>) -> object<nn::am::service::IApplicationProxy>
        public ResultCode OpenSystemApplicationProxy(ServiceCtx context)
        {
            MakeObject(context, new IApplicationProxy(context.Request.HandleDesc.PId));

            return ResultCode.Success;
        }
    }
}
