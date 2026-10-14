namespace Ryujinx.HLE.HOS.Services.Account.Acc.AccountService
{
    class IManagerForSystemService : IpcService
    {
        private readonly ManagerServer _managerServer;
        private readonly UserId _userId;

        public IManagerForSystemService(UserId userId)
        {
            _managerServer = new ManagerServer(userId);
            _userId = userId;
        }

        [CommandCmif(0)]
        // CheckAvailability()
        public ResultCode CheckAvailability(ServiceCtx context)
        {
            // A local user has no network service account (no Nextendo account linked): 2124-0200
            // makes the HOME Menu show it as a local user ("Link Nintendo Account", "Delete User").
            if (Ryujinx.HLE.HOS.HomeMenuStatus.Running && !IsNextendoUser())
            {
                return (ResultCode)((200 << 9) | 124);
            }

            return _managerServer.CheckAvailability(context);
        }

        [CommandCmif(1)]
        // GetAccountId() -> nn::account::NetworkServiceAccountId
        public ResultCode GetAccountId(ServiceCtx context)
        {
            return _managerServer.GetAccountId(context);
        }

        [CommandCmif(2)]
        // EnsureIdTokenCacheAsync() -> object<nn::account::detail::IAsyncContext>
        public ResultCode EnsureIdTokenCacheAsync(ServiceCtx context)
        {
            ResultCode resultCode = _managerServer.EnsureIdTokenCacheAsync(context, out IAsyncContext asyncContext);

            if (resultCode == ResultCode.Success)
            {
                MakeObject(context, asyncContext);
            }

            return resultCode;
        }

        [CommandCmif(3)]
        // LoadIdTokenCacheDeprecated() -> (u32 id_token_cache_size, buffer<bytes, 6>)
        public ResultCode LoadIdTokenCacheDeprecated(ServiceCtx context)
        {
            return _managerServer.LoadIdTokenCache(context);
        }

        [CommandCmif(4)] // 19.0.0+
        // LoadIdTokenCache() -> (u32 id_token_cache_size, buffer<bytes, 6>)
        public ResultCode LoadIdTokenCache(ServiceCtx context)
        {
            return _managerServer.LoadIdTokenCache(context);
        }
        [CommandCmif(136)] // 19.0.0+
        // GetNintendoAccountUserResourceCache() -> (u64 id, buffer<0x800, type 6>)
        public ResultCode GetNintendoAccountUserResourceCache(ServiceCtx context)
        {
            // [Nextendo, HOME Menu work] The Nintendo Account the user page shows in its header
            // ("Nintendo Account (e-mail)").
            Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.ServiceAcc,
                $"[acc] 136 recv {context.Request.ReceiveBuff.Count} ({(context.Request.ReceiveBuff.Count > 0 ? context.Request.ReceiveBuff[0].Size : 0):X}), recvlist {context.Request.RecvListBuff.Count} ({(context.Request.RecvListBuff.Count > 0 ? context.Request.RecvListBuff[0].Size : 0):X}), in {context.RequestData.BaseStream.Length} bytes");

            if (context.Request.ReceiveBuff.Count == 0)
            {
                return ResultCode.Success;
            }

            ulong position = context.Request.ReceiveBuff[0].Position;
            byte[] cache = new byte[System.Math.Min(0x800UL, context.Request.ReceiveBuff[0].Size)];

            context.Memory.Write(position, cache);

            // The account itself (nn::account::nas::NasUserBase) goes to the out-pointer buffer of
            // the caller, whose descriptor the copy of the request does not hold (it ends with the
            // raw data): it is read from the message the caller wrote. The user page shows the
            // name at offset 1 ("Nintendo Account (...)"): there, the e-mail of the account.
            string email = _userId.ToString().Equals(Ryujinx.Common.Configuration.NextendoAccount.ProfileUserId, System.StringComparison.OrdinalIgnoreCase)
                ? Ryujinx.Common.Configuration.NextendoAccountLink.Email()
                : "";

            // The request also holds a map buffer: the server loop left the out-pointer one to us.
            // It is written to the pointer buffer of the server, and declared in the reply: the
            // kernel copies it to the buffer of the caller.
            if (email.Length > 0 && context.Request.ServerPointerBuffer != 0)
            {
                const int NasUserBaseSize = 0x270; // the caller keeps it in a 0x278-byte object, after the u64 id
                ulong baseAddress = context.Request.ServerPointerBuffer;

                context.Response.PtrBuff.Clear();
                context.Response.PtrBuff.Add(new Ryujinx.HLE.HOS.Ipc.IpcPtrBuffDesc(baseAddress, 0, NasUserBaseSize));

                byte[] user = new byte[NasUserBaseSize];
                byte[] text = System.Text.Encoding.UTF8.GetBytes(email);

                System.Array.Copy(text, 0, user, 1, System.Math.Min(text.Length, System.Math.Min(0x7F, user.Length - 2)));
                context.Memory.Write(baseAddress, user);
            }

            context.ResponseData.Write(email.Length > 0 ? Ryujinx.Common.Configuration.NextendoAccount.Pid : 0UL);

            return ResultCode.Success;
        }

        // The first receive-list (C) descriptor of the request, read in the message as the caller
        // wrote it (in its thread-local storage).
        private static bool ClientReceiveBuffer(ServiceCtx context, out ulong address, out ulong size)
        {
            address = 0;
            size = 0;

            try
            {
                ulong tls = context.Thread.TlsAddress;
                uint word0 = context.Memory.Read<uint>(tls);
                uint word1 = context.Memory.Read<uint>(tls + 4);
                uint listType = (word1 >> 10) & 0xF;

                if (listType < 2)
                {
                    return false;
                }

                ulong offset = 8;

                if ((word1 >> 31) != 0)
                {
                    uint handles = context.Memory.Read<uint>(tls + offset);

                    offset += 4 + ((handles & 1) != 0 ? 8UL : 0UL) + (((handles >> 1) & 0xF) + ((handles >> 5) & 0xF)) * 4UL;
                }

                offset += ((word0 >> 16) & 0xF) * 8UL;                                              // X
                offset += (((word0 >> 20) & 0xF) + ((word0 >> 24) & 0xF) + ((word0 >> 28) & 0xF)) * 12UL; // A, B, W
                offset += (word1 & 0x3FF) * 4UL;                                                      // raw data

                uint listOffset = (word1 >> 20) & 0x7FF;

                if (listOffset != 0)
                {
                    offset = listOffset * 4UL;
                }

                ulong descriptor = context.Memory.Read<ulong>(tls + offset);

                address = descriptor & 0xFFFFFFFFFFFFUL;
                size = descriptor >> 48;

                return address != 0 && size != 0;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        private bool IsNextendoUser()
        {

            return Ryujinx.Common.Configuration.NextendoAccount.IsLinked &&
                string.Equals(_userId.ToString(), Ryujinx.Common.Configuration.NextendoAccount.ProfileUserId, System.StringComparison.OrdinalIgnoreCase);
        }

        [CommandCmif(200)]
        // IsRegistered() -> bool
        public ResultCode IsRegistered(ServiceCtx context)
        {
            context.ResponseData.Write(IsNextendoUser());

            return ResultCode.Success;
        }

        [CommandCmif(201)]
        // RegisterAsync() -> object<nn::account::detail::IAsyncContext>
        public ResultCode RegisterAsync(ServiceCtx context)
        {
            // [Nextendo, HOME Menu work] A user just made (the initial setup, "Add User") is put on
            // the network service: nothing to do here, done at once (a user becomes a Nextendo one
            // by signing in to its account).
            Ryujinx.HLE.HOS.Kernel.Threading.KEvent asyncEvent = new(context.Device.System.KernelContext);
            AsyncContext.AsyncExecution asyncExecution = new(asyncEvent);

            asyncExecution.Initialize(1000, _ => System.Threading.Tasks.Task.CompletedTask);

            MakeObject(context, new IAsyncContext(asyncExecution));

            return ResultCode.Success;
        }

        [CommandCmif(251)]
        // CreateProcedureToLinkWithNintendoAccount() -> object<nn::account::nas::IOAuthProcedureForNintendoAccountLinkage>
        public ResultCode CreateProcedureToLinkWithNintendoAccount(ServiceCtx context)
        {
            MakeObject(context, new IOAuthProcedureForNintendoAccountLinkage(_userId));

            return ResultCode.Success;
        }

        [CommandCmif(250)]
        // IsLinkedWithNintendoAccount() -> bool
        public ResultCode IsLinkedWithNintendoAccount(ServiceCtx context)
        {
            // Only the user of the Nextendo account the emulator is signed in with is linked; the
            // others are local users.
            context.ResponseData.Write(IsNextendoUser());

            return ResultCode.Success;
        }
    }
}
