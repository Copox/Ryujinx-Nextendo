using Ryujinx.Common;
using LibHac.Ns;
using Ryujinx.Common.Utilities;

namespace Ryujinx.HLE.HOS.Services.Ns
{
    [Service("ns:am")]
    class IApplicationManagerInterface : IpcService
    {
        // [Nextendo, HOME Menu work]
        [CommandCmif(38)]
        // CheckApplicationLaunchVersion(nn::ncm::ApplicationId): every version may start.
        public ResultCode CheckApplicationLaunchVersion(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(70)] // 3.0.0+
        // ResumeAll()
        public ResultCode ResumeAll(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(512)] // 19.0.0+
        // IsGameCardApplicationRunning() -> bool: there is no game card.
        public ResultCode IsGameCardApplicationRunning(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(906)]
        // IsApplicationUpdateRequested(nn::ncm::ApplicationId) -> (bool, u32): no update is asked for.
        public ResultCode IsApplicationUpdateRequested(ServiceCtx context)
        {
            context.ResponseData.Write(false);
            context.ResponseData.Write(0u);

            return ResultCode.Success;
        }

        private readonly StubEvent _recordUpdateEvent;
        private readonly StubEvent _sdCardMountStatusEvent;
        private readonly StubEvent _gameCardUpdateEvent;
        private readonly StubEvent _gameCardMountFailureEvent;

        public IApplicationManagerInterface(ServiceCtx context)
        {
            _recordUpdateEvent = new StubEvent(context);
            Ryujinx.HLE.HOS.HomeMenuLibrary.Changed += _recordUpdateEvent.Signal;
            _sdCardMountStatusEvent = new StubEvent(context);
            _gameCardUpdateEvent = new StubEvent(context);
            _gameCardMountFailureEvent = new StubEvent(context);
        }

        [CommandCmif(0)]
        // ListApplicationRecord(s32 offset) -> (s32 count, buffer<nn::ns::ApplicationRecord, 6>)
        public ResultCode ListApplicationRecord(ServiceCtx context)
        {
            // No installed application is reported to the HOME Menu yet.
            context.ResponseData.Write(0);

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // GetApplicationRecordUpdateSystemEvent() -> handle<copy>
        public ResultCode GetApplicationRecordUpdateSystemEvent(ServiceCtx context) => _recordUpdateEvent.ReplyWithHandle(context);

        [CommandCmif(44)]
        // GetSdCardMountStatusChangedEvent() -> handle<copy>
        public ResultCode GetSdCardMountStatusChangedEvent(ServiceCtx context) => _sdCardMountStatusEvent.ReplyWithHandle(context);

        [CommandCmif(52)]
        // GetGameCardUpdateDetectionEvent() -> handle<copy>
        public ResultCode GetGameCardUpdateDetectionEvent(ServiceCtx context) => _gameCardUpdateEvent.ReplyWithHandle(context);

        [CommandCmif(505)]
        // GetGameCardMountFailureEvent() -> handle<copy>
        public ResultCode GetGameCardMountFailureEvent(ServiceCtx context) => _gameCardMountFailureEvent.ReplyWithHandle(context);

        [CommandCmif(400)]
        // GetApplicationControlData(u8, u64) -> (unknown<4>, buffer<unknown, 6>)
        public ResultCode GetApplicationControlData(ServiceCtx context)
        {
#pragma warning disable IDE0059 // Remove unnecessary value assignment
            byte source = (byte)context.RequestData.ReadInt64();
            ulong titleId = context.RequestData.ReadUInt64();
#pragma warning restore IDE0059

            ulong position = context.Request.ReceiveBuff[0].Position;

            ApplicationControlProperty nacp = context.Device.Processes.GetProcess(context.ClientProcessId).ApplicationControlProperties;

            context.Memory.Write(position, SpanHelpers.AsByteSpan(ref nacp).ToArray());

            return ResultCode.Success;
        }
        [CommandCmif(1706)] // 20.0.0+
        // GetApplicationView(buffer<nn::ncm::ApplicationId, 5>) -> buffer<nn::ns::ApplicationView, 6>
        public ResultCode GetApplicationView(ServiceCtx context)
        {
            const int ViewSize = 0x58;

            ulong inPosition = context.Request.SendBuff[0].Position;
            int count = (int)(context.Request.SendBuff[0].Size / 8);
            ulong outPosition = context.Request.ReceiveBuff[0].Position;
            int capacity = (int)(context.Request.ReceiveBuff[0].Size / ViewSize);

            for (int i = 0; i < count && i < capacity; i++)
            {
                ulong applicationId = context.Memory.Read<ulong>(inPosition + (ulong)(i * 8));
                ulong view = outPosition + (ulong)(i * ViewSize);

                // A title that is fully on the console: nothing downloading, nothing to update.
                context.Memory.Write(view, new byte[ViewSize]);
                context.Memory.Write(view + 0x00, applicationId);
                context.Memory.Write(view + 0x08, 0u);         // version
                context.Memory.Write(view + 0x0C, 0x401f17u | GameCardViewFlags(applicationId));  // can be launched, has all its contents
            }

            return ResultCode.Success;
        }
        [CommandCmif(1704)] // 20.0.0+
        // GetApplicationViewWithPromotionInfo(buffer<nn::ncm::ApplicationId, 5>) -> buffer<nn::ns::ApplicationViewWithPromotionInfo, 6>
        public ResultCode GetApplicationViewWithPromotionInfo(ServiceCtx context)
        {
            const int EntrySize = 0x78; // the 0x58 bytes view, then 0x20 bytes of promotion info

            ulong inPosition = context.Request.SendBuff[0].Position;
            int count = (int)(context.Request.SendBuff[0].Size / 8);
            ulong outPosition = context.Request.ReceiveBuff[0].Position;
            int capacity = (int)(context.Request.ReceiveBuff[0].Size / EntrySize);

            for (int i = 0; i < count && i < capacity; i++)
            {
                ulong applicationId = context.Memory.Read<ulong>(inPosition + (ulong)(i * 8));
                ulong entry = outPosition + (ulong)(i * EntrySize);

                context.Memory.Write(entry, new byte[EntrySize]);
                context.Memory.Write(entry + 0x00, applicationId);
                context.Memory.Write(entry + 0x08, 0u);
                context.Memory.Write(entry + 0x0C, 0x401f17u | GameCardViewFlags(applicationId));
            }

            return ResultCode.Success;
        }

        // [Nextendo, HOME Menu work] The view of the virtual game card loaded to the console: on a
        // game card (bits 6 and 7), the HOME Menu shows the game card's picture by its name.
        private static uint GameCardViewFlags(ulong applicationId) =>
            applicationId != 0 && applicationId == Ryujinx.HLE.HOS.NextendoVirtualCards.Inserted && Ryujinx.HLE.HOS.NextendoVirtualCards.Enabled ? 0xC0u : 0;

        [CommandCmif(2050)] // 6.0.0+
        // GetApplicationRightsOnClient(u32 flags, nn::ncm::ApplicationId, nn::account::Uid) -> (s32 count, buffer<nn::ns::ApplicationRightsOnClient, 6>)
        public ResultCode GetApplicationRightsOnClient(ServiceCtx context)
        {
            context.RequestData.ReadUInt64(); // flags
            ulong applicationId = context.RequestData.ReadUInt64();
            ulong uidLow = context.RequestData.ReadUInt64();
            ulong uidHigh = context.RequestData.ReadUInt64();

            ulong position = context.Request.ReceiveBuff[0].Position;

            // One entry: the title belongs to this console, for every user.
            context.Memory.Write(position, new byte[0x20]);
            context.Memory.Write(position + 0x00, applicationId);
            context.Memory.Write(position + 0x08, uidLow);
            context.Memory.Write(position + 0x10, uidHigh);
            context.Memory.Write(position + 0x18, (byte)1);
            context.Memory.Write(position + 0x19, (byte)0);

            context.ResponseData.Write(1);

            return ResultCode.Success;
        }
        private StubEvent _virtualCardEvent;

        [CommandCmif(4072)] // 20.0.0+
        // (uid) -> handle<copy>: an event the virtual game card page waits on.
        public ResultCode GetVirtualGameCardEvent(ServiceCtx context)
        {
            _virtualCardEvent ??= new StubEvent(context);
            _virtualCardEvent.Signal();
            _virtualCardEvent.ReplyWithHandle(context);

            return ResultCode.Success;
        }

        [CommandCmif(4042)] // 20.0.0+
        [CommandCmif(4093)] // 20.0.0+
        [CommandCmif(4008)] // 20.0.0+, with a transfer memory
        // The other requests the HOME Menu makes the same way (found in its code: they all go
        // through its helper for "request, then wait on the event, then read the result").
        [CommandCmif(2366)]
        [CommandCmif(2367)]
        [CommandCmif(4007)]
        [CommandCmif(4009)]
        [CommandCmif(4010)]
        [CommandCmif(4011)]
        [CommandCmif(4013)]
        [CommandCmif(4028)]
        [CommandCmif(4029)]
        [CommandCmif(4030)]
        [CommandCmif(4033)]
        [CommandCmif(4038)]
        [CommandCmif(4060)]
        [CommandCmif(4069)]
        [CommandCmif(4071)]
        [CommandCmif(4073)]
        [CommandCmif(4076)]
        [CommandCmif(4079)]
        [CommandCmif(4087)]
        [CommandCmif(4080)]
        [CommandCmif(4086)]
        // Asked by the virtual game card page of the HOME Menu, which keeps the returned object
        // (an asynchronous request): left without one, the page followed a null pointer.
        // -> (handle<copy>, object)
        public ResultCode RequestVirtualGameCardOperation(ServiceCtx context)
        {

            // A transfer memory handed with the request is let go at once: kept, it held the
            // menu's own heap, which then could not shrink (fatal 2001-0106).
            if (context.Request.HandleDesc != null)
            {
                foreach (int handle in context.Request.HandleDesc.ToCopy)
                {
                    context.Device.System.KernelContext.Syscall.CloseHandle(handle);
                }
            }

            // [Nextendo, HOME Menu work] No virtual game card is held here: the request is over
            // at once, with nothing in it.
            _virtualCardEvent ??= new StubEvent(context);
            _virtualCardEvent.Signal();
            _virtualCardEvent.ReplyWithHandle(context);

            byte[] value = new byte[8];
            long vgcCommand = context.RequestData.BaseStream.Position >= 8
                ? System.BitConverter.ToInt64(((System.IO.MemoryStream)context.RequestData.BaseStream).GetBuffer(), (int)context.RequestData.BaseStream.Position - 8) : -1;

            // [Nextendo, HOME Menu work] The game cards of the library, as virtual game cards (the
            // option on): the list (4008), one of them loaded to this console (4076).
            if (vgcCommand == 4008 && Ryujinx.HLE.HOS.NextendoVirtualCards.Enabled)
            {
                value = Ryujinx.HLE.HOS.NextendoVirtualCards.Entries();
            }

            if (vgcCommand == 4076 && Ryujinx.HLE.HOS.NextendoVirtualCards.Enabled && context.Request.SendBuff.Count > 0)
            {
                int count = System.BitConverter.ToInt32(((System.IO.MemoryStream)context.RequestData.BaseStream).GetBuffer(), (int)context.RequestData.BaseStream.Position);
                Ipc.IpcBuffDesc entries = context.Request.SendBuff[0];

                Ryujinx.HLE.HOS.NextendoVirtualCards.Load(context.Memory.GetSpan(entries.Position, (int)entries.Size), count);
            }

            // the welcome of the page seen (4073, after its last dialog): only once from now on
            if (vgcCommand == 4073 && !Ryujinx.HLE.HOS.NextendoVirtualCards.WelcomeSeen)
            {
                Ryujinx.HLE.HOS.NextendoVirtualCards.WelcomeSeen = true;
            }

            // the virtual game card ejected from this console (4038)
            if (vgcCommand == 4038 && Ryujinx.HLE.HOS.NextendoVirtualCards.Enabled)
            {
                Ryujinx.HLE.HOS.NextendoVirtualCards.Eject();
            }

            MakeObject(context, new IAsyncValue(value));

            return ResultCode.Success;
        }

        [CommandCmif(4022)] // 20.0.0+
        [CommandCmif(4023)] // 20.0.0+
        [CommandCmif(4053)] // 20.0.0+
        [CommandCmif(4054)] // 20.0.0+
        [CommandCmif(4088)] // 20.0.0+
        // Asked by the virtual game cards page, without arguments.
        public ResultCode GetVirtualGameCardValue(ServiceCtx context)
        {
            long command = System.BitConverter.ToInt64(((System.IO.MemoryStream)context.RequestData.BaseStream).GetBuffer(), 8);
            byte[] value = new byte[8];

            context.ResponseData.Write(value);

            return ResultCode.Success;
        }

        private StubEvent _controlDataDownloadEvent;

        [CommandCmif(412)] // 20.0.0+
        // RequestDownloadApplicationControlData(u8, nn::ncm::ApplicationId, u64) -> (handle<copy>, object<nn::ns::detail::IAsyncResult>)
        public ResultCode RequestDownloadApplicationControlData(ServiceCtx context)
        {
            // Nothing is downloaded: what is known of a title is already served by the control
            // data interface. The request completes at once.
            _controlDataDownloadEvent ??= new StubEvent(context);
            _controlDataDownloadEvent.Signal();
            _controlDataDownloadEvent.ReplyWithHandle(context);

            MakeObject(context, new IAsyncResult());

            return ResultCode.Success;
        }

        [CommandCmif(5)]
        // DeleteApplicationCompletely(nn::ncm::ApplicationId)
        public ResultCode DeleteApplicationCompletely(ServiceCtx context)
        {
            // [Nextendo, HOME Menu work] "Delete Software": the game, its update and its add-on
            // content leave the computer (to the recycle bin); the saves stay, as on a Switch.
            ulong id = context.RequestData.ReadUInt64();
            HomeMenuTitle title = HomeMenuLibrary.Find(id);

            if (title == null)
            {
                return ResultCode.Success;
            }

            bool done = HomeMenuLibrary.Delete?.Invoke(title) ?? false;

            Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.ServiceNs, $"[home] delete software {title.Name} ({id:X16}): {(done ? "done" : "not done")}");

            return ResultCode.Success;
        }

        [CommandCmif(201)]
        // DeleteUserSaveDataAll(nn::account::Uid) -> object<nn::ns::detail::IProgressMonitorForDeleteUserSaveDataAll>
        public ResultCode DeleteUserSaveDataAll(ServiceCtx context)
        {
            Account.Acc.UserId userId = context.RequestData.ReadStruct<Account.Acc.UserId>();

            context.Device.System.AccountManager.DeleteUserSaveData(userId);

            MakeObject(context, new IProgressMonitorForDeleteUserSaveDataAll());

            return ResultCode.Success;
        }

        // [Nextendo, HOME Menu work] "Online License Settings" of a user (User Settings): the
        // choice is kept with the emulator data.
        private static string OnlineLicensePath => System.IO.Path.Combine(Ryujinx.Common.Configuration.AppDataManager.BaseDirPath, "nextendo_online_license.txt");

        private StubEvent _onlineLicenseEvent;

        private static System.Collections.Generic.HashSet<string> OnlineLicenseUsers()
        {
            try
            {
                return System.IO.File.Exists(OnlineLicensePath) ? [.. System.IO.File.ReadAllLines(OnlineLicensePath)] : [];
            }
            catch (System.Exception)
            {
                return [];
            }
        }

        [CommandCmif(4031)]
        // SetOnlineLicenseEnabled(u64 enabled, nn::account::Uid) -> (handle<copy>, object<nn::ns::detail::IAsyncResult>)
        public ResultCode SetOnlineLicenseEnabled(ServiceCtx context)
        {
            bool enabled = (context.RequestData.ReadUInt64() & 1) != 0;
            string userId = context.RequestData.ReadStruct<Account.Acc.UserId>().ToString();

            System.Collections.Generic.HashSet<string> users = OnlineLicenseUsers();

            if (enabled ? users.Add(userId) : users.Remove(userId))
            {
                try
                {
                    System.IO.File.WriteAllLines(OnlineLicensePath, users);
                }
                catch (System.Exception)
                {
                    // kept for this session only
                }
            }

            _onlineLicenseEvent ??= new StubEvent(context);
            _onlineLicenseEvent.Signal();
            _onlineLicenseEvent.ReplyWithHandle(context);

            MakeObject(context, new IAsyncResult());

            return ResultCode.Success;
        }

        [CommandCmif(4032)]
        // IsOnlineLicenseEnabled(nn::account::Uid) -> bool
        public ResultCode IsOnlineLicenseEnabled(ServiceCtx context)
        {
            string userId = context.RequestData.ReadStruct<Account.Acc.UserId>().ToString();

            context.ResponseData.Write(OnlineLicenseUsers().Contains(userId));

            return ResultCode.Success;
        }
    }
}
