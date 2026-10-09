using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.Horizon.Common;
using System;

namespace Ryujinx.HLE.HOS.Services.Nifm.StaticService
{
    // [Nextendo, HOME Menu work] A wireless scan, asked by the Internet settings. The emulator
    // has no radio of its own: the scan is over at once, and the network the computer is on is
    // the one listed.
    class IScanRequest : IpcService
    {
        private readonly KEvent _event;
        private int _eventHandle;

        public IScanRequest(ServiceCtx context)
        {
            _event = new KEvent(context.Device.System.KernelContext);
        }

        [CommandCmif(0)]
        // Submit()
        public ResultCode Submit(ServiceCtx context)
        {
            _event.ReadableEvent.Signal();

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        // IsProcessing() -> bool
        public ResultCode IsProcessing(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // GetResult()
        public ResultCode GetResult(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(3)]
        // GetSystemEventReadableHandle() -> handle<copy>
        public ResultCode GetSystemEventReadableHandle(ServiceCtx context)
        {
            if (_eventHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_event.ReadableEvent, out _eventHandle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_eventHandle);

            return ResultCode.Success;
        }

        [CommandCmif(4)] // 6.0.0+
        // SetChannels(buffer<s16>)
        public ResultCode SetChannels(ServiceCtx context)
        {
            return ResultCode.Success;
        }
    }
}
