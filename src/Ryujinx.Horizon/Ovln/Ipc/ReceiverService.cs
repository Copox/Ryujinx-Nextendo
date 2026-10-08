using Ryujinx.Common.Logging;
using Ryujinx.Horizon.Common;
using Ryujinx.Horizon.Sdk.Ovln;
using Ryujinx.Horizon.Sdk.Sf;

namespace Ryujinx.Horizon.Ovln.Ipc
{
    partial class ReceiverService : IReceiverService
    {
        [CmifCommand(0)]
        public Result OpenReceiver(out IReceiver receiver)
        {
            receiver = new Receiver();

            Logger.Info?.Print(LogClass.Service, "[Nextendo] ovln:rcv OpenReceiver -> IReceiver");

            return Result.Success;
        }
    }
}
