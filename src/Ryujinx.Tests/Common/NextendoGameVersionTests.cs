using NUnit.Framework;
using Ryujinx.Common;

namespace Ryujinx.Tests.Common
{
    // [Nextendo] Controle de version en ligne : seule la version exacte de la table passe,
    // ni une plus ancienne ni une plus recente.
    public class NextendoGameVersionTests
    {
        [TestCase("4.0.1", "4.0.1")] // MK8 : la version exigee par le serveur depuis le 18/10
        [TestCase("1.0.17", "1.0.17")]
        [TestCase("11.3.0", "11.3.0")]
        [TestCase("4.0", "4.0.0")] // composants absents = 0
        [TestCase("4.0.0.0", "4.0.0")]
        [TestCase("4.0.1.0", "4.0.1")]
        [TestCase("v4.0.1", "4.0.1")]
        [TestCase(" 4.0.1 ", "4.0.1")]
        [TestCase("2.7.7.92380", "2.7.7.92380")]
        public void AcceptsExactVersion(string installed, string required)
        {
            Assert.IsTrue(NextendoGameVersion.IsExactly(installed, required));
        }

        [TestCase("4.0.0", "4.0.1")] // MK8 4.0.0 refuse par le serveur : l'emulateur le refuse aussi
        [TestCase("4.0.2", "4.0.1")] // mise a jour pas encore preparee par Nextendo : refusee
        [TestCase("4.1.0", "4.0.1")]
        [TestCase("5.0.0", "4.0.1")]
        [TestCase("3.0.3", "4.0.1")]
        [TestCase("1.0.10", "1.0.9")]
        [TestCase("1.0.9", "1.0.10")]
        [TestCase("13.0.10", "13.0.5")]
        [TestCase("13.0.4", "13.0.5")]
        [TestCase("11.2.0", "11.3.0")]
        [TestCase("11.4.0", "11.3.0")]
        [TestCase("", "4.0.1")]
        [TestCase(null, "4.0.1")]
        [TestCase("4.0.1", "")]
        [TestCase("abc", "4.0.1")] // illisible : seule l'egalite exacte du texte passerait
        [TestCase("4.0.1-beta", "4.0.1")]
        [TestCase("4.0.1.0.0", "4.0.1")]
        [TestCase("4..1", "4.0.1")]
        [TestCase("-5.0.0", "4.0.1")]
        public void RejectsOtherOrUnreadable(string installed, string required)
        {
            Assert.IsFalse(NextendoGameVersion.IsExactly(installed, required));
        }

        [Test]
        public void UnreadableButIdenticalPasses()
        {
            Assert.IsTrue(NextendoGameVersion.IsExactly("1.0.0 (Patch)", "1.0.0 (Patch)"));
        }

        [Test]
        public void ParsesMissingComponentsAsZero()
        {
            Assert.IsTrue(NextendoGameVersion.TryParse("4", out System.Version v));
            Assert.AreEqual(new System.Version(4, 0, 0, 0), v);
            Assert.IsFalse(NextendoGameVersion.TryParse("v", out _));
        }
    }
}
