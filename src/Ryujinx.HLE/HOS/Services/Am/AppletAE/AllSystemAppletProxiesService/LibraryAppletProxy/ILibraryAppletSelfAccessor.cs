using Ryujinx.Common;
using Ryujinx.Common.Logging;
using System;

namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.LibraryAppletProxy
{
    class ILibraryAppletSelfAccessor : IpcService
    {
        // [Nextendo, HOME Menu work]
        [CommandCmif(150)] // 10.0.0+
        // ShouldSetGpuTimeSliceManually() -> bool
        public ResultCode ShouldSetGpuTimeSliceManually(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(160)] // 17.0.0+
        // GetLibraryAppletInfoEx() -> u64 (always 0)
        public ResultCode GetLibraryAppletInfoEx(ServiceCtx context)
        {
            context.ResponseData.Write(0UL);

            return ResultCode.Success;
        }

        private readonly AppletStandalone _appletStandalone = new();

        // Set when the applet was started by a running system applet (see RealApplet).
        private readonly RealApplet _real;
        private int _popInDataEventHandle;
        private int _popInteractiveInDataEventHandle;

        public ILibraryAppletSelfAccessor(ServiceCtx context, ulong pid)
        {
            ulong programId = context.Device.Processes.GetProcess(pid).ProgramId;

            if (RealAppletManager.TryGet(pid, out _real))
            {
                _appletStandalone = new AppletStandalone()
                {
                    AppletId = _real.AppletId,
                    LibraryAppletMode = _real.Mode,
                };
            }
            else if (programId == 0x0100000000001009)
            {
                // Create MiiEdit data.
                _appletStandalone = new AppletStandalone()
                {
                    AppletId = AppletId.MiiEdit,
                    LibraryAppletMode = LibraryAppletMode.AllForeground,
                };

                byte[] miiEditInputData = new byte[0x100];
                miiEditInputData[0] = 0x03; // Hardcoded unknown value.

                _appletStandalone.InputData.Enqueue(miiEditInputData);
            }
            else
            {
                throw new NotImplementedException($"{programId} applet is not implemented.");
            }
        }

        [CommandCmif(0)]
        // PopInData() -> object<nn::am::service::IStorage>
        public ResultCode PopInData(ServiceCtx context)
        {
            if (_real != null)
            {
                // What the applet gave back before it wound comes first.
                if (!_real.Unpopped.TryPop(out byte[] data) && !_real.Normal.TryPop(out data))
                {
                    return ResultCode.NotAvailable;
                }

                MakeObject(context, new IStorage(data));

                return ResultCode.Success;
            }

            byte[] appletData = _appletStandalone.InputData.Dequeue();

            if (appletData.Length == 0)
            {
                return ResultCode.NotAvailable;
            }

            MakeObject(context, new IStorage(appletData));

            return ResultCode.Success;
        }
        
        [CommandCmif(30)]
        // UnpopInData(object<nn::am::service::IStorage>): an in-data given back, to be read
        // again (the applet does it before it winds, and reads it when it starts again).
        public ResultCode UnpopInData(ServiceCtx context)
        {
            IStorage data = GetObject<IStorage>(context, 0);

            if (_real == null || data == null)
            {
                return ResultCode.NullObject;
            }

            _real.Unpopped.Push(data.Data);

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        // PushOutData(object<nn::am::service::IStorage>)
        public ResultCode PushOutData(ServiceCtx context)
        {
            IStorage appletData = GetObject<IStorage>(context, 0);
            
            if (appletData == null || appletData.Data.Length == 0) // is this necessary?
            {
                return ResultCode.NullObject;
            }

            if (_real != null)
            {
                _real.Normal.Push(appletData.Data);

                return ResultCode.Success;
            }
    
            _appletStandalone.InputData.Enqueue(appletData.Data);

            return ResultCode.Success;
        }
        
        [CommandCmif(10)]
        // ExitProcessAndReturn -> nn::am::service::LibraryAppletInfo
        public ResultCode ExitProcessAndReturn(ServiceCtx context)
        {
            if (_real != null)
            {
                // The applet now sleeps until the system ends its process.
                RealAppletManager.RequestFinish(_real);

                return ResultCode.Success;
            }

            // The applet (e.g. Mii editor) is closing and "returning to the title which launched it"
            // (qlaunch). Since the home menu isn't emulated, we signal the host to stop emulation:
            // AppHost.UpdateFrame notices ActiveApplication == null and unwinds to the launcher.
            // We must NOT terminate the process from here: killing it while its other threads are
            // mid-IPC (e.g. sm:GetService) disposed shared wait handles and crashed the emulator.
            // Returning Success lets the guest settle into its sleep loop while the host teardown
            // (Horizon.Dispose) terminates the process safely.

            Logger.Info?.Print(LogClass.Service,
                "[Nextendo] applet 0x0100000000001009 requested ExitProcessAndReturn; stopping emulation to return to the launcher.");

            context.Device.Processes.AppletExitRequested = true;

            return ResultCode.Success;
        }


        [CommandCmif(11)]
        // GetLibraryAppletInfo() -> nn::am::service::LibraryAppletInfo
        public ResultCode GetLibraryAppletInfo(ServiceCtx context)
        {
            LibraryAppletInfo libraryAppletInfo = new()
            {
                AppletId = _appletStandalone.AppletId,
                LibraryAppletMode = _appletStandalone.LibraryAppletMode,
            };

            context.ResponseData.WriteStruct(libraryAppletInfo);

            return ResultCode.Success;
        }

        [CommandCmif(14)]
        // GetCallerAppletIdentityInfo() -> nn::am::service::AppletIdentityInfo
        public ResultCode GetCallerAppletIdentityInfo(ServiceCtx context)
        {
            AppletIdentifyInfo appletIdentifyInfo = new()
            {
                AppletId = AppletId.QLaunch,
                // 0x4 padding
                TitleId = 0x0100000000001000, // qlaunch systemAppletMenu title ID
            };

            context.ResponseData.WriteStruct(appletIdentifyInfo);

            return ResultCode.Success;
        }
        [CommandCmif(2)]
        // PopInteractiveInData() -> object<nn::am::service::IStorage>
        public ResultCode PopInteractiveInData(ServiceCtx context)
        {
            if (_real == null || !_real.Interactive.TryPop(out byte[] data))
            {
                return ResultCode.NotAvailable;
            }

            MakeObject(context, new IStorage(data));

            return ResultCode.Success;
        }

        [CommandCmif(3)]
        // PushInteractiveOutData(object<nn::am::service::IStorage>)
        public ResultCode PushInteractiveOutData(ServiceCtx context)
        {
            IStorage data = GetObject<IStorage>(context, 0);

            if (_real != null && data != null)
            {
                _real.Interactive.Push(data.Data);
            }

            return ResultCode.Success;
        }

        [CommandCmif(5)]
        // GetPopInDataEvent() -> handle<copy>
        public ResultCode GetPopInDataEvent(ServiceCtx context)
        {
            return ReplyWithEvent(context, _real?.PopInDataEvent, ref _popInDataEventHandle);
        }

        [CommandCmif(6)]
        // GetPopInteractiveInDataEvent() -> handle<copy>
        public ResultCode GetPopInteractiveInDataEvent(ServiceCtx context)
        {
            return ReplyWithEvent(context, _real?.PopInteractiveInDataEvent, ref _popInteractiveInDataEventHandle);
        }

        private static ResultCode ReplyWithEvent(ServiceCtx context, Ryujinx.HLE.HOS.Kernel.Threading.KEvent kernelEvent, ref int handle)
        {
            kernelEvent ??= new Ryujinx.HLE.HOS.Kernel.Threading.KEvent(context.Device.System.KernelContext);

            if (handle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(kernelEvent.ReadableEvent, out handle) != Ryujinx.Horizon.Common.Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = Ryujinx.HLE.HOS.Ipc.IpcHandleDesc.MakeCopy(handle);

            return ResultCode.Success;
        }

        [CommandCmif(12)]
        // GetMainAppletIdentityInfo() -> nn::am::service::AppletIdentityInfo
        public ResultCode GetMainAppletIdentityInfo(ServiceCtx context)
        {
            return GetCallerAppletIdentityInfo(context);
        }

        [CommandCmif(13)]
        // CanUseApplicationCore() -> bool
        public ResultCode CanUseApplicationCore(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }
    }
}
