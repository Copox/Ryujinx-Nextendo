using Ryujinx.Common;
using Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.LibraryAppletCreator;

namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.LibraryAppletProxy
{
    // [Nextendo, HOME Menu work] "Winding": a library applet that starts another one in its place
    // (the user page starting the icon editor). It keeps its context, names the applet to start,
    // and ends; the system starts that applet, and when it is over starts the first one again,
    // which reads the result of the other through OpenCallingLibraryApplet.
    class IProcessWindingController : IpcService
    {
        private readonly ulong _pid;

        public IProcessWindingController(ulong pid = 0)
        {
            _pid = pid;
        }

        private RealApplet Self => RealAppletManager.TryGet(_pid, out RealApplet applet) ? applet : null;

        [CommandCmif(0)]
        // GetLaunchReason() -> nn::am::service::AppletProcessLaunchReason
        public ResultCode GetLaunchReason(ServiceCtx context)
        {
            // Flag set: the applet is started again after the one it wound to (it then opens it
            // with OpenCallingLibraryApplet).
            AppletProcessLaunchReason appletProcessLaunchReason = new()
            {
                Flag = (byte)(Self is { Calling: not null } ? 1 : 0),
            };

            context.ResponseData.WriteStruct(appletProcessLaunchReason);

            return ResultCode.Success;
        }

        [CommandCmif(11)]
        // OpenCallingLibraryApplet() -> object<nn::am::service::ILibraryAppletAccessor>
        public ResultCode OpenCallingLibraryApplet(ServiceCtx context)
        {
            RealApplet self = Self;

            if (self?.Calling is not ILibraryAppletAccessor calling)
            {
                return ResultCode.ObjectInvalid;
            }

            self.Calling = null;
            calling.ReuseFor();

            MakeObject(context, calling);

            return ResultCode.Success;
        }

        [CommandCmif(21)]
        // PushContext(object<nn::am::service::IStorage>)
        public ResultCode PushContext(ServiceCtx context)
        {
            IStorage data = GetObject<IStorage>(context, 0);

            Self?.WindContext.Push(data.Data);

            return ResultCode.Success;
        }

        [CommandCmif(22)]
        // PopContext() -> object<nn::am::service::IStorage>
        public ResultCode PopContext(ServiceCtx context)
        {
            if (Self is not { } self || !self.WindContext.TryPop(out byte[] data))
            {
                return ResultCode.NotAvailable;
            }

            MakeObject(context, new IStorage(data));

            return ResultCode.Success;
        }

        [CommandCmif(23)]
        // CancelWindingReservation()
        public ResultCode CancelWindingReservation(ServiceCtx context)
        {
            if (Self is { } self)
            {
                self.Reserved = null;
            }

            return ResultCode.Success;
        }

        [CommandCmif(30)]
        // WindAndDoReserved(): the applet ends, the reserved one starts.
        public ResultCode WindAndDoReserved(ServiceCtx context)
        {
            RealApplet self = Self;

            if (self?.Reserved == null)
            {
                return ResultCode.NotAvailable;
            }

            RealAppletManager.Wind(context.Device, self);

            return ResultCode.Success;
        }

        [CommandCmif(40)]
        // ReserveToStartAndWaitAndUnwindThis(object<nn::am::service::ILibraryAppletAccessor>)
        public ResultCode ReserveToStartAndWaitAndUnwindThis(ServiceCtx context)
        {
            return Reserve(context);
        }

        [CommandCmif(41)] // 4.0.0+
        // ReserveToStartAndWait(object<nn::am::service::ILibraryAppletAccessor>)
        public ResultCode ReserveToStartAndWait(ServiceCtx context)
        {
            return Reserve(context);
        }

        private ResultCode Reserve(ServiceCtx context)
        {
            ILibraryAppletAccessor accessor = GetObject<ILibraryAppletAccessor>(context, 0);

            if (Self is not { } self || accessor == null)
            {
                return ResultCode.ObjectInvalid;
            }

            self.Reserved = accessor;

            return ResultCode.Success;
        }
    }
}
