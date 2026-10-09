using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.LibraryAppletCreator;
using Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.LibraryAppletProxy;
using Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.SystemAppletProxy;

namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService
{
    // [Nextendo, HOME Menu work] The proxy of the overlay applet (overlayDisp), which draws the
    // notifications of the system above everything else.
    class IOverlayAppletProxy : IpcService
    {
        private readonly ulong _pid;

        public IOverlayAppletProxy(ulong pid)
        {
            _pid = pid;
        }

        [CommandCmif(0)]
        public ResultCode GetCommonStateGetter(ServiceCtx context)
        {
            MakeObject(context, new ICommonStateGetter(context, _pid));

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        public ResultCode GetSelfController(ServiceCtx context)
        {
            MakeObject(context, new ISelfController(context, _pid));

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        public ResultCode GetWindowController(ServiceCtx context)
        {
            MakeObject(context, new IWindowController(_pid));

            return ResultCode.Success;
        }

        [CommandCmif(3)]
        public ResultCode GetAudioController(ServiceCtx context)
        {
            MakeObject(context, new IAudioController());

            return ResultCode.Success;
        }

        [CommandCmif(4)]
        public ResultCode GetDisplayController(ServiceCtx context)
        {
            MakeObject(context, new IDisplayController(context, _pid));

            return ResultCode.Success;
        }

        [CommandCmif(10)]
        public ResultCode GetProcessWindingController(ServiceCtx context)
        {
            MakeObject(context, new IProcessWindingController(_pid));

            return ResultCode.Success;
        }

        [CommandCmif(11)]
        public ResultCode GetLibraryAppletCreator(ServiceCtx context)
        {
            MakeObject(context, new ILibraryAppletCreator(_pid));

            return ResultCode.Success;
        }

        [CommandCmif(20)]
        public ResultCode GetOverlayFunctions(ServiceCtx context)
        {
            MakeObject(context, new IOverlayFunctions());

            return ResultCode.Success;
        }

        [CommandCmif(21)] // 7.0.0+
        public ResultCode GetAppletCommonFunctions(ServiceCtx context)
        {
            MakeObject(context, new IAppletCommonFunctions());

            return ResultCode.Success;
        }

        [CommandCmif(23)] // 15.0.0+
        public ResultCode GetGlobalStateController(ServiceCtx context)
        {
            MakeObject(context, new IGlobalStateController());

            return ResultCode.Success;
        }

        [CommandCmif(1000)]
        public ResultCode GetDebugFunctions(ServiceCtx context)
        {
            MakeObject(context, new IDebugFunctions());

            return ResultCode.Success;
        }
    }

    // What the overlay applet asks of the system: answered as they turn up.
    class IOverlayFunctions : IpcService
    {
        public IOverlayFunctions() { }

        [CommandCmif(0)]
        public ResultCode BeginToWatchShortHomeButtonMessage(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(1)]
        public ResultCode EndToWatchShortHomeButtonMessage(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(2)]
        // GetApplicationIdForLogo() -> nn::ncm::ApplicationId
        public ResultCode GetApplicationIdForLogo(ServiceCtx context)
        {
            context.ResponseData.Write(context.Device.Processes.ActiveApplication?.ProgramId ?? 0UL);

            return ResultCode.Success;
        }

        [CommandCmif(3)]
        public ResultCode SetGpuTimeSliceBoost(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(4)]
        public ResultCode SetAutoSleepTimeAndDimmingTimeEnabled(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(6)]
        public ResultCode SetScreenShotPermissionGlobally(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(10)]
        public ResultCode StartShutdownSequenceForOverlay(ServiceCtx context)
        {
            HomeMenuStatus.RequestPower(false);

            return ResultCode.Success;
        }

        [CommandCmif(11)]
        public ResultCode StartRebootSequenceForOverlay(ServiceCtx context)
        {
            HomeMenuStatus.RequestPower(true);

            return ResultCode.Success;
        }

        [CommandCmif(20)]
        public ResultCode SetHandlingHomeButtonShortPressedEnabled(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(21)]
        public ResultCode SetHandlingTouchScreenInputEnabled(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(30)]
        public ResultCode SetHealthWarningShowingState(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(31)]
        // IsHealthWarningRequired() -> bool
        public ResultCode IsHealthWarningRequired(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(50)]
        public ResultCode SetGpuTimeSliceBoostForApplication(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(90)]
        public ResultCode SetRequiresGpuResourceUse(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(101)]
        public ResultCode BeginToObserveHidInputForDevelop(ServiceCtx context) => ResultCode.Success;
    }
}
