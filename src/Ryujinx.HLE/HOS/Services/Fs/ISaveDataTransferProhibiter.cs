namespace Ryujinx.HLE.HOS.Services.Fs
{
    // [Nextendo, HOME Menu work] Held while a save may not be moved to another console; it has
    // no command of its own.
    class ISaveDataTransferProhibiter : IpcService
    {
        public ISaveDataTransferProhibiter() { }
    }
}
