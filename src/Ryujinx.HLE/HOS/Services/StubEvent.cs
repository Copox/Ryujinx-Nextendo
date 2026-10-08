using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.Horizon.Common;
using System;

namespace Ryujinx.HLE.HOS.Services
{
    // An event a guest may wait on but that the emulator never signals (no system update, no
    // game card swap, no parental control change...). The caller gets a real handle: a null one
    // makes its wait loop spin on InvalidHandle.
    class StubEvent
    {
        private readonly KEvent _event;
        private int _handle;
        private int _writableHandle;

        public StubEvent(ServiceCtx context)
        {
            _event = new KEvent(context.Device.System.KernelContext);
        }

        public void Signal() => _event.ReadableEvent.Signal();

        public ResultCode ReplyWithWritableHandle(ServiceCtx context)
        {
            if (_writableHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_event.WritableEvent, out _writableHandle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_writableHandle);

            return ResultCode.Success;
        }

        public ResultCode ReplyWithHandle(ServiceCtx context)
        {
            if (_handle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_event.ReadableEvent, out _handle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_handle);

            return ResultCode.Success;
        }
    }
}
