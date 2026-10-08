using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.HLE.HOS.Services.Nv.NvDrvServices.NvMap;
using Ryujinx.HLE.HOS.Services.SurfaceFlinger;
using Ryujinx.HLE.HOS.Services.SurfaceFlinger.Types;
using Ryujinx.Horizon.Common;

namespace Ryujinx.HLE.HOS.Services.Vi
{
    // nn::vi::fbshare: the frame buffers a system applet (the HOME Menu) draws into belong to
    // the system. It hands the applet an nvmap handle on a pool of 1280x720 buffers, a layout
    // of that pool and a layer; the applet acquires a buffer, renders, presents it.
    //
    // One session, for the one system applet that can run. The pool is mapped in the applet
    // with its heap (it never sizes that heap itself), the layer is an ordinary layer whose
    // buffer queue is driven from here instead of through the binder.
    static class SystemSharedBuffer
    {
        public const int Width = 1280;
        public const int Height = 720;

        private const int BlockLinearHeight = 768;
        private const int BytesPerPixel = 4;
        private const int SlotsCount = 7;

        public const uint SlotSize = Width * BlockLinearHeight * BytesPerPixel; // 0x3C0000
        public const uint PoolSize = SlotSize * SlotsCount;                    // 0x1A40000

        public const int LayoutSize = 0x188;

        private static readonly object _lock = new();

        private static bool _created;
        private static bool _connected;

        public static long LayerId { get; private set; }
        public static int NvMapHandle { get; private set; }

        public static bool EnsureSession(ServiceCtx context)
        {
            lock (_lock)
            {
                if (_created)
                {
                    return true;
                }

                // The heap grows by 2 MiB steps.
                ulong heapSize = (PoolSize + 0x1FFFFFUL) & ~0x1FFFFFUL;

                Result result = context.Process.MemoryManager.SetHeapSize(heapSize, out ulong address);

                if (result != Result.Success)
                {
                    Logger.Error?.Print(LogClass.ServiceVi, $"Shared frame buffers: no memory for the pool ({result}).");

                    return false;
                }

                NvMapHandle = NvMapDeviceFile.CreateSharedHandle(address, PoolSize);

                SurfaceFlinger.SurfaceFlinger surfaceFlinger = context.Device.System.SurfaceFlinger;

                IGraphicBufferProducer producer = surfaceFlinger.CreateLayer(out long layerId, context.Process.Pid, LayerState.ManagedOpened);

                surfaceFlinger.SetRenderLayer(layerId);

                LayerId = layerId;

                producer.SetPreallocatedBuffer(0, new AndroidStrongPointer<GraphicBuffer>(MakeGraphicBuffer()));

                _created = true;

                Logger.Notice.Print(LogClass.ServiceVi, $"Shared frame buffers: pool at 0x{address:X} (0x{PoolSize:X}), nvmap handle {NvMapHandle}, layer {layerId}.");

                return true;
            }
        }

        private static GraphicBuffer MakeGraphicBuffer()
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
                Offset = 0,
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

        private static BufferQueueProducer Producer(ServiceCtx context)
        {
            return (BufferQueueProducer)context.Device.System.SurfaceFlinger.GetProducerByLayerId(LayerId);
        }

        public static void Connect(ServiceCtx context)
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

        public static void Disconnect(ServiceCtx context)
        {
            lock (_lock)
            {
                if (_connected)
                {
                    Producer(context).Disconnect(NativeWindowApi.NVN);

                    _connected = false;
                }
            }
        }

        public static Status Acquire(ServiceCtx context, out int slot, out AndroidFence fence)
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

        public static Status Present(ServiceCtx context, int slot, Rect crop, uint transform, int swapInterval, AndroidFence fence)
        {
            if (crop.Width <= 0 || crop.Height <= 0)
            {
                crop = new Rect(Width, Height);
            }

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

        public static void Cancel(ServiceCtx context, int slot)
        {
            AndroidFence fence = AndroidFence.NoFence;

            Producer(context).CancelBuffer(slot, ref fence);
        }

        public static KReadableEvent AcquirableEvent(ServiceCtx context)
        {
            return Producer(context).Core.GetWaitBufferFreeEvent();
        }
    }
}
