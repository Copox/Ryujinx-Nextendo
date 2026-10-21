using NUnit.Framework;
using Ryujinx.Common.Configuration;


namespace Ryujinx.Tests.HLE
{
    /// <summary>
    /// Un ami n'est « dans le meme jeu » (SamePresenceGroupApplication) que si sa presence nomme le
    /// jeu en cours. Cas du 21/10 : Portal 2 affichait « Playing Portal 2 » pour un ami en Splatoon 3.
    /// </summary>
    [NonParallelizable]
    public class FriendSamePresenceGroupTests
    {
        private string _previous;

        [SetUp]
        public void Save() => _previous = NextendoFriends.CurrentTitleId;

        [TearDown]
        public void Restore() => NextendoFriends.CurrentTitleId = _previous;

        private static NextendoFriends.Entry Friend(int status, ulong appId)
            => new(101, "a", null, status: status) { AppId = appId };

        [Test]
        public void AmiDansUnAutreJeuNestPasDansLeJeuEnCours()
        {
            NextendoFriends.CurrentTitleId = "0100abd01785c000"; // Portal 2
            Assert.That(NextendoFriends.PlaysCurrentGame(Friend(2, 0x0100C2500FC20000)), Is.False); // Splatoon 3
        }

        [Test]
        public void AmiDansLeMemeJeu()
        {
            NextendoFriends.CurrentTitleId = "0100ABD01785C000";
            Assert.That(NextendoFriends.PlaysCurrentGame(Friend(2, 0x0100ABD01785C000)), Is.True);
        }

        [Test]
        public void HorsLigneOuSansJeuJamaisDansLeJeu()
        {
            NextendoFriends.CurrentTitleId = "0100152000022000"; // MK8
            Assert.That(NextendoFriends.PlaysCurrentGame(Friend(0, 0x0100152000022000)), Is.False);
            Assert.That(NextendoFriends.PlaysCurrentGame(Friend(1, 0)), Is.False);
            NextendoFriends.CurrentTitleId = "";
            Assert.That(NextendoFriends.PlaysCurrentGame(Friend(2, 0x0100152000022000)), Is.False);
        }

        [Test]
        public void VersionsRegionalesDeSplatoon2()
        {
            NextendoFriends.CurrentTitleId = "0100f8f0000a2000";
            Assert.That(NextendoFriends.PlaysCurrentGame(Friend(2, 0x01003BC0000A0000)), Is.True);
            Assert.That(NextendoFriends.PlaysCurrentGame(Friend(2, 0x01003C700009C800)), Is.True);
        }
    }
}
