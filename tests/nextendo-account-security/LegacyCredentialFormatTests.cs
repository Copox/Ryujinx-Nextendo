using NUnit.Framework;
using Ryujinx.Common.Configuration;
using System.Net;
using System.Threading.Tasks;

namespace Ryujinx.AccountSecurity.Tests
{
    [NonParallelizable]
    public class LegacyCredentialFormatTests : AccountFixture
    {
        [TestCase("legacy-opaque-credential")]
        [TestCase("nx2.synthetic.signature")]
        public async Task AuthorityDecidesWhetherTheCredentialFormatIsAccepted(string token)
        {
            var result = await NextendoAccount.Authority.ResolveAsync(token);
            Assert.That(result.Identity.Pid, Is.EqualTo(Pid));
            Assert.That(AuthorityHandler.Calls, Is.EqualTo(1));
            Assert.That(AuthorityHandler.Destination.AbsoluteUri, Is.EqualTo("https://nextendo.network/api/me"));
        }

        [TestCase(HttpStatusCode.Unauthorized, true)]
        [TestCase(HttpStatusCode.Forbidden, true)]
        [TestCase(HttpStatusCode.ServiceUnavailable, false)]
        public async Task LegacyFormatKeepsRejectionDistinctFromAvailability(HttpStatusCode status, bool rejected)
        {
            AuthorityHandler.Status = status;
            var result = await NextendoAccount.Authority.ResolveAsync("legacy-opaque-credential");
            Assert.That(result.Identity, Is.Null);
            Assert.That(result.Rejected, Is.EqualTo(rejected));
            Assert.That(AuthorityHandler.Calls, Is.EqualTo(1));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("contains space")]
        [TestCase("injected\r\nheader")]
        [TestCase("contains\tcontrol")]
        [TestCase("contains\u007fcontrol")]
        public async Task UnsafeHeaderValuesRequireSignInWithoutSendingARequest(string token)
        {
            var result = await NextendoAccount.Authority.ResolveAsync(token);
            Assert.That(result.Rejected, Is.True);
            Assert.That(AuthorityHandler.Calls, Is.Zero);
        }

        [Test]
        public async Task OversizedCredentialRequiresSignInWithoutRetryingTheAuthority()
        {
            var result = await NextendoAccount.Authority.ResolveAsync(new string('a', 2049));
            Assert.That(result.Rejected, Is.True);
            Assert.That(AuthorityHandler.Calls, Is.Zero);
        }

        [Test]
        public async Task RejectedLegacyCredentialBecomesAnActionableAccountState()
        {
            Windows();
            Legacy(token: "legacy-opaque-credential");
            AuthorityHandler.Status = HttpStatusCode.Unauthorized;
            Assert.That(await NextendoAccount.ProtectLegacyAsync(), Is.EqualTo(NextendoCredentialStorageState.Rejected));
            Assert.That(NextendoAccount.IsLinked, Is.False);
            Assert.That(AuthorityHandler.Calls, Is.EqualTo(1));
        }
    }
}
