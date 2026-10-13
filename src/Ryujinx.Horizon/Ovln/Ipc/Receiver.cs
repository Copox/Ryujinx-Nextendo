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

            // NX_OVLN_TEST="secs:tag:hexdata;secs:tag:hexdata": test messages (study of the overlay).
            if (System.Environment.GetEnvironmentVariable("NX_OVLN_TEST") is { Length: > 0 } test)
            {
                new System.Threading.Thread(() =>
                {
                    foreach (string item in test.Split(';'))
                    {
                        string[] f = item.Split(':');
                        System.Threading.Thread.Sleep((int)(double.Parse(f[0], System.Globalization.CultureInfo.InvariantCulture) * 1000));
                        OvlnNotifications.Post(System.Convert.ToUInt32(f[1], 16), System.Convert.FromHexString(f.Length > 2 ? f[2] : ""));
                    }
                }) { IsBackground = true }.Start();
            }
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
            // The overlay applet keeps asking once it got a message: the server is never idle
            // again, so what the emulator has to send goes out here too.
            OvlnQueue.SendPending();

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
