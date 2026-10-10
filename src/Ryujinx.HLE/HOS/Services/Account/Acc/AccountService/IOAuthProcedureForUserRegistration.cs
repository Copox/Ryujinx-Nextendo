using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.HLE.HOS.Services.Account.Acc.AsyncContext;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Ryujinx.HLE.HOS.Services.Account.Acc.AccountService
{
    /// <summary>
    /// [Nextendo, HOME Menu work] "Link a Nintendo Account" when a user is imported (PlayerSelect):
    /// the account linked is the Nextendo account. The sign-in happens in the browser of the
    /// computer (the emulator's own Nextendo OAuth sign-in), when the applet applies the answer of
    /// its web page; the user made is then bound to that account, with its nickname and picture.
    /// </summary>
    class IOAuthProcedureForUserRegistration : IpcService
    {
        private const string RequestUrl = "https://nextendo.network/connect/console";
        private const string CallbackUri = "nintendo://nextendo.link/callback";

        // 2124-0291: the Nintendo Account signed in already has a user on this console. Read in
        // PlayerSelect (SceneApplyLoginResponse): this answer to the sign-in shows "This Nintendo
        // Account is already being used on this console" before the confirmation, like a Switch.
        private const int AlreadyLinkedDescription = 291;

        private bool _signedIn;
        private UserId _created;

        public IOAuthProcedureForUserRegistration() { }

        private static IAsyncContext Async(ServiceCtx context, Func<Task<ResultCode>> work)
        {
            KEvent asyncEvent = new(context.Device.System.KernelContext);
            AsyncExecution execution = new(asyncEvent);

            execution.Initialize(10 * 60 * 1000, async _ => execution.Result = await work());

            return new IAsyncContext(execution);
        }

        [CommandCmif(0)]
        // PrepareAsync() -> object<nn::account::detail::IAsyncContext>
        public ResultCode PrepareAsync(ServiceCtx context)
        {
            MakeObject(context, Async(context, () => Task.FromResult(ResultCode.Success)));

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        // GetRequest() -> (buffer<nn::account::RequestUrl, 0x1a>, buffer<nn::account::CallbackUri, 0x1a>)
        public ResultCode GetRequest(ServiceCtx context)
        {
            WriteText(context, 0, RequestUrl);
            WriteText(context, 1, CallbackUri);

            return ResultCode.Success;
        }

        private static void WriteText(ServiceCtx context, int index, string text)
        {
            if (context.Request.RecvListBuff.Count <= index)
            {
                return;
            }

            byte[] bytes = new byte[Math.Min(0x1000, (int)Math.Max(context.Request.RecvListBuff[index].Size, (ulong)text.Length + 1))];

            Encoding.ASCII.GetBytes(text).AsSpan(0, Math.Min(text.Length, bytes.Length - 1)).CopyTo(bytes);
            context.Memory.Write(context.Request.RecvListBuff[index].Position, bytes);
            context.Response.PtrBuff[index] = context.Response.PtrBuff[index].WithSize((ulong)bytes.Length);
        }

        [CommandCmif(2)]
        // ApplyResponse(buffer<bytes, 9>)
        public ResultCode ApplyResponse(ServiceCtx context)
        {
            return SignIn(context.Device.System.AccountManager).GetAwaiter().GetResult();
        }

        [CommandCmif(3)]
        // ApplyResponseAsync(buffer<bytes, 9>) -> object<nn::account::detail::IAsyncContext>
        public ResultCode ApplyResponseAsync(ServiceCtx context)
        {
            AccountManager accounts = context.Device.System.AccountManager;

            MakeObject(context, Async(context, () => SignIn(accounts)));

            return ResultCode.Success;
        }

        [CommandCmif(200)] // 17.0.0+
        // ApplyResponseForUserCreationAsync(buffer<bytes, 9>) -> object<nn::account::detail::IAsyncContext>
        public ResultCode ApplyResponseForUserCreationAsync(ServiceCtx context)
        {
            AccountManager accounts = context.Device.System.AccountManager;

            MakeObject(context, Async(context, () => SignIn(accounts)));

            return ResultCode.Success;
        }

        // The Nextendo sign-in in the browser of the computer.
        private async Task<ResultCode> SignIn(AccountManager accounts)
        {
            // Already signed in by the sign-in page of the console (NextendoLoginApplet).
            if (NextendoAccountLink.SignedIn && NextendoAccount.IsLinked)
            {
                _signedIn = true;

                return AlreadyLinked(accounts) ? (ResultCode)((AlreadyLinkedDescription << 9) | 124) : ResultCode.Success;
            }

            if (NextendoAccountLink.SignIn == null)
            {
                return ResultCode.InternetRequestDenied;
            }

            (bool ok, string error) = await NextendoAccountLink.SignIn();

            Logger.Info?.Print(LogClass.ServiceAcc, $"[acc] user import: Nextendo sign-in {(ok ? "done, " + NextendoAccount.Username : "failed: " + error)}");

            _signedIn = ok && NextendoAccount.IsLinked;

            if (!_signedIn)
            {
                return ResultCode.InternetRequestDenied;
            }

            return AlreadyLinked(accounts) ? (ResultCode)((AlreadyLinkedDescription << 9) | 124) : ResultCode.Success;
        }

        // The Nextendo account signed in already has its user on this emulator.
        private static bool AlreadyLinked(AccountManager accounts)
        {
            foreach (UserProfile profile in accounts.GetAllUsers())
            {
                if (string.Equals(profile.UserId.ToString(), NextendoAccount.ProfileUserId, StringComparison.OrdinalIgnoreCase))
                {
                    NextendoAccountLink.AlreadyRegistered = true;

                    Logger.Info?.Print(LogClass.ServiceAcc, $"[acc] user import: the Nextendo account already has the user {profile.UserId} (2124-0291)");

                    return true;
                }
            }

            return false;
        }

        [CommandCmif(10)]
        // Suspend() -> nn::account::detail::Uuid
        public ResultCode Suspend(ServiceCtx context)
        {
            context.ResponseData.Write(new byte[0x10]);

            return ResultCode.Success;
        }

        [CommandCmif(205)] // 17.0.0+
        // SuspendAfterApplyResponse() -> nn::account::detail::Uuid
        public ResultCode SuspendAfterApplyResponse(ServiceCtx context)
        {
            return Suspend(context);
        }

        [CommandCmif(100)]
        // GetAccountId() -> nn::account::NetworkServiceAccountId
        public ResultCode GetAccountId(ServiceCtx context)
        {
            context.ResponseData.Write(NextendoAccount.Pid);

            return ResultCode.Success;
        }

        [CommandCmif(101)]
        // GetLinkedNintendoAccountId() -> nn::account::NintendoAccountId
        public ResultCode GetLinkedNintendoAccountId(ServiceCtx context)
        {
            context.ResponseData.Write(NextendoAccount.Pid);

            return ResultCode.Success;
        }

        [CommandCmif(102)]
        // GetNickname() -> nn::account::Nickname (0x21 bytes)
        public ResultCode GetNickname(ServiceCtx context)
        {
            byte[] nickname = new byte[0x21];

            Encoding.UTF8.GetBytes(NextendoAccount.Username ?? "").AsSpan(0, Math.Min(Encoding.UTF8.GetByteCount(NextendoAccount.Username ?? ""), 0x20)).CopyTo(nickname);
            context.ResponseData.Write(nickname);

            return ResultCode.Success;
        }

        [CommandCmif(103)]
        // GetProfileImage(buffer<bytes, 6>) -> u32 size
        public ResultCode GetProfileImage(ServiceCtx context)
        {
            return WriteImage(context);
        }

        [CommandCmif(104)] // 18.0.0+
        // GetProfileLargeImage(buffer<bytes, 6>) -> u32 size
        public ResultCode GetProfileLargeImage(ServiceCtx context)
        {
            return WriteImage(context);
        }

        private static ResultCode WriteImage(ServiceCtx context)
        {
            byte[] image = NextendoAccountLink.Picture?.Invoke() ?? [];
            ulong room = context.Request.ReceiveBuff.Count > 0 ? context.Request.ReceiveBuff[0].Size : 0;

            if ((ulong)image.Length > room)
            {
                image = [];
            }

            if (image.Length > 0)
            {
                context.Memory.Write(context.Request.ReceiveBuff[0].Position, image);
            }

            context.ResponseData.Write((uint)image.Length);

            return ResultCode.Success;
        }

        [CommandCmif(210)] // 17.0.0+
        // IsProfileAvailable() -> bool
        public ResultCode IsProfileAvailable(ServiceCtx context)
        {
            context.ResponseData.Write(_signedIn);

            return ResultCode.Success;
        }

        [CommandCmif(110)]
        // RegisterUserAsync() -> object<nn::account::detail::IAsyncContext>
        public ResultCode RegisterUserAsync(ServiceCtx context)
        {
            MakeObject(context, Async(context, () => Task.FromResult(Register(context))));

            return ResultCode.Success;
        }

        [CommandCmif(220)] // 17.0.0+
        // RegisterUserAsyncWithoutProfile() -> object<nn::account::detail::IAsyncContext>
        public ResultCode RegisterUserAsyncWithoutProfile(ServiceCtx context)
        {
            return RegisterUserAsync(context);
        }

        [CommandCmif(221)] // 17.0.0+
        // RegisterUserWithProfileAsync(...) -> object<nn::account::detail::IAsyncContext>
        public ResultCode RegisterUserWithProfileAsync(ServiceCtx context)
        {
            return RegisterUserAsync(context);
        }

        [CommandCmif(230)] // 18.0.0+
        // RegisterUserWithLargeImageProfileAsync(...) -> object<nn::account::detail::IAsyncContext>
        public ResultCode RegisterUserWithLargeImageProfileAsync(ServiceCtx context)
        {
            return RegisterUserAsync(context);
        }

        // The user bound to the Nextendo account: made here, unless the account already has
        // one on this emulator (then that one is given, and the import says so).
        private ResultCode Register(ServiceCtx context)
        {
            if (!_signedIn)
            {
                return ResultCode.InternetRequestDenied;
            }

            AccountManager accounts = context.Device.System.AccountManager;

            foreach (UserProfile profile in accounts.GetAllUsers())
            {
                if (string.Equals(profile.UserId.ToString(), NextendoAccount.ProfileUserId, StringComparison.OrdinalIgnoreCase))
                {
                    _created = profile.UserId;
                    NextendoAccountLink.AlreadyRegistered = true;

                    Logger.Info?.Print(LogClass.ServiceAcc, $"[acc] user import: the account already has the user {profile.UserId}");

                    // The answer that makes the import say "this account is already linked to a
                    // user of this console" (a description of the account module, 2124-xxxx).
                    int description = AlreadyLinkedDescription;

                    return (ResultCode)((description << 9) | 124);
                }
            }

            UserId userId = new(Guid.NewGuid().ToString("N"));
            byte[] image = NextendoAccountLink.Picture?.Invoke() ?? [];

            accounts.AddUser(string.IsNullOrEmpty(NextendoAccount.Username) ? "Nextendo" : NextendoAccount.Username, image, userId);
            NextendoAccount.SetProfileUserId(userId.ToString());
            NextendoAccountLink.AlreadyRegistered = false;
            _created = userId;

            Logger.Info?.Print(LogClass.ServiceAcc, $"[acc] user import: user {userId} made for {NextendoAccount.Username}");

            return ResultCode.Success;
        }

        [CommandCmif(111)]
        // GetUid() -> nn::account::Uid
        public ResultCode GetUid(ServiceCtx context)
        {
            _created.Write(context.ResponseData);

            return ResultCode.Success;
        }
    }
}
