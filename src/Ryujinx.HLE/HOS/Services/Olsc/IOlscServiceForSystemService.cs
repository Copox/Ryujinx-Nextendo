namespace Ryujinx.HLE.HOS.Services.Olsc
{
    [Service("olsc:s")] // 4.0.0+
    class IOlscServiceForSystemService : IpcService
    {
        // [Nextendo, HOME Menu work]
        [CommandCmif(200)]
        // GetDataTransferPolicy(u64 application_id) -> (u8, u8)
        public ResultCode GetDataTransferPolicy(ServiceCtx context)
        {
            context.RequestData.ReadUInt64();

            context.ResponseData.Write((byte)0);
            context.ResponseData.Write((byte)0);

            return ResultCode.Success;
        }

        public IOlscServiceForSystemService(ServiceCtx context) { }

        [CommandCmif(0)]
        // GetTransferTaskListController() -> object<nn::olsc::srv::ITransferTaskListController>
        public ResultCode GetTransferTaskListController(ServiceCtx context)
        {
            MakeObject(context, new ITransferTaskListController());

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        // GetRemoteStorageController() -> object<nn::olsc::srv::IRemoteStorageController>
        public ResultCode GetRemoteStorageController(ServiceCtx context)
        {
            MakeObject(context, new IRemoteStorageController());

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // GetDaemonController() -> object<nn::olsc::srv::IDaemonController>
        public ResultCode GetDaemonController(ServiceCtx context)
        {
            MakeObject(context, new IDaemonController());

            return ResultCode.Success;
        }

        [CommandCmif(10000)]
        // CloneService() -> object<nn::olsc::srv::IOlscServiceForSystemService>
        public ResultCode CloneService(ServiceCtx context)
        {
            MakeObject(context, new IOlscServiceForSystemService(context));

            return ResultCode.Success;
        }
    }

    // Cloud save controllers the HOME Menu opens; their commands are answered as they turn up.
    class ITransferTaskListController : IpcService
    {
        public ITransferTaskListController() { }

        [CommandCmif(5)]
        [CommandCmif(9)]
        // GetNativeHandleHolder() -> object<nn::olsc::srv::INativeHandleHolder>
        public ResultCode GetNativeHandleHolder(ServiceCtx context)
        {
            MakeObject(context, new INativeHandleHolder(context));

            return ResultCode.Success;
        }
    }

    class INativeHandleHolder : IpcService
    {
        private readonly Ryujinx.HLE.HOS.Kernel.Threading.KEvent _event;
        private int _eventHandle;

        public INativeHandleHolder(ServiceCtx context)
        {
            _event = new Ryujinx.HLE.HOS.Kernel.Threading.KEvent(context.Device.System.KernelContext);
        }

        [CommandCmif(0)]
        // GetNativeHandle() -> handle<copy>
        public ResultCode GetNativeHandle(ServiceCtx context)
        {
            // Never signalled: there is no transfer task to report.
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

    class IRemoteStorageController : IpcService
    {
        public IRemoteStorageController() { }
    }

    class IDaemonController : IpcService
    {
        public IDaemonController() { }
    }
}
