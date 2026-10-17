using NUnit.Framework;
using Ryujinx.Common.Configuration;

namespace Ryujinx.Tests.HLE
{
    public class SmashFriendListTests
    {
        [TestCase("01006a800016e000", true)]
        [TestCase("01006A800016E000", true)]
        [TestCase("0100152000022000", false)] // Mario Kart 8 Deluxe : liste interrogee en boucle, jamais bloquee
        [TestCase("01003bc0000a0000", false)] // Splatoon 2
        [TestCase("", false)]
        [TestCase(null, false)]
        public void OnlySmashUltimateWaitsForItsFriendListAtLaunch(string titleId, bool expected)
        {
            Assert.That(NextendoFriends.ReadsListOnlyOnLaunch(titleId), Is.EqualTo(expected));
        }
    }
}
