using NUnit.Framework;
using Ryujinx.Common.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Ryujinx.AccountSecurity.Tests
{
    [NonParallelizable]
    public class FailedMutationTests : AccountFixture
    {
        private const ulong SecondPid = 1800000099, ThirdPid = 1800000077;

        private void SignInFirst()
        {
            Windows();
            NextendoAccount.Save(Pid, "First", "SW-1234-5678-9012", Token);
            NextendoAccount.SetProfileUserId("first-profile");
            NextendoAccount.SetMiiData("first-mii");
        }

        private static void AssertFirstSession(NextendoIdentitySnapshot before)
        {
            NextendoIdentitySnapshot after = NextendoAccount.GetIdentitySnapshot();
            Assert.Multiple(() =>
            {
                Assert.That(after.Pid, Is.EqualTo(before.Pid));
                Assert.That(after.Token, Is.EqualTo(before.Token));
                Assert.That(after.ProfileUserId, Is.EqualTo(before.ProfileUserId));
                Assert.That(after.Generation, Is.GreaterThan(before.Generation));
                Assert.That(NextendoAccount.Username, Is.EqualTo("First"));
                Assert.That(NextendoAccount.MiiData, Is.EqualTo("first-mii"));
                Assert.That(NextendoAccount.CredentialPersistenceFailed, Is.True);
            });
        }

        [Test]
        public void ConflictingSignInPreservesPreviousIdentityAndAllowsSelectingIt()
        {
            Windows();
            NextendoAccount.Save(SecondPid, "Second", "SW-1111-2222-3333", OtherToken);
            NextendoAccount.SetProfileUserId("second-profile");
            SignInFirst();
            NextendoCredentialFile.Write(Path.Combine(DirectoryPath, "nextendo_accounts", SecondPid + ".txt"),
                Account(SecondPid, OtherToken, "conflicting-profile", ""));
            string beforeDisk = NextendoCredentialFile.Read(Active);
            NextendoIdentitySnapshot before = NextendoAccount.GetIdentitySnapshot();
            Assert.That(NextendoAccount.Save(SecondPid, "Second", "SW-1111-2222-3333", "nx2.second.new"), Is.False);
            AssertFirstSession(before);
            Assert.That(NextendoCredentialFile.Read(Active), Is.EqualTo(beforeDisk));
            Assert.That(NextendoAccount.StorageState, Is.EqualTo(NextendoCredentialStorageState.Conflict));
            Assert.That(NextendoAccount.Activate(Pid), Is.True);
        }

        [TestCase("prepared")]
        [TestCase("journal")]
        [TestCase("write:nextendo_accounts/1800000031.txt")]
        [TestCase("write:nextendo_account.txt")]
        [TestCase("committed")]
        public void InterruptedReauthenticationRestoresPreviousMemorySession(string checkpoint)
        {
            SignInFirst();
            NextendoIdentitySnapshot before = NextendoAccount.GetIdentitySnapshot();
            NextendoAccountStorage.FaultPoint = step => { if (step == checkpoint) throw new IOException("Synthetic interruption."); };
            Assert.That(NextendoAccount.Save(Pid, "Replacement", "SW-1111-2222-3333", OtherToken, isGuest: true), Is.False);
            AssertFirstSession(before);
            Assert.That(NextendoAccount.IsGuest, Is.False);
        }

        [TestCase("profile")]
        [TestCase("mii")]
        [TestCase("name")]
        public void MetadataWriteFailureRestoresPreviousValues(string operation)
        {
            SignInFirst();
            string beforeDisk = NextendoCredentialFile.Read(Active);
            NextendoIdentitySnapshot before = NextendoAccount.GetIdentitySnapshot();
            NextendoAccountStorage.FaultPoint = step => { if (step == "prepared") throw new IOException("Synthetic interruption."); };
            bool? result = null;
            switch (operation)
            {
                case "profile": result = NextendoAccount.SetProfileUserId("replacement-profile"); break;
                case "mii": result = NextendoAccount.SetMiiData("replacement-mii"); break;
                case "name": result = NextendoAccount.UpdateDisplayName(before, "Replacement"); break;
            }
            AssertFirstSession(before);
            Assert.That(NextendoCredentialFile.Read(Active), Is.EqualTo(beforeDisk));
            if (result.HasValue) Assert.That(result.Value, Is.False);
        }

        [Test]
        public void FailedFirstSignInDoesNotCreateAnInMemorySession()
        {
            Windows();
            NextendoAccountStorage.FaultPoint = step => { if (step == "prepared") throw new IOException("Synthetic interruption."); };
            Assert.That(NextendoAccount.Save(Pid, "First", "SW-1234-5678-9012", Token), Is.False);
            Assert.That(NextendoAccount.IsLinked, Is.False);
            Assert.That(NextendoAccount.GetIdentitySnapshot().Pid, Is.Zero);
            Assert.That(NextendoAccount.CredentialPersistenceFailed, Is.True);
            Assert.That(File.Exists(Active), Is.False);
        }

        [TestCase(NextendoCredentialStorageState.Rejected)]
        [TestCase(NextendoCredentialStorageState.IdentityMismatch)]
        public void SignOutSkipsRejectedAccountAndSelectsRemainingValidAccount(NextendoCredentialStorageState rejection)
        {
            Windows();
            NextendoAccount.Save(ThirdPid, "Zed", "SW-1111-2222-3333", "nx2.third.signature");
            NextendoAccount.SetProfileUserId("third-profile");
            NextendoAccount.Save(SecondPid, "Aaron", "SW-1111-2222-3334", OtherToken);
            SignInFirst();
            NextendoAccountStorage.Execute(DirectoryPath, plan =>
            {
                var state = NextendoProfileLinks.Fields(plan.Read(NextendoProfileLinks.StateName));
                state[Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(OtherToken)))] = rejection.ToString();
                plan.Put(NextendoProfileLinks.StateName, string.Join("\n", state.Select(p => p.Key + "=" + p.Value)));
            });
            Assert.That(NextendoAccount.Clear(), Is.True);
            Assert.That(NextendoAccount.GetIdentitySnapshot().Pid, Is.EqualTo(ThirdPid));
            Assert.That(NextendoAccount.ProfileUserId, Is.EqualTo("third-profile"));
            Assert.That(NextendoAccount.All.Single(a => a.Pid == SecondPid).RequiresSignIn, Is.True);
        }

        [Test]
        public void SignOutTriesAnotherAccountAfterAnActivationConflict()
        {
            Windows();
            NextendoAccount.Save(ThirdPid, "Zed", "SW-1111-2222-3333", "nx2.third.signature");
            NextendoAccount.Save(SecondPid, "Aaron", "SW-1111-2222-3334", OtherToken);
            NextendoAccount.SetProfileUserId("expected-second-profile");
            SignInFirst();
            NextendoCredentialFile.Write(Path.Combine(DirectoryPath, "nextendo_accounts", SecondPid + ".txt"),
                Account(SecondPid, OtherToken, "conflicting-second-profile", ""));
            Assert.That(NextendoAccount.Clear(), Is.True);
            Assert.That(NextendoAccount.GetIdentitySnapshot().Pid, Is.EqualTo(ThirdPid));
        }

        [Test]
        public void StartupRecoveryFailureDoesNotCrashOrSealAnUnrecoveredMap()
        {
            Windows();
            string map = Path.Combine(DirectoryPath, "nextendo_profile_links.txt");
            File.WriteAllText(map, Pid + "=first-profile");
            File.WriteAllText(Path.Combine(DirectoryPath, "nextendo_account_transaction.dat"), NextendoCredentialFile.Prefix + "invalid");
            Assert.DoesNotThrow(() => AppDataManager.Initialize(DirectoryPath));
            Assert.That(File.ReadAllText(map), Is.EqualTo(Pid + "=first-profile"));
            Assert.That(NextendoAccount.StorageState, Is.EqualTo(NextendoCredentialStorageState.RecoveryRequired));
        }
    }
}
