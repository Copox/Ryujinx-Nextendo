using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Memory;
using Ryujinx.HLE.HOS.Kernel.Process;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.HLE.HOS.Services.Nv.NvDrvServices.NvMap;
using Ryujinx.HLE.HOS.Services.SurfaceFlinger;
using Ryujinx.HLE.HOS.Services.SurfaceFlinger.Types;
using Ryujinx.Horizon.Common;
using System.Collections.Generic;

namespace Ryujinx.HLE.HOS.Services.Vi
{
    // nn::vi::fbshare: the frame buffers a system applet (the HOME Menu, a library applet)
    // draws into belong to the system. It hands the applet an nvmap handle on a pool of
    // 1280x720 buffers, a layout of that pool and a layer; the applet acquires a buffer,
    // renders, presents it.
    //
    // One session per applet process. The pool is mapped in the applet's own address space
    // (outside its heap, which it is free to resize), the layer is an ordinary layer whose
    // buffer queue is driven from here instead of through the binder. The applet that opened
    // its session last is the one on screen; when it goes, the one before it comes back.
    class SystemSharedBuffer
    {
        public const int Width = 1280;
        public const int Height = 720;
        private const int BlockLinearHeight = 768;
        private const int BytesPerPixel = 4;
        private const int SlotsCount = 7;
        private const int QueueBuffersCount = 4;

        public const uint SlotSize = Width * BlockLinearHeight * BytesPerPixel; // 0x3C0000
        public const uint PoolSize = SlotSize * SlotsCount;                    // 0x1A40000
        public const int LayoutSize = 0x188;

        private const ulong BufferHandleBase = 0x100000000;

        private static readonly object _lock = new();
        private static readonly Dictionary<ulong, SystemSharedBuffer> _sessions = new();

        private bool _connected;
        private long _previousRenderLayer;
        private ulong _poolAddress;
        private KProcess _process;
        private int _lastPresentedSlot = -1;

        // The three captures applets hand each other (last application, last foreground,
        // caller applet): the picture an applet shows blurred behind its own window. They sit
        // in the pool after the four frame buffers.
        private const int FirstCaptureSlot = 4;
        private static readonly byte[][] _captures = new byte[3][];

        /// <summary>
        /// A new emulation starts: nothing of the previous one holds any more (its processes are
        /// gone, and the new ones get the same process ids).
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _sessions.Clear();
                System.Array.Clear(_captures);
            }
        }

        // Keeps the last frame of `pid` as capture number `index`.
        public static void Capture(ulong pid, int index)
        {
            SystemSharedBuffer session = Of(pid);

            if (session == null || session._lastPresentedSlot < 0 || (uint)index >= _captures.Length)
            {
                return;
            }

            try
            {
                ulong address = session._poolAddress + (ulong)session._lastPresentedSlot * SlotSize;

                // A tracked read: the GPU writes the frame back to guest memory first.
                _captures[index] = session._process.CpuMemory.GetSpan(address, (int)SlotSize, true).ToArray();

                // A capture is a picture of the screen: opaque. The menu's own frame keeps
                // whatever alpha its last draw left (labels, some icons), which an applet
                // blending the capture would show as boxes.
                byte[] capture = _captures[index];

                for (int i = 3; i < capture.Length; i += 4)
                {
                    capture[i] = 0xFF;
                }
            }
            catch (System.Exception e)
            {
                Logger.Warning?.Print(LogClass.ServiceVi, $"Shared frame buffers: capture {index} of process {pid} failed ({e.Message}).");
            }
        }

        // Puts capture number `index` in the pool of `pid`; returns its slot there.
        public static int PlaceCapture(ServiceCtx context, ulong pid, int index)
        {
            SystemSharedBuffer session = EnsureSession(context, pid);

            if (session == null || (uint)index >= _captures.Length)
            {
                return -1;
            }

            int slot = FirstCaptureSlot + index;
            // A capture nobody took yet: the most recent one stands in for it.
            byte[] pixels = _captures[index] ?? _captures[2] ?? _captures[1] ?? _captures[0] ?? new byte[SlotSize];

            session._process.CpuMemory.Write(session._poolAddress + (ulong)slot * SlotSize, pixels);

            return slot;
        }

        public ulong Pid { get; private set; }
        public long LayerId { get; private set; }
        public int NvMapHandle { get; private set; }
        public ulong BufferHandle => BufferHandleBase | Pid;

        // Finds a session from either of the two handles an applet is given.
        public static SystemSharedBuffer Find(ulong handle)
        {
            lock (_lock)
            {
                foreach (SystemSharedBuffer session in _sessions.Values)
                {
                    if (session.BufferHandle == handle || (ulong)session.LayerId == handle)
                    {
                        return session;
                    }
                }

                return null;
            }
        }

        // `pid` is the applet's own process: a service's ServiceCtx.Process is the service, and
        // the pool has to be mapped, and the layer owned, on the applet's side.
        public static SystemSharedBuffer EnsureSession(ServiceCtx context, ulong pid)
        {
            lock (_lock)
            {
                if (_sessions.TryGetValue(pid, out SystemSharedBuffer session))
                {
                    return session;
                }

                if (!context.Device.System.KernelContext.Processes.TryGetValue(pid, out KProcess applet))
                {
                    Logger.Error?.Print(LogClass.ServiceVi, $"Shared frame buffers: process {pid} not found.");

                    return null;
                }

                KPageTableBase memory = applet.MemoryManager;
                ulong regionStart = memory.StackRegionStart;
                ulong regionPagesCount = (memory.StackRegionEnd - regionStart) / KPageTableBase.PageSize;

                Result result = memory.MapPages(
                    PoolSize / KPageTableBase.PageSize,
                    KPageTableBase.PageSize,
                    0,
                    false,
                    regionStart,
                    regionPagesCount,
                    MemoryState.Stack,
                    KMemoryPermission.ReadAndWrite,
                    out ulong address);

                if (result != Result.Success)
                {
                    Logger.Error?.Print(LogClass.ServiceVi, $"Shared frame buffers: no memory for the pool ({result}).");

                    return null;
                }

                session = new SystemSharedBuffer
                {
                    Pid = pid,
                    _poolAddress = address,
                    _process = applet,
                    NvMapHandle = NvMapDeviceFile.CreateSharedHandle(address, PoolSize),
                };

                SurfaceFlinger.SurfaceFlinger surfaceFlinger = context.Device.System.SurfaceFlinger;

                IGraphicBufferProducer producer = surfaceFlinger.CreateLayer(out long layerId, pid, LayerState.ManagedOpened);

                session._previousRenderLayer = surfaceFlinger.RenderLayerId;
                session.LayerId = layerId;

                // [Nextendo, HOME Menu work] The overlay applet draws above the others: its
                // layer never becomes the one on screen.
                if (!Ryujinx.HLE.HOS.Services.Am.AppletAE.RealAppletManager.IsOverlay(pid))
                {
                    surfaceFlinger.SetRenderLayer(layerId);
                }

                // The applet is told that queue slots 0 to 3 are pool slots 0 to 3 (see
                // AcquireSharedFrameBuffer): one graphic buffer per slot, each at its own offset.
                // A single buffer also leaves the queue with nothing to hand out while that one
                // is on screen, and its "buffer free" event is never raised below two buffers.
                for (int i = 0; i < QueueBuffersCount; i++)
                {
                    Status status = producer.SetPreallocatedBuffer(i, new AndroidStrongPointer<GraphicBuffer>(session.MakeGraphicBuffer(i)));

                    if (status != Status.Success)
                    {
                        Logger.Error?.Print(LogClass.ServiceVi, $"Shared frame buffers: slot {i} not accepted ({status}).");
                    }
                }

                _sessions[pid] = session;

                Logger.Notice.Print(LogClass.ServiceVi, $"Shared frame buffers (process {pid}): pool at 0x{address:X} (0x{PoolSize:X}), nvmap handle {session.NvMapHandle}, layer {layerId}.");

                return session;
            }
        }

        public static SystemSharedBuffer Of(ulong pid)
        {
            lock (_lock)
            {
                return _sessions.GetValueOrDefault(pid);
            }
        }

        // Puts an applet's layer on screen.
        public static void Show(Switch device, ulong pid)
        {
            SystemSharedBuffer session = Of(pid);

            if (session != null)
            {
                device.System.SurfaceFlinger.SetRenderLayer(session.LayerId);
            }
        }

        // The applet is gone: its layer leaves the screen for the one that was there before.
        public static void Release(Switch device, ulong pid)
        {
            lock (_lock)
            {
                if (!_sessions.Remove(pid, out SystemSharedBuffer session))
                {
                    return;
                }

                SurfaceFlinger.SurfaceFlinger surfaceFlinger = device.System.SurfaceFlinger;

                if (surfaceFlinger.RenderLayerId == session.LayerId)
                {
                    surfaceFlinger.SetRenderLayer(session._previousRenderLayer);
                }

                // Sessions opened on top of this one fall back to what was under it.
                foreach (SystemSharedBuffer other in _sessions.Values)
                {
                    if (other._previousRenderLayer == session.LayerId)
                    {
                        other._previousRenderLayer = session._previousRenderLayer;
                    }
                }

                surfaceFlinger.DestroyManagedLayer(session.LayerId);
            }
        }

        private GraphicBuffer MakeGraphicBuffer(int slot)
        {
            GraphicBuffer buffer = new();

            buffer.Header.Magic = 0x47424652; // 'GBFR'
            buffer.Header.Width = Width;
            buffer.Header.Height = Height;
            buffer.Header.Stride = Width;
            buffer.Header.Format = PixelFormat.Rgba8888;
            buffer.Header.Usage = 0xb00;
            buffer.Header.FdsCount = 0;
            buffer.Header.IntsCount = System.Runtime.CompilerServices.Unsafe.SizeOf<NvGraphicBuffer>() / 4;

            buffer.Buffer.NvMapId = NvMapHandle;
            buffer.Buffer.Magic = unchecked((int)0xDAFFCAFF);
            buffer.Buffer.Pid = 42;
            buffer.Buffer.Usage = 0xb00;
            buffer.Buffer.PixelFormat = (int)PixelFormat.Rgba8888;
            buffer.Buffer.ExternalPixelFormat = (int)PixelFormat.Rgba8888;
            buffer.Buffer.Stride = Width;
            buffer.Buffer.FrameBufferSize = (int)SlotSize;
            buffer.Buffer.PlanesCount = 1;
            buffer.Buffer.Surfaces.SetFirst(new NvGraphicBufferSurface
            {
                Width = Width,
                Height = Height,
                ColorFormat = ColorFormat.A8B8G8R8,
                Layout = 3, // block linear
                Pitch = Width * BytesPerPixel,
                NvMapHandle = NvMapHandle,
                Offset = (int)(slot * SlotSize),
                Kind = 0xfe, // generic 16Bx2
                BlockHeightLog2 = 4,
                Size = SlotSize,
            });

            return buffer;
        }

        public static void WriteLayout(ServiceCtx context, ulong position)
        {
            // SharedMemoryPoolLayout: s32 count, then 16 slots of {u64 offset, u64 size, s32 width, s32 height}.
            context.Memory.Write(position, new byte[LayoutSize]);
            context.Memory.Write(position, SlotsCount);

            for (int i = 0; i < SlotsCount; i++)
            {
                ulong slot = position + 8 + (ulong)(i * 0x18);

                context.Memory.Write(slot + 0x00, (ulong)i * SlotSize);
                context.Memory.Write(slot + 0x08, (ulong)SlotSize);
                context.Memory.Write(slot + 0x10, Width);
                context.Memory.Write(slot + 0x14, Height);
            }
        }

        private BufferQueueProducer Producer(ServiceCtx context)
        {
            return (BufferQueueProducer)context.Device.System.SurfaceFlinger.GetProducerByLayerId(LayerId);
        }

        public void Connect(ServiceCtx context)
        {
            lock (_lock)
            {
                if (_connected)
                {
                    return;
                }

                Status status = Producer(context).Connect(null, NativeWindowApi.NVN, false, out _);

                _connected = status == Status.Success;

                if (!_connected)
                {
                    Logger.Error?.Print(LogClass.ServiceVi, $"Shared frame buffers: connect failed ({status}).");
                }
            }
        }

        public void Disconnect(ServiceCtx context)
        {
            lock (_lock)
            {
                if (_connected)
                {
                    Producer(context)?.Disconnect(NativeWindowApi.NVN);

                    _connected = false;
                }
            }
        }

        public Status Acquire(ServiceCtx context, out int slot, out AndroidFence fence)
        {
            BufferQueueProducer producer = Producer(context);

            Status status = producer.DequeueBuffer(out slot, out fence, false, Width, Height, PixelFormat.Rgba8888, 0);

            if (status == Status.Success || status == (Status)1 /* buffer needs reallocation */)
            {
                producer.RequestBuffer(slot, out _);

                return Status.Success;
            }

            return status;
        }

        public Status Present(ServiceCtx context, int slot, Rect crop, uint transform, int swapInterval, AndroidFence fence)
        {
            if (crop.Width <= 0 || crop.Height <= 0)
            {
                crop = new Rect(Width, Height);
            }

            if (_lastPresentedSlot < 0)
            {
                Ryujinx.HLE.HOS.Services.Am.AppletAE.RealAppletManager.OnFirstFrame(Pid);
            }

            _lastPresentedSlot = slot;

            IGraphicBufferProducer.QueueBufferInput input = new()
            {
                Timestamp = System.Diagnostics.Stopwatch.GetTimestamp(),
                IsAutoTimestamp = 1,
                Crop = crop,
                ScalingMode = NativeWindowScalingMode.ScaleToWindow,
                Transform = (NativeWindowTransform)transform,
                StickyTransform = 0,
                Async = 0,
                SwapInterval = swapInterval,
                Fence = fence,
            };

            return Producer(context).QueueBuffer(slot, ref input, out _);
        }

        public void Cancel(ServiceCtx context, int slot)
        {
            AndroidFence fence = AndroidFence.NoFence;

            Producer(context).CancelBuffer(slot, ref fence);
        }

        public KReadableEvent AcquirableEvent(ServiceCtx context)
        {
            BufferQueueCore core = Producer(context).Core;

            // The queue only raises its "a buffer is free" event for a client that asked for
            // it; through the binder that is what fetching the native handle does.
            lock (core.Lock)
            {
                core.EnableExternalEvent = true;
            }

            return core.GetWaitBufferFreeEvent();
        }
    }
}
