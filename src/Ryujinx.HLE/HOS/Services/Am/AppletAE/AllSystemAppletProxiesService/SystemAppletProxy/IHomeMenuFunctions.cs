using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.Horizon.Common;
using System;

namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.SystemAppletProxy
{
    class IHomeMenuFunctions : IpcService
    {
        // [Nextendo, HOME Menu work]
        [CommandCmif(11)]
        // LockForeground()
        public ResultCode LockForeground(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(12)]
        // UnlockForeground()
        public ResultCode UnlockForeground(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        private readonly KEvent _channelEvent;
        private int _channelEventHandle;

        private readonly ulong _pid;

        public IHomeMenuFunctions(Horizon system, ulong pid = 0)
        {
            _pid = pid;
            // Signalled while a message waits in the general channel (RealAppletManager).
            _channelEvent = new KEvent(system.KernelContext);

            RealAppletManager.GeneralChannelPushed += () => _channelEvent.ReadableEvent.Signal();

            if (RealAppletManager.GeneralChannelCount > 0)
            {
                _channelEvent.ReadableEvent.Signal();
            }
        }

        [CommandCmif(20)]
        // PopFromGeneralChannel() -> object<nn::am::service::IStorage>
        public ResultCode PopFromGeneralChannel(ServiceCtx context)
        {
            if (!RealAppletManager.TryPopFromGeneralChannel(out byte[] data))
            {
                _channelEvent.ReadableEvent.Clear();

                return ResultCode.NotAvailable;
            }

            if (RealAppletManager.GeneralChannelCount == 0)
            {
                _channelEvent.ReadableEvent.Clear();
            }

            MakeObject(context, new IStorage(data));

            return ResultCode.Success;
        }

        [CommandCmif(10)]
        // RequestToGetForeground()
        public ResultCode RequestToGetForeground(ServiceCtx context)
        {
            RealAppletManager.SystemAppletToForeground(context.Device, _pid);

            return ResultCode.Success;
        }

        [CommandCmif(21)]
        // GetPopFromGeneralChannelEvent() -> handle<copy>
        public ResultCode GetPopFromGeneralChannelEvent(ServiceCtx context)
        {
            if (_channelEventHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_channelEvent.ReadableEvent, out _channelEventHandle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_channelEventHandle);

            Logger.Stub?.PrintStub(LogClass.ServiceAm);

            return ResultCode.Success;
        }

        [CommandCmif(40)]
        // IsSleepEnabled() -> bool
        public ResultCode IsSleepEnabled(ServiceCtx context)
        {
            context.ResponseData.Write(true);

            return ResultCode.Success;
        }

        [CommandCmif(41)]
        // IsRebootEnabled() -> bool
        public ResultCode IsRebootEnabled(ServiceCtx context)
        {
            // [Nextendo, HOME Menu work] Without it the HOME Menu turns the console off where it
            // means to restart it (after a change of language).
            context.ResponseData.Write(true);

            return ResultCode.Success;
        }
    }
}
