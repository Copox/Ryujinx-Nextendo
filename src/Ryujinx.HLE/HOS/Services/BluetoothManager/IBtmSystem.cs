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
    }
}
