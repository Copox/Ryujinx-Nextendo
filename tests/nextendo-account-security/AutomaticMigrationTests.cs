using NUnit.Framework;
using Ryujinx.Common.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.AccountSecurity.Tests
{
    [NonParallelizable]
    public class AutomaticMigrationTests : AccountFixture
    {
        private static HttpResponseMessage Verified(ulong pid) => new(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"account\":{{\"pid\":{pid},\"username\":\"Synthetic\",\"friend_code\":\"SW-1234-5678-9012\"}}}}"),
        };

        [Test]
        public async Task InactiveMismatchForAnotherTokenOfSameIdentityDoesNotBlockActiveSession()
        {
            Windows();
            Legacy();
            string stale = Path.Combine(DirectoryPath, "nextendo_accounts", "42.txt");
            Legacy(stale, 42, OtherToken);
            byte[] before = File.ReadAllBytes(stale);
            AuthorityHandler.Response = () => Task.FromResult(Verified(Pid));
            await NextendoAccount.MigrateLegacyAccountsAsync();
            Assert.That(NextendoAccount.BackgroundMigrationIssue, Is.EqualTo(NextendoCredentialStorageState.IdentityMismatch));
            Assert.That(NextendoAccount.StorageState, Is.EqualTo(NextendoCredentialStorageState.Protected));
            Assert.That(NextendoAccount.IsLinked, Is.True);
            Assert.That(NextendoAccount.GetIdentitySnapshot().Pid, Is.EqualTo(Pid));
            Assert.That(NextendoAccount.GetIdentitySnapshot().Token, Is.EqualTo(Token));
            Assert.That(File.ReadAllBytes(stale), Is.EqualTo(before));
            Assert.That(NextendoAccount.All.Single(a => a.Pid == 42).RequiresSignIn, Is.True);
            Assert.That(NextendoAccount.All.Single(a => a.Pid == Pid).RequiresSignIn, Is.False);
            NextendoIdentitySnapshot current = NextendoAccount.GetIdentitySnapshot();
            Assert.That(NextendoAccount.Activate(42), Is.False);
            Assert.That(NextendoAccount.GetIdentitySnapshot().Pid, Is.EqualTo(current.Pid));
            Assert.That(NextendoAccount.GetIdentitySnapshot().Token, Is.EqualTo(current.Token));
            Reset();
            Assert.That(NextendoAccount.IsLinked, Is.True, "An inactive rejection must not block the active account after restart.");
        }

        [Test]
        public async Task BackgroundPassProtectsAllAccountsWithoutSwitchingIdentity()
        {
            Windows();
            Legacy();
            string other = Path.Combine(DirectoryPath, "nextendo_accounts", "42.txt");
            Legacy(other, 42, OtherToken, "other-profile");
            NextendoIdentitySnapshot before = NextendoAccount.GetIdentitySnapshot();
            AuthorityHandler.Response = () => Task.FromResult(Verified(AuthorityHandler.Calls == 1 ? Pid : 42UL));
            await NextendoAccount.MigrateLegacyAccountsAsync();
            Assert.That(NextendoAccount.GetIdentitySnapshot(), Is.EqualTo(before));
            foreach (string path in new[] { Active, Canonical, other })
                Assert.That(File.ReadAllText(path), Does.StartWith(NextendoCredentialFile.Prefix));
            Assert.That(NextendoCredentialFile.Read(other), Does.Contain("profile_user_id=other-profile\n"));
            Assert.That(AuthorityHandler.Calls, Is.EqualTo(2));
            await NextendoAccount.MigrateLegacyAccountsAsync();
            Assert.That(AuthorityHandler.Calls, Is.EqualTo(2), "Protected records must never be periodically revalidated.");
        }

        [Test]
        public async Task AutomaticRetryPreservesOfflineSessionThenMigrates()
        {
            Windows();
            Legacy();
            AuthorityHandler.Status = HttpStatusCode.ServiceUnavailable;
            await NextendoAccount.MigrateLegacyAccountsAsync();
            Assert.That(NextendoAccount.IsLinked, Is.True);
            Assert.That(File.ReadAllText(Active), Is.EqualTo(Account()));
            Assert.That(NextendoAccount.BackgroundMigrationIssue, Is.EqualTo(NextendoCredentialStorageState.None));
            AuthorityHandler.Status = HttpStatusCode.OK;
            await NextendoAccount.MigrateLegacyAccountsAsync();
            Assert.That(NextendoAccount.StorageState, Is.EqualTo(NextendoCredentialStorageState.Protected));
        }

        [Test]
        public async Task ConcurrentAutomaticRequestsShareOnePass()
        {
            Windows();
            Legacy();
            TaskCompletionSource<HttpResponseMessage> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
            AuthorityHandler.Response = () => response.Task;
            Task first = NextendoAccount.MigrateLegacyAccountsAsync();
            Task second = NextendoAccount.MigrateLegacyAccountsAsync();
            Assert.That(second, Is.SameAs(first));
            Assert.That(SpinWait.SpinUntil(() => AuthorityHandler.Calls == 1, TimeSpan.FromSeconds(3)), Is.True);
            Assert.That(NextendoAccount.Pid, Is.EqualTo(Pid));
            response.SetResult(Verified(Pid));
            await Task.WhenAll(first, second);
            Assert.That(AuthorityHandler.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task CustomServerModePreventsAutomaticAuthorityRequests()
        {
            Windows();
            Legacy();
            Legacy(Path.Combine(DirectoryPath, "nextendo_accounts", "42.txt"), 42, OtherToken);
            NextendoServerOverride.Save(true, "127.0.0.2", "127.0.0.3");
            await NextendoAccount.MigrateLegacyAccountsAsync();
            Assert.That(AuthorityHandler.Calls, Is.Zero);
            Assert.That(File.ReadAllText(Active), Is.EqualTo(Account()));
        }

        [Test]
        public async Task ModeChangeDuringRequestCancelsMigrationWrites()
        {
            Windows();
            Legacy();
            TaskCompletionSource<HttpResponseMessage> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
            AuthorityHandler.Response = () => response.Task;
            Task migration = NextendoAccount.MigrateLegacyAccountsAsync();
            Assert.That(SpinWait.SpinUntil(() => AuthorityHandler.Calls == 1, TimeSpan.FromSeconds(3)), Is.True);
            NextendoServerOverride.Save(true, "127.0.0.2", "127.0.0.3");
            response.SetResult(Verified(Pid));
            await migration;
            Assert.That(File.ReadAllText(Active), Is.EqualTo(Account()));
            Assert.That(File.Exists(Canonical), Is.False);
        }

        [Test]
        public async Task LogoutDuringAutomaticMigrationCannotRestoreOldAccount()
        {
            Windows();
            Legacy();
            TaskCompletionSource<HttpResponseMessage> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
            AuthorityHandler.Response = () => response.Task;
            Task migration = NextendoAccount.MigrateLegacyAccountsAsync();
            Assert.That(SpinWait.SpinUntil(() => AuthorityHandler.Calls == 1, TimeSpan.FromSeconds(3)), Is.True);
            Assert.That(NextendoAccount.Clear(), Is.True);
            response.SetResult(Verified(Pid));
            await migration;
            Assert.That(NextendoAccount.IsLinked, Is.False);
            Assert.That(File.Exists(Active), Is.False);
        }

        [Test]
        public async Task StoredAccountConflictNeverChangesActiveAccount()
        {
            Windows();
            NextendoCredentialFile.Write(Active, Account());
            string other = Path.Combine(DirectoryPath, "nextendo_accounts", "42.txt");
            string alias = Path.Combine(DirectoryPath, "nextendo_accounts", "43.txt");
            Legacy(other, 42, OtherToken, "profile-a");
            Legacy(alias, 42, OtherToken, "profile-b");
            byte[] active = File.ReadAllBytes(Active), saved = File.ReadAllBytes(other), savedAlias = File.ReadAllBytes(alias);
            AuthorityHandler.Response = () => Task.FromResult(Verified(42));
            await NextendoAccount.MigrateLegacyAccountsAsync();
            Assert.That(NextendoAccount.Pid, Is.EqualTo(Pid));
            Assert.That(File.ReadAllBytes(Active), Is.EqualTo(active));
            Assert.That(File.ReadAllBytes(other), Is.EqualTo(saved));
            Assert.That(File.ReadAllBytes(alias), Is.EqualTo(savedAlias));
            Assert.That(NextendoAccount.BackgroundMigrationIssue, Is.EqualTo(NextendoCredentialStorageState.Conflict));
        }

        [Test]
        public async Task LegacyAliasesOfProtectedActiveAccountNeedNoNetwork()
        {
            Windows();
            NextendoCredentialFile.Write(Active, Account());
            string alias = Path.Combine(DirectoryPath, "nextendo_accounts", "43.txt");
            Legacy(alias);
            await NextendoAccount.MigrateLegacyAccountsAsync();
            Assert.That(File.ReadAllText(alias), Does.StartWith(NextendoCredentialFile.Prefix));
            Assert.That(AuthorityHandler.Calls, Is.Zero);
        }

        [Test]
        public async Task AccountsMigrateWithoutAnActiveSessionFile()
        {
            Windows();
            Legacy(Canonical);
            await NextendoAccount.MigrateLegacyAccountsAsync();
            Assert.That(File.ReadAllText(Canonical), Does.StartWith(NextendoCredentialFile.Prefix));
            Assert.That(File.Exists(Active), Is.False);
            Assert.That(NextendoAccount.IsLinked, Is.False);
        }

        [TestCase(HttpStatusCode.ServiceUnavailable)]
        [TestCase(HttpStatusCode.Unauthorized)]
        public async Task StoredAccountFailureDoesNotDisableActiveAccount(HttpStatusCode status)
        {
            Windows();
            NextendoCredentialFile.Write(Active, Account());
            string other = Path.Combine(DirectoryPath, "nextendo_accounts", "42.txt");
            Legacy(other, 42, OtherToken, "other-profile");
            byte[] before = File.ReadAllBytes(other);
            AuthorityHandler.Status = status;
            await NextendoAccount.MigrateLegacyAccountsAsync();
            Assert.That(NextendoAccount.Pid, Is.EqualTo(Pid));
            Assert.That(NextendoAccount.NexToken, Is.EqualTo(Token));
            Assert.That(File.ReadAllBytes(other), Is.EqualTo(before));
            if (status == HttpStatusCode.Unauthorized) Assert.That(NextendoAccount.Activate(42), Is.False);
        }

        [Test]
        public async Task SelectingAccountDuringStoredVerificationKeepsSelectedIdentity()
        {
            Windows();
            NextendoCredentialFile.Write(Active, Account());
            string other = Path.Combine(DirectoryPath, "nextendo_accounts", "42.txt");
            Legacy(other, 42, OtherToken, "other-profile");
            TaskCompletionSource<HttpResponseMessage> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
            AuthorityHandler.Response = () => response.Task;
            Task migration = NextendoAccount.MigrateLegacyAccountsAsync();
            Assert.That(SpinWait.SpinUntil(() => AuthorityHandler.Calls == 1, TimeSpan.FromSeconds(3)), Is.True);
            Assert.That(NextendoAccount.Activate(42), Is.True);
            NextendoIdentitySnapshot selected = NextendoAccount.GetIdentitySnapshot();
            response.SetResult(Verified(42));
            await migration;
            Assert.That(NextendoAccount.GetIdentitySnapshot(), Is.EqualTo(selected));
            Assert.That(NextendoAccount.ProfileUserId, Is.EqualTo("other-profile"));
        }

        [Test]
        public async Task InterruptedActiveMigrationDefersOtherAccountsUntilRecovery()
        {
            Windows();
            Legacy();
            string other = Path.Combine(DirectoryPath, "nextendo_accounts", "42.txt");
            Legacy(other, 42, OtherToken, "other-profile");
            byte[] before = File.ReadAllBytes(other);
            NextendoAccountStorage.FaultPoint = step => { if (step == "journal") throw new IOException("Synthetic interruption."); };
            await NextendoAccount.MigrateLegacyAccountsAsync();
            Assert.That(AuthorityHandler.Calls, Is.EqualTo(1));
            Assert.That(NextendoAccount.BackgroundMigrationIssue, Is.EqualTo(NextendoCredentialStorageState.RecoveryRequired));
            Assert.That(File.ReadAllBytes(other), Is.EqualTo(before));
        }
    }
}
