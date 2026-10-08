using Ryujinx.Common;
using Ryujinx.Common.Logging;

namespace Ryujinx.HLE.HOS.Services.Vi.RootService.ApplicationDisplayService
{
    class ISystemDisplayService : IpcService
    {
        private readonly IApplicationDisplayService _applicationDisplayService;

        public ISystemDisplayService(IApplicationDisplayService applicationDisplayService)
        {
            _applicationDisplayService = applicationDisplayService;
        }

        [CommandCmif(2205)]
        // SetLayerZ(u64, u64)
        public ResultCode SetLayerZ(ServiceCtx context)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceVi);

            return ResultCode.Success;
        }

        [CommandCmif(2207)]
        // SetLayerVisibility(b8, u64)
        public ResultCode SetLayerVisibility(ServiceCtx context)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceVi);

            return ResultCode.Success;
        }

        [CommandCmif(2312)] // 1.0.0-6.2.0
        // CreateStrayLayer(u32, u64) -> (u64, u64, buffer<bytes, 6>)
        public ResultCode CreateStrayLayer(ServiceCtx context)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceVi);

            return _applicationDisplayService.CreateStrayLayer(context);
        }

        [CommandCmif(3000)]
        // ListDisplayModes(u64) -> (u64 count, buffer<nn::vi::DisplayModeInfo, 6>)
        public ResultCode ListDisplayModes(ServiceCtx context)
        {
            ulong displayId = context.RequestData.ReadUInt64();

            (ulong width, ulong height) = AndroidSurfaceComposerClient.GetDisplayInfo(context, displayId);

            // One mode: the current one, the same GetDisplayMode reports.
            ulong position = context.Request.ReceiveBuff[0].Position;

            context.Memory.Write(position + 0x0, (uint)width);
            context.Memory.Write(position + 0x4, (uint)height);
            context.Memory.Write(position + 0x8, 60.0f);
            context.Memory.Write(position + 0xC, 0);

            context.ResponseData.Write(1L);

            return ResultCode.Success;
        }

        [CommandCmif(3200)]
        // GetDisplayMode(u64) -> nn::vi::DisplayModeInfo
        public ResultCode GetDisplayMode(ServiceCtx context)
        {
            ulong displayId = context.RequestData.ReadUInt64();

            (ulong width, ulong height) = AndroidSurfaceComposerClient.GetDisplayInfo(context, displayId);

            context.ResponseData.Write((uint)width);
            context.ResponseData.Write((uint)height);
            context.ResponseData.Write(60.0f);
            context.ResponseData.Write(0);

            Logger.Stub?.PrintStub(LogClass.ServiceVi);

            return ResultCode.Success;
        }
        // [Nextendo, HOME Menu work] nn::vi::fbshare, see SystemSharedBuffer.

        private static readonly bool _trace = System.Environment.GetEnvironmentVariable("NEXTENDO_IPC_TRACE") == "1";

        private int _acquirableEventHandle;
        private SystemSharedBuffer _shared;

        // The session of the applet on the other end: every command names it by its buffer or
        // layer handle, first in the request.
        private SystemSharedBuffer Shared(ServiceCtx context)
        {
            if (_shared == null)
            {
                long position = context.RequestData.BaseStream.Position;

                _shared = SystemSharedBuffer.Find(context.RequestData.ReadUInt64());

                context.RequestData.BaseStream.Position = position;
            }

            return _shared;
        }

        [CommandCmif(8225)] // 4.0.0+
        // GetSharedBufferMemoryHandleId(u64 bufferId, nn::applet::AppletResourceUserId) -> (s32 nvmapHandle, u64 size, buffer<SharedMemoryPoolLayout, 0x16>)
        public ResultCode GetSharedBufferMemoryHandleId(ServiceCtx context)
        {
            if (Shared(context) == null)
            {
                return ResultCode.InvalidValue;
            }

            SystemSharedBuffer.WriteLayout(context, context.Request.ReceiveBuff[0].Position);

            context.ResponseData.Write(_shared.NvMapHandle);
            context.ResponseData.Write(0);
            context.ResponseData.Write((ulong)SystemSharedBuffer.PoolSize);

            return ResultCode.Success;
        }

        [CommandCmif(8250)] // 4.0.0+
        // OpenSharedLayer(u64 layerId)
        public ResultCode OpenSharedLayer(ServiceCtx context)
        {
            return Shared(context) != null ? ResultCode.Success : ResultCode.InvalidValue;
        }

        [CommandCmif(8251)] // 4.0.0+
        // CloseSharedLayer(u64 layerId)
        public ResultCode CloseSharedLayer(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(8252)] // 4.0.0+
        // ConnectSharedLayer(u64 layerId)
        public ResultCode ConnectSharedLayer(ServiceCtx context)
        {
            Shared(context)?.Connect(context);

            return ResultCode.Success;
        }

        [CommandCmif(8253)] // 4.0.0+
        // DisconnectSharedLayer(u64 layerId)
        public ResultCode DisconnectSharedLayer(ServiceCtx context)
        {
            Shared(context)?.Disconnect(context);

            return ResultCode.Success;
        }

        [CommandCmif(8254)] // 4.0.0+
        // AcquireSharedFrameBuffer(u64 layerId) -> (android::Fence, s32[4] slots, s64 targetSlot)
        public ResultCode AcquireSharedFrameBuffer(ServiceCtx context)
        {
            SurfaceFlinger.Status status = Shared(context).Acquire(context, out int slot, out SurfaceFlinger.AndroidFence fence);

            if (_trace) Logger.Notice.Print(LogClass.ServiceVi, $"[fbshare] acquire -> {status} slot={slot} fences={fence.FenceCount}");

            if (status != SurfaceFlinger.Status.Success)
            {
                Logger.Warning?.Print(LogClass.ServiceVi, $"AcquireSharedFrameBuffer: {status}");

                return ResultCode.InvalidValue;
            }

            context.ResponseData.WriteStruct(fence);
            context.ResponseData.Write(0);
            context.ResponseData.Write(1);
            context.ResponseData.Write(2);
            context.ResponseData.Write(3);
            context.ResponseData.Write(0);
            context.ResponseData.Write((long)slot);

            return ResultCode.Success;
        }

        [CommandCmif(8255)] // 4.0.0+
        // PresentSharedFrameBuffer(android::Fence, nn::vi::CropRegion, u32 transform, s32 swapInterval, u64 layerId, s64 slot)
        public ResultCode PresentSharedFrameBuffer(ServiceCtx context)
        {
            SurfaceFlinger.AndroidFence fence = context.RequestData.ReadStruct<SurfaceFlinger.AndroidFence>();

            SurfaceFlinger.Rect crop = new()
            {
                Left = context.RequestData.ReadInt32(),
                Top = context.RequestData.ReadInt32(),
                Right = context.RequestData.ReadInt32(),
                Bottom = context.RequestData.ReadInt32(),
            };

            uint transform = context.RequestData.ReadUInt32();
            int swapInterval = context.RequestData.ReadInt32();

            context.RequestData.ReadInt32(); // Alignment.
            context.RequestData.ReadUInt64(); // Layer id.

            int slot = (int)context.RequestData.ReadInt64();

            SurfaceFlinger.Status status = _shared.Present(context, slot, crop, transform, swapInterval, fence);

            if (_trace) Logger.Notice.Print(LogClass.ServiceVi, $"[fbshare] present slot={slot} crop=({crop.Left},{crop.Top},{crop.Right},{crop.Bottom}) transform={transform} swap={swapInterval} fences={fence.FenceCount} f0=({fence.NvFences[0].Id},{fence.NvFences[0].Value}) raw={System.Convert.ToHexString(context.RequestData.BaseStream is System.IO.MemoryStream ms ? ms.ToArray() : System.Array.Empty<byte>())} -> {status}");

            if (status != SurfaceFlinger.Status.Success)
            {
                Logger.Warning?.Print(LogClass.ServiceVi, $"PresentSharedFrameBuffer(slot {slot}): {status}");
            }

            return ResultCode.Success;
        }

        [CommandCmif(8256)] // 4.0.0+
        // GetSharedFrameBufferAcquirableEvent(u64 layerId) -> handle<copy>
        public ResultCode GetSharedFrameBufferAcquirableEvent(ServiceCtx context)
        {
            if (Shared(context) == null)
            {
                return ResultCode.InvalidValue;
            }

            if (_acquirableEventHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_shared.AcquirableEvent(context), out _acquirableEventHandle) != Ryujinx.Horizon.Common.Result.Success)
                {
                    throw new System.InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = Ryujinx.HLE.HOS.Ipc.IpcHandleDesc.MakeCopy(_acquirableEventHandle);

            return ResultCode.Success;
        }

        [CommandCmif(8257)] // 4.0.0+
        // FillSharedFrameBufferColor(...)
        public ResultCode FillSharedFrameBufferColor(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(8258)] // 5.0.0+
        // CancelSharedFrameBuffer(u64 layerId, s64 slot)
        public ResultCode CancelSharedFrameBuffer(ServiceCtx context)
        {
            context.RequestData.ReadUInt64();

            _shared?.Cancel(context, (int)context.RequestData.ReadInt64());

            return ResultCode.Success;
        }
    }
}
