using Ryujinx.Horizon.Common;
using Ryujinx.Horizon.Sdk.Ovln;
using Ryujinx.Horizon.Sdk.Sf;

namespace Ryujinx.Horizon.Ovln.Ipc
{
    partial class Sender : ISender
    {
        [CmifCommand(0)]
        public Result Send(RawMessage message, SendOption option)
        {
            // Notifications d'applets en surimpression : rien a afficher dans
            // l'emulateur, on accepte et on jette.
            Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.Service, "[Nextendo] ovln ISender.Send (ignore)");
            return Result.Success;
        }

        [CmifCommand(1)]
        public Result GetUnreceivedMessageCount(out uint count)
        {
            count = 0;

            return Result.Success;
        }
    }
}
