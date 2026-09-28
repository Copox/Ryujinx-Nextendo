using NUnit.Framework;
using Ryujinx.Common;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Ryujinx.Tests.HLE
{
    public class PokemonBcatTests
    {
        private static byte[] Package(string path, string content)
        {
            using MemoryStream stream = new();
            using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
            using (Stream entry = zip.CreateEntry(path).Open()) entry.Write(Encoding.ASCII.GetBytes(content));
            return stream.ToArray();
        }

        [Test]
        public void KeepsScarletAndVioletEventsIsolatedAndPreservesOldEventOnInvalidArchive()
        {
            string root = Path.Combine(Path.GetTempPath(), "violet-bcat-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "vsdata"));
            File.WriteAllText(Path.Combine(root, "vsdata", "VSSetting_0.byaml"), "Splatoon");
            string violet = Path.Combine(root, "01008f6008c5e000");
            string scarlet = Path.Combine(root, "0100a3d008c5c000");
            try
            {
                NextendoPokemonBcat.Install(Package("raid/old", "old event"), violet);
                NextendoPokemonBcat.Install(Package("raid/scarlet", "scarlet event"), scarlet);
                Assert.Throws<InvalidDataException>(() => NextendoPokemonBcat.Install(Package("../outside", "bad"), violet));
                Assert.That(File.ReadAllText(Path.Combine(violet, "raid", "old")), Is.EqualTo("old event"));
                NextendoPokemonBcat.Install(Package("raid/current", "new event"), violet);
                Assert.That(File.Exists(Path.Combine(violet, "raid", "old")), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(violet, "raid", "current")), Is.EqualTo("new event"));
                Assert.That(File.ReadAllText(Path.Combine(scarlet, "raid", "scarlet")), Is.EqualTo("scarlet event"));
                Assert.That(File.ReadAllText(Path.Combine(root, "vsdata", "VSSetting_0.byaml")), Is.EqualTo("Splatoon"));
            }
            finally { Directory.Delete(root, recursive: true); }
        }
    }
}
