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
            // [Nextendo, HOME Menu work] To the receivers (the overlay applet draws it).
            OvlnQueue.Send(System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref message, 1)), option.EnqueuePosition == 1);

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
