using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.HLE.HOS.Services.Nim.ShopServiceAccessServerInterface.ShopServiceAccessServer.ShopServiceAccessor;
using Ryujinx.Horizon.Common;
using System;

namespace Ryujinx.HLE.HOS.Services.Nim.ShopServiceAccessServerInterface.ShopServiceAccessServer
{
    class IShopServiceAccessor : IpcService
    {
        private readonly KEvent _event;

        private int _eventHandle;

        public IShopServiceAccessor(Horizon system)
        {
            _event = new KEvent(system.KernelContext);
        }

        [CommandCmif(0)]
        // CreateAsyncInterface(u64) -> (handle<copy>, object<nn::ec::IShopServiceAsync>)
        public ResultCode CreateAsyncInterface(ServiceCtx context)
        {
            if (_eventHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_event.ReadableEvent, out _eventHandle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            // Set the event first: MakeObject keeps copy handles already in the response and adds the
            // session as a move handle. Assigning the copy handle afterwards dropped the session, so the
            // guest received a null IShopServiceAsync and crashed on its first call (Super Kirby Clash).
            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_eventHandle);

            MakeObject(context, new IShopServiceAsync());

            Logger.Stub?.PrintStub(LogClass.ServiceNim);

            return ResultCode.Success;
        }
    }
}
