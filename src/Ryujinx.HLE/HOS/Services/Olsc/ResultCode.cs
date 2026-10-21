namespace Ryujinx.HLE.HOS.Services.Olsc
{
    enum ResultCode
    {
        ModuleId = 179,
        ErrorCodeShift = 9,

        Success = 0,

        NullArgument = (100 << ErrorCodeShift) | ModuleId,
        NotInitialized = (101 << ErrorCodeShift) | ModuleId,

        // [Nextendo] ACNH 3.0.3 (0x5b7bf0) lit « pas de cache » dans la plage 2179-1200..1399 de
        // GetSaveDataBackupInfoCache et ABANDONNE sur tout autre code : seul celui-ci peut sortir.
        SaveDataBackupInfoCacheNotFound = (1200 << ErrorCodeShift) | ModuleId,

        // [Nextendo] Échecs des opérations asynchrones : le jeu les affiche par nn::err (2179-09xx),
        // sans traitement particulier (seuls 2124-0200..0269 et 2179-0010 en ont un).
        NextendoUnavailable = (900 << ErrorCodeShift) | ModuleId,
        NextendoNetworkError = (901 << ErrorCodeShift) | ModuleId,
        NextendoServerRefused = (902 << ErrorCodeShift) | ModuleId,
        NextendoNoLocalSave = (903 << ErrorCodeShift) | ModuleId,
        NextendoInvalidIsland = (904 << ErrorCodeShift) | ModuleId,
        NextendoNotDone = (905 << ErrorCodeShift) | ModuleId,
    }
}
