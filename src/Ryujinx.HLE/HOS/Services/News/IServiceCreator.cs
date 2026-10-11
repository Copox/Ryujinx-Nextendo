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
        // [Nextendo, HOME Menu work]
        [CommandCmif(10100)]
        // PostLocalNews(buffer<bytes, 5>)
        public ResultCode PostLocalNews(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(30200)]
        // IsSystemUpdateRequired() -> bool
        public ResultCode IsSystemUpdateRequired(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(40101)] // 3.0.0+
        // RequestAutoSubscription(u64)
        public ResultCode RequestAutoSubscription(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        public INewsService() { }
        [CommandCmif(30201)]
        // (u32 format, u32 semantics) -> bool: can this console show news data of that version? The
        // channel page asks it for each article of a channel; no answer (false) made it say an
        // update was needed. The news Nextendo serves is of a version this firmware shows.
        public ResultCode IsVersionSupported(ServiceCtx context)
        {
            context.ResponseData.Write(true);

            return ResultCode.Success;
        }

        [CommandCmif(30500)] // 20.0.0+
        // (buffer) -> object, an event holder: Get() -> handle<copy>
        public ResultCode Unknown30500(ServiceCtx context)
        {
            // The HOME Menu's news page keeps the returned interface and calls into it: a bare
            // success leaves it with a null pointer.
            // The address to download (ptr buffer) and the memory it goes to (copy handle).
            string url = "";

            if (context.Request.PtrBuff.Count > 0)
            {
                byte[] raw = new byte[System.Math.Min(context.Request.PtrBuff[0].Size, 0x400UL)];

                context.Memory.Read(context.Request.PtrBuff[0].Position, raw);

                int end = System.Array.IndexOf(raw, (byte)0);

                url = System.Text.Encoding.ASCII.GetString(raw, 0, end < 0 ? raw.Length : end);
            }

            Ryujinx.HLE.HOS.Kernel.Memory.KTransferMemory memory = null;

            if (context.Request.HandleDesc != null && context.Request.HandleDesc.ToCopy.Length > 0)
            {
                memory = context.Process.HandleTable.GetObject<Ryujinx.HLE.HOS.Kernel.Memory.KTransferMemory>(context.Request.HandleDesc.ToCopy[0]);
            }

            MakeObject(context, new IDownloadContext(context, url, memory));

            CloseReceivedHandles(context);

            return ResultCode.Success;
        }
    }

    // [Nextendo, HOME Menu work] A download the news page starts by itself (the channel list,
    // a channel's picture): fetched from Nextendo in the background, deciphered, written in the
    // memory the page gave; the event says it is over. Nothing to be had: over, empty (the page
    // shows "could not load"; left without an answer it waited for ever and the menu froze).
    class IDownloadContext : IpcService
    {
        private readonly Ryujinx.HLE.HOS.Kernel.Threading.KEvent _done;
        private int _doneHandle;
        private long _size;

        public IDownloadContext(ServiceCtx context, string url, Ryujinx.HLE.HOS.Kernel.Memory.KTransferMemory memory)
        {
            _done = new Ryujinx.HLE.HOS.Kernel.Threading.KEvent(context.Device.System.KernelContext);

            System.Threading.Tasks.Task.Run(() =>
            {
                byte[] data = url.Length > 0 && memory != null ? NextendoNews.Download(url) : null;

                if (data != null && (ulong)data.Length <= memory.Size)
                {
                    memory.Creator.CpuMemory.Write(memory.Address, data);
                    _size = data.Length;
                }

                Ryujinx.Common.Logging.Logger.Notice.Print(Ryujinx.Common.Logging.LogClass.Service, $"[news] download {url} -> {_size} bytes");

                _done.WritableEvent.Signal();
            });
        }

        [CommandCmif(0)]
        // Get() -> handle<copy>
        public ResultCode Get(ServiceCtx context)
        {
            if (_doneHandle == 0 &&
                context.Process.HandleTable.GenerateHandle(_done.ReadableEvent, out _doneHandle) != Ryujinx.Horizon.Common.Result.Success)
            {
                throw new System.InvalidOperationException("Out of handles!");
            }

            context.Response.HandleDesc = Ryujinx.HLE.HOS.Ipc.IpcHandleDesc.MakeCopy(_doneHandle);

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        // Cancel()
        public ResultCode Cancel(ServiceCtx context)
        {
            _done.WritableEvent.Signal();

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // GetResult(): over (GetSize says what came; an error here made the menu abort).
        public ResultCode GetResult(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(3)]
        // GetSize() -> u64
        public ResultCode GetSize(ServiceCtx context)
        {
            context.ResponseData.Write(_size);

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
