using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.Horizon.Common;
using Ryujinx.Common.Logging;
using System;
using System.Threading.Tasks;

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
    // [Nextendo, HOME Menu work] The system update is the update of the emulator: the check
    // asks GitHub for a newer Ryujinx | Nextendo; "Update" installs it (download, files
    // replaced, the emulator starts the HOME Menu again).
    class ISystemUpdateControl : IpcService
    {
        // nn::ns::LatestSystemUpdate
        private const byte UpToDate = 0;
        private const byte NeedsDownload = 2;

        private StubEvent _checkDone;
        private StubEvent _downloadDone;
        // Kept across the controls the HOME Menu opens (one checks, another applies).
        private static bool _available;

        [CommandCmif(0)]
        // HasDownloaded() -> b8
        public ResultCode HasDownloaded(ServiceCtx context)
        {
            context.ResponseData.Write(_available);

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        // RequestCheckLatestUpdate() -> (handle<copy>, object<nn::ns::detail::IAsyncValue>)
        public ResultCode RequestCheckLatestUpdate(ServiceCtx context)
        {
            byte[] value = new byte[4];
            StubEvent done = _checkDone = new StubEvent(context);

            done.ReplyWithHandle(context);
            MakeObject(context, new IAsyncValue(value));

            Task.Run(async () =>
            {
                string version = null;

                try
                {
                    Task<string> check = Ryujinx.Common.Configuration.NextendoSystemUpdate.Check?.Invoke() ?? Task.FromResult<string>(null);

                    if (await Task.WhenAny(check, Task.Delay(20000)) == check)
                    {
                        version = check.Result;
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ServiceNs, $"[home] update check failed: {e.Message}");
                }

                _available = version != null;
                value[0] = _available ? NeedsDownload : UpToDate;

                Logger.Info?.Print(LogClass.ServiceNs, $"[home] update check: {(version != null ? "Ryujinx | Nextendo " + version + " available" : "up to date")}");

                done.Signal();
            });

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // RequestDownloadLatestUpdate() -> (handle<copy>, object<nn::ns::detail::IAsyncResult>)
        public ResultCode RequestDownloadLatestUpdate(ServiceCtx context)
        {
            // The updater of the emulator downloads with its own progress window, once applied.
            _downloadDone ??= new StubEvent(context);
            _downloadDone.Signal();
            _downloadDone.ReplyWithHandle(context);
            MakeObject(context, new IAsyncResult());

            return ResultCode.Success;
        }

        [CommandCmif(3)]
        // GetDownloadProgress() -> (s64 loaded, s64 total)
        public ResultCode GetDownloadProgress(ServiceCtx context)
        {
            context.ResponseData.Write(1L);
            context.ResponseData.Write(1L);

            return ResultCode.Success;
        }

        [CommandCmif(9)]
        // GetDownloadedEulaDataSize(nn::ns::detail::EulaDataPath) -> u64
        public ResultCode GetDownloadedEulaDataSize(ServiceCtx context)
        {
            context.ResponseData.Write(0UL); // no new agreement to read

            return ResultCode.Success;
        }

        [CommandCmif(4)]
        // ApplyDownloadedUpdate()
        public ResultCode ApplyDownloadedUpdate(ServiceCtx context)
        {
            if (_available)
            {
                Logger.Info?.Print(LogClass.ServiceNs, "[home] update: the emulator installs the new version and restarts");

                Ryujinx.Common.Configuration.NextendoSystemUpdate.Install?.Invoke();
            }

            return ResultCode.Success;
        }
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

            // The value may be smaller than the room kept for it (LatestSystemUpdate is one byte).
            if (context.Request.ReceiveBuff.Count > 0 && context.Request.ReceiveBuff[0].Size > 0)
            {
                int size = (int)Math.Min(context.Request.ReceiveBuff[0].Size, (ulong)_value.Length);

                context.Memory.Write(context.Request.ReceiveBuff[0].Position, _value.AsSpan(0, size));

                // the size read (the HOME Menu counts the virtual game cards with it)
                context.ResponseData.Write((long)size);
            }
            else
            {
                context.ResponseData.Write(0L);
            }

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // Cancel()
        public ResultCode Cancel(ServiceCtx context) => ResultCode.Success;
    }
}
