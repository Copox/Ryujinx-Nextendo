using NUnit.Framework;
using Ryujinx.Common.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Ryujinx.AccountSecurity.Tests
{
    [NonParallelizable]
    public class PortableStorageTests : AccountFixture
    {
        private string JournalPath => Path.Combine(DirectoryPath, "nextendo_account_transaction.dat");
        private string StatePath => Path.Combine(DirectoryPath, NextendoProfileLinks.StateName);
        private string MapPath => Path.Combine(DirectoryPath, NextendoProfileLinks.Name);
        private static string Rejection(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))) + "=Rejected";

        [TestCase("journal")]
        [TestCase("write:nextendo_account.txt")]
        [TestCase("write:nextendo_accounts/1800000031.txt")]
        [TestCase("committed")]
        public void PlatformJournalRecoversInterruptedMultiFileWrites(string checkpoint)
        {
            NextendoAccountStorage.Execute(DirectoryPath, plan =>
            {
                plan.Put("nextendo_account.txt", "old-active");
                plan.Put("nextendo_accounts/" + Pid + ".txt", "old-copy");
            });
            string save = Path.Combine(DirectoryPath, "save.bin");
            File.WriteAllBytes(save, new byte[] { 7, 8, 9 });
            NextendoAccountStorage.FaultPoint = step => { if (step == checkpoint) throw new IOException("Synthetic interruption."); };
            Assert.Throws<IOException>(() => NextendoAccountStorage.Execute(DirectoryPath, plan =>
            {
                plan.Put("nextendo_account.txt", "new-active");
                plan.Put("nextendo_accounts/" + Pid + ".txt", "new-copy");
            }));
            Assert.That(File.Exists(JournalPath), Is.True);
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(JournalPath), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
                Assert.That(File.ReadAllText(JournalPath), Does.StartWith(NextendoAccountStorage.PortableJournalPrefix));
            }
            NextendoAccountStorage.FaultPoint = null;
            NextendoAccountStorage.Recover(DirectoryPath);
            NextendoAccountStorage.Recover(DirectoryPath);
            Assert.That(NextendoCredentialFile.Read(Active), Is.EqualTo("new-active"));
            Assert.That(NextendoCredentialFile.Read(Canonical), Is.EqualTo("new-copy"));
            Assert.That(File.Exists(JournalPath), Is.False);
            Assert.That(File.ReadAllBytes(save), Is.EqualTo(new byte[] { 7, 8, 9 }));
        }

        [Test]
        public void RecoveryDoesNotOverwriteAnExternalChangeOnAnyPlatform()
        {
            NextendoAccountStorage.FaultPoint = step => { if (step == "journal") throw new IOException("Synthetic interruption."); };
            Assert.Throws<IOException>(() => NextendoAccountStorage.Execute(DirectoryPath, plan =>
            {
                plan.Put("nextendo_account.txt", "candidate-active");
                plan.Put("nextendo_accounts/" + Pid + ".txt", "candidate-copy");
            }));
            NextendoAccountStorage.FaultPoint = null;
            File.WriteAllText(Active, "external-change");
            Assert.Throws<IOException>(() => NextendoAccountStorage.Recover(DirectoryPath));
            Assert.That(File.ReadAllText(Active), Is.EqualTo("external-change"));
            Assert.That(File.Exists(Canonical), Is.False);
            Assert.That(File.Exists(JournalPath), Is.True);
        }

        [Test]
        public void ImportedPortableJournalRecoversBeforeWindowsSealsLegacyState()
        {
            NextendoAccountStorage.Journal journal = new(1, new List<NextendoAccountStorage.Entry>
            {
                new("nextendo_account.txt", null, Encoding.UTF8.GetBytes(Account())),
                new(NextendoProfileLinks.Name, null, Encoding.UTF8.GetBytes(Pid + "=local-profile")),
                new(NextendoProfileLinks.StateName, null, Encoding.UTF8.GetBytes(Rejection(OtherToken))),
            });
            NextendoCredentialFile.WriteBytes(JournalPath, NextendoAccountStorage.EncodePortableJournal(journal));
            AppDataManager.Initialize(DirectoryPath);
            Assert.That(NextendoAccount.IsLinked, Is.True);
            Assert.That(File.Exists(JournalPath), Is.False);
            Assert.That(NextendoProfileLinks.ReadState(DirectoryPath), Does.Contain(Rejection(OtherToken)));
            Assert.That(NextendoProfileLinks.Read(DirectoryPath), Is.EqualTo(Pid + "=local-profile"));
            if (OperatingSystem.IsWindows())
            {
                Assert.That(File.ReadAllText(StatePath), Does.StartWith(NextendoCredentialFile.Prefix));
                Assert.That(File.ReadAllText(MapPath), Does.StartWith(NextendoProfileLinks.Prefix));
            }
            Assert.That(AuthorityHandler.Calls, Is.Zero);
        }

        [TestCase("not-json")]
        [TestCase("{\"Version\":1,\"Entries\":[null]}")]
        [TestCase("{\"Version\":1,\"Entries\":[{}]}")]
        [TestCase("{\"Version\":1,\"Entries\":[{\"Path\":\"../escape\",\"Data\":null}]}")]
        public void MalformedPortableJournalIsPreservedWithoutCrashingStartup(string json)
        {
            string original = NextendoAccountStorage.PortableJournalPrefix + json;
            File.WriteAllText(JournalPath, original);
            Assert.DoesNotThrow(() => AppDataManager.Initialize(DirectoryPath));
            Assert.That(File.ReadAllText(JournalPath), Is.EqualTo(original));
            Assert.That(NextendoAccount.StorageState, Is.EqualTo(NextendoCredentialStorageState.RecoveryRequired));
        }

        [Test]
        public void WindowsImportsRecognizedPlaintextStatusWithoutRemovingRejections()
        {
            Windows();
            Legacy();
            File.WriteAllText(StatePath, Rejection(Token));
            AppDataManager.Initialize(DirectoryPath);
            Assert.That(File.ReadAllText(StatePath), Does.StartWith(NextendoCredentialFile.Prefix));
            Assert.That(NextendoAccount.IsLinked, Is.False);
            Assert.That(NextendoAccount.StorageState, Is.EqualTo(NextendoCredentialStorageState.Rejected));
            Assert.That(NextendoProfileLinks.ReadState(DirectoryPath), Does.Contain(Rejection(Token)));
        }

        [TestCase("_profile_links_protected=1")]
        [TestCase("_profile_links_protected=0")]
        [TestCase("_profile_links_snapshot=")]
        [TestCase("unknown=Rejected")]
        public void WindowsNeverAcceptsPlaintextProtectionMetadataAsLegacyState(string text)
        {
            Windows();
            File.WriteAllText(StatePath, text);
            AppDataManager.Initialize(DirectoryPath);
            Assert.That(File.ReadAllText(StatePath), Is.EqualTo(text));
            Assert.That(NextendoProfileLinks.Issue(DirectoryPath), Is.EqualTo(NextendoCredentialStorageState.Failed));
        }
    }
}
