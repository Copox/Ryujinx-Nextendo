using Ryujinx.Horizon.Common;
using Ryujinx.Horizon.Sdk.Sf;

namespace Ryujinx.Horizon.Sdk.Ovln
{
    interface ISender : IServiceObject
    {
        Result Send(RawMessage message, SendOption option);
        Result GetUnreceivedMessageCount(out uint count);
    }
}
