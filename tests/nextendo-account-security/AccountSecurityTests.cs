using NUnit.Framework;
using Ryujinx.Common.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.AccountSecurity.Tests
{
    public abstract class AccountFixture
    {
        protected const ulong Pid = 1800000031;
        protected const string Token = "nx2.synthetic.signature", OtherToken = "nx2.other.signature";
        protected string DirectoryPath;
        protected Handler AuthorityHandler;
        private NextendoIdentityAuthority _previousAuthority;
        protected string Active => Path.Combine(DirectoryPath, "nextendo_account.txt");
        protected string Canonical => Path.Combine(DirectoryPath, "nextendo_accounts", Pid + ".txt");
        protected sealed class Handler : HttpMessageHandler
        {
            internal HttpStatusCode Status = HttpStatusCode.OK;
            internal string Body;
            internal int Calls;
            internal Uri Destination;
            internal Func<Task<HttpResponseMessage>> Response;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref Calls);
                Destination = request.RequestUri;
                return Response?.Invoke() ?? Task.FromResult(new HttpResponseMessage(Status)
                {
                    Content = new StringContent(Body ?? "{\"account\":{\"pid\":1800000031,\"username\":\"Canonical\",\"friend_code\":\"SW-1234-5678-9012\",\"email\":\"fixture@example.invalid\"}}"),
                    RequestMessage = request,
                });
            }
        }
        [SetUp]
        public void Setup()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "nextendo-revision-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            AppDataManager.Initialize(DirectoryPath);
            Reset();
            _previousAuthority = NextendoAccount.Authority;
            AuthorityHandler = new Handler();
            NextendoAccount.Authority = new NextendoIdentityAuthority(AuthorityHandler);
        }
        protected static void Reset()
        {
            Set("_loaded", false); Set("_identityValid", false); Set("_pid", 0UL); Set("_nexToken", "");
            typeof(NextendoServerOverride).GetField("_charge", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
            NextendoAccountStorage.FaultPoint = null;
        }
        protected static void Set(string field, object value) => typeof(NextendoAccount).GetField(field, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value);
        [TearDown]
        public void Cleanup()
        {
            NextendoAccountStorage.FaultPoint = null;
            NextendoAccount.Authority = _previousAuthority;
            Reset();
            Assert.That(Path.GetFullPath(DirectoryPath), Does.StartWith(Path.Combine(Path.GetTempPath(), "nextendo-revision-test-")));
            Directory.Delete(DirectoryPath, true);
        }
        protected static string Account(ulong pid = Pid, string token = Token, string profile = "local-profile", string mii = "synthetic-mii") =>
            $"pid={pid}\nusername=Display Name\nfriend_code=SW-1234-5678-9012\nnex_token={token}\nprofile_user_id={profile}\nmii_data={mii}\nis_guest=0\n";
        protected void Legacy(string path = null, ulong pid = Pid, string token = Token, string profile = "local-profile", string mii = "synthetic-mii")
        {
            path ??= Active;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Account(pid, token, profile, mii));
        }
        protected static void Windows() { if (!OperatingSystem.IsWindows()) Assert.Ignore("Windows DPAPI integration test."); }
    }

    [NonParallelizable]
    public class AccountSecurityTests : AccountFixture
    {
        [Test]
        public async Task EditedLegacyPidRequiresSignInWithoutOverwritingProfiles()
        {
            Windows(); Legacy(pid: 1800000067);
            byte[] before = File.ReadAllBytes(Active);
            Assert.That(await NextendoAccount.ProtectLegacyAsync(), Is.EqualTo(NextendoCredentialStorageState.IdentityMismatch));
            Assert.That(NextendoAccount.IsLinked, Is.False);
            Assert.That(NextendoAccount.NexToken, Is.Empty);
            Assert.That(File.ReadAllBytes(Active), Is.EqualTo(before));
            Assert.That(NextendoAccount.ProfileUserId, Is.EqualTo("local-profile"));
            Assert.That(AuthorityHandler.Destination.ToString(), Is.EqualTo("https://nextendo.network/api/me"));
        }
        [TestCase(HttpStatusCode.Unauthorized)]
        [TestCase(HttpStatusCode.Forbidden)]
        public async Task RejectionPreservesRecoveryFiles(HttpStatusCode status)
        {
            Windows(); Legacy(); byte[] before = File.ReadAllBytes(Active); AuthorityHandler.Status = status;
            Assert.That(await NextendoAccount.ProtectLegacyAsync(), Is.EqualTo(NextendoCredentialStorageState.Rejected));
            Assert.That(NextendoAccount.IsLinked, Is.False);
            Assert.That(File.ReadAllBytes(Active), Is.EqualTo(before));
        }
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        [TestCase(HttpStatusCode.TooManyRequests)]
        [TestCase(HttpStatusCode.Redirect)]
        public async Task UnavailableAuthorityPreservesLegacySession(HttpStatusCode status)
        {
            Windows(); Legacy(); byte[] before = File.ReadAllBytes(Active); AuthorityHandler.Status = status;
            Assert.That(await NextendoAccount.ProtectLegacyAsync(), Is.EqualTo(NextendoCredentialStorageState.Unavailable));
            Assert.That(NextendoAccount.Pid, Is.EqualTo(Pid));
            Assert.That(File.ReadAllBytes(Active), Is.EqualTo(before));
        }
        [TestCase("{}")]
        [TestCase("{\"account\":{\"pid\":\"001\"}}")]
        [TestCase("{\"account\":{\"pid\":0}}")]
        public async Task MalformedAuthorityCannotMigrateLegacy(string response)
        {
            Windows(); Legacy(); AuthorityHandler.Body = response;
            Assert.That(await NextendoAccount.ProtectLegacyAsync(), Is.EqualTo(NextendoCredentialStorageState.Unavailable));
            Assert.That(File.ReadAllText(Active), Is.EqualTo(Account()));
        }
        [Test]
        public async Task MigrationProtectsAliasesAndPreservesMetadata()
        {
            Windows(); Legacy(); string alias = Path.Combine(DirectoryPath, "nextendo_accounts", "1800000067.txt"); Legacy(alias, pid: 1800000067);
            Assert.That(await NextendoAccount.ProtectLegacyAsync(), Is.EqualTo(NextendoCredentialStorageState.Protected));
            foreach (string path in new[] { Active, Canonical, alias })
            {
                Assert.That(File.ReadAllText(path), Does.StartWith(NextendoCredentialFile.Prefix));
                Assert.That(File.ReadAllText(path), Does.Not.Contain(Token));
                Assert.That(NextendoCredentialFile.Read(path), Does.Contain("profile_user_id=local-profile\n"));
            }
            Assert.That(NextendoAccount.All.Count, Is.EqualTo(1));
            Assert.That(NextendoAccountStorage.HasPending(DirectoryPath), Is.False);
        }
        [TestCase("other-profile", "synthetic-mii")]
        [TestCase("local-profile", "other-mii")]
        public async Task MigrationConflictWritesNothing(string profile, string mii)
        {
            Windows(); Legacy(); Legacy(Canonical, profile: profile, mii: mii);
            byte[] active = File.ReadAllBytes(Active), canonical = File.ReadAllBytes(Canonical);
            Assert.That(await NextendoAccount.ProtectLegacyAsync(), Is.EqualTo(NextendoCredentialStorageState.Conflict));
            Assert.That(File.ReadAllBytes(Active), Is.EqualTo(active)); Assert.That(File.ReadAllBytes(Canonical), Is.EqualTo(canonical));
            Assert.That(File.Exists(Path.Combine(DirectoryPath, "nextendo_profile_links.txt")), Is.False);
            Assert.That(NextendoAccountStorage.HasPending(DirectoryPath), Is.False);
        }
        [Test]
        public void ProtectedFileRejectsCorruption()
        {
            Windows(); NextendoCredentialFile.Write(Active, Account()); Assert.That(NextendoCredentialFile.Read(Active), Is.EqualTo(Account()));
            byte[] bytes = Convert.FromBase64String(File.ReadAllText(Active)[NextendoCredentialFile.Prefix.Length..]); bytes[bytes.Length / 2] ^= 1;
            File.WriteAllText(Active, NextendoCredentialFile.Prefix + Convert.ToBase64String(bytes));
            Assert.Throws<CryptographicException>(() => NextendoCredentialFile.Read(Active));
        }
        [Test]
        public void CorruptProtectedFileCannotFallBackOrBeOverwrittenByLogin()
        {
            File.WriteAllText(Active, NextendoCredentialFile.Prefix + "bad\npid=31\nnex_token=fixture"); byte[] before = File.ReadAllBytes(Active);
            Assert.That(NextendoAccount.IsLinked, Is.False); NextendoAccount.Save(Pid, "Display", "SW-1234-5678-9012", Token);
            Assert.That(NextendoAccount.CredentialPersistenceFailed, Is.True); Assert.That(File.ReadAllBytes(Active), Is.EqualTo(before));
        }
        [Test]
        public void StaleRejectionDoesNotInvalidateNewAccount()
        {
            NextendoAccount.Save(Pid, "Display", "SW-1234-5678-9012", OtherToken);
            Assert.That(NextendoAccount.RejectCredential(Token), Is.False); Assert.That(NextendoAccount.NexToken, Is.EqualTo(OtherToken));
            Assert.That(NextendoAccount.RejectCredential(OtherToken), Is.True); Assert.That(NextendoAccount.Pid, Is.Zero);
        }
        [Test]
        public void ConcurrentReadersNeverCallAuthority()
        {
            Legacy(); Parallel.For(0, 16, _ => Assert.That(NextendoAccount.GetIdentitySnapshot().Pid, Is.EqualTo(Pid)));
            Assert.That(AuthorityHandler.Calls, Is.Zero); Assert.That(File.ReadAllText(Active), Is.EqualTo(Account()));
        }
        [Test]
        public void OversizedLegacyCannotLoad()
        {
            File.WriteAllText(Active, new string('x', 128 * 1024 + 1)); Assert.That(NextendoAccount.IsLinked, Is.False); Assert.That(AuthorityHandler.Calls, Is.Zero);
        }
        [Test]
        public async Task LogoutDeletesAliasesAndPreservesProfileLinksAndSaves()
        {
            Windows(); Legacy(); Legacy(Path.Combine(DirectoryPath, "nextendo_accounts", "1800000067.txt"), pid: 1800000067);
            await NextendoAccount.ProtectLegacyAsync(); string save = Path.Combine(DirectoryPath, "synthetic-save.bin"); File.WriteAllBytes(save, new byte[] { 1, 2, 3 });
            string links = File.ReadAllText(Path.Combine(DirectoryPath, "nextendo_profile_links.txt")); Assert.That(NextendoAccount.Clear(), Is.True); Reset();
            Assert.That(NextendoAccount.IsLinked, Is.False); Assert.That(NextendoAccount.All, Is.Empty);
            Assert.That(File.ReadAllText(Path.Combine(DirectoryPath, "nextendo_profile_links.txt")), Is.EqualTo(links)); Assert.That(File.ReadAllBytes(save), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }
        [Test]
        public void OfflineAccountSelectionPreservesBothAccounts()
        {
            NextendoAccount.Save(Pid, "First", "SW-1234-5678-9012", Token); NextendoAccount.SetProfileUserId("first-profile");
            Legacy(Path.Combine(DirectoryPath, "nextendo_accounts", "42.txt"), 42, OtherToken, "second-profile"); AuthorityHandler.Status = HttpStatusCode.ServiceUnavailable;
            Assert.That(NextendoAccount.Activate(42), Is.True); Assert.That(NextendoAccount.ProfileUserId, Is.EqualTo("second-profile"));
            Assert.That(NextendoAccount.Activate(Pid), Is.True); Assert.That(NextendoAccount.ProfileUserId, Is.EqualTo("first-profile")); Assert.That(AuthorityHandler.Calls, Is.Zero);
        }
        [Test]
        public void FailedSelectionPreservesCurrentSession()
        {
            NextendoAccount.Save(Pid, "Display", "SW-1234-5678-9012", Token); string previous = NextendoCredentialFile.Read(Active);
            Legacy(Path.Combine(DirectoryPath, "nextendo_accounts", "42.txt"), 43, OtherToken);
            Assert.That(NextendoAccount.Activate(42), Is.False); Assert.That(NextendoAccount.Pid, Is.EqualTo(Pid)); Assert.That(NextendoCredentialFile.Read(Active), Is.EqualTo(previous));
        }
        [Test]
        public void UnreadableUnrelatedAccountDoesNotBlockLogout()
        {
            NextendoAccount.Save(Pid, "Display", "SW-1234-5678-9012", Token); File.WriteAllText(Path.Combine(DirectoryPath, "nextendo_accounts", "123.txt"), NextendoCredentialFile.Prefix + "bad");
            Assert.That(NextendoAccount.All.Count, Is.EqualTo(1)); Assert.That(NextendoAccount.Clear(), Is.True); Assert.That(NextendoAccount.All, Is.Empty);
        }
        [Test]
        public void StorageFailureIsVisibleAndNeverFallsBackToPlaintext()
        {
            Windows(); File.WriteAllText(Path.Combine(DirectoryPath, "nextendo_accounts"), "blocked directory fixture"); NextendoAccount.Save(Pid, "Display", "SW-1234-5678-9012", Token);
            Assert.That(NextendoAccount.CredentialPersistenceFailed, Is.True); Assert.That(NextendoAccount.StorageState, Is.EqualTo(NextendoCredentialStorageState.RecoveryRequired));
            Assert.That(NextendoAccount.IsLinked, Is.False); Assert.That(File.Exists(Active), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(DirectoryPath, "nextendo_account_transaction.dat")), Does.Not.Contain(Token));
        }
        [Test]
        public void SnapshotDiagnosticsExcludeCredential() => Assert.That(new NextendoIdentitySnapshot(Pid, Token, "local", 1).ToString(), Does.Not.Contain(Token));
        [Test]
        public void LoginResponseCannotUndoLogout()
        {
            long generation = NextendoAccount.GetIdentitySnapshot().Generation; Assert.That(NextendoAccount.Clear(), Is.True);
            Assert.Throws<InvalidOperationException>(() => NextendoAccount.Save(Pid, "Display", "SW-1234-5678-9012", Token, expectedGeneration: generation));
        }
        [Test]
        public async Task PendingMigrationCannotUndoLogoutOrHoldAccountLock()
        {
            Windows(); Legacy(); TaskCompletionSource<HttpResponseMessage> response = new(TaskCreationOptions.RunContinuationsAsynchronously); AuthorityHandler.Response = () => response.Task;
            Task<NextendoCredentialStorageState> migration = NextendoAccount.ProtectLegacyAsync(); Assert.That(AuthorityHandler.Calls, Is.EqualTo(1));
            Assert.That(NextendoAccount.Pid, Is.EqualTo(Pid)); Assert.That(NextendoAccount.Clear(), Is.True);
            response.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"account\":{\"pid\":1800000031,\"username\":\"Canonical\",\"friend_code\":\"SW-1234-5678-9012\"}}") });
            await migration; Assert.That(NextendoAccount.IsLinked, Is.False); Assert.That(File.Exists(Active), Is.False);
        }
        [Test]
        public async Task TimeoutKeepsLegacySessionRecoverable()
        {
            Windows(); Legacy(); AuthorityHandler.Response = () => Task.FromException<HttpResponseMessage>(new TaskCanceledException());
            Assert.That(await NextendoAccount.ProtectLegacyAsync(), Is.EqualTo(NextendoCredentialStorageState.Unavailable)); Assert.That(NextendoAccount.IsLinked, Is.True);
        }
        [Test]
        public async Task OversizedAuthorityCannotMigrate()
        {
            Windows(); Legacy(); AuthorityHandler.Body = new string('x', 2 * 1024 * 1024 + 1);
            Assert.That(await NextendoAccount.ProtectLegacyAsync(), Is.EqualTo(NextendoCredentialStorageState.Unavailable));
        }
        [TestCase("prepared")]
        [TestCase("journal")]
        [TestCase("write:nextendo_accounts/1800000031.txt")]
        [TestCase("write:nextendo_account.txt")]
        [TestCase("write:nextendo_profile_links.txt")]
        [TestCase("committed")]
        public async Task InterruptedMigrationRecoversIdempotently(string step)
        {
            Windows(); Legacy(); NextendoAccountStorage.FaultPoint = current => { if (current == step) throw new IOException("Synthetic interruption."); };
            await NextendoAccount.ProtectLegacyAsync(); Assert.That(NextendoAccount.CredentialPersistenceFailed, Is.True); Reset();
            Assert.That(NextendoAccount.IsLinked, Is.True); Assert.That(NextendoAccount.ProfileUserId, Is.EqualTo("local-profile")); Assert.That(NextendoAccountStorage.HasPending(DirectoryPath), Is.False);
            Assert.That(NextendoAccount.StorageState, Is.EqualTo(step == "prepared" ? NextendoCredentialStorageState.LegacyPending : NextendoCredentialStorageState.Protected));
            byte[] before = File.ReadAllBytes(Active); Reset(); Assert.That(NextendoAccount.Pid, Is.EqualTo(Pid)); Assert.That(File.ReadAllBytes(Active), Is.EqualTo(before));
        }
        [Test]
        public async Task RecoveryNeverOverwritesExternalChange()
        {
            Windows(); Legacy(); NextendoAccountStorage.FaultPoint = step => { if (step == "journal") throw new IOException("Synthetic interruption."); };
            await NextendoAccount.ProtectLegacyAsync(); string changed = Account(token: OtherToken); File.WriteAllText(Active, changed); Reset();
            Assert.That(NextendoAccount.IsLinked, Is.False); Assert.That(NextendoAccount.StorageState, Is.EqualTo(NextendoCredentialStorageState.RecoveryRequired)); Assert.That(File.ReadAllText(Active), Is.EqualTo(changed));
        }
        [Test]
        public async Task ConfirmedMismatchStaysDisabledAfterRestart()
        {
            Windows(); Legacy(pid: 1800000067); await NextendoAccount.ProtectLegacyAsync(); Reset();
            Assert.That(NextendoAccount.IsLinked, Is.False); Assert.That(NextendoAccount.StorageState, Is.EqualTo(NextendoCredentialStorageState.IdentityMismatch));
        }
        [Test]
        public void RejectedTokenStaysDisabledButNewLoginWorks()
        {
            Windows(); NextendoAccount.Save(Pid, "First", "SW-1234-5678-9012", Token); NextendoAccount.RejectCredential(Token); Reset();
            Assert.That(NextendoAccount.IsLinked, Is.False); NextendoAccount.Save(Pid, "Second", "SW-1234-5678-9012", OtherToken); Reset();
            Assert.That(NextendoAccount.IsLinked, Is.True); Assert.That(NextendoAccount.NexToken, Is.EqualTo(OtherToken));
        }
        [Test]
        public void PendingLegacyCannotDowngradeProtectedCopy()
        {
            Windows(); Legacy(); Directory.CreateDirectory(Path.GetDirectoryName(Canonical)); NextendoCredentialFile.Write(Canonical, Account());
            byte[] before = File.ReadAllBytes(Canonical); Assert.That(NextendoAccount.IsLinked, Is.True);
            NextendoAccount.UpdateDisplayName(NextendoAccount.GetIdentitySnapshot(), "New Name");
            Assert.That(NextendoAccount.StorageState, Is.EqualTo(NextendoCredentialStorageState.Conflict)); Assert.That(File.ReadAllBytes(Canonical), Is.EqualTo(before));
        }
        [Test]
        public void ExternalEditCannotBeSilentlyOverwrittenByMetadata()
        {
            Legacy(); NextendoIdentitySnapshot expected = NextendoAccount.GetIdentitySnapshot(); string external = Account(token: OtherToken); File.WriteAllText(Active, external);
            NextendoAccount.UpdateDisplayName(expected, "New Name"); Assert.That(NextendoAccount.CredentialPersistenceFailed, Is.True); Assert.That(File.ReadAllText(Active), Is.EqualTo(external));
        }
        [Test]
        public async Task SignInAfterMismatchPreservesThePreviousProfileRecord()
        {
            Windows(); Legacy(pid: 1800000067); await NextendoAccount.ProtectLegacyAsync();
            NextendoAccount.Save(Pid, "Recovered", "SW-1234-5678-9012", OtherToken);
            Assert.That(NextendoAccount.IsLinked, Is.True); Assert.That(NextendoAccount.Pid, Is.EqualTo(Pid));
            string previous = Path.Combine(DirectoryPath, "nextendo_accounts", "1800000067.txt");
            Assert.That(NextendoCredentialFile.Read(previous), Does.Contain("profile_user_id=local-profile\n"));
            Assert.That(NextendoAccount.Activate(1800000067), Is.False);
        }
        [Test]
        public void LoginCannotDiscardAConflictingPreviousProfile()
        {
            Legacy(); Legacy(Canonical, profile: "other-profile"); Assert.That(NextendoAccount.IsLinked, Is.True);
            byte[] active = File.ReadAllBytes(Active), stored = File.ReadAllBytes(Canonical);
            Assert.Throws<IOException>(() => NextendoAccount.Save(42, "Second", "SW-1234-5678-9012", OtherToken));
            Assert.That(NextendoAccount.Pid, Is.EqualTo(Pid)); Assert.That(File.ReadAllBytes(Active), Is.EqualTo(active)); Assert.That(File.ReadAllBytes(Canonical), Is.EqualTo(stored));
        }
        [Test]
        public void FailedLogoutIsVisibleAndFinishesOnRestart()
        {
            Windows(); NextendoAccount.Save(Pid, "Display", "SW-1234-5678-9012", Token);
            using (FileStream blocked = new(Active, FileMode.Open, FileAccess.Read, FileShare.Read)) { Assert.That(NextendoAccount.Clear(), Is.False); Assert.That(NextendoAccount.IsLinked, Is.True); }
            Reset(); Assert.That(NextendoAccount.IsLinked, Is.False);
        }
    }
}
