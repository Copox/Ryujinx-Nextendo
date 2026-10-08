using Ryujinx.Horizon.Common;
using Ryujinx.Horizon.Sdk.Sf;

namespace Ryujinx.Horizon.Sdk.Ovln
{
    interface IReceiver : IServiceObject
    {
        Result AddSource(SourceName sourceName);
        Result RemoveSource(SourceName sourceName);
        Result GetReceiveEventHandle([CopyHandle] out int eventHandle);
        Result Receive(out RawMessage message);
        Result ReceiveWithTick(out RawMessage message, out long tick);
    }
}
