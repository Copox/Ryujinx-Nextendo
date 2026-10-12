using System;
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
        // [Nextendo, HOME Menu work]
        [CommandCmif(26)] // 18.0.0+
        // HasAccountRestrictedRightsInRunningApplications() -> bool
        public ResultCode HasAccountRestrictedRightsInRunningApplications(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        public IDynamicRightsInterface() { }
    }

    class IReadOnlyApplicationRecordInterface : IpcService
    {
        public IReadOnlyApplicationRecordInterface() { }

        [CommandCmif(3)] // 20.0.0+
        // ListApplicationRecord() -> (s32 count, buffer<nn::ns::ApplicationRecord, 6>)
        public ResultCode ListApplicationRecord(ServiceCtx context)
        {
            // The emulator's library is the console's list of installed titles.
            const int RecordSize = 0x18;

            ulong position = context.Request.ReceiveBuff[0].Position;
            int capacity = (int)(context.Request.ReceiveBuff[0].Size / RecordSize);
            int count = 0;

            foreach (Ryujinx.HLE.HOS.HomeMenuTitle title in Ryujinx.HLE.HOS.HomeMenuLibrary.Titles())
            {
                if (count == capacity)
                {
                    break;
                }

                // [Nextendo] a game card of the library that is not loaded to the console: in the
                // virtual game cards page only
                if (!Ryujinx.HLE.HOS.NextendoVirtualCards.Shown(title))
                {
                    continue;
                }

                ulong record = position + (ulong)(count * RecordSize);

                context.Memory.Write(record + 0x00, title.Id);
                context.Memory.Write(record + 0x08, (byte)3);  // installed
                context.Memory.Write(record + 0x09, (byte)0);
                context.Memory.Write(record + 0x0A, (ushort)0);
                context.Memory.Write(record + 0x0C, 0);
                context.Memory.Write(record + 0x10, (long)(count + 1)); // order of last use

                count++;
            }

Ryujinx.Common.Logging.Logger.Notice.Print(Ryujinx.Common.Logging.LogClass.Service, $"[home] {count} title(s) listed to the HOME Menu");

                        context.ResponseData.Write(count);

            return ResultCode.Success;
        }
    }

    // nn::ns::detail::IAsyncResult of a request that is already over.
    class IAsyncResult : IpcService
    {
        public IAsyncResult() { }

        [CommandCmif(0)]
        // Get()
        public ResultCode Get(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(1)]
        // Cancel()
        public ResultCode Cancel(ServiceCtx context) => ResultCode.Success;
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
        // [Nextendo, HOME Menu work]
        [CommandCmif(708)] // 4.0.0+
        // DisableAutoCommit()
        public ResultCode DisableAutoCommit(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        public IDownloadTaskInterface() { }
        [CommandCmif(707)]
        // EnableAutoCommit()
        public ResultCode EnableAutoCommit(ServiceCtx context)
        {

            return ResultCode.Success;
        }
    }

    class IContentManagementInterface : IpcService
    {
        public IContentManagementInterface() { }

        [CommandCmif(11)]
        // CalculateApplicationOccupiedSize(nn::ncm::ApplicationId) -> nn::ns::ApplicationOccupiedSize
        public ResultCode CalculateApplicationOccupiedSize(ServiceCtx context)
        {
            // [Nextendo, HOME Menu work] "Data Management > Software": the size of the game, its
            // update and its add-on content, as files of the computer, in the console memory.
            ulong id = context.RequestData.ReadUInt64();
            HomeMenuTitle title = HomeMenuLibrary.Find(id);
            byte[] sizes = new byte[0x80];

            if (title != null)
            {
                sizes[0] = 4; // BuiltInUser
                BitConverter.TryWriteBytes(sizes.AsSpan(0x08), title.ApplicationSize);
                BitConverter.TryWriteBytes(sizes.AsSpan(0x10), title.UpdateSize);
                BitConverter.TryWriteBytes(sizes.AsSpan(0x18), title.DlcSize);
            }

            context.ResponseData.Write(sizes);

            return ResultCode.Success;
        }

        [CommandCmif(43)]
        // CheckSdCardMountStatus()
        public ResultCode CheckSdCardMountStatus(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(47)]
        // GetTotalSpaceSize(u8 storageId) -> s64
        public ResultCode GetTotalSpaceSize(ServiceCtx context)
        {
            context.ResponseData.Write(HostDrive(context.RequestData.ReadByte()).Total);

            return ResultCode.Success;
        }

        [CommandCmif(48)]
        // GetFreeSpaceSize(u8 storageId) -> s64
        public ResultCode GetFreeSpaceSize(ServiceCtx context)
        {
            context.ResponseData.Write(HostDrive(context.RequestData.ReadByte()).Free);

            return ResultCode.Success;
        }

        // [Nextendo, HOME Menu work] The storage of the console is a folder of the computer:
        // its size and free space are those of the disk that folder is on (the console memory
        // is the data folder, the microSD card is the "sdcard" folder).
        private static (long Total, long Free) HostDrive(byte storageId)
        {
            try
            {
                string folder = Ryujinx.Common.Configuration.AppDataManager.BaseDirPath;

                if (storageId == 5)
                {
                    string card = System.IO.Path.Combine(folder, "sdcard");

                    if (System.IO.Directory.Exists(card))
                    {
                        // A card folder may be a link to another disk.
                        folder = System.IO.Directory.ResolveLinkTarget(card, true)?.FullName ?? card;
                    }
                }

                System.IO.DriveInfo drive = new(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(folder)));

                return (drive.TotalSize, drive.AvailableFreeSpace);
            }
            catch (System.Exception)
            {
                return (32L * 1024 * 1024 * 1024, 16L * 1024 * 1024 * 1024);
            }
        }
    }

    class IDocumentInterface : IpcService
    {
        public IDocumentInterface() { }
    }
}
