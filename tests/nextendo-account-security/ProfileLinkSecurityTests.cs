using NUnit.Framework;
using Ryujinx.Common.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ryujinx.AccountSecurity.Tests
{
    [NonParallelizable]
    public class ProfileLinkSecurityTests : AccountFixture
    {
        private string Links => NextendoTextPaths.GetFilePath(NextendoProfileLinks.Name);
        private string State => Path.Combine(DirectoryPath, NextendoProfileLinks.StateName);
        private const string Mapping = "1800000031=remembered-profile\n42=second-profile";

        private void ProtectMap(string text = Mapping)
        {
            Windows();
            File.WriteAllText(Links, text);
            NextendoProfileLinks.Initialize(DirectoryPath);
            Assert.That(File.ReadAllText(Links), Does.StartWith(NextendoProfileLinks.Prefix));
        }

        [Test]
        public void LegacyMapMigratesAutomaticallyWithoutNetworkAndWithoutRewritingAgain()
        {
            ProtectMap();
            Assert.That(NextendoProfileLinks.Read(DirectoryPath), Is.EqualTo(Mapping));
            Assert.That(File.ReadAllText(Links), Does.Not.Contain("remembered-profile"));
            Assert.That(File.ReadAllText(State), Does.Not.Contain("remembered-profile"));
            byte[] before = File.ReadAllBytes(Links), state = File.ReadAllBytes(State);
            NextendoProfileLinks.Initialize(DirectoryPath);
            Assert.That(File.ReadAllBytes(Links), Is.EqualTo(before));
            Assert.That(File.ReadAllBytes(State), Is.EqualTo(state));
            Assert.That(AuthorityHandler.Calls, Is.Zero);
        }

        [Test]
        public void PlaintextReplacementCannotChangeAssociation()
        {
            ProtectMap();
            File.WriteAllText(Links, "1800000031=edited-profile");
            Assert.That(NextendoProfileLinks.Read(DirectoryPath), Is.EqualTo(Mapping));
            Assert.That(NextendoProfileLinks.Issue(DirectoryPath), Is.EqualTo(NextendoCredentialStorageState.RecoveryRequired));
            NextendoProfileLinks.Initialize(DirectoryPath);
            Assert.That(File.ReadAllText(Links), Does.StartWith(NextendoProfileLinks.Prefix));
            Assert.That(NextendoProfileLinks.Read(DirectoryPath), Is.EqualTo(Mapping));
        }

        [Test]
        public void ValidButDifferentEncryptedMapCannotReplaceRecoverySnapshot()
        {
            ProtectMap();
            File.WriteAllBytes(Links, NextendoProfileLinks.Encode("1800000031=edited-profile"));
            Assert.That(NextendoProfileLinks.Read(DirectoryPath), Is.EqualTo(Mapping));
            NextendoProfileLinks.Initialize(DirectoryPath);
            Assert.That(NextendoProfileLinks.Read(DirectoryPath), Is.EqualTo(Mapping));
        }

        [Test]
        public void DeletedMapRecoversWithoutLosingAnyAssociation()
        {
            ProtectMap();
            File.Delete(Links);
            Assert.That(NextendoProfileLinks.Read(DirectoryPath), Is.EqualTo(Mapping));
            NextendoProfileLinks.Initialize(DirectoryPath);
            Assert.That(File.ReadAllText(Links), Does.StartWith(NextendoProfileLinks.Prefix));
            Assert.That(NextendoProfileLinks.Read(DirectoryPath), Is.EqualTo(Mapping));
        }

        [TestCase("nextendo-profile-links-protected-v1\ninvalid-base64")]
        [TestCase("31=edited-profile\n")]
        public void CorruptOrDowngradedMapRecoversFromProtectedState(string replacement)
        {
            ProtectMap();
            File.WriteAllText(Links, replacement);
            NextendoProfileLinks.Initialize(DirectoryPath);
            Assert.That(NextendoProfileLinks.Read(DirectoryPath), Is.EqualTo(Mapping));
            Assert.That(NextendoProfileLinks.Issue(DirectoryPath), Is.EqualTo(NextendoCredentialStorageState.None));
        }

        [Test]
        public void AccountEnvelopeCannotBeUsedAsProfileLinkEnvelope()
        {
            ProtectMap();
            File.WriteAllBytes(Links, NextendoCredentialFile.Encode("1800000031=edited-profile"));
            NextendoProfileLinks.Initialize(DirectoryPath);
            Assert.That(NextendoProfileLinks.Read(DirectoryPath), Is.EqualTo(Mapping));
            Assert.Throws<CryptographicException>(() => NextendoCredentialFile.Read(Links, allowPlaintext: false));
        }

        [Test]
        public void LogoutAndSignInReuseOriginalProfileAndPreserveSaveBytes()
        {
            Windows();
            NextendoAccount.Save(Pid, "Synthetic", "SW-1234-5678-9012", Token);
            NextendoAccount.SetProfileUserId("remembered-profile");
            string save = Path.Combine(DirectoryPath, "synthetic-save.bin");
            File.WriteAllBytes(save, new byte[] { 7, 8, 9 });
            Assert.That(NextendoAccount.Clear(), Is.True);
            Reset();
            AppDataManager.Initialize(DirectoryPath);
            NextendoAccount.Save(Pid, "Synthetic", "SW-1234-5678-9012", Token);
            Assert.That(NextendoAccount.ProfileUserId, Is.EqualTo("remembered-profile"));
            Assert.That(File.ReadAllBytes(save), Is.EqualTo(new byte[] { 7, 8, 9 }));
        }

        [Test]
        public void MissingLegacyMapCanBeRebuiltFromProtectedAccountBeforeLogout()
        {
            Windows();
            NextendoAccount.Save(Pid, "Synthetic", "SW-1234-5678-9012", Token);
            NextendoAccount.SetProfileUserId("remembered-profile");
            File.Delete(Links);
            File.Delete(State);
            AppDataManager.Initialize(DirectoryPath);
            Assert.That(NextendoProfileLinks.Fields(NextendoProfileLinks.Read(DirectoryPath))[Pid.ToString()], Is.EqualTo("remembered-profile"));
            Assert.That(NextendoAccount.Clear(), Is.True);
            NextendoAccount.Save(Pid, "Synthetic", "SW-1234-5678-9012", Token);
            Assert.That(NextendoAccount.ProfileUserId, Is.EqualTo("remembered-profile"));
        }

        [Test]
        public void TrustedReauthenticationCanClearSameTokenRejectionWithoutLosingSnapshot()
        {
            Windows();
            NextendoAccount.Save(Pid, "Synthetic", "SW-1234-5678-9012", Token);
            NextendoAccount.SetProfileUserId("remembered-profile");
            NextendoAccount.RejectCredential(Token);
            NextendoAccount.Save(Pid, "Synthetic", "SW-1234-5678-9012", Token);
            Reset();
            Assert.That(NextendoAccount.IsLinked, Is.True);
            Assert.That(NextendoAccount.ProfileUserId, Is.EqualTo("remembered-profile"));
        }

        [Test]
        public void PreMigrationConflictWithProtectedAccountIsNotAccepted()
        {
            Windows();
            NextendoAccount.Save(Pid, "Synthetic", "SW-1234-5678-9012", Token);
            NextendoAccount.SetProfileUserId("remembered-profile");
            File.Delete(State);
            File.WriteAllText(Links, Pid + "=edited-profile");
            NextendoProfileLinks.Initialize(DirectoryPath);
            Assert.That(NextendoProfileLinks.Issue(DirectoryPath), Is.EqualTo(NextendoCredentialStorageState.Conflict));
            NextendoIdentitySnapshot before = NextendoAccount.GetIdentitySnapshot();
            Assert.Throws<NextendoProfileLinks.ConflictException>(() => NextendoAccount.Save(42, "Other", "SW-1234-5678-9012", OtherToken));
            Assert.That(NextendoAccount.GetIdentitySnapshot(), Is.EqualTo(before));
            Assert.That(File.ReadAllText(Links), Is.EqualTo(Pid + "=edited-profile"));
        }

        [Test]
        public void UnreadableMapAndStateDoNotSilentlyCreateAnEmptyAssociation()
        {
            Windows();
            NextendoAccount.Save(Pid, "Synthetic", "SW-1234-5678-9012", Token);
            NextendoAccount.SetProfileUserId("remembered-profile");
            NextendoIdentitySnapshot before = NextendoAccount.GetIdentitySnapshot();
            File.WriteAllText(Links, NextendoProfileLinks.Prefix + "invalid");
            File.WriteAllText(State, NextendoCredentialFile.Prefix + "invalid");
            Assert.Catch<Exception>(() => NextendoAccount.Save(42, "Other", "SW-1234-5678-9012", OtherToken));
            Assert.That(NextendoAccount.GetIdentitySnapshot(), Is.EqualTo(before));
            Assert.That(NextendoAccount.ProfileUserId, Is.EqualTo("remembered-profile"));
        }

        [Test]
        public void RecoverySnapshotSupportsMapWhoseBase64ExceedsOldStateLimit()
        {
            Windows();
            string text = string.Join("\n", Enumerable.Range(1, 1000).Select(i => i + "=" + new string('a', 110)));
            Assert.That(Encoding.UTF8.GetByteCount(text), Is.LessThan(NextendoCredentialFile.MaxPlaintextBytes));
            ProtectMap(text);
            string state = NextendoProfileLinks.ReadState(DirectoryPath);
            Assert.That(Encoding.UTF8.GetByteCount(state), Is.GreaterThan(NextendoCredentialFile.MaxPlaintextBytes));
            Assert.That(NextendoProfileLinks.Fields(NextendoProfileLinks.Read(DirectoryPath)).Count, Is.EqualTo(1000));
        }

        [TestCase("prepared")]
        [TestCase("journal")]
        [TestCase("write:nextendo_profile_links.txt")]
        [TestCase("write:nextendo_account_status.dat")]
        [TestCase("committed")]
        public void InterruptedMapMigrationRecoversAllAssociations(string step)
        {
            Windows();
            File.WriteAllText(Links, Mapping);
            NextendoAccountStorage.FaultPoint = point => { if (point == step) throw new IOException("Synthetic interruption."); };
            NextendoProfileLinks.Initialize(DirectoryPath);
            NextendoAccountStorage.FaultPoint = null;
            AppDataManager.Initialize(DirectoryPath);
            Assert.That(NextendoProfileLinks.Read(DirectoryPath), Is.EqualTo(Mapping));
            Assert.That(File.ReadAllText(Links), Does.StartWith(NextendoProfileLinks.Prefix));
            Assert.That(NextendoAccountStorage.HasPending(DirectoryPath), Is.False);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void OldPlaintextJournalCanStillBeRecoveredAndMigrated(int version)
        {
            Windows();
            NextendoAccountStorage.Journal old = new(version,
            [new(NextendoProfileLinks.Name, null, Encoding.UTF8.GetBytes(Mapping))]);
            NextendoCredentialFile.Write(Path.Combine(DirectoryPath, "nextendo_account_transaction.dat"), JsonSerializer.Serialize(old));
            AppDataManager.Initialize(DirectoryPath);
            Assert.That(NextendoProfileLinks.Read(DirectoryPath), Is.EqualTo(Mapping));
            Assert.That(File.ReadAllText(Links), Does.StartWith(NextendoProfileLinks.Prefix));
        }

        [TestCase("0=profile")]
        [TestCase("31=one\n31=two")]
        [TestCase("not-a-pid=profile")]
        [TestCase("31=")]
        public void MalformedOrAmbiguousLegacyMapIsPreserved(string text)
        {
            Windows();
            File.WriteAllText(Links, text);
            NextendoProfileLinks.Initialize(DirectoryPath);
            Assert.That(File.ReadAllText(Links), Is.EqualTo(text));
            Assert.That(File.Exists(State), Is.False);
            Assert.That(NextendoProfileLinks.Issue(DirectoryPath), Is.Not.EqualTo(NextendoCredentialStorageState.None));
        }
    }
}
