using Ryujinx.Horizon.Common;
using Ryujinx.Horizon.Sdk.OsTypes;
using Ryujinx.Horizon.Sdk.Ovln;
using Ryujinx.Horizon.Sdk.Sf;

namespace Ryujinx.Horizon.Ovln.Ipc
{
    partial class Receiver : IReceiver
    {
        private SystemEventType _receiveEvent;

        public Receiver()
        {
            Os.CreateSystemEvent(out _receiveEvent, EventClearMode.AutoClear, interProcess: true).AbortOnFailure();
        }

        [CmifCommand(0)]
        public Result AddSource(SourceName sourceName)
        {
            return Result.Success;
        }

        [CmifCommand(1)]
        public Result RemoveSource(SourceName sourceName)
        {
            return Result.Success;
        }

        [CmifCommand(2)]
        public Result GetReceiveEventHandle([CopyHandle] out int eventHandle)
        {
            // Jamais signale : aucune notification n'arrive, qlaunch continue
            // au lieu d'attendre un evenement qui ne viendra pas.
            eventHandle = Os.GetReadableHandleOfSystemEvent(ref _receiveEvent);
            Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.Service, "[Nextendo] ovln IReceiver.GetReceiveEventHandle");
            return Result.Success;
        }

        [CmifCommand(3)]
        public Result Receive(out RawMessage message)
        {
            message = new RawMessage();
            Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.Service, "[Nextendo] ovln IReceiver.Receive -> vide");
            return Result.Success;
        }

        [CmifCommand(4)]
        public Result ReceiveWithTick(out RawMessage message, out long tick)
        {
            message = new RawMessage();
            tick = 0;
            Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.Service, "[Nextendo] ovln IReceiver.ReceiveWithTick -> vide");
            return Result.Success;
        }
    }
}
