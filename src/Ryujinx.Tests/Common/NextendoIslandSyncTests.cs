using NUnit.Framework;
using Ryujinx.Common.Configuration;
using System.IO;
using System.IO.Compression;
using System.Linq;
using static Ryujinx.Common.Configuration.NextendoIslandSync;

namespace Ryujinx.Tests.Common
{
    // [Nextendo] Île ACNH dans le cloud : la décision au lancement ne doit jamais écraser une île
    // sans demander, et le zip envoyé doit avoir la forme que le serveur accepte.
    public class NextendoIslandSyncTests
    {
        private static readonly SyncState Synced = new() { Rev = 4, LocalHash = "aaa" };

        [Test]
        public void NothingAnywhere() => Assert.AreEqual(LaunchAction.Nothing, Decide(false, 0, false, "", null));

        [Test] // question c : île injectée, cloud vide -> on DEMANDE avant de la lier au compte
        public void InjectedIslandWithEmptyCloudAsksBeforeLinking() =>
            Assert.AreEqual(LaunchAction.OfferFirstUpload, Decide(false, 0, true, "bbb", null));

        [Test]
        public void EmptyLocalOffersDownload() =>
            Assert.AreEqual(LaunchAction.OfferDownloadEmptyLocal, Decide(true, 4, false, "", Synced));

        [Test]
        public void InSync() => Assert.AreEqual(LaunchAction.Nothing, Decide(true, 4, true, "aaa", Synced));

        [Test] // joué ici hors ligne ou coupé avant l'envoi : le local avance, pas de question
        public void LocalAheadNeedsNoQuestion() => Assert.AreEqual(LaunchAction.Nothing, Decide(true, 4, true, "ccc", Synced));

        [Test] // joué sur la Switch, rien touché ici : proposer le téléchargement
        public void CloudAheadOffersDownload() =>
            Assert.AreEqual(LaunchAction.OfferDownloadCloudNewer, Decide(true, 5, true, "aaa", Synced));

        [Test] // question a : joué des deux côtés -> conflit, le joueur choisit
        public void BothChangedIsConflict() => Assert.AreEqual(LaunchAction.Conflict, Decide(true, 5, true, "ccc", Synced));

        [Test] // question b : île injectée alors que le cloud en a une -> conflit, jamais d'écrasement
        public void InjectedIslandOverCloudIsConflict() => Assert.AreEqual(LaunchAction.Conflict, Decide(true, 4, true, "zzz", null));

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
