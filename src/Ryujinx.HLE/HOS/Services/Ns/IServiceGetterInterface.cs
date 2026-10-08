namespace Ryujinx.HLE.HOS.Services.Ns
{
    [Service("ns:am2")]
    [Service("ns:ec")]
    [Service("ns:rid")]
    [Service("ns:rt")]
    [Service("ns:web")]
    [Service("ns:ro")]
    [Service("ns:sweb")]
    class IServiceGetterInterface : IpcService
    {
        public IServiceGetterInterface(ServiceCtx context) { }

        [CommandCmif(7996)]
        // GetApplicationManagerInterface() -> object<nn::ns::detail::IApplicationManagerInterface>
        public ResultCode GetApplicationManagerInterface(ServiceCtx context)
        {
            MakeObject(context, new IApplicationManagerInterface(context));

            return ResultCode.Success;
        }

        [CommandCmif(7989)]
        // GetReadOnlyApplicationControlDataInterface() -> object<nn::ns::detail::IReadOnlyApplicationControlDataInterface>
        public ResultCode GetReadOnlyApplicationControlDataInterface(ServiceCtx context)
        {
            MakeObject(context, new IReadOnlyApplicationControlDataInterface(context));

            return ResultCode.Success;
        }
        [CommandCmif(7988)]
        // GetDynamicRightsInterface() -> object<nn::ns::detail::IDynamicRightsInterface>
        public ResultCode GetDynamicRightsInterface(ServiceCtx context)
        {
            MakeObject(context, new IDynamicRightsInterface());

            return ResultCode.Success;
        }

        [CommandCmif(7991)]
        // GetReadOnlyApplicationRecordInterface() -> object<nn::ns::detail::IReadOnlyApplicationRecordInterface>
        public ResultCode GetReadOnlyApplicationRecordInterface(ServiceCtx context)
        {
            MakeObject(context, new IReadOnlyApplicationRecordInterface());

            return ResultCode.Success;
        }

        [CommandCmif(7992)]
        // GetECommerceInterface() -> object<nn::ns::detail::IECommerceInterface>
        public ResultCode GetECommerceInterface(ServiceCtx context)
        {
            MakeObject(context, new IECommerceInterface());

            return ResultCode.Success;
        }

        [CommandCmif(7993)]
        // GetApplicationVersionInterface() -> object<nn::ns::detail::IApplicationVersionInterface>
        public ResultCode GetApplicationVersionInterface(ServiceCtx context)
        {
            MakeObject(context, new IApplicationVersionInterface());

            return ResultCode.Success;
        }

        [CommandCmif(7994)]
        // GetFactoryResetInterface() -> object<nn::ns::detail::IFactoryResetInterface>
        public ResultCode GetFactoryResetInterface(ServiceCtx context)
        {
            MakeObject(context, new IFactoryResetInterface());

            return ResultCode.Success;
        }

        [CommandCmif(7995)]
        // GetAccountProxyInterface() -> object<nn::ns::detail::IAccountProxyInterface>
        public ResultCode GetAccountProxyInterface(ServiceCtx context)
        {
            MakeObject(context, new IAccountProxyInterface());

            return ResultCode.Success;
        }

        [CommandCmif(7997)]
        // GetDownloadTaskInterface() -> object<nn::ns::detail::IDownloadTaskInterface>
        public ResultCode GetDownloadTaskInterface(ServiceCtx context)
        {
            MakeObject(context, new IDownloadTaskInterface());

            return ResultCode.Success;
        }

        [CommandCmif(7998)]
        // GetContentManagementInterface() -> object<nn::ns::detail::IContentManagementInterface>
        public ResultCode GetContentManagementInterface(ServiceCtx context)
        {
            MakeObject(context, new IContentManagementInterface());

            return ResultCode.Success;
        }

        [CommandCmif(7999)]
        // GetDocumentInterface() -> object<nn::ns::detail::IDocumentInterface>
        public ResultCode GetDocumentInterface(ServiceCtx context)
        {
            MakeObject(context, new IDocumentInterface());

            return ResultCode.Success;
        }
    }
    // Sub-interfaces of the application manager opened by the HOME Menu; their commands are
    // answered as they turn up.
    class IDynamicRightsInterface : IpcService
    {
        public IDynamicRightsInterface() { }
    }

    class IReadOnlyApplicationRecordInterface : IpcService
    {
        public IReadOnlyApplicationRecordInterface() { }
    }

    class IECommerceInterface : IpcService
    {
        public IECommerceInterface() { }
    }

    class IApplicationVersionInterface : IpcService
    {
        public IApplicationVersionInterface() { }
    }

    class IFactoryResetInterface : IpcService
    {
        public IFactoryResetInterface() { }
    }

    class IAccountProxyInterface : IpcService
    {
        public IAccountProxyInterface() { }
    }

    class IDownloadTaskInterface : IpcService
    {
        public IDownloadTaskInterface() { }
    }

    class IContentManagementInterface : IpcService
    {
        public IContentManagementInterface() { }
    }

    class IDocumentInterface : IpcService
    {
        public IDocumentInterface() { }
    }
}
