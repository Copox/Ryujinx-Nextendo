using Ryujinx.Horizon.Common;
using Ryujinx.Horizon.Sdk.Sf;

namespace Ryujinx.Horizon.Sdk.Ovln
{
    interface ISenderService : IServiceObject
    {
        // ovln:snd cmd 0. Entrees (SourceName + QueueAttribute) ignorees :
        // on ne les declare pas pour ne pas dependre de leur taille exacte.
        // qlaunch l'appelle au demarrage pour ses notifications en
        // surimpression ; sans l'objet ISender, qlaunch s'auto-detruit (2010-0212).
        Result OpenSender(out ISender sender);
    }
}
