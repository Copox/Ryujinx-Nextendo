using NUnit.Framework;
using Ryujinx.HLE.HOS.Applets.Browser;
using System;
using System.Text;

namespace Ryujinx.Tests.HLE
{
    public class VioletTermsResponseTests
    {
        [TestCase(0x01008F6008C5E000UL, true)]
        [TestCase(0x0100A3D008C5C000UL, true)]
        [TestCase(0x0100F43008C44000UL, true)]
        [TestCase(0x0100C2500FC20000UL, false)]
        public void TermsBypassIsLimitedToPokemonTitles(ulong programId, bool expected)
        {
            Assert.That(BrowserApplet.IsPokemonTermsTitle(programId), Is.EqualTo(expected));
        }

        [TestCase("https://battle-%.pokemon-home.com/scvi/terms/es", "https://battle-%.pokemon-home.com/scvi/terms/callback", true)]
        [TestCase("https://battle-%.pokemon-home.com/scvi/competition/terms/es", "https://battle-%.pokemon-home.com/scvi/competition/terms/callback", true)]
        [TestCase("https://battle-%.pokemon-home.com/scvi/competition/es?lang=es", "https://battle-%.pokemon-home.com/scvi/competition/callback", true)]
        [TestCase("https://battle-%.pokemon-home.com/scvi/terms/es", "https://other.pokemon-home.com/scvi/terms/callback", false)]
        [TestCase("https://battle-%.pokemon-home.com.evil/scvi/terms/es", "https://battle-%.pokemon-home.com.evil/scvi/terms/callback", false)]
        [TestCase("https://news-%.pokemon-home.com/scvi/list/en", "https://news-%.pokemon-home.com/scvi/list/callback", false)]
        [TestCase("https://battle-%.pokemon-home.com/other/terms/es", "https://battle-%.pokemon-home.com/other/terms/callback", false)]
        [TestCase("https://battle-%.pokemon-home.com/plza/terms/en", "https://battle-%.pokemon-home.com/plza/terms/callback", true)]
        [TestCase("https://battle-%.pokemon-home.com/plza/terms/en", "https://battle-%.pokemon-home.com/scvi/terms/callback", false)]
        public void CallbackMustBelongToThePokemonBattlePage(string page, string callback, bool expected)
        {
            Assert.That(BrowserApplet.IsPokemonTermsCallback(page, callback), Is.EqualTo(expected));
        }

        [Test]
        public void CallbackUrlSizeIncludesNullTerminator()
        {
            const string callback = "https://battle-%.pokemon-home.com/scvi/terms/callback";
            const string accepted = callback + "/agree";
            byte[] response = BrowserApplet.BuildPokemonTermsResponse(callback);
            Assert.That(response.Length, Is.EqualTo(0x2000));
            Assert.That(BitConverter.ToUInt16(response, 0), Is.EqualTo(3));
            Assert.That(BitConverter.ToUInt32(response, 4), Is.EqualTo((uint)ShimKind.Web));
            Assert.That(BitConverter.ToUInt16(response, 8), Is.EqualTo(1));
            Assert.That(BitConverter.ToUInt32(response, 16), Is.EqualTo((uint)WebExitReason.LastUrl));
            Assert.That(BitConverter.ToUInt16(response, 20), Is.EqualTo(2));
            int size = BitConverter.ToUInt16(response, 22);
            Assert.That(size, Is.EqualTo(Encoding.UTF8.GetByteCount(accepted) + 1));
            Assert.That(response[28 + size - 1], Is.Zero);
            Assert.That(Encoding.UTF8.GetString(response, 28, size - 1), Is.EqualTo(accepted));
            int next = 28 + size;
            Assert.That(BitConverter.ToUInt16(response, next), Is.EqualTo(3));
            Assert.That(BitConverter.ToUInt64(response, next + 8), Is.EqualTo((ulong)size));
        }

        [Test]
        public void CompetitionRegistrationCallbackReportsAgreement()
        {
            const string callback = "https://battle-%.pokemon-home.com/scvi/competition/callback";
            byte[] response = BrowserApplet.BuildPokemonTermsResponse(callback);
            int size = BitConverter.ToUInt16(response, 22);
            Assert.That(Encoding.UTF8.GetString(response, 28, size - 1), Is.EqualTo(callback + "/agree"));
        }

        [Test]
        public void ObservedBattleTermsCallbackReportsAgreement()
        {
            const string callback = "https://battle-%.pokemon-home.com/scvi/battle-terms/callback";
            byte[] response = BrowserApplet.BuildPokemonTermsResponse(callback);
            int size = BitConverter.ToUInt16(response, 22);
            Assert.That(Encoding.UTF8.GetString(response, 28, size - 1), Is.EqualTo(callback + "/agree"));
        }
    }
}
