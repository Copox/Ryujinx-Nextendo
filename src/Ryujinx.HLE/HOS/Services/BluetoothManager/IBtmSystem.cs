namespace Ryujinx.HLE.HOS.Services.BluetoothManager
{
    [Service("btm:sys")]
    class IBtmSystem : IpcService
    {
        public IBtmSystem(ServiceCtx context) { }

        [CommandCmif(0)]
        // GetCore() -> object<nn::btm::IBtmSystemCore>
        public ResultCode GetCore(ServiceCtx context)
        {
            MakeObject(context, new IBtmSystemCore());

            return ResultCode.Success;
        }
    }

    // Bluetooth settings as seen by the HOME Menu; its commands are answered as they turn up.
    class IBtmSystemCore : IpcService
    {
        public IBtmSystemCore() { }
        [CommandCmif(3)]
        // GetPairedGamepadCount() -> u8
        public ResultCode GetPairedGamepadCount(ServiceCtx context)
        {
            context.ResponseData.Write((byte)0);

            return ResultCode.Success;
        }

        [CommandCmif(6)]
        // IsRadioEnabled() -> bool
        public ResultCode IsRadioEnabled(ServiceCtx context)
        {
            context.ResponseData.Write(true);

            return ResultCode.Success;
        }

        private Ryujinx.HLE.HOS.Services.StubEvent _radioEvent;
        private Ryujinx.HLE.HOS.Services.StubEvent _gamepadPairingEvent;

        [CommandCmif(7)]
        // AcquireRadioEvent() -> (bool, handle<copy>)
        public ResultCode AcquireRadioEvent(ServiceCtx context)
        {
            _radioEvent ??= new Ryujinx.HLE.HOS.Services.StubEvent(context);
            _radioEvent.ReplyWithHandle(context);
            context.ResponseData.Write(true);

            return ResultCode.Success;
        }

        [CommandCmif(8)]
        // AcquireGamepadPairingEvent() -> (bool, handle<copy>)
        public ResultCode AcquireGamepadPairingEvent(ServiceCtx context)
        {
            _gamepadPairingEvent ??= new Ryujinx.HLE.HOS.Services.StubEvent(context);
            _gamepadPairingEvent.ReplyWithHandle(context);
            context.ResponseData.Write(true);

            return ResultCode.Success;
        }

        [CommandCmif(9)]
        // IsGamepadPairingStarted() -> bool
        public ResultCode IsGamepadPairingStarted(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }
    }
}
