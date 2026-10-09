namespace Ryujinx.HLE.HOS.Services.Account.Acc.AccountService
{
    class IManagerForSystemService : IpcService
    {
        private readonly ManagerServer _managerServer;

        public IManagerForSystemService(UserId userId)
        {
            _managerServer = new ManagerServer(userId);
        }

        [CommandCmif(0)]
        // CheckAvailability()
        public ResultCode CheckAvailability(ServiceCtx context)
        {
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
                $"[acc] 136: recv {context.Request.ReceiveBuff.Count} ({(context.Request.ReceiveBuff.Count > 0 ? context.Request.ReceiveBuff[0].Size : 0):X}), recvlist {context.Request.RecvListBuff.Count} ({(context.Request.RecvListBuff.Count > 0 ? context.Request.RecvListBuff[0].Size : 0):X}), in {context.RequestData.BaseStream.Length} bytes");

            if (context.Request.ReceiveBuff.Count == 0)
            {
                return ResultCode.Success;
            }

            ulong position = context.Request.ReceiveBuff[0].Position;
            byte[] cache = new byte[System.Math.Min(0x800UL, context.Request.ReceiveBuff[0].Size)];

            if (System.Environment.GetEnvironmentVariable("NEXTENDO_NA_PROBE") == "1")
            {
                for (int at = 0; at + 0x10 <= cache.Length; at += 0x10)
                {
                    System.Text.Encoding.ASCII.GetBytes($"o{at:X3}@x.fr").CopyTo(cache, at);
                }
            }

            context.Memory.Write(position, cache);

            context.ResponseData.Write(System.Environment.GetEnvironmentVariable("NEXTENDO_NA_PROBE") == "1" ? 0x1122334455667788UL : 0UL);

            return ResultCode.Success;
        }

        [CommandCmif(200)]
        // IsRegistered() -> bool
        public ResultCode IsRegistered(ServiceCtx context)
        {
            context.ResponseData.Write(true);

            return ResultCode.Success;
        }

        [CommandCmif(250)]
        // IsLinkedWithNintendoAccount() -> bool
        public ResultCode IsLinkedWithNintendoAccount(ServiceCtx context)
        {
            // The local user is the Nextendo account the emulator is signed in with.
            context.ResponseData.Write(true);

            return ResultCode.Success;
        }
    }
}
