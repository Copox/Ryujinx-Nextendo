using NUnit.Framework;
using Ryujinx.Common.Configuration;
using System.Text.Json;

namespace Ryujinx.Tests.HLE
{
    public class NextendoBaasNsaTests
    {
        private const ulong Pid = 1800000123;
        private const ulong Baas = 0x8f3a5c0e12d4b679;

        [Test]
        public void AcnhSeulementEtParSonIdentifiant()
        {
            Assert.That(NextendoBaasNsa.TitleUsesBaasNsa(0x01006F8002326000UL), Is.True);
            Assert.That(NextendoBaasNsa.TitleUsesBaasNsa("01006f8002326000"), Is.True);
            Assert.That(NextendoBaasNsa.TitleUsesBaasNsa("01006F8002326000"), Is.True);
            Assert.That(NextendoBaasNsa.TitleUsesBaasNsa(0x0100C2500FC20000UL), Is.False); // Splatoon 3 : NPLN, inchange
            Assert.That(NextendoBaasNsa.TitleUsesBaasNsa(0x0100152000022000UL), Is.False); // Mario Kart 8 Deluxe
            Assert.That(NextendoBaasNsa.TitleUsesBaasNsa(""), Is.False);
            Assert.That(NextendoBaasNsa.TitleUsesBaasNsa("pas-un-titre"), Is.False);
        }

        [Test]
        public void ChoixDuNsaAnnonce()
        {
            // ACNH avec le baasUserID connu : celui d'une vraie Switch
            Assert.That(NextendoBaasNsa.Choose(0x01006F8002326000UL, Pid, Baas), Is.EqualTo(Baas));
            // ACNH sans baasUserID (site ancien ou injoignable) : le PID, comme avant
            Assert.That(NextendoBaasNsa.Choose(0x01006F8002326000UL, Pid, 0), Is.EqualTo(Pid));
            // autres jeux : toujours le PID (identite NEX inchangee)
            Assert.That(NextendoBaasNsa.Choose(0x0100152000022000UL, Pid, Baas), Is.EqualTo(Pid));
        }

        [Test]
        public void LectureDeApiMe()
        {
            using JsonDocument ok = JsonDocument.Parse("{\"account\":{\"pid\":1800000123},\"nsa\":\"8f3a5c0e12d4b679\"}");
            Assert.That(NextendoBaasNsa.Parse(ok.RootElement), Is.EqualTo(Baas));

            foreach (string json in new[]
            {
                "{\"account\":{\"pid\":1800000123}}",          // site d'avant : pas de champ
                "{\"nsa\":\"\"}",
                "{\"nsa\":\"8f3a5c0e12d4b67\"}",                // 15 chiffres
                "{\"nsa\":\"8f3a5c0e12d4b6799\"}",              // 17 chiffres
                "{\"nsa\":\"zz3a5c0e12d4b679\"}",
                "{\"nsa\":1234}",
                "[]",
            })
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                Assert.That(NextendoBaasNsa.Parse(doc.RootElement), Is.EqualTo(0UL), json);
            }

            Assert.That(NextendoBaasNsa.ParseHex(" 8F3A5C0E12D4B679 "), Is.EqualTo(Baas));
            Assert.That(NextendoBaasNsa.ParseHex(null), Is.EqualTo(0UL));
        }
    }
}
