namespace Ryujinx.HLE.HOS.Services.Caps
{
    [Service("caps:a")]
    class IAlbumAccessorService : IpcService
    {
        // [Nextendo, HOME Menu work] The album storage is the microSD card.
        [CommandCmif(5)]
        // IsAlbumMounted(u8 storage) -> bool
        public ResultCode IsAlbumMounted(ServiceCtx context)
        {
            context.ResponseData.Write(true);

            return ResultCode.Success;
        }

        [CommandCmif(18)] // 6.0.0+
        // GetAppletProgramIdTable(buffer<u64, 0x46>) -> bool: the range of the system applets.
        public ResultCode GetAppletProgramIdTable(ServiceCtx context)
        {
            (ulong position, ulong size) = context.Request.GetBufferType0x22();

            if (size < 0x10)
            {
                context.ResponseData.Write(false);

                return ResultCode.Success;
            }

            context.Memory.Write(position, 0x0100000000001000UL);
            context.Memory.Write(position + 8, 0x0100000000001FFFUL);
            context.ResponseData.Write(true);

            return ResultCode.Success;
        }

        [CommandCmif(401)]
        // GetAutoSavingStorage() -> u8 (1: microSD card)
        public ResultCode GetAutoSavingStorage(ServiceCtx context)
        {
            context.ResponseData.Write((byte)1);

            return ResultCode.Success;
        }

        public IAlbumAccessorService(ServiceCtx context) { }
    }
}
