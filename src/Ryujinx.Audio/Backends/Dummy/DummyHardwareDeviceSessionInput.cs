using Ryujinx.Audio.Backends.Common;
using Ryujinx.Audio.Common;
using Ryujinx.Audio.Integration;
using Ryujinx.Memory;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Ryujinx.Audio.Backends.Dummy
{
    class DummyHardwareDeviceSessionInput : IHardwareDeviceSession
    {
        private float _volume;
        private readonly IHardwareDeviceDriver _manager;
        private readonly IVirtualMemoryManager _memoryManager;

        // [Nextendo] A silent microphone still fills its buffers in real time, like the real one.
        // Handing each buffer back the moment it was queued let a game that reads the microphone
        // in a loop (Portal 2's voice chat, opened in the co-op lobby) spin on AudioIn at ~46 000
        // calls a second; its audio thread then kept the lock the main thread waits on, and the
        // lobby fell to under 1 frame a second.
        private readonly double _bytesPerSecond;
        private readonly Queue<(ulong Tag, long EndTicks)> _pending = new();
        private readonly Lock _lock = new();
        private readonly Timer _timer;
        private long _lastEndTicks;

        public DummyHardwareDeviceSessionInput(IHardwareDeviceDriver manager, IVirtualMemoryManager memoryManager, SampleFormat sampleFormat, uint sampleRate, uint channelCount)
        {
            _volume = 1.0f;
            _manager = manager;
            _memoryManager = memoryManager;

            int sampleSize = sampleFormat == SampleFormat.Invalid ? sizeof(short) : BackendHelper.GetSampleSize(sampleFormat);

            _bytesPerSecond = (double)sampleSize * Math.Max(1u, channelCount) * (sampleRate == 0 ? Constants.TargetSampleRate : sampleRate);
            _timer = new Timer(_ => _manager.GetUpdateRequiredEvent().Set());
        }

        public void Dispose()
        {
            _timer.Dispose();
        }

        public ulong GetPlayedSampleCount()
        {
            // Not implemented for input.
            throw new NotSupportedException();
        }

        public float GetVolume()
        {
            return _volume;
        }

        public void PrepareToClose() { }

        public void QueueBuffer(AudioBuffer buffer)
        {
            _memoryManager.Fill(buffer.DataPointer, buffer.DataSize, 0);

            lock (_lock)
            {
                long now = Stopwatch.GetTimestamp();
                long duration = (long)(buffer.DataSize / _bytesPerSecond * Stopwatch.Frequency);
                long end = Math.Max(now, _lastEndTicks) + duration;

                _lastEndTicks = end;
                _pending.Enqueue((buffer.BufferTag, end));

                ArmTimer(now);
            }
        }

        public bool RegisterBuffer(AudioBuffer buffer)
        {
            return buffer.DataPointer != 0;
        }

        public void SetVolume(float volume)
        {
            _volume = volume;
        }

        public void Start() { }

        public void Stop() { }

        public void UnregisterBuffer(AudioBuffer buffer) { }

        public bool WasBufferFullyConsumed(AudioBuffer buffer)
        {
            lock (_lock)
            {
                long now = Stopwatch.GetTimestamp();

                // Buffers are handed back in order: drop the ones whose time has come.
                while (_pending.TryPeek(out (ulong Tag, long EndTicks) head) && head.EndTicks <= now)
                {
                    _pending.Dequeue();

                    if (head.Tag == buffer.BufferTag)
                    {
                        ArmTimer(now);

                        return true;
                    }
                }

                foreach ((ulong tag, long _) in _pending)
                {
                    if (tag == buffer.BufferTag)
                    {
                        ArmTimer(now);

                        return false;
                    }
                }

                // Not queued here (or already dropped above): nothing left to wait for.
                return true;
            }
        }

        // Caller holds _lock. Wakes the audio manager when the next buffer is due.
        private void ArmTimer(long now)
        {
            if (_pending.TryPeek(out (ulong Tag, long EndTicks) head))
            {
                long dueMs = Math.Max(0, (head.EndTicks - now) * 1000 / Stopwatch.Frequency);

                _timer.Change(dueMs, Timeout.Infinite);
            }
        }
    }
}
