using Ryujinx.Common.Logging;
using Ryujinx.Horizon.Common;
using Ryujinx.Horizon.Sdk.Ovln;
using Ryujinx.Horizon.Sdk.Sf;

namespace Ryujinx.Horizon.Ovln.Ipc
{
    partial class SenderService : ISenderService
    {
        [CmifCommand(0)]
        public Result OpenSender(out ISender sender)
        {
            sender = new Sender();

            Logger.Info?.Print(LogClass.Service, "[Nextendo] ovln:snd OpenSender -> ISender (notifications ignorees)");

            return Result.Success;
        }
    }
}
