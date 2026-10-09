using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.Horizon.Common;
using System;

namespace Ryujinx.HLE.HOS.Services.Ns
{
    [Service("ns:su")]
    class ISystemUpdateInterface : IpcService
    {
        private readonly KEvent _contentDeliveryEvent;
        private int _contentDeliveryEventHandle;

        public ISystemUpdateInterface(ServiceCtx context)
        {
            _contentDeliveryEvent = new KEvent(context.Device.System.KernelContext);
        }

        [CommandCmif(0)]
        // GetBackgroundNetworkUpdateState() -> u8
        public ResultCode GetBackgroundNetworkUpdateState(ServiceCtx context)
        {
            context.ResponseData.Write((byte)0); // none

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        // OpenSystemUpdateControl() -> object<nn::ns::detail::ISystemUpdateControl>
        public ResultCode OpenSystemUpdateControl(ServiceCtx context)
        {
            // "System update" in the settings: without the object the menu followed a null
            // pointer and the whole emulation went down.
            MakeObject(context, new ISystemUpdateControl());

            return ResultCode.Success;
        }

        [CommandCmif(9)] // 4.0.0+
        // GetSystemUpdateNotificationEventForContentDelivery() -> handle<copy>
        public ResultCode GetSystemUpdateNotificationEventForContentDelivery(ServiceCtx context)
        {
            // Never signalled: no system update is ever delivered here.
            if (_contentDeliveryEventHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_contentDeliveryEvent.ReadableEvent, out _contentDeliveryEventHandle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_contentDeliveryEventHandle);

            return ResultCode.Success;
        }
    }
    // [Nextendo, HOME Menu work] The console is always up to date here: the check ends at
    // once and says so, nothing is ever downloaded.
    class ISystemUpdateControl : IpcService
    {
        private StubEvent _done;

        private void ReplyDone(ServiceCtx context)
        {
            _done ??= new StubEvent(context);
            _done.Signal();
            _done.ReplyWithHandle(context);
        }

        [CommandCmif(0)]
        // HasDownloaded() -> b8
        public ResultCode HasDownloaded(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        // RequestCheckLatestUpdate() -> (handle<copy>, object<nn::ns::detail::IAsyncValue>)
        public ResultCode RequestCheckLatestUpdate(ServiceCtx context)
        {
            ReplyDone(context);
            MakeObject(context, new IAsyncValue(new byte[4])); // 0: up to date

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // RequestDownloadLatestUpdate() -> (handle<copy>, object<nn::ns::detail::IAsyncResult>)
        public ResultCode RequestDownloadLatestUpdate(ServiceCtx context)
        {
            ReplyDone(context);
            MakeObject(context, new IAsyncResult());

            return ResultCode.Success;
        }

        [CommandCmif(3)]
        // GetDownloadProgress() -> (s64 loaded, s64 total)
        public ResultCode GetDownloadProgress(ServiceCtx context)
        {
            context.ResponseData.Write(0L);
            context.ResponseData.Write(0L);

            return ResultCode.Success;
        }

        [CommandCmif(4)]
        // ApplyDownloadedUpdate()
        public ResultCode ApplyDownloadedUpdate(ServiceCtx context) => ResultCode.Success;
    }

    class IAsyncValue : IpcService
    {
        private readonly byte[] _value;

        public IAsyncValue(byte[] value)
        {
            _value = value;
        }

        [CommandCmif(0)]
        // GetSize() -> u64
        public ResultCode GetSize(ServiceCtx context)
        {
            context.ResponseData.Write((long)_value.Length);

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        // Get() -> buffer<bytes, 6>
        public ResultCode Get(ServiceCtx context)
        {
            if (context.Request.ReceiveBuff.Count > 0 && context.Request.ReceiveBuff[0].Size >= (ulong)_value.Length)
            {
                context.Memory.Write(context.Request.ReceiveBuff[0].Position, _value);
            }

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // Cancel()
        public ResultCode Cancel(ServiceCtx context) => ResultCode.Success;
    }
}
