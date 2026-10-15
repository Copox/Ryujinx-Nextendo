namespace Ryujinx.HLE.HOS.Services.Account.Acc.AccountService
{
    class IManagerForApplication : IpcService
    {
        private readonly ManagerServer _managerServer;

        public IManagerForApplication(UserId userId)
        {
            // [Nextendo] Plusieurs comptes : l'utilisateur que le jeu ouvre decide du compte
            // Nextendo actif, comme une console ou chaque utilisateur porte son propre compte.
            // Un profil local sans compte ne change rien (il reste hors ligne).
            Ryujinx.Common.Configuration.NextendoAccount.ActivateForProfile(userId.ToString());

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

        [CommandCmif(130)]
        // GetNintendoAccountUserResourceCacheForApplication() -> (nn::account::NintendoAccountId, nn::account::nas::NasUserBaseForApplication, buffer<bytes, 6>)
        public ResultCode GetNintendoAccountUserResourceCacheForApplication(ServiceCtx context)
        {
            return _managerServer.GetNintendoAccountUserResourceCacheForApplication(context);
        }

        [CommandCmif(150)] // 6.0.0+
        // CreateAuthorizationRequest(u32, handle<copy>, buffer<nn::account::NintendoAccountAuthorizationRequestParameters, 0x19>) -> object<nn::account::nas::IAuthorizationRequest>
        public ResultCode CreateAuthorizationRequest(ServiceCtx context)
        {
            ResultCode resultCode = _managerServer.CreateAuthorizationRequest(context, out IAuthorizationRequest authorizationRequest);

            if (resultCode == ResultCode.Success)
            {
                MakeObject(context, authorizationRequest);
            }

            return resultCode;
        }

        [CommandCmif(160)] // 5.0.0+
        // StoreOpenContext()
        public ResultCode StoreOpenContext(ServiceCtx context)
        {
            return _managerServer.StoreOpenContext(context);
        }

        [CommandCmif(170)] // 6.0.0+
        // LoadNetworkServiceLicenseKindAsync() -> object<nn::account::detail::IAsyncNetworkServiceLicenseKindContext>
        public ResultCode LoadNetworkServiceLicenseKindAsync(ServiceCtx context)
        {
            ResultCode resultCode = _managerServer.LoadNetworkServiceLicenseKindAsync(context, out IAsyncNetworkServiceLicenseKindContext asyncContext);

            if (resultCode == ResultCode.Success)
            {
                MakeObject(context, asyncContext);
            }

            return resultCode;
        }
    }
}
