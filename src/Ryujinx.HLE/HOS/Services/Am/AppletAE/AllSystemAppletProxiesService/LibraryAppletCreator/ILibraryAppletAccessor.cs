using Ryujinx.Common.Logging;
using Ryujinx.HLE.Exceptions;
using Ryujinx.HLE.HOS.Applets;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.Horizon.Common;
using System;

namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.LibraryAppletCreator
{
    class ILibraryAppletAccessor : DisposableIpcService
    {
        private readonly KernelContext _kernelContext;

        private readonly IApplet _applet;

        private readonly AppletSession _normalSession;
        private readonly AppletSession _interactiveSession;

        private readonly KEvent _stateChangedEvent;
        private readonly KEvent _normalOutDataEvent;
        private readonly KEvent _interactiveOutDataEvent;

        private int _stateChangedEventHandle;
        private int _normalOutDataEventHandle;
        private int _interactiveOutDataEventHandle;

        private int _indirectLayerHandle;

        // Set when the applet runs from its real system title (see RealApplet).
        private readonly RealApplet _real;
        private readonly string _realPath;
        private readonly AppletId _appletId;
        private readonly LibraryAppletMode _mode;
        private KEvent _unknown170Event;
        private int _unknown170EventHandle;

        public ILibraryAppletAccessor(AppletId appletId, Horizon system, ulong callerPid = 0, LibraryAppletMode mode = LibraryAppletMode.AllForeground)
        {
            _kernelContext = system.KernelContext;
            _appletId = appletId;
            _mode = mode;

            _stateChangedEvent = new KEvent(system.KernelContext);
            _normalOutDataEvent = new KEvent(system.KernelContext);
            _interactiveOutDataEvent = new KEvent(system.KernelContext);

            _normalSession = new AppletSession();
            _interactiveSession = new AppletSession();

            Switch device = system.KernelContext.Device;

            if (RealAppletManager.CallerIsSystemApplet(device, callerPid))
            {
                _realPath = RealAppletManager.PathOf(device, appletId);
            }

            if (_realPath != null)
            {
                _real = new RealApplet
                {
                    AppletId = appletId,
                    Mode = mode,
                    CallerPid = callerPid,
                    Normal = _normalSession.GetConsumer(),
                    Interactive = _interactiveSession.GetConsumer(),
                    OnExited = () => _stateChangedEvent.ReadableEvent.Signal(),
                };
            }
            else
            {
                _applet = AppletManager.Create(appletId, system);
                _applet.AppletStateChanged += OnAppletStateChanged;
            }
            _normalSession.DataAvailable += OnNormalOutData;
            _interactiveSession.DataAvailable += OnInteractiveOutData;

            Logger.Info?.Print(LogClass.ServiceAm, $"Applet '{appletId}' created.");
        }

        // [Nextendo, HOME Menu work] Winding: the applet this accessor was made for is started
        // by the system, in the place of the one that asked for it (which ended meanwhile).
        // Returns what tells when that applet is over.
        public Func<bool> StartInPlaceOf(Switch device, ulong callerPid)
        {
            if (_eshop)
            {
                AnswerEshop(device.System);

                return () => true;
            }

            if (_real == null)
            {
                // An applet of the emulator (the Nextendo sign-in page): over when it says so.
                if (_applet == null)
                {
                    return () => true;
                }

                bool over = false;

                _applet.AppletStateChanged += (_, _) => over = true;
                _applet.Start(_normalSession.GetConsumer(), _interactiveSession.GetConsumer());

                return () => over;
            }

            _real.CallerPid = callerPid;

            RealAppletManager.Start(device, _real, _realPath);

            return () => _real.Exited;
        }

        private bool _eshop;

        // The eShop: the store opened in the browser, the user selection answered as cancelled
        // (ResultCancelledByUser 0x27C and no user, as the selection answers B): the HOME Menu
        // stays where it was.
        private void AnswerEshop(Horizon system)
        {
            Applets.Browser.BrowserApplet.OpenNintendoStore(system);

            byte[] answer = new byte[0x18];

            BitConverter.TryWriteBytes(answer.AsSpan(0, 8), 0x27CUL);
            _normalSession.GetConsumer().Push(answer);
            _stateChangedEvent.ReadableEvent.Signal();
        }

        // The accessor handed again to the applet that wound, in its new process: the handles
        // given to the previous one are not valid there.
        public void ReuseFor()
        {
            _stateChangedEventHandle = 0;
            _normalOutDataEventHandle = 0;
            _interactiveOutDataEventHandle = 0;
            _unknown170EventHandle = 0;

            if (_real != null && _real.Exited)
            {
                _stateChangedEvent.ReadableEvent.Signal();
            }

            if (_normalSession.Length > 0)
            {
                _normalOutDataEvent.WritableEvent.Signal();
            }
        }

        private void OnAppletStateChanged(object sender, EventArgs e)
        {
            _stateChangedEvent.WritableEvent.Signal();
        }

        private void OnNormalOutData(object sender, EventArgs e)
        {
            _normalOutDataEvent.WritableEvent.Signal();
        }

        private void OnInteractiveOutData(object sender, EventArgs e)
        {
            _interactiveOutDataEvent.WritableEvent.Signal();
        }

        [CommandCmif(0)]
        // GetAppletStateChangedEvent() -> handle<copy>
        public ResultCode GetAppletStateChangedEvent(ServiceCtx context)
        {
            if (_stateChangedEventHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_stateChangedEvent.ReadableEvent, out _stateChangedEventHandle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_stateChangedEventHandle);

            return ResultCode.Success;
        }

        [CommandCmif(10)]
        // Start()
        public ResultCode Start(ServiceCtx context)
        {
            if (_eshop)
            {
                AnswerEshop(context.Device.System);

                return ResultCode.Success;
            }

            if (_real != null)
            {
                RealAppletManager.Start(context.Device, _real, _realPath);

                return ResultCode.Success;
            }

            return (ResultCode)_applet.Start(_normalSession.GetConsumer(), _interactiveSession.GetConsumer());
        }

        [CommandCmif(20)]
        // RequestExit()
        public ResultCode RequestExit(ServiceCtx context)
        {
            if (_real != null)
            {
                if (_real.State != null && !_real.Exited)
                {
                    _real.State.Messages.Enqueue(SystemAppletProxy.AppletMessage.Exit);
                    _real.State.MessageEvent.ReadableEvent.Signal();
                }

                return ResultCode.Success;
            }

            // TODO: Since we don't support software Applet for now, we can just signals the changed state of the applet.
            _stateChangedEvent.ReadableEvent.Signal();

            Logger.Stub?.PrintStub(LogClass.ServiceAm);

            return ResultCode.Success;
        }

        [CommandCmif(30)]
        // GetResult()
        public ResultCode GetResult(ServiceCtx context)
        {
            if (_real != null)
            {
                return ResultCode.Success;
            }

            return (ResultCode)_applet.GetResult();
        }

        [CommandCmif(60)]
        // PresetLibraryAppletGpuTimeSliceZero()
        public ResultCode PresetLibraryAppletGpuTimeSliceZero(ServiceCtx context)
        {
            // NOTE: This call reset two internal fields to 0 and one internal field to "true".
            //       It seems to be used only with software keyboard inline.
            //       Since we don't support applets for now, it's fine to stub it.

            Logger.Stub?.PrintStub(LogClass.ServiceAm);

            return ResultCode.Success;
        }
        
        [CommandCmif(90)]
        // Unknown90(ulong[4])
        public ResultCode Unknown90(ServiceCtx context)
        {
            // NOTE: This call is performed on SDK 20+ when applet is called.
            //       Since we don't support most applets for now, it's fine to stub it.
            //       Throw if values are not 0 to learn more about what this function does.

            if (context.RequestData.ReadUInt64() != 0 || context.RequestData.ReadUInt64() != 0 ||
                context.RequestData.ReadUInt64() != 0 || context.RequestData.ReadUInt64() != 0)
            {
                throw new ServiceNotImplementedException(this, context, $"{GetType().FullName}: 90");
            }

            Logger.Stub?.PrintStub(LogClass.ServiceAm);
            return ResultCode.Success;
        }

        [CommandCmif(100)]
        // PushInData(object<nn::am::service::IStorage>)
        public ResultCode PushInData(ServiceCtx context)
        {
            IStorage data = GetObject<IStorage>(context, 0);

            // [Nextendo] The HOME Menu asks who visits the eShop: the user selection's settings
            // (0xA0 bytes) with no application (+0x88) and the purpose 2 (+0x98; 0x10 when a
            // game is started). The Nintendo website's store is opened instead (no account needed).
            if (_appletId == AppletId.PlayerSelect && _real != null && data.Data.Length == 0xA0 &&
                BitConverter.ToUInt64(data.Data, 0x88) == 0 && BitConverter.ToUInt32(data.Data, 0x98) == 2)
            {
                _eshop = true;
            }

            _normalSession.Push(data.Data);

            return ResultCode.Success;
        }

        [CommandCmif(101)]
        // PopOutData() -> object<nn::am::service::IStorage>
        public ResultCode PopOutData(ServiceCtx context)
        {
            if (_normalSession.TryPop(out byte[] data))
            {
                MakeObject(context, new IStorage(data));

                _normalOutDataEvent.WritableEvent.Clear();

                return ResultCode.Success;
            }

            return ResultCode.NotAvailable;
        }

        [CommandCmif(103)]
        // PushInteractiveInData(object<nn::am::service::IStorage>)
        public ResultCode PushInteractiveInData(ServiceCtx context)
        {
            IStorage data = GetObject<IStorage>(context, 0);

            _interactiveSession.Push(data.Data);

            return ResultCode.Success;
        }

        [CommandCmif(104)]
        // PopInteractiveOutData() -> object<nn::am::service::IStorage>
        public ResultCode PopInteractiveOutData(ServiceCtx context)
        {
            if (_interactiveSession.TryPop(out byte[] data))
            {
                MakeObject(context, new IStorage(data));

                _interactiveOutDataEvent.WritableEvent.Clear();

                return ResultCode.Success;
            }

            return ResultCode.NotAvailable;
        }

        [CommandCmif(105)]
        // GetPopOutDataEvent() -> handle<copy>
        public ResultCode GetPopOutDataEvent(ServiceCtx context)
        {
            if (_normalOutDataEventHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_normalOutDataEvent.ReadableEvent, out _normalOutDataEventHandle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_normalOutDataEventHandle);

            return ResultCode.Success;
        }

        [CommandCmif(106)]
        // GetPopInteractiveOutDataEvent() -> handle<copy>
        public ResultCode GetPopInteractiveOutDataEvent(ServiceCtx context)
        {
            if (_interactiveOutDataEventHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_interactiveOutDataEvent.ReadableEvent, out _interactiveOutDataEventHandle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_interactiveOutDataEventHandle);

            return ResultCode.Success;
        }

        [CommandCmif(110)]
        // NeedsToExitProcess()
        public ResultCode NeedsToExitProcess(ServiceCtx context)
        {
            return ResultCode.Stubbed;
        }

        [CommandCmif(150)]
        // RequestForAppletToGetForeground()
        public ResultCode RequestForAppletToGetForeground(ServiceCtx context)
        {
            return ResultCode.Stubbed;
        }

        [CommandCmif(160)] // 2.0.0+
        // GetIndirectLayerConsumerHandle() -> u64 indirect_layer_consumer_handle
        public ResultCode GetIndirectLayerConsumerHandle(ServiceCtx context)
        {
            Horizon horizon = _kernelContext.Device.System;

            _indirectLayerHandle = horizon.AppletState.IndirectLayerHandles.Add(_applet);

            context.ResponseData.Write((ulong)_indirectLayerHandle);

            return ResultCode.Success;
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                if (_stateChangedEventHandle != 0)
                {
                    _kernelContext.Syscall.CloseHandle(_stateChangedEventHandle);
                }

                if (_normalOutDataEventHandle != 0)
                {
                    _kernelContext.Syscall.CloseHandle(_normalOutDataEventHandle);
                }

                if (_interactiveOutDataEventHandle != 0)
                {
                    _kernelContext.Syscall.CloseHandle(_interactiveOutDataEventHandle);
                }
            }

            Horizon horizon = _kernelContext.Device.System;

            horizon.AppletState.IndirectLayerHandles.Delete(_indirectLayerHandle);
        }
        [CommandCmif(1)]
        // IsCompleted() -> bool
        public ResultCode IsCompleted(ServiceCtx context)
        {
            context.ResponseData.Write(_real == null || _real.Exited);

            return ResultCode.Success;
        }

        [CommandCmif(25)]
        // Terminate()
        public ResultCode Terminate(ServiceCtx context)
        {
            if (_real != null)
            {
                RealAppletManager.RequestFinish(_real);
            }

            return ResultCode.Success;
        }

        [CommandCmif(120)]
        // GetLibraryAppletInfo() -> nn::am::service::LibraryAppletInfo
        public ResultCode GetLibraryAppletInfo(ServiceCtx context)
        {
            context.ResponseData.Write((uint)_appletId);
            context.ResponseData.Write((uint)_mode);

            return ResultCode.Success;
        }

        [CommandCmif(170)] // 20.0.0+
        // () -> handle<copy>
        public ResultCode Unknown170(ServiceCtx context)
        {
            // An event the caller waits on together with the "launchable" one before it starts
            // the applet. Never signalled here: the launch always goes through.
            _unknown170Event ??= new KEvent(_kernelContext);

            if (_unknown170EventHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_unknown170Event.ReadableEvent, out _unknown170EventHandle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_unknown170EventHandle);

            return ResultCode.Success;
        }
    }
}
