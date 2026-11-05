using NUnit.Framework;
using Ryujinx.Common.Configuration;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace Ryujinx.AccountSecurity.Tests
{
    // Regression contracts replacing the characterization of the unsafe experiment.
    [NonParallelizable]
    public class CompatibilityAuditTests : AccountFixture
    {
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        [TestCase(HttpStatusCode.TooManyRequests)]
        public void ProtectedAccountLoadsOfflineWithoutAuthorityOrWrites(HttpStatusCode status)
        {
            Windows(); NextendoCredentialFile.Write(Active, Account()); byte[] before = File.ReadAllBytes(Active); AuthorityHandler.Status = status;
            Assert.That(NextendoAccount.IsLinked, Is.True); Assert.That(NextendoAccount.Pid, Is.EqualTo(Pid));
            Assert.That(File.ReadAllBytes(Active), Is.EqualTo(before)); Assert.That(AuthorityHandler.Calls, Is.Zero);
        }
        [Test]
        public void ReadsNeverRevalidateOrChangeGeneration()
        {
            Legacy(); NextendoIdentitySnapshot before = NextendoAccount.GetIdentitySnapshot(); AuthorityHandler.Status = HttpStatusCode.ServiceUnavailable;
            for (int i = 0; i < 100; i++) Assert.That(NextendoAccount.GetIdentitySnapshot(), Is.EqualTo(before));
            Assert.That(File.ReadAllText(Active), Is.EqualTo(Account())); Assert.That(AuthorityHandler.Calls, Is.Zero);
        }
        [Test]
        public void DisplayMetadataPreservesNameGuestFlagAndMakesNoAuthorityCall()
        {
            NextendoAccount.Save(Pid, "Display Name", "SW-1234-5678-9012", Token, isGuest: true);
            Assert.That(NextendoAccount.UpdateDisplayName(NextendoAccount.GetIdentitySnapshot(), "New Display Name"), Is.True);
            Assert.That(NextendoAccount.Username, Is.EqualTo("New Display Name")); Assert.That(NextendoAccount.IsGuest, Is.True); Assert.That(AuthorityHandler.Calls, Is.Zero);
        }
        [Test]
        public void LoginPersistsWithoutAdditionalAuthorityRequest()
        {
            Windows(); AuthorityHandler.Status = HttpStatusCode.ServiceUnavailable; NextendoAccount.Save(Pid, "Display Name", "SW-1234-5678-9012", Token);
            Assert.That(NextendoAccount.IsLinked, Is.True); Assert.That(NextendoAccount.StorageState, Is.EqualTo(NextendoCredentialStorageState.Protected)); Assert.That(AuthorityHandler.Calls, Is.Zero);
        }
        [Test]
        public async Task CustomServerModeNeverResolvesLocalMetadata()
        {
            Legacy(); NextendoServerOverride.Save(true, "127.0.0.2", "127.0.0.3");
            Assert.That(NextendoAccount.ProfileUserId, Is.EqualTo("local-profile")); Assert.That(NextendoAccount.MiiData, Is.EqualTo("synthetic-mii"));
            Assert.That(NextendoAccount.NexToken, Is.Empty); Assert.That(NextendoAccount.IsLinked, Is.False); await NextendoAccount.ProtectLegacyAsync(); Assert.That(AuthorityHandler.Calls, Is.Zero);
            NextendoServerOverride.Save(false, "", ""); Assert.That(NextendoAccount.Pid, Is.EqualTo(Pid));
        }
        [Test]
        public void ProtectedEnvelopeSupportsFullLegacyTextLimit()
        {
            Windows(); string legacy = new('x', 128 * 1024); NextendoCredentialFile.Write(Active, legacy); Assert.That(NextendoCredentialFile.Read(Active), Is.EqualTo(legacy));
        }
        [Test]
        public void LateMetadataCannotRenameAnotherAccount()
        {
            NextendoAccount.Save(Pid, "First", "SW-1234-5678-9012", Token); NextendoIdentitySnapshot previous = NextendoAccount.GetIdentitySnapshot();
            NextendoAccount.Save(42, "Second", "SW-1234-5678-9012", OtherToken); Assert.That(NextendoAccount.UpdateDisplayName(previous, "Wrong"), Is.False); Assert.That(NextendoAccount.Username, Is.EqualTo("Second"));
        }
        [Test]
        public void LegacyAccountIsPreservedBeforeSwitchingToNewLogin()
        {
            Legacy(); Assert.That(NextendoAccount.IsLinked, Is.True); NextendoAccount.Save(42, "Second", "SW-1234-5678-9012", OtherToken);
            Assert.That(NextendoCredentialFile.Read(Canonical), Does.Contain("profile_user_id=local-profile\n")); Assert.That(NextendoAccount.Activate(Pid), Is.True); Assert.That(NextendoAccount.ProfileUserId, Is.EqualTo("local-profile"));
        }
    }
}
