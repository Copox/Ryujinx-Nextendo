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
    /// [Nextendo, HOME Menu work] "Link a Nintendo Account" for a user of the console (the initial
    /// setup after "Add User", the user's settings): the account linked is the Nextendo account,
    /// signed in to in the browser of the computer as for the user import. The user is then the
    /// one bound to that account.
    /// </summary>
    class IOAuthProcedureForNintendoAccountLinkage : IpcService
    {
        private const string RequestUrl = "https://nextendo.network/connect/console";
        private const string CallbackUri = "nintendo://nextendo.link/callback";

        // 2124-0291: the account signed in already has another user on this console.
        private const int AlreadyLinkedDescription = 291;

        private readonly UserId _userId;

        public IOAuthProcedureForNintendoAccountLinkage(UserId userId)
        {
            _userId = userId;
        }

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

        [CommandCmif(100)]
        // GetRequestWithTheme(i32) -> (buffer<nn::account::RequestUrl, 0x1a>, buffer<nn::account::CallbackUri, 0x1a>)
        public ResultCode GetRequestWithTheme(ServiceCtx context)
        {
            return GetRequest(context);
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
            return Link(context.Device.System.AccountManager).GetAwaiter().GetResult();
        }

        [CommandCmif(3)]
        // ApplyResponseAsync(buffer<bytes, 9>) -> object<nn::account::detail::IAsyncContext>
        public ResultCode ApplyResponseAsync(ServiceCtx context)
        {
            AccountManager accounts = context.Device.System.AccountManager;

            MakeObject(context, Async(context, () => Link(accounts)));

            return ResultCode.Success;
        }

        // The Nextendo sign-in, then the user bound to the account.
        private async Task<ResultCode> Link(AccountManager accounts)
        {
            bool signedIn = NextendoAccountLink.SignedIn && NextendoAccount.IsLinked;

            if (!signedIn)
            {
                if (NextendoAccountLink.SignIn == null)
                {
                    return ResultCode.InternetRequestDenied;
                }

                (bool ok, string error) = await NextendoAccountLink.SignIn();

                Logger.Info?.Print(LogClass.ServiceAcc, $"[acc] link: Nextendo sign-in {(ok ? "done, " + NextendoAccount.Username : "failed: " + error)}");

                signedIn = ok && NextendoAccount.IsLinked;
            }

            if (!signedIn)
            {
                return ResultCode.InternetRequestDenied;
            }

            // The account already has another user here: the console says so (2124-0291).
            foreach (UserProfile profile in accounts.GetAllUsers())
            {
                if (profile.UserId != _userId && string.Equals(profile.UserId.ToString(), NextendoAccount.ProfileUserId, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info?.Print(LogClass.ServiceAcc, $"[acc] link: the Nextendo account already has the user {profile.UserId} (2124-0291)");

                    return (ResultCode)((AlreadyLinkedDescription << 9) | 124);
                }
            }

            NextendoAccount.SetProfileUserId(_userId.ToString());

            Logger.Info?.Print(LogClass.ServiceAcc, $"[acc] link: user {_userId} linked to {NextendoAccount.Username}");

            return ResultCode.Success;
        }

        [CommandCmif(10)]
        // Suspend() -> nn::account::detail::Uuid
        public ResultCode Suspend(ServiceCtx context)
        {
            context.ResponseData.Write(new byte[0x10]);

            return ResultCode.Success;
        }

        [CommandCmif(101)]
        // IsNetworkServiceAccountReplaced() -> bool
        public ResultCode IsNetworkServiceAccountReplaced(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }
    }
}
