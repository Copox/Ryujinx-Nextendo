using Ryujinx.Common;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Account.Acc.AccountService;

namespace Ryujinx.HLE.HOS.Services.Account.Acc
{
    [Service("acc:su", AccountServiceFlag.Administrator)] // Max Sessions: 8
    class IAccountServiceForAdministrator : IpcService
    {
        // [Nextendo, HOME Menu work] The command took this number in 19.0.0.
        [CommandCmif(52)] // 19.0.0+
        // TrySelectUserWithoutInteraction(bool) -> nn::account::Uid
        public ResultCode TrySelectUserWithoutInteraction19(ServiceCtx context)
        {
            return _applicationServiceServer.TrySelectUserWithoutInteraction(context);
        }

        private readonly ApplicationServiceServer _applicationServiceServer;

        public IAccountServiceForAdministrator(ServiceCtx context, AccountServiceFlag serviceFlag)
        {
            _applicationServiceServer = new ApplicationServiceServer(serviceFlag);
        }

        [CommandCmif(0)]
        // GetUserCount() -> i32
        public ResultCode GetUserCount(ServiceCtx context)
        {
            return _applicationServiceServer.GetUserCountImpl(context);
        }

        [CommandCmif(1)]
        // GetUserExistence(nn::account::Uid) -> bool
        public ResultCode GetUserExistence(ServiceCtx context)
        {
            return _applicationServiceServer.GetUserExistenceImpl(context);
        }

        [CommandCmif(2)]
        // ListAllUsers() -> array<nn::account::Uid, 0xa>
        public ResultCode ListAllUsers(ServiceCtx context)
        {
            return _applicationServiceServer.ListAllUsers(context);
        }

        [CommandCmif(3)]
        // ListOpenUsers() -> array<nn::account::Uid, 0xa>
        public ResultCode ListOpenUsers(ServiceCtx context)
        {
            return _applicationServiceServer.ListOpenUsers(context);
        }

        [CommandCmif(4)]
        // GetLastOpenedUser() -> nn::account::Uid
        public ResultCode GetLastOpenedUser(ServiceCtx context)
        {
            return _applicationServiceServer.GetLastOpenedUser(context);
        }

        [CommandCmif(5)]
        // GetProfile(nn::account::Uid) -> object<nn::account::profile::IProfile>
        public ResultCode GetProfile(ServiceCtx context)
        {
            ResultCode resultCode = _applicationServiceServer.GetProfile(context, out IProfile iProfile);

            if (resultCode == ResultCode.Success)
            {
                MakeObject(context, iProfile);
            }

            return resultCode;
        }

        [CommandCmif(50)]
        // IsUserRegistrationRequestPermitted(pid) -> bool
        public ResultCode IsUserRegistrationRequestPermitted(ServiceCtx context)
        {
            // NOTE: pid is unused.

            return _applicationServiceServer.IsUserRegistrationRequestPermitted(context);
        }

        [CommandCmif(51)]
        // TrySelectUserWithoutInteraction(bool) -> nn::account::Uid
        public ResultCode TrySelectUserWithoutInteraction(ServiceCtx context)
        {
            return _applicationServiceServer.TrySelectUserWithoutInteraction(context);
        }

        [CommandCmif(102)]
        // GetBaasAccountManagerForSystemService(nn::account::Uid) -> object<nn::account::baas::IManagerForApplication>
        public ResultCode GetBaasAccountManagerForSystemService(ServiceCtx context)
        {
            ResultCode resultCode = _applicationServiceServer.CheckUserId(context, out UserId userId);

            if (resultCode != ResultCode.Success)
            {
                return resultCode;
            }

            MakeObject(context, new IManagerForSystemService(userId));

            // Doesn't occur in our case.
            // return ResultCode.NullObject;

            return ResultCode.Success;
        }

        [CommandCmif(140)] // 6.0.0+
        // ListQualifiedUsers() -> array<nn::account::Uid, 0xa>
        public ResultCode ListQualifiedUsers(ServiceCtx context)
        {
            return _applicationServiceServer.ListQualifiedUsers(context);
        }

        [CommandCmif(205)]
        // GetProfileEditor(nn::account::Uid) -> object<nn::account::profile::IProfileEditor>
        public ResultCode GetProfileEditor(ServiceCtx context)
        {
            UserId userId = context.RequestData.ReadStruct<UserId>();

            if (!context.Device.System.AccountManager.TryGetUser(userId, out UserProfile userProfile))
            {
                Logger.Warning?.Print(LogClass.ServiceAcc, $"User 0x{userId} not found!");

                return ResultCode.UserNotFound;
            }

            MakeObject(context, new IProfileEditor(userProfile));

            // Doesn't occur in our case.
            // return ResultCode.NullObject;

            return ResultCode.Success;
        }
        [CommandCmif(100)]
        // GetUserRegistrationNotifier() -> object<nn::account::detail::INotifier>
        public ResultCode GetUserRegistrationNotifier(ServiceCtx context)
        {
            MakeObject(context, new INotifier(context));

            return ResultCode.Success;
        }

        [CommandCmif(101)]
        // GetUserStateChangeNotifier() -> object<nn::account::detail::INotifier>
        public ResultCode GetUserStateChangeNotifier(ServiceCtx context)
        {
            MakeObject(context, new INotifier(context));

            return ResultCode.Success;
        }

        [CommandCmif(103)]
        // GetBaasUserAvailabilityChangeNotifier() -> object<nn::account::detail::INotifier>
        public ResultCode GetBaasUserAvailabilityChangeNotifier(ServiceCtx context)
        {
            MakeObject(context, new INotifier(context));

            return ResultCode.Success;
        }

        [CommandCmif(104)]
        // GetProfileUpdateNotifier() -> object<nn::account::detail::INotifier>
        public ResultCode GetProfileUpdateNotifier(ServiceCtx context)
        {
            MakeObject(context, new INotifier(context));

            return ResultCode.Success;
        }

        [CommandCmif(250)]
        // GetBaasAccountAdministrator(nn::account::Uid) -> object<nn::account::baas::IAdministrator>
        public ResultCode GetBaasAccountAdministrator(ServiceCtx context)
        {
            ResultCode resultCode = _applicationServiceServer.CheckUserId(context, out UserId userId);

            if (resultCode != ResultCode.Success)
            {
                return resultCode;
            }

            // IAdministrator starts with the commands of IManagerForSystemService; the HOME Menu
            // opens it when the user page is entered.
            MakeObject(context, new IManagerForSystemService(userId));

            return ResultCode.Success;
        }
        [CommandCmif(112)]
        // LoadSaveDataThumbnail(nn::account::Uid, nn::ncm::ApplicationId) -> (u32 size, buffer<bytes, 6>)
        public ResultCode LoadSaveDataThumbnail(ServiceCtx context)
        {
            // No thumbnail is kept for a save: the user selector then shows the user's own
            // picture in full, instead of an empty thumbnail with the picture in a corner.
            return ResultCode.InvalidArgument;
        }
    }
}
