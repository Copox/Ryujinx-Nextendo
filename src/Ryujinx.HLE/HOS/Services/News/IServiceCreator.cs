namespace Ryujinx.HLE.HOS.Services.News
{
    [Service("news:a")]
    [Service("news:c")]
    [Service("news:m")]
    [Service("news:p")]
    [Service("news:v")]
    class IServiceCreator : IpcService
    {
        public IServiceCreator(ServiceCtx context) { }

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

    class INewsDataService : IpcService
    {
        public INewsDataService() { }
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
