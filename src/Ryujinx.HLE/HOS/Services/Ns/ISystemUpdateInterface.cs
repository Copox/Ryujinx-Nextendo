using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.Horizon.Common;
using System;

namespace Ryujinx.HLE.HOS.Services.Ns
{
    [Service("ns:su")]
    class ISystemUpdateInterface : IpcService
    {
        private readonly KEvent _contentDeliveryEvent;
        private int _contentDeliveryEventHandle;

        public ISystemUpdateInterface(ServiceCtx context)
        {
            _contentDeliveryEvent = new KEvent(context.Device.System.KernelContext);
        }

        [CommandCmif(9)] // 4.0.0+
        // GetSystemUpdateNotificationEventForContentDelivery() -> handle<copy>
        public ResultCode GetSystemUpdateNotificationEventForContentDelivery(ServiceCtx context)
        {
            // Never signalled: no system update is ever delivered here.
            if (_contentDeliveryEventHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_contentDeliveryEvent.ReadableEvent, out _contentDeliveryEventHandle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_contentDeliveryEventHandle);

            return ResultCode.Success;
        }
    }
}
