using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.Horizon.Common;
using System;

namespace Ryujinx.HLE.HOS.Services.Audio
{
    // nn::audioctrl::detail::IAudioController: system volume and output routing, used by the
    // HOME Menu. Its commands are answered as they turn up.
    [Service("audctl")]
    class IAudioControlService : IpcService
    {
        public IAudioControlService(ServiceCtx context) { }

        [CommandCmif(5000)] // 19.0.0+
        // (u64) -> object
        // Undocumented. qlaunch 22.5.0 passes 8 bytes and keeps the returned interface as its
        // audio controller for the rest of the session (seen in its code: the out parameter is
        // a reference-counted object, released through its vtable).
        public ResultCode OpenAudioControllerSession(ServiceCtx context)
        {
            MakeObject(context, new IAudioControllerSession(context));

            return ResultCode.Success;
        }
    }

    class IAudioControllerSession : IpcService
    {
        private readonly KEvent _targetNotification;
        private int _targetNotificationHandle;

        public IAudioControllerSession(ServiceCtx context)
        {
            _targetNotification = new KEvent(context.Device.System.KernelContext);
        }

        [CommandCmif(34)]
        // AcquireTargetNotification() -> handle<copy>
        // The overlay applet waits on it (with its other events) to draw the volume bar: never
        // signalled, the volume being the emulator's.
        public ResultCode AcquireTargetNotification(ServiceCtx context)
        {
            if (_targetNotificationHandle == 0 &&
                context.Process.HandleTable.GenerateHandle(_targetNotification.ReadableEvent, out _targetNotificationHandle) != Result.Success)
            {
                throw new InvalidOperationException("Out of handles!");
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_targetNotificationHandle);

            return ResultCode.Success;
        }
        [CommandCmif(13)]
        // GetOutputModeSetting(u32 target) -> u32
        public ResultCode GetOutputModeSetting(ServiceCtx context)
        {
            context.ResponseData.Write(0u);

            return ResultCode.Success;
        }

        [CommandCmif(18)]
        // GetHeadphoneOutputLevelMode() -> u32
        public ResultCode GetHeadphoneOutputLevelMode(ServiceCtx context)
        {
            context.ResponseData.Write(0u);

            return ResultCode.Success;
        }

        [CommandCmif(31)]
        // IsSpeakerAutoMuteEnabled() -> bool
        public ResultCode IsSpeakerAutoMuteEnabled(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }
    }
}
