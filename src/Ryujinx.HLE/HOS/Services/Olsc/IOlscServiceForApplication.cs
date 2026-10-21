using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Account.Acc;
using System;
using System.Threading.Tasks;

namespace Ryujinx.HLE.HOS.Services.Olsc
{
    /// <summary>
    /// olsc:u. [Nextendo] Les commandes et leurs types viennent du client d'ACNH 3.0.3 (proxy
    /// 0x47f9090, emballages 0x5b7a60..0x5b7ed0, requêtes 0x5b9828..0x5bb004) : c'est ce qui fait
    /// marcher l'archivage de l'île depuis le menu du jeu (touche − à l'écran titre). Les jeux sans
    /// archivage Nextendo gardent le comportement d'avant pour 13 et 14.
    /// </summary>
    [Service("olsc:u")] // 10.0.0+
    class IOlscServiceForApplication : IpcService
    {
        private bool _initialized;

        public IOlscServiceForApplication(ServiceCtx context) { }

        private static ulong TitleId(ServiceCtx context) => context.Device.Processes.ActiveApplication?.ProgramId ?? 0;

        private static string Key(ServiceCtx context, UserId userId) => NextendoIslandSync.Key(TitleId(context), userId.ToString());

        [CommandCmif(0)]
        // Initialize(pid)
        public ResultCode Initialize(ServiceCtx context)
        {
            _initialized = true;

            Logger.Stub?.PrintStub(LogClass.ServiceOlsc);

            return ResultCode.Success;
        }

        [CommandCmif(10)]
        // VerifySaveDataBackupLicenseAsync(nn::account::Uid) -> object<IAsyncResult>
        public ResultCode VerifySaveDataBackupLicenseAsync(ServiceCtx context)
        {
            UserId userId = context.RequestData.ReadStruct<UserId>();

            if (!_initialized)
            {
                return ResultCode.NotInitialized;
            }

            ulong titleId = TitleId(context);

            // Chez Nintendo : l'abonnement. Ici : un compte Nextendo utilisable ET un serveur qui
            // archive ce jeu (tant qu'il ne le fait pas, l'archivage ne peut pas être activé, au
            // lieu d'échouer ensuite à chaque sauvegarde).
            MakeObject(context, new IOlscAsyncResult(context, async () =>
            {
                if (!NextendoIslandSync.IsStrict(titleId))
                {
                    return ResultCode.NextendoUnavailable;
                }

                (NextendoIslandCloud.Outcome rc, _) = await NextendoIslandCloud.GetMetaAsync(titleId);

                return NextendoOlscCloud.ToResult(rc);
            }));

            Logger.Info?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: VerifySaveDataBackupLicenseAsync {userId}");

            return ResultCode.Success;
        }

        [CommandCmif(13)]
        // GetSaveDataBackupSetting(nn::account::Uid) -> u8
        public ResultCode GetSaveDataBackupSetting(ServiceCtx context)
        {
            UserId userId = context.RequestData.ReadStruct<UserId>();

            if (!_initialized)
            {
                return ResultCode.NotInitialized;
            }

            if (userId.IsNull)
            {
                return ResultCode.NullArgument;
            }

            // ACNH ne compare qu'à 1 (activé) : 0x5b7b00 et ses 9 appelants.
            context.ResponseData.Write(NextendoIslandSync.Get(Key(context, userId)).Setting);

            return ResultCode.Success;
        }

        [CommandCmif(14)]
        // SetSaveDataBackupSettingEnabled(bool, nn::account::Uid)
        public ResultCode SetSaveDataBackupSettingEnabled(ServiceCtx context)
        {
            bool enabled = context.RequestData.ReadUInt64() != 0;
            UserId userId = context.RequestData.ReadStruct<UserId>();

            if (!_initialized)
            {
                return ResultCode.NotInitialized;
            }

            if (userId.IsNull)
            {
                return ResultCode.NullArgument;
            }

            NextendoIslandSync.Update(Key(context, userId), e => e.Setting = enabled ? (byte)1 : (byte)2);

            Logger.Info?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: SetSaveDataBackupSettingEnabled {userId} {enabled}");

            return ResultCode.Success;
        }

        [CommandCmif(15)]
        // SetCustomData(nn::account::Uid, buffer<bytes, 5>)
        public ResultCode SetCustomData(ServiceCtx context)
        {
            UserId userId = context.RequestData.ReadStruct<UserId>();

            if (!_initialized)
            {
                return ResultCode.NotInitialized;
            }

            if (userId.IsNull)
            {
                return ResultCode.NullArgument;
            }

            byte[] data = [];
            if (context.Request.SendBuff.Count > 0)
            {
                ulong size = Math.Min(context.Request.SendBuff[0].Size, (ulong)NextendoIslandSync.CustomDataSize);
                data = new byte[size];
                context.Memory.Read(context.Request.SendBuff[0].Position, data);
            }

            NextendoIslandSync.Update(Key(context, userId), e => e.Custom = Convert.ToBase64String(data));

            return ResultCode.Success;
        }

        [CommandCmif(18)]
        // GetSaveDataBackupInfoCache(nn::account::Uid, buffer<SaveDataBackupInfo, 0x16>)
        public ResultCode GetSaveDataBackupInfoCache(ServiceCtx context)
        {
            UserId userId = context.RequestData.ReadStruct<UserId>();

            if (!_initialized)
            {
                return ResultCode.NotInitialized;
            }

            NextendoIslandSync.OlscEntry entry = NextendoIslandSync.Get(Key(context, userId));
            if (!entry.HasCache || context.Request.ReceiveBuff.Count == 0 || context.Request.ReceiveBuff[0].Size < NextendoIslandSync.BackupInfoSize)
            {
                return ResultCode.SaveDataBackupInfoCacheNotFound;
            }

            byte[] info = new byte[NextendoIslandSync.BackupInfoSize];
            NextendoIslandSync.WriteBackupInfo(info, entry.CacheId, entry.CacheTime, NextendoIslandCloud.DecodeCustom(entry.CacheCustom));
            context.Memory.Write(context.Request.ReceiveBuff[0].Position, info);

            return ResultCode.Success;
        }

        [CommandCmif(19)]
        // UpdateSaveDataBackupInfoCacheAsync(nn::account::Uid) -> object<IAsyncResult>
        public ResultCode UpdateSaveDataBackupInfoCacheAsync(ServiceCtx context)
        {
            UserId userId = context.RequestData.ReadStruct<UserId>();

            if (!_initialized)
            {
                return ResultCode.NotInitialized;
            }

            ulong titleId = TitleId(context);
            string key = Key(context, userId);

            MakeObject(context, new IOlscAsyncResult(context, async () =>
            {
                if (!NextendoIslandSync.IsStrict(titleId))
                {
                    return ResultCode.NextendoUnavailable;
                }

                (NextendoIslandCloud.Outcome rc, NextendoIslandSync.CloudIsland island) = await NextendoIslandCloud.GetMetaAsync(titleId);
                if (rc == NextendoIslandCloud.Outcome.Ok)
                {
                    NextendoIslandCloud.StoreCache(key, island);
                }

                return NextendoOlscCloud.ToResult(rc);
            }));

            return ResultCode.Success;
        }

        [CommandCmif(25)]
        // ListDownloadableSaveDataBackupInfoAsync(u32 size, nn::account::Uid, handle<copy, transfer_memory>) -> object<IAsyncListDownloadableSaveDataBackupInfo>
        public ResultCode ListDownloadableSaveDataBackupInfoAsync(ServiceCtx context)
        {
            context.RequestData.ReadUInt32(); // taille de la mémoire de travail (0x2000 dans ACNH)
            context.RequestData.ReadUInt32();
            UserId userId = context.RequestData.ReadStruct<UserId>();

            // Le jeu détruit sa mémoire de transfert dès le retour de l'appel : elle ne sert pas ici.
            if (context.Request.HandleDesc?.ToCopy.Length > 0)
            {
                context.Device.System.KernelContext.Syscall.CloseHandle(context.Request.HandleDesc.ToCopy[0]);
            }

            if (!_initialized)
            {
                return ResultCode.NotInitialized;
            }

            ulong titleId = TitleId(context);
            string key = Key(context, userId);
            byte[][] entries = [];

            MakeObject(context, new IAsyncListDownloadableSaveDataBackupInfo(context, async () =>
            {
                if (!NextendoIslandSync.IsStrict(titleId))
                {
                    return ResultCode.NextendoUnavailable;
                }

                (NextendoIslandCloud.Outcome rc, NextendoIslandSync.CloudIsland island) = await NextendoIslandCloud.GetMetaAsync(titleId);
                if (rc != NextendoIslandCloud.Outcome.Ok)
                {
                    return NextendoOlscCloud.ToResult(rc);
                }

                NextendoIslandCloud.StoreCache(key, island);
                if (NextendoIslandSync.IsRestorable(island))
                {
                    byte[] info = new byte[NextendoIslandSync.BackupInfoSize];
                    NextendoIslandSync.WriteBackupInfo(info, island.Rev, island.PosixTime, island.Custom);
                    entries = [info];
                }

                Logger.Info?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: {entries.Length} island(s) to restore");

                return ResultCode.Success;
            }, () => entries));

            return ResultCode.Success;
        }

        [CommandCmif(26)]
        // DownloadSaveDataBackupAsync(nn::account::Uid, u64 id) -> object<IAsyncTransferSaveDataBackup>
        public ResultCode DownloadSaveDataBackupAsync(ServiceCtx context)
        {
            UserId userId = context.RequestData.ReadStruct<UserId>();
            ulong id = context.RequestData.ReadUInt64();

            if (!_initialized)
            {
                return ResultCode.NotInitialized;
            }

            ulong titleId = TitleId(context);
            string key = Key(context, userId);
            Switch device = context.Device;

            Logger.Info?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: DownloadSaveDataBackupAsync {userId} id={id}");

            MakeObject(context, new IAsyncTransferSaveDataBackup(context, () =>
                NextendoIslandSync.IsStrict(titleId) ? NextendoOlscCloud.DownloadAsync(device, titleId, key) : Task.FromResult(ResultCode.NextendoUnavailable)));

            return ResultCode.Success;
        }

        [CommandCmif(27)]
        // UploadSaveDataBackupAsync(nn::account::Uid) -> object<IAsyncTransferSaveDataBackup>
        public ResultCode UploadSaveDataBackupAsync(ServiceCtx context)
        {
            UserId userId = context.RequestData.ReadStruct<UserId>();

            if (!_initialized)
            {
                return ResultCode.NotInitialized;
            }

            ulong titleId = TitleId(context);
            string key = Key(context, userId);
            Switch device = context.Device;

            Logger.Info?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: UploadSaveDataBackupAsync {userId}");

            MakeObject(context, new IAsyncTransferSaveDataBackup(context, () =>
                NextendoIslandSync.IsStrict(titleId) ? NextendoOlscCloud.UploadAsync(device, titleId, key) : Task.FromResult(ResultCode.NextendoUnavailable)));

            return ResultCode.Success;
        }
    }
}
