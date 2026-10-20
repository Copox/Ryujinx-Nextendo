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

        [Test]
        public void OnlyAcnhIsStrict()
        {
            Assert.IsTrue(IsStrict(0x01006f8002326000));
            Assert.IsFalse(IsStrict(0x0100c2500fc20000)); // Splatoon 3 garde sa synchro actuelle
        }
    }
}
