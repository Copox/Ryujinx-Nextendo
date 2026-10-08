namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.SystemAppletProxy
{
    // nn::am::service::ILockAccessor: a system-wide lock (HOME button, capture button...) the
    // HOME Menu takes and releases. Nothing else competes for it here, so it is always free.
    class ILockAccessor : IpcService
    {
        private readonly Ryujinx.HLE.HOS.Services.StubEvent _event;
        private bool _locked;

        public ILockAccessor(ServiceCtx context)
        {
            _event = new Ryujinx.HLE.HOS.Services.StubEvent(context);

            // The event is the "lock is free" signal: the HOME Menu waits on it before
            // trying to lock (seen in its code), so it starts signalled and is raised again
            // on every unlock.
            _event.Signal();
        }

        [CommandCmif(1)]
        // TryLock(bool returnHandle) -> (bool locked, handle<copy>)
        public ResultCode TryLock(ServiceCtx context)
        {
            bool returnHandle = context.RequestData.ReadBoolean();

            bool acquired = !_locked;

            _locked = true;

            if (returnHandle)
            {
                _event.ReplyWithHandle(context);
            }

            context.ResponseData.Write(acquired);

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // Unlock()
        public ResultCode Unlock(ServiceCtx context)
        {
            _locked = false;
            _event.Signal();

            return ResultCode.Success;
        }

        [CommandCmif(3)]
        // GetEvent() -> handle<copy>
        public ResultCode GetEvent(ServiceCtx context)
        {
            _event.ReplyWithHandle(context);

            return ResultCode.Success;
        }

        [CommandCmif(4)] // 9.0.0+
        // IsLocked() -> bool
        public ResultCode IsLocked(ServiceCtx context)
        {
            context.ResponseData.Write(_locked);

            return ResultCode.Success;
        }
    }
}
