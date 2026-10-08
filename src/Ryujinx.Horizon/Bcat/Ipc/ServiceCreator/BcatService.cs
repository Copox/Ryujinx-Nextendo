using LibHac.Bcat;
using Ryujinx.Horizon.Bcat.Types;
using Ryujinx.Horizon.Common;
using Ryujinx.Horizon.Sdk.Bcat;
using Ryujinx.Horizon.Sdk.Sf;
using Ryujinx.Horizon.Sdk.Sf.Hipc;
using System;

namespace Ryujinx.Horizon.Bcat.Ipc
{
    partial class BcatService : IBcatService
    {
        public BcatService(BcatServicePermissionLevel permissionLevel) { }

        [CmifCommand(10100)]
        public Result RequestSyncDeliveryCache(out IDeliveryCacheProgressService deliveryCacheProgressService)
        {
            deliveryCacheProgressService = new DeliveryCacheProgressService();

            Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.ServiceBcat, "[SEED] RequestSyncDeliveryCache called");

            return Result.Success;
        }

        // [Nextendo] Meme chose, mais pour UN dossier de cache nomme.
        //
        // Pourquoi elle manquait, et ce que ca coutait : Splatoon 3, une fois son calendrier de
        // Splatfest recu, demande a BCAT de synchroniser le dossier du fest annonce. Cette commande
        // n'existait pas, Ryujinx la journalisait en « Missing service BcatService (command ID:
        // 10101) ignored » et ne repondait rien — le jeu restait sur sa demande et affichait
        // 2010-0212 au demarrage.
        //
        // On repond comme pour 10100 : un service de progression, que le jeu interroge et qui lui
        // dit que la synchronisation est terminee. Rien n'est telecharge — le cache de livraison
        // local fait foi, exactement comme quand une console n'a rien de neuf a recevoir.
        [CmifCommand(10101)]
        public Result RequestSyncDeliveryCacheWithDirectoryName(DirectoryName directoryName, out IDeliveryCacheProgressService deliveryCacheProgressService)
        {
            deliveryCacheProgressService = new DeliveryCacheProgressService();

            Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.ServiceBcat,
                $"[Nextendo] RequestSyncDeliveryCacheWithDirectoryName(\"{directoryName}\") -> synchronisation consideree comme terminee");

            return Result.Success;
        }
        // [Nextendo, HOME Menu work] RegisterSystemApplicationDeliveryTasks(): no input, no
        // output (the request carries no buffer at all, seen with the [ipc-shape] trace).
        // Nothing is delivered in the background here, so there is nothing to register.
        // [Nextendo, HOME Menu work] RequestSuspendDeliveryTask(ApplicationId) -> object. The
        // HOME Menu asks for it before opening the eShop or Nintendo Switch Online and keeps the
        // returned interface; no delivery runs here, so the object has nothing to suspend.
        [CmifCommand(20301)]
        public Result RequestSuspendDeliveryTask(ulong applicationId, out IDeliveryCacheProgressService suspension)
        {
            suspension = new DeliveryCacheProgressService();

            return Result.Success;
        }

        [CmifCommand(30300)]
        public Result RegisterSystemApplicationDeliveryTasks()
        {
            return Result.Success;
        }
    }
}
