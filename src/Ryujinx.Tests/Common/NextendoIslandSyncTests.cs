using NUnit.Framework;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using static Ryujinx.Common.Configuration.NextendoIslandSync;

namespace Ryujinx.Tests.Common
{
    // [Nextendo] Archivage de l'île d'ACNH par olsc : formats lus dans le client d'ACNH 3.0.3.
    public class NextendoIslandSyncTests
    {
        private static string IslandDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "nxile_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(d, "Villager0"));
            File.WriteAllBytes(Path.Combine(d, "main.dat"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(d, "mainHeader.dat"), new byte[768]);
            File.WriteAllBytes(Path.Combine(d, "Villager0", "personal.dat"), new byte[] { 9 });
            return d;
        }

        private static byte[] Zip(params (string Name, byte[] Data)[] files)
        {
            using MemoryStream ms = new();
            using (ZipArchive z = new(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach ((string name, byte[] data) in files)
                {
                    using Stream s = z.CreateEntry(name).Open();
                    s.Write(data);
                }
            }

            return ms.ToArray();
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

        [Test] // aller-retour : ce qu'on envoie est ce qu'on restaure
        public void ExtractRestoresWhatWasZipped()
        {
            string d = IslandDir();
            string target = Path.Combine(Path.GetTempPath(), "nxile_out_" + Path.GetRandomFileName());
            try
            {
                ExtractIsland(ZipDirectory(d), target);
                Assert.AreEqual(Fingerprint(d), Fingerprint(target));
            }
            finally
            {
                Directory.Delete(d, true);
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, true);
                }
            }
        }

        [Test] // jamais un fichier hors de la sauvegarde, jamais autre chose qu'une île
        public void ExtractRefusesEscapesAndNonIslands()
        {
            foreach (byte[] bad in new[]
            {
                Zip(("main.dat", new byte[1]), ("../evil.dat", new byte[1])),
                Zip(("main.dat", new byte[1]), ("a/../../evil.dat", new byte[1])),
                Zip(("main.dat", new byte[1]), ("C:/evil.dat", new byte[1])),
                Zip(("/main.dat", new byte[1])),
                Zip(("Villager0/personal.dat", new byte[1])),
            })
            {
                string target = Path.Combine(Path.GetTempPath(), "nxile_bad_" + Path.GetRandomFileName());
                Assert.Throws<InvalidDataException>(() => ExtractIsland(bad, target));
                Assert.IsFalse(Directory.Exists(target), "rien ne doit être écrit");
            }
        }

        [Test] // élément SaveDataBackupInfo de 0x410 octets : +0 id, +8 PosixTime, +0x10 données du jeu
        public void BackupInfoLayout()
        {
            byte[] info = new byte[BackupInfoSize];
            Array.Fill(info, (byte)0xAA);
            byte[] custom = Encoding.ASCII.GetBytes("ile\0");
            WriteBackupInfo(info, 0x1122334455667788, 1760000000, custom);
            Assert.AreEqual(0x410, info.Length);
            Assert.AreEqual(0x1122334455667788UL, BitConverter.ToUInt64(info, 0));
            Assert.AreEqual(1760000000L, BitConverter.ToInt64(info, 8));
            CollectionAssert.AreEqual(custom, info.Skip(0x10).Take(custom.Length).ToArray());
            Assert.IsTrue(info.Skip(0x10 + custom.Length).All(b => b == 0), "le reste est remis à zéro");
        }

        [Test]
        public void MetaIsParsed()
        {
            string custom = Convert.ToBase64String(Encoding.ASCII.GetBytes("ile\0"));
            CloudIsland c = ParseMeta("{\"exists\":true,\"rev\":7,\"updatedAt\":\"2026-10-20T10:00:00Z\",\"custom\":\"" + custom + "\"}");
            Assert.IsTrue(c.Exists);
            Assert.AreEqual(7UL, c.Rev);
            Assert.AreEqual(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), c.PosixTime);
            Assert.IsTrue(IsRestorable(c));
        }

        [Test] // une île sans les données du jeu ferait échouer toute la liste dans le jeu : non proposée
        public void IslandWithoutGameDataIsNotOffered()
        {
            Assert.IsFalse(IsRestorable(ParseMeta("{\"exists\":true,\"rev\":3}")));
            Assert.IsFalse(IsRestorable(ParseMeta("{\"exists\":false,\"rev\":0}")));
            Assert.IsFalse(IsRestorable(ParseMeta("{\"exists\":true,\"rev\":3,\"custom\":\"pas du base64\"}")));
            Assert.IsFalse(IsRestorable(null));
        }

        private static string SaveRoot(byte content)
        {
            string root = Path.Combine(Path.GetTempPath(), "nxsave_" + Path.GetRandomFileName());
            foreach (string sub in new[] { "0", "1" })
            {
                Directory.CreateDirectory(Path.Combine(root, sub, "Villager0"));
                File.WriteAllBytes(Path.Combine(root, sub, "main.dat"), new[] { content });
                File.WriteAllBytes(Path.Combine(root, sub, "Villager0", "personal.dat"), new[] { content });
            }

            return root;
        }

        [Test] // restauration : « 0 » et « 1 » remplacés, l'île d'avant gardée en copie
        public void ReplaceIslandSwapsBothDirectoriesAndKeepsACopy()
        {
            string root = SaveRoot(1);
            string cloud = IslandDir();
            string backups = Path.Combine(Path.GetTempPath(), "nxbak_" + Path.GetRandomFileName());
            try
            {
                string before = Fingerprint(Path.Combine(root, "0"));
                Assert.AreEqual(Ryujinx.Common.Configuration.NextendoIslandCloud.Outcome.Ok, ReplaceIsland(root, ZipDirectory(cloud), backups, "acnh"));
                Assert.AreEqual(Fingerprint(cloud), Fingerprint(Path.Combine(root, "0")));
                Assert.AreEqual(Fingerprint(cloud), Fingerprint(Path.Combine(root, "1")));
                CollectionAssert.AreEquivalent(new[] { "0", "1" }, Directory.GetDirectories(root).Select(Path.GetFileName));

                string[] copies = Directory.GetFiles(backups, "acnh-*.zip");
                Assert.AreEqual(1, copies.Length);
                string restored = Path.Combine(Path.GetTempPath(), "nxback_" + Path.GetRandomFileName());
                ExtractIsland(File.ReadAllBytes(copies[0]), restored);
                Assert.AreEqual(before, Fingerprint(restored));
                Directory.Delete(restored, true);
            }
            finally
            {
                Directory.Delete(root, true);
                Directory.Delete(cloud, true);
                if (Directory.Exists(backups))
                {
                    Directory.Delete(backups, true);
                }
            }
        }

        [Test] // une île téléchargée invalide ne touche à rien
        public void ReplaceIslandWithInvalidZipKeepsTheLocalIsland()
        {
            string root = SaveRoot(7);
            string backups = Path.Combine(Path.GetTempPath(), "nxbak_" + Path.GetRandomFileName());
            try
            {
                string before0 = Fingerprint(Path.Combine(root, "0"));
                string before1 = Fingerprint(Path.Combine(root, "1"));
                byte[] bad = Zip(("main.dat", new byte[1]), ("../evil.dat", new byte[1]));
                Assert.AreEqual(Ryujinx.Common.Configuration.NextendoIslandCloud.Outcome.InvalidIsland, ReplaceIsland(root, bad, backups, "acnh"));
                Assert.AreEqual(Ryujinx.Common.Configuration.NextendoIslandCloud.Outcome.InvalidIsland, ReplaceIsland(root, new byte[] { 1, 2, 3 }, backups, "acnh"));
                Assert.AreEqual(before0, Fingerprint(Path.Combine(root, "0")));
                Assert.AreEqual(before1, Fingerprint(Path.Combine(root, "1")));
                CollectionAssert.AreEquivalent(new[] { "0", "1" }, Directory.GetDirectories(root).Select(Path.GetFileName));
                Assert.IsFalse(Directory.Exists(backups));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // Rejeu de l'activation vue dans ACNH 3.0.3 (écran titre, touche −, « Yep! Enable, please! ») :
        // 14 (autre profil, false), 10 (licence), 14 (profil, false), 15 (données du jeu), 14 (profil,
        // true) ; puis 13 doit rendre 1 (« Backups - Enabled »), et la fin de partie marque l'envoi.
        [Test]
        public void EnableFlowReplay()
        {
            string dir = Path.Combine(Path.GetTempPath(), "nxolsc_" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            string saved = StoreDirectory;
            StoreDirectory = dir;
            try
            {
                const ulong Acnh = 0x01006f8002326000;
                string user = Key(Acnh, "DC6F8D4E825A4D9C858F6DA4178BE3D0");
                string other = Key(Acnh, "00000000000000010000000000000000");
                Assert.AreEqual("01006f8002326000:dc6f8d4e825a4d9c858f6da4178be3d0", user);
                Assert.AreEqual(0, Get(user).Setting, "jamais réglé : « Backups - Not Set »");

                Update(other, e => e.Setting = 2);
                Update(user, e => e.Setting = 2);
                byte[] custom = Encoding.ASCII.GetBytes(new string('i', 405) + "\0");
                Update(user, e => e.Custom = Convert.ToBase64String(custom));
                Update(user, e => e.Setting = 1);

                Assert.AreEqual(1, Get(user).Setting);
                Assert.AreEqual(2, Get(other).Setting);
                CollectionAssert.AreEqual(custom, Convert.FromBase64String(Get(user).Custom));

                // fin de partie : marqué en attente avant l'envoi, retrouvé au lancement suivant
                Ryujinx.Common.Configuration.NextendoIslandCloud.MarkPending(user, @"C:\save\1b");
                var pending = AllPending();
                Assert.AreEqual(1, pending.Count);
                Assert.AreEqual(user, pending[0].Key);
                Assert.AreEqual(@"C:\save\1b", pending[0].Entry.PendingSaveRoot);
                Assert.AreEqual(1, Get(user).Setting, "la marque ne touche pas au réglage");
            }
            finally
            {
                StoreDirectory = saved;
                Directory.Delete(dir, true);
            }
        }

        [Test] // cache de l'île (cmd 18) : rendu tel que le jeu le lit, date +8 en PosixTime
        public void InfoCacheFromServerMeta()
        {
            byte[] custom = Encoding.ASCII.GetBytes("ile\0");
            CloudIsland c = ParseMeta("{\"exists\":true,\"rev\":4,\"updatedAt\":\"2026-10-20T12:06:10Z\",\"custom\":\"" + Convert.ToBase64String(custom) + "\"}");
            byte[] info = new byte[BackupInfoSize];
            WriteBackupInfo(info, c.Rev, c.PosixTime, c.Custom);
            Assert.AreEqual(4UL, BitConverter.ToUInt64(info, 0));
            Assert.AreEqual(DateTimeOffset.Parse("2026-10-20T12:06:10Z").ToUnixTimeSeconds(), BitConverter.ToInt64(info, 8));
        }

        [Test]
        public void OnlyAcnhIsStrict()
        {
            Assert.IsTrue(IsStrict(0x01006f8002326000));
            Assert.IsFalse(IsStrict(0x0100c2500fc20000)); // Splatoon 3 garde sa synchro actuelle
        }
    }
}
