using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Memory;
using Ryujinx.Horizon.Common;
using System;

namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.SystemAppletProxy
{
    class IDisplayController : IpcService
    {
        // [Nextendo, HOME Menu work] No capture of the screen is kept.
        [CommandCmif(5)]
        // GetLastForegroundCaptureImageEx(buffer<bytes, 6>) -> bool
        public ResultCode GetLastForegroundCaptureImageEx(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(7)]
        // GetCallerAppletCaptureImageEx(buffer<bytes, 6>) -> bool
        public ResultCode GetCallerAppletCaptureImageEx(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(20)] // 3.0.0+
        // ClearCaptureBuffer(bool, s32 buffer, u32 color)
        public ResultCode ClearCaptureBuffer(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        private readonly KTransferMemory _transferMem;
        private bool _lastApplicationCaptureBufferAcquired;
        private bool _callerAppletCaptureBufferAcquired;

        private readonly ulong _pid;

        public IDisplayController(ServiceCtx context, ulong pid = 0)
        {
            _pid = pid;
            _transferMem = context.Device.System.AppletCaptureBufferTransfer;
        }

        [CommandCmif(8)] // 2.0.0+
        // TakeScreenShotOfOwnLayer(b8, s32)
        public ResultCode TakeScreenShotOfOwnLayer(ServiceCtx context)
        {
            // (bool, s32) with the s32 aligned: 0 last application, 1 last foreground, 2 caller applet.
            int flags = context.RequestData.ReadInt32();
            int captureIndex = context.RequestData.ReadInt32();

            _ = flags;

            Ryujinx.HLE.HOS.Services.Vi.SystemSharedBuffer.Capture(_pid, captureIndex);

            return ResultCode.Success;
        }

        [CommandCmif(11)]
        // ReleaseLastApplicationCaptureBuffer()
        public ResultCode ReleaseLastApplicationCaptureBuffer(ServiceCtx context)
        {
            if (!_lastApplicationCaptureBufferAcquired)
            {
                return ResultCode.BufferNotAcquired;
            }

            _lastApplicationCaptureBufferAcquired = false;

            return ResultCode.Success;
        }

        [CommandCmif(15)]
        // ReleaseCallerAppletCaptureBuffer()
        public ResultCode ReleaseCallerAppletCaptureBuffer(ServiceCtx context)
        {
            if (!_callerAppletCaptureBufferAcquired)
            {
                return ResultCode.BufferNotAcquired;
            }

            _callerAppletCaptureBufferAcquired = false;

            return ResultCode.Success;
        }

        [CommandCmif(16)]
        // AcquireLastApplicationCaptureBufferEx() -> (b8, handle<copy>)
        public ResultCode AcquireLastApplicationCaptureBufferEx(ServiceCtx context)
        {
            if (_lastApplicationCaptureBufferAcquired)
            {
                return ResultCode.BufferAlreadyAcquired;
            }

            if (context.Process.HandleTable.GenerateHandle(_transferMem, out int handle) != Result.Success)
            {
                throw new InvalidOperationException("Out of handles!");
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(handle);

            _lastApplicationCaptureBufferAcquired = true;

            context.ResponseData.Write(_lastApplicationCaptureBufferAcquired);

            return ResultCode.Success;
        }

        [CommandCmif(18)]
        // AcquireCallerAppletCaptureBufferEx() -> (b8, handle<copy>)
        public ResultCode AcquireCallerAppletCaptureBufferEx(ServiceCtx context)
        {
            if (_callerAppletCaptureBufferAcquired)
            {
                return ResultCode.BufferAlreadyAcquired;
            }

            if (context.Process.HandleTable.GenerateHandle(_transferMem, out int handle) != Result.Success)
            {
                throw new InvalidOperationException("Out of handles!");
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(handle);

            _callerAppletCaptureBufferAcquired = true;

            context.ResponseData.Write(_callerAppletCaptureBufferAcquired);

            return ResultCode.Success;
        }
        // [Nextendo, HOME Menu work] The captures as slots of the shared frame buffer pool: what
        // an applet draws, blurred, behind its own window.
        private ResultCode AcquireCaptureSharedBuffer(ServiceCtx context, int captureIndex)
        {
            int slot = Ryujinx.HLE.HOS.Services.Vi.SystemSharedBuffer.PlaceCapture(context, _pid, captureIndex);

            context.ResponseData.Write(slot >= 0 ? 1 : 0); // the capture may be shown
            context.ResponseData.Write(slot >= 0 ? slot : 0);

            return ResultCode.Success;
        }

        [CommandCmif(22)] // 4.0.0+
        // AcquireLastApplicationCaptureSharedBuffer() -> (b8, s32)
        public ResultCode AcquireLastApplicationCaptureSharedBuffer(ServiceCtx context) => AcquireCaptureSharedBuffer(context, 0);

        [CommandCmif(23)] // 4.0.0+
        // ReleaseLastApplicationCaptureSharedBuffer()
        public ResultCode ReleaseLastApplicationCaptureSharedBuffer(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(24)] // 4.0.0+
        // AcquireLastForegroundCaptureSharedBuffer() -> (b8, s32)
        public ResultCode AcquireLastForegroundCaptureSharedBuffer(ServiceCtx context) => AcquireCaptureSharedBuffer(context, 1);

        [CommandCmif(25)] // 4.0.0+
        // ReleaseLastForegroundCaptureSharedBuffer()
        public ResultCode ReleaseLastForegroundCaptureSharedBuffer(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(26)] // 4.0.0+
        // AcquireCallerAppletCaptureSharedBuffer() -> (b8, s32)
        public ResultCode AcquireCallerAppletCaptureSharedBuffer(ServiceCtx context) => AcquireCaptureSharedBuffer(context, 2);

        [CommandCmif(27)] // 4.0.0+
        // ReleaseCallerAppletCaptureSharedBuffer()
        public ResultCode ReleaseCallerAppletCaptureSharedBuffer(ServiceCtx context) => ResultCode.Success;
    }
}
