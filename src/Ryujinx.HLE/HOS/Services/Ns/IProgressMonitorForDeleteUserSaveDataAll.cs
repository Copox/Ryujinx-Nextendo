namespace Ryujinx.HLE.HOS.Services.Ns
{
    // [Nextendo, HOME Menu work] "Delete User": the saves of the user are deleted at once, so
    // the progress the HOME Menu follows is already finished.
    class IProgressMonitorForDeleteUserSaveDataAll : IpcService
    {
        private StubEvent _event;

        [CommandCmif(0)]
        // GetSystemEvent() -> handle<copy>
        public ResultCode GetSystemEvent(ServiceCtx context)
        {
            if (_event == null)
            {
                _event = new StubEvent(context);
                _event.Signal();
            }

            return _event.ReplyWithHandle(context);
        }

        [CommandCmif(1)]
        // IsFinished() -> bool
        public ResultCode IsFinished(ServiceCtx context)
        {
            context.ResponseData.Write(true);

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // GetResult()
        public ResultCode GetResult(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(10)]
        // GetProgress() -> nn::ns::ProgressForDeleteUserSaveDataAll
        public ResultCode GetProgress(ServiceCtx context)
        {
            context.ResponseData.Write(new byte[0x28]);

            return ResultCode.Success;
        }
    }
}
