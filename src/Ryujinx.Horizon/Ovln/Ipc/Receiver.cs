using Ryujinx.Horizon.Common;
using Ryujinx.Horizon.Sdk.OsTypes;
using Ryujinx.Horizon.Sdk.Ovln;
using Ryujinx.Horizon.Sdk.Sf;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Ryujinx.Horizon.Ovln.Ipc
{
    partial class Receiver : IReceiver
    {
        private SystemEventType _receiveEvent;
        private readonly LinkedList<byte[]> _messages = new();

        public Receiver()
        {
            Os.CreateSystemEvent(out _receiveEvent, EventClearMode.AutoClear, interProcess: true).AbortOnFailure();

            OvlnQueue.Add(this);
        }

        // [Nextendo, HOME Menu work] A notification for this receiver: kept, and the event signalled.
        public void Push(byte[] message, bool front)
        {
            lock (_messages)
            {
                if (front)
                {
                    _messages.AddFirst(message);
                }
                else
                {
                    _messages.AddLast(message);
                }
            }

            Os.SignalSystemEvent(ref _receiveEvent);
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
            eventHandle = Os.GetReadableHandleOfSystemEvent(ref _receiveEvent);

            return Result.Success;
        }

        [CmifCommand(3)]
        public Result Receive(out RawMessage message)
        {
            return ReceiveWithTick(out message, out _);
        }

        [CmifCommand(4)]
        public Result ReceiveWithTick(out RawMessage message, out long tick)
        {
            message = new RawMessage();
            tick = 0;

            byte[] next = null;

            lock (_messages)
            {
                if (_messages.Count > 0)
                {
                    next = _messages.First.Value;
                    _messages.RemoveFirst();
                }
            }

            if (next != null)
            {
                next.AsSpan(0, System.Math.Min(next.Length, 0x80)).CopyTo(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref message, 1)));
                tick = System.Diagnostics.Stopwatch.GetTimestamp();
            }

            return Result.Success;
        }
    }
}
