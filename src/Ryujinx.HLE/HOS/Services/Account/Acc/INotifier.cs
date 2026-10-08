namespace Ryujinx.HLE.HOS.Services.Account.Acc
{
    // nn::account::detail::INotifier: an event raised when users are added, opened, or when a
    // profile changes. Nothing of the sort happens behind the HOME Menu's back here.
    class INotifier : IpcService
    {
        private readonly Ryujinx.HLE.HOS.Services.StubEvent _event;

        public INotifier(ServiceCtx context)
        {
            _event = new Ryujinx.HLE.HOS.Services.StubEvent(context);
        }

        [CommandCmif(0)]
        // GetSystemEvent() -> handle<copy>
        public ResultCode GetSystemEvent(ServiceCtx context)
        {
            _event.ReplyWithHandle(context);

            return ResultCode.Success;
        }
    }
}
