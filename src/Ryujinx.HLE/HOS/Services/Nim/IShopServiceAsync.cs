using Ryujinx.Common.Logging;

namespace Ryujinx.HLE.HOS.Services.Nim.ShopServiceAccessServerInterface.ShopServiceAccessServer.ShopServiceAccessor
{
    class IShopServiceAsync : IpcService
    {
        public IShopServiceAsync() { }

        // There is no eShop backend. nn::ec::ShopServiceAccessor::Request calls Prepare, then Request,
        // then waits for completion and reads the response. When commands were missing,
        // IgnoreMissingServices answered them with an empty success, the SDK went on to read a response
        // that never existed, and Super Kirby Clash crashed at boot while loading its consumables catalog.
        // Failing here makes the SDK release the async object and return the error to the game, which
        // records it and keeps running, as it does when the shop cannot be reached.

        [CommandCmif(0)]
        // Cancel()
        public ResultCode Cancel(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(4)]
        // Request()
        public ResultCode Request(ServiceCtx context)
        {
            return ResultCode.ShopServiceUnavailable;
        }

        [CommandCmif(5)]
        // Prepare(request description, post data)
        public ResultCode Prepare(ServiceCtx context)
        {
            Logger.Info?.Print(LogClass.ServiceNim, "Shop service request refused: no eShop backend.");

            return ResultCode.ShopServiceUnavailable;
        }
    }
}
