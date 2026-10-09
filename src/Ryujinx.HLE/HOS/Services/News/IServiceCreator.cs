namespace Ryujinx.HLE.HOS.Services.News
{
    [Service("news:a")]
    [Service("news:c")]
    [Service("news:m")]
    [Service("news:p")]
    [Service("news:v")]
    class IServiceCreator : IpcService
    {
        public IServiceCreator(ServiceCtx context)
        {
            // The news of Nextendo, fetched once in the background, in the console's language.
            string language = Ryujinx.HLE.HOS.SystemState.SystemStateMgr.LanguageCodes[(int)context.Device.System.State.DesiredSystemLanguage];

            NextendoNews.Start(language);
        }

        // The HOME Menu calls these by position and keeps each returned interface: every one
        // of them has to hand back an object, a bare success leaves it with a null pointer.

        [CommandCmif(0)]
        // CreateNewsService() -> object<nn::news::detail::ipc::INewsService>
        public ResultCode CreateNewsService(ServiceCtx context)
        {
            MakeObject(context, new INewsService());

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        // CreateNewlyArrivedEventHolder() -> object<nn::news::detail::ipc::INewlyArrivedEventHolder>
        public ResultCode CreateNewlyArrivedEventHolder(ServiceCtx context)
        {
            MakeObject(context, new INewsEventHolder(context));

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // CreateNewsDataService() -> object<nn::news::detail::ipc::INewsDataService>
        public ResultCode CreateNewsDataService(ServiceCtx context)
        {
            MakeObject(context, new INewsDataService());

            return ResultCode.Success;
        }

        [CommandCmif(3)]
        // CreateNewsDatabaseService() -> object<nn::news::detail::ipc::INewsDatabaseService>
        public ResultCode CreateNewsDatabaseService(ServiceCtx context)
        {
            MakeObject(context, new INewsDatabaseService());

            return ResultCode.Success;
        }

        [CommandCmif(4)]
        // CreateOverwriteEventHolder() -> object<nn::news::detail::ipc::IOverwriteEventHolder>
        public ResultCode CreateOverwriteEventHolder(ServiceCtx context)
        {
            MakeObject(context, new INewsEventHolder(context));

            return ResultCode.Success;
        }
    }

    class INewsService : IpcService
    {
        public INewsService() { }
        [CommandCmif(30500)] // 20.0.0+
        // (buffer) -> object, an event holder: Get() -> handle<copy>
        public ResultCode Unknown30500(ServiceCtx context)
        {
            // The HOME Menu's news page keeps the returned interface and calls into it: a bare
            // success leaves it with a null pointer.
            MakeObject(context, new INewsEventHolder(context));

            return ResultCode.Success;
        }
    }

    // One article, read as a file: the menu opens it by its row and reads the msgpack.
    class INewsDataService : IpcService
    {
        private byte[] _data = System.Array.Empty<byte>();

        public INewsDataService() { }

        private ResultCode OpenArticle(string newsId)
        {
            string file = NextendoNews.ArticlePath(newsId);

            _data = newsId.Length > 0 && System.IO.File.Exists(file) ? System.IO.File.ReadAllBytes(file) : System.Array.Empty<byte>();

            Ryujinx.Common.Logging.Logger.Notice.Print(Ryujinx.Common.Logging.LogClass.Service, $"[news] open \"{newsId}\" -> {_data.Length} bytes");

            return ResultCode.Success;
        }

        private static string TextAt(ServiceCtx context, ulong position, ulong size)
        {
            byte[] raw = new byte[System.Math.Min(size, 0x80UL)];

            context.Memory.Read(position, raw);

            int end = System.Array.IndexOf(raw, (byte)0);

            return System.Text.Encoding.ASCII.GetString(raw, 0, end < 0 ? raw.Length : System.Math.Min(end, 0x18));
        }

        [CommandCmif(0)]
        // Open(buffer<string, 9> name)
        public ResultCode Open(ServiceCtx context)
        {
            string name = context.Request.PtrBuff.Count > 0 ? TextAt(context, context.Request.PtrBuff[0].Position, context.Request.PtrBuff[0].Size) : "";

            return OpenArticle(name);
        }

        [CommandCmif(1)]
        [CommandCmif(1001)] // 6.0.0+
        // OpenWithNewsRecord(NewsRecord): the row comes as a buffer, its id leads it.
        public ResultCode OpenWithNewsRecord(ServiceCtx context)
        {
            string name = "";

            if (context.Request.PtrBuff.Count > 0 && context.Request.PtrBuff[0].Size > 0)
            {
                name = TextAt(context, context.Request.PtrBuff[0].Position, context.Request.PtrBuff[0].Size);
            }
            else if (context.Request.SendBuff.Count > 0 && context.Request.SendBuff[0].Size > 0)
            {
                name = TextAt(context, context.Request.SendBuff[0].Position, context.Request.SendBuff[0].Size);
            }
            else
            {
                byte[] raw = context.RequestData.ReadBytes((int)System.Math.Min(0x18, context.RequestData.BaseStream.Length - context.RequestData.BaseStream.Position));
                int end = System.Array.IndexOf(raw, (byte)0);

                name = System.Text.Encoding.ASCII.GetString(raw, 0, end < 0 ? raw.Length : end);
            }

            return OpenArticle(name);
        }

        [CommandCmif(2)]
        // Read(u64 offset) -> (u64 size, buffer<bytes, 6>)
        public ResultCode Read(ServiceCtx context)
        {
            long offset = context.RequestData.ReadInt64();
            ulong position = context.Request.ReceiveBuff[0].Position;
            long room = (long)context.Request.ReceiveBuff[0].Size;
            long size = System.Math.Max(0, System.Math.Min(room, _data.Length - offset));

            if (size > 0)
            {
                context.Memory.Write(position, new System.ReadOnlySpan<byte>(_data, (int)offset, (int)size));
            }

            context.ResponseData.Write(size);

            return ResultCode.Success;
        }

        [CommandCmif(3)]
        // GetSize() -> u64
        public ResultCode GetSize(ServiceCtx context)
        {
            context.ResponseData.Write((long)_data.Length);

            return ResultCode.Success;
        }
    }

    // INewlyArrivedEventHolder and IOverwriteEventHolder: one command, Get() -> handle<copy>.
    class INewsEventHolder : IpcService
    {
        private readonly Ryujinx.HLE.HOS.Kernel.Threading.KEvent _event;
        private int _eventHandle;

        public INewsEventHolder(ServiceCtx context)
        {
            _event = new Ryujinx.HLE.HOS.Kernel.Threading.KEvent(context.Device.System.KernelContext);
        }

        [CommandCmif(0)]
        // Get() -> handle<copy>
        public ResultCode Get(ServiceCtx context)
        {
            // Never signalled: no news arrives while the emulator runs.
            if (_eventHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_event.ReadableEvent, out _eventHandle) != Ryujinx.Horizon.Common.Result.Success)
                {
                    throw new System.InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = Ryujinx.HLE.HOS.Ipc.IpcHandleDesc.MakeCopy(_eventHandle);

            return ResultCode.Success;
        }
    }
}
