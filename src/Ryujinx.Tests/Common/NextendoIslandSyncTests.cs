using NUnit.Framework;
using Ryujinx.Common.Configuration;
using System.IO;
using System.IO.Compression;
using System.Linq;
using static Ryujinx.Common.Configuration.NextendoIslandSync;

namespace Ryujinx.Tests.Common
{
    // [Nextendo] Île ACNH dans le cloud, règle de Nintendo : un emplacement par compte, le dernier envoi gagne,
    // restauration seulement si débloquée sur le site ; le zip envoyé a la forme que le serveur accepte.
    public class NextendoIslandSyncTests
    {
        private static readonly SyncState Linked = new() { Linked = true, Rev = 4, LocalHash = "aaa" };

        [Test]
        public void NothingAnywhere() => Assert.AreEqual(LaunchAction.Nothing, Decide(false, false, false, null));

        [Test] // île injectée ou jamais liée ici : on DEMANDE avant de la lier (elle remplacera l'archive)
        public void UnlinkedLocalIslandAsksToLink() => Assert.AreEqual(LaunchAction.OfferLink, Decide(false, false, true, null));

        [Test] // même si le cloud a une île : lier celle-ci la remplacera, comme dans le jeu
        public void UnlinkedLocalIslandOverCloudAsksToLink() => Assert.AreEqual(LaunchAction.OfferLink, Decide(true, false, true, new SyncState()));

        [Test] // île liée : plus aucune question, le dernier envoi gagne
        public void LinkedIslandNeedsNoQuestion() => Assert.AreEqual(LaunchAction.Nothing, Decide(true, false, true, Linked));

        [Test] // console neuve, île dans le cloud, restauration verrouillée : rien (comme chez Nintendo)
        public void EmptyLocalWithLockedRestoreDoesNothing() => Assert.AreEqual(LaunchAction.Nothing, Decide(true, false, false, null));

        [Test] // restauration débloquée sur le site : on la propose, île locale ou non
        public void UnlockedRestoreIsOffered()
        {
            Assert.AreEqual(LaunchAction.OfferRestore, Decide(true, true, false, null));
            Assert.AreEqual(LaunchAction.OfferRestore, Decide(true, true, true, Linked));
        }

        [Test] // débloquée mais rien dans le cloud : rien à restaurer
        public void UnlockedRestoreWithoutCloudIsIgnored() => Assert.AreEqual(LaunchAction.OfferLink, Decide(false, true, true, null));

        private static string IslandDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "nxile_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(d, "Villager0"));
            File.WriteAllBytes(Path.Combine(d, "main.dat"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(d, "mainHeader.dat"), new byte[768]);
            File.WriteAllBytes(Path.Combine(d, "Villager0", "personal.dat"), new byte[] { 9 });
            return d;
        }

        [Test]
        public void FingerprintFollowsContent()
        {
            string d = IslandDir();
            try
            {
                string h1 = Fingerprint(d);
                Assert.AreEqual(64, h1.Length);
                Assert.AreEqual(h1, Fingerprint(d));
                File.WriteAllBytes(Path.Combine(d, "main.dat"), new byte[] { 1, 2, 4 });
                Assert.AreNotEqual(h1, Fingerprint(d));
                Assert.AreEqual("", Fingerprint(Path.Combine(d, "absent")));
            }
            finally
            {
                Directory.Delete(d, true);
            }
        }

        [Test] // le serveur n'accepte que « Villager0/personal.dat », pas « Villager0\personal.dat »
        public void ZipUsesForwardSlashesAndNoDirectoryEntries()
        {
            string d = IslandDir();
            try
            {
                using ZipArchive z = new(new MemoryStream(ZipDirectory(d)));
                string[] names = z.Entries.Select(e => e.FullName).ToArray();
                CollectionAssert.AreEqual(new[] { "Villager0/personal.dat", "main.dat", "mainHeader.dat" }, names);
                Assert.AreEqual(768, z.GetEntry("mainHeader.dat").Length);
            }
            finally
            {
                Directory.Delete(d, true);
            }
        }

        [Test]
        public void OnlyAcnhIsStrict()
        {
            Assert.IsTrue(IsStrict(0x01006f8002326000));
            Assert.IsFalse(IsStrict(0x0100c2500fc20000)); // Splatoon 3 garde sa synchro actuelle
        }
    }
}
