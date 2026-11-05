using NUnit.Framework;
using Ryujinx.Common.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Ryujinx.AccountSecurity.Tests
{
    [NonParallelizable]
    public class TextLayoutTests
    {
        private sealed class SimulatedTermination : Exception { }
        private string _root;
        private string Old(string name) => Path.Combine(_root, name);
        private string New(string name) => Path.Combine(_root, "system", "nextendo", name);

        [SetUp]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "nextendo-text-layout-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            NextendoTextPaths.FaultPoint = null;
            NextendoAccountStorage.FaultPoint = null;
        }

        [TearDown]
        public void Cleanup()
        {
            NextendoTextPaths.FaultPoint = null;
            NextendoAccountStorage.FaultPoint = null;
            typeof(NextendoAccount).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
            Assert.That(Path.GetFullPath(_root), Does.StartWith(Path.Combine(Path.GetTempPath(), "nextendo-text-layout-test-")));
            Directory.Delete(_root, true);
        }


        [Test]
        public void Review_CorruptCommittedManifestKeepsOrganizedReadersAndData()
        {
            File.WriteAllText(Old("nextendo_known_badges.txt"), "organized-badges");
            File.WriteAllText(Old("nextendo_account.txt"), "pid=31\nnex_token=nx2.synthetic.signature\n");
            AppDataManager.Initialize(_root);
            const string corrupt = "{\"Version\":1,\"Files\":[";
            File.WriteAllText(New("text-layout-v1.json"), corrupt);
            for (int start = 0; start < 2; start++)
            {
                AppDataManager.Initialize(_root);
                Assert.That(NextendoTextPaths.IsOrganized(_root), Is.True);
                Assert.That(File.ReadAllText(NextendoTextPaths.GetFilePath("nextendo_known_badges.txt")), Is.EqualTo("organized-badges"));
                Assert.That(NextendoTextPaths.GetFilePath("nextendo_account.txt"), Is.EqualTo(New("nextendo_account.txt")));
                Assert.That(NextendoTextPaths.HasPendingCredentialCopy(_root), Is.True);
                Assert.That(File.ReadAllText(New("text-layout-v1.json")), Is.EqualTo(corrupt));
                Assert.Throws<System.Text.Json.JsonException>(() => NextendoAccountStorage.Execute(_root, plan => plan.Put("nextendo_account.txt", "replacement")));
                Assert.That(File.Exists(Old("nextendo_account.txt")), Is.False);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Review_DeletedUncommittedSourceIsArchivedWithoutRestoringSignedOutAccount(bool interruptArchive)
        {
            const string original = "pid=31\nnex_token=nx2.signed-out.signature\n";
            File.WriteAllText(Old("nextendo_account.txt"), original);
            File.WriteAllText(Old("nextendo_known_badges.txt"), "badges");
            NextendoTextPaths.FaultPoint = step => { if (step == "copy:nextendo_account.txt") throw new SimulatedTermination(); };
            Assert.Throws<SimulatedTermination>(() => AppDataManager.Initialize(_root));
            File.Delete(Old("nextendo_account.txt"));
            if (interruptArchive)
            {
                NextendoTextPaths.FaultPoint = step => { if (step == "archive:nextendo_account.txt") throw new SimulatedTermination(); };
                Assert.Throws<SimulatedTermination>(() => AppDataManager.Initialize(_root));
            }
            NextendoTextPaths.FaultPoint = null;
            for (int start = 0; start < 3; start++) AppDataManager.Initialize(_root);
            Assert.That(NextendoTextPaths.IsOrganized(_root), Is.True);
            Assert.That(NextendoTextPaths.HasPendingCredentialCopy(_root), Is.False);
            Assert.That(File.Exists(New("nextendo_account.txt")), Is.False);
            Assert.That(File.Exists(New("text-layout-pending.json")), Is.False);
            string archive = Directory.GetFiles(Path.GetDirectoryName(New("nextendo_account.txt")), "nextendo_account.txt.*.conflict").Single();
            Assert.That(NextendoTextPaths.ReadConflict(archive), Is.EqualTo(Encoding.UTF8.GetBytes(original)));
            if (OperatingSystem.IsWindows()) Assert.That(File.ReadAllText(archive), Does.Not.Contain("nx2.signed-out.signature"));
            else Assert.That(File.GetUnixFileMode(archive), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            NextendoAccountStorage.Execute(_root, plan => plan.Put("nextendo_account.txt", "fresh-account"));
            Assert.That(NextendoCredentialFile.Read(New("nextendo_account.txt")), Is.EqualTo("fresh-account"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Review_LargeNonSensitiveStagingDoesNotBlockAccountWrites(bool interruptArchive)
        {
            string original = new('t', 3_300_000);
            File.WriteAllText(Old("nextendo_known_titles.txt"), original);
            NextendoTextPaths.FaultPoint = step => { if (step == "copy:nextendo_known_titles.txt") throw new SimulatedTermination(); };
            Assert.Throws<SimulatedTermination>(() => AppDataManager.Initialize(_root));
            File.AppendAllText(Old("nextendo_known_titles.txt"), "new-title");
            if (interruptArchive)
            {
                NextendoTextPaths.FaultPoint = step => { if (step == "archive:nextendo_known_titles.txt") throw new SimulatedTermination(); };
                Assert.Throws<SimulatedTermination>(() => AppDataManager.Initialize(_root));
            }
            NextendoTextPaths.FaultPoint = null;
            for (int start = 0; start < 2; start++) AppDataManager.Initialize(_root);
            Assert.That(NextendoTextPaths.HasPendingCredentialCopy(_root), Is.False);
            Assert.That(File.Exists(New("text-layout-pending.json")), Is.False);
            Assert.That(File.ReadAllText(New("nextendo_known_titles.txt")), Is.EqualTo(original + "new-title"));
            string archive = Directory.GetFiles(Path.GetDirectoryName(New("nextendo_known_titles.txt")), "*.raw.conflict").Single();
            Assert.That(File.ReadAllText(archive), Is.EqualTo(original));
            if (!OperatingSystem.IsWindows()) Assert.That(File.GetUnixFileMode(archive), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            NextendoAccountStorage.Execute(_root, plan => plan.Put("nextendo_account.txt", "fresh-account"));
            Assert.That(NextendoCredentialFile.Read(New("nextendo_account.txt")), Is.EqualTo("fresh-account"));
        }

        [TestCase("text-layout-v1.json")]
        [TestCase("text-layout-pending.json")]
        public void Review_NullManifestEntryDoesNotAbortStartup(string name)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(New(name)));
            File.WriteAllText(New(name), "{\"Version\":1,\"Files\":[null]}");
            Assert.DoesNotThrow(() => AppDataManager.Initialize(_root));
            Assert.That(File.ReadAllText(New(name)), Does.Contain("null"));
            Assert.That(NextendoTextPaths.HasPendingCredentialCopy(_root), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Review_OriginalIsNotDeletedWhenCommittedDestinationIsMissingOrChanged(bool changed)
        {
            const string original = "pid=31\nnex_token=nx2.original.signature\n";
            File.WriteAllText(Old("nextendo_account.txt"), original);
            AppDataManager.Initialize(_root);
            File.Move(New("nextendo_account.txt"), Old("nextendo_account.txt"));
            if (changed) File.WriteAllText(New("nextendo_account.txt"), "external-change");
            AppDataManager.Initialize(_root);
            Assert.That(File.Exists(Old("nextendo_account.txt")) || File.Exists(New("nextendo_account.txt")), Is.True);
            if (changed)
            {
                Assert.That(File.ReadAllText(Old("nextendo_account.txt")), Is.EqualTo(original));
                Assert.That(File.ReadAllText(New("nextendo_account.txt")), Is.EqualTo("external-change"));
            }
            else Assert.That(File.ReadAllText(New("nextendo_account.txt")), Is.EqualTo(original));
        }

        [Test]
        public void Review_SensitiveConflictIsVisibleWithoutBlockingIndependentImports()
        {
            File.WriteAllText(Old("nextendo_account.txt"), "pid=31\nnex_token=nx2.original.signature\n");
            AppDataManager.Initialize(_root);
            File.WriteAllText(Old("nextendo_account.txt"), "pid=32\nnex_token=nx2.external.signature\n");
            File.WriteAllText(Old("nextendo_known_badges.txt"), "independent-import");
            AppDataManager.Initialize(_root);
            Assert.Multiple(() =>
            {
                Assert.That(NextendoTextPaths.HasPendingCredentialCopy(_root), Is.True);
                Assert.That(File.Exists(New("nextendo_known_badges.txt")), Is.True);
                Assert.That(File.Exists(Old("nextendo_account.txt")), Is.True);
            });
        }

        [Test]
        public void Review_ChangedSourceRetiresOldPlanAndPreservesItsStagingCopy()
        {
            const string oldText = "pid=31\nnex_token=nx2.original.signature\n";
            const string newText = "pid=31\nnex_token=nx2.renewed.signature\n";
            File.WriteAllText(Old("nextendo_account.txt"), oldText);
            NextendoTextPaths.FaultPoint = step => { if (step == "copy:nextendo_account.txt") throw new SimulatedTermination(); };
            Assert.Throws<SimulatedTermination>(() => AppDataManager.Initialize(_root));
            File.WriteAllText(Old("nextendo_account.txt"), newText);
            NextendoTextPaths.FaultPoint = null;
            for (int start = 0; start < 3; start++) AppDataManager.Initialize(_root);
            Assert.That(NextendoTextPaths.HasPendingCredentialCopy(_root), Is.False);
            Assert.That(File.ReadAllText(New("nextendo_account.txt")), Is.EqualTo(newText));
            Assert.That(File.Exists(New("text-layout-pending.json")), Is.False);
            string[] archives = Directory.GetFiles(Path.GetDirectoryName(New("nextendo_account.txt")), "*.conflict");
            Assert.That(archives.Length, Is.EqualTo(1));
            Assert.That(NextendoTextPaths.ReadConflict(archives[0]), Is.EqualTo(Encoding.UTF8.GetBytes(oldText)));
            if (OperatingSystem.IsWindows()) Assert.That(File.ReadAllText(archives[0]), Does.Not.Contain("nx2.original.signature"));
            else Assert.That(File.GetUnixFileMode(archives[0]), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }

        [Test]
        public void Review_ArchiveInterruptionIsIdempotentAndRetainsAllBytes()
        {
            File.WriteAllText(Old("nextendo_account.txt"), "old-credential");
            NextendoTextPaths.FaultPoint = step => { if (step == "copy:nextendo_account.txt") throw new SimulatedTermination(); };
            Assert.Throws<SimulatedTermination>(() => AppDataManager.Initialize(_root));
            File.WriteAllText(Old("nextendo_account.txt"), "new-credential");
            NextendoTextPaths.FaultPoint = step => { if (step == "archive:nextendo_account.txt") throw new SimulatedTermination(); };
            Assert.Throws<SimulatedTermination>(() => AppDataManager.Initialize(_root));
            NextendoTextPaths.FaultPoint = null;
            AppDataManager.Initialize(_root);
            Assert.That(File.ReadAllText(New("nextendo_account.txt")), Is.EqualTo("new-credential"));
            string[] archives = Directory.GetFiles(Path.GetDirectoryName(New("nextendo_account.txt")), "*.conflict");
            Assert.That(archives.Length, Is.EqualTo(1));
            Assert.That(NextendoTextPaths.ReadConflict(archives[0]), Is.EqualTo(Encoding.UTF8.GetBytes("old-credential")));
            Assert.That(NextendoTextPaths.HasPendingCredentialCopy(_root), Is.False);
        }

        [Test]
        public void Review_StaleWriterRefreshesCommittedLayoutUnderTheStorageLock()
        {
            File.WriteAllText(Old("nextendo_known_badges.txt"), "original");
            using (FileStream held = new(Old("nextendo_known_badges.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
                AppDataManager.Initialize(_root);
            FieldInfo layout = typeof(NextendoTextPaths).GetField("_layout", BindingFlags.Static | BindingFlags.NonPublic);
            object deferred = layout.GetValue(null);
            AppDataManager.Initialize(_root);
            Assert.That(NextendoTextPaths.IsOrganized(_root), Is.True);
            layout.SetValue(null, deferred);
            NextendoAccountStorage.Execute(_root, plan => plan.Put("nextendo_account.txt", "new-account"));
            Assert.That(NextendoTextPaths.IsOrganized(_root), Is.True);
            Assert.That(File.Exists(Old("nextendo_account.txt")), Is.False);
            Assert.That(NextendoCredentialFile.Read(New("nextendo_account.txt")), Is.EqualTo("new-account"));
        }

        [Test]
        public void Review_DanglingDestinationLinkCannotCreateAnExternalFile()
        {
            if (OperatingSystem.IsWindows()) Assert.Ignore("Native symbolic-link integration test.");
            Directory.CreateDirectory(Path.GetDirectoryName(New("nextendo_known_badges.txt")));
            string external = _root + "-external.txt";
            try
            {
                File.WriteAllText(Old("nextendo_known_badges.txt"), "original");
                File.CreateSymbolicLink(New("nextendo_known_badges.txt"), external);
                AppDataManager.Initialize(_root);
                Assert.That(File.Exists(external), Is.False);
                Assert.That(File.ReadAllText(Old("nextendo_known_badges.txt")), Is.EqualTo("original"));
            }
            finally { if (File.Exists(external)) File.Delete(external); }
        }

        [Test]
        public void Review_ThemeCopyPreservesOriginalLastWriteTime()
        {
            File.WriteAllText(Old("nextendo_color_set.txt"), "0");
            DateTime original = DateTime.UtcNow.AddHours(-2);
            File.SetLastWriteTimeUtc(Old("nextendo_color_set.txt"), original);
            original = File.GetLastWriteTimeUtc(Old("nextendo_color_set.txt"));
            AppDataManager.Initialize(_root);
            Assert.That(File.GetLastWriteTimeUtc(New("nextendo_color_set.txt")), Is.EqualTo(original));
        }

        [Test]
        public void OnlyLooseNextendoTxtFilesAreRelocated()
        {
            File.WriteAllText(Old("nextendo_account.txt"), "synthetic-account");
            File.WriteAllText(Old("nextendo_profile_links.txt"), "31=synthetic-profile");
            File.WriteAllText(Old("nextendo_known_badges.txt"), "synthetic-badge");
            string[] untouched = { "Config.json", "notes.txt", "nextendo_setup_done", "nextendo_patchnote_version", "nextendo_account_status.dat",
                "bis/save.bin", "games/game.bin", "profiles/controller.json", "sdcard/file.bin", "service-catalog-v1/catalog.json", "system/Profiles.json", "nextendo_accounts/31.txt" };
            foreach (string relative in untouched)
            {
                string path = Old(relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "untouched:" + relative);
            }
            AppDataManager.Initialize(_root);
            Assert.That(Directory.GetFiles(_root, "nextendo_*.txt"), Is.Empty);
            Assert.That(File.ReadAllText(New("nextendo_account.txt")), Is.EqualTo("synthetic-account"));
            Assert.That(NextendoTextPaths.GetFilePath("nextendo_profile_links.txt"), Is.EqualTo(New("nextendo_profile_links.txt")));
            foreach (string relative in untouched) Assert.That(File.ReadAllText(Old(relative)), Is.EqualTo("untouched:" + relative));
        }

        [Test]
        public void ProtectedCredentialRemainsByteIdenticalAndLoadsWithoutNetwork()
        {
            if (!OperatingSystem.IsWindows()) Assert.Ignore("DPAPI requires Windows.");
            NextendoCredentialFile.Write(Old("nextendo_account.txt"), "pid=31\nnex_token=nx2.synthetic.signature\nprofile_user_id=synthetic-profile\n");
            byte[] before = File.ReadAllBytes(Old("nextendo_account.txt"));
            AppDataManager.Initialize(_root);
            typeof(NextendoAccount).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
            Assert.That(File.ReadAllBytes(New("nextendo_account.txt")), Is.EqualTo(before));
            Assert.That(NextendoAccount.Pid, Is.EqualTo(31));
            Assert.That(NextendoAccount.ProfileUserId, Is.EqualTo("synthetic-profile"));
            AppDataManager.Initialize(_root);
            Assert.That(File.ReadAllBytes(New("nextendo_account.txt")), Is.EqualTo(before));
        }

        [Test]
        public void DifferentFilesInBothLocationsArePreservedAndLegacyStaysActive()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(New("nextendo_account.txt")));
            File.WriteAllText(Old("nextendo_account.txt"), "original");
            File.WriteAllText(New("nextendo_account.txt"), "different");
            AppDataManager.Initialize(_root);
            Assert.That(NextendoTextPaths.IsOrganized(_root), Is.False);
            Assert.That(NextendoTextPaths.GetFilePath("nextendo_account.txt"), Is.EqualTo(Old("nextendo_account.txt")));
            Assert.That(File.ReadAllText(Old("nextendo_account.txt")), Is.EqualTo("original"));
            Assert.That(File.ReadAllText(New("nextendo_account.txt")), Is.EqualTo("different"));
        }

        [TestCase("prepared")]
        [TestCase("copy:nextendo_account.txt")]
        [TestCase("committed")]
        [TestCase("cleanup:nextendo_account.txt")]
        public void InterruptedOrganizationResumesWithoutLosingData(string step)
        {
            File.WriteAllText(Old("nextendo_account.txt"), "synthetic-account");
            File.WriteAllText(Old("nextendo_profile_links.txt"), "31=synthetic-profile");
            NextendoTextPaths.FaultPoint = current => { if (current == step) throw new IOException("Synthetic interruption."); };
            AppDataManager.Initialize(_root);
            Assert.That(File.ReadAllText(NextendoTextPaths.GetFilePath("nextendo_account.txt")), Is.EqualTo("synthetic-account"));
            NextendoTextPaths.FaultPoint = null;
            AppDataManager.Initialize(_root);
            Assert.That(NextendoTextPaths.IsOrganized(_root), Is.True);
            Assert.That(File.ReadAllText(New("nextendo_account.txt")), Is.EqualTo("synthetic-account"));
            Assert.That(NextendoProfileLinks.Read(_root), Is.EqualTo("31=synthetic-profile"));
            Assert.That(Directory.GetFiles(_root, "nextendo_*.txt"), Is.Empty);
            Assert.That(File.Exists(Path.Combine(_root, "system", "nextendo", "text-layout-pending.json")), Is.False);
        }

        [Test]
        public void OwnedPartialCopyCanResumeAfterAnInterruptedWrite()
        {
            File.WriteAllText(Old("nextendo_known_badges.txt"), "synthetic-badges");
            NextendoTextPaths.FaultPoint = step => { if (step == "prepared") throw new SimulatedTermination(); };
            Assert.Throws<SimulatedTermination>(() => AppDataManager.Initialize(_root));
            File.WriteAllText(New("nextendo_known_badges.txt"), "synthetic-");
            NextendoTextPaths.FaultPoint = null;
            AppDataManager.Initialize(_root);
            Assert.That(File.ReadAllText(New("nextendo_known_badges.txt")), Is.EqualTo("synthetic-badges"));
            Assert.That(File.Exists(Old("nextendo_known_badges.txt")), Is.False);
        }

        [Test]
        public void EditedStagingFileIsNotOverwritten()
        {
            File.WriteAllText(Old("nextendo_known_badges.txt"), "original");
            NextendoTextPaths.FaultPoint = step => { if (step == "prepared") throw new SimulatedTermination(); };
            Assert.Throws<SimulatedTermination>(() => AppDataManager.Initialize(_root));
            File.WriteAllText(New("nextendo_known_badges.txt"), "edited");
            NextendoTextPaths.FaultPoint = null;
            AppDataManager.Initialize(_root);
            Assert.That(File.ReadAllText(New("nextendo_known_badges.txt")), Is.EqualTo("edited"));
            Assert.That(File.ReadAllText(Old("nextendo_known_badges.txt")), Is.EqualTo("original"));
            Assert.That(NextendoTextPaths.IsOrganized(_root), Is.False);
        }

        [Test]
        public void LockedSourceDefersMigrationAndRetriesAtNextStartup()
        {
            File.WriteAllText(Old("nextendo_account.txt"), "synthetic-account");
            using (FileStream held = new(Old("nextendo_account.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                AppDataManager.Initialize(_root);
                Assert.That(NextendoTextPaths.IsOrganized(_root), Is.False);
            }
            AppDataManager.Initialize(_root);
            Assert.That(File.ReadAllText(New("nextendo_account.txt")), Is.EqualTo("synthetic-account"));
        }

        [Test]
        public void LegacyFilesCopiedAfterFirstEmptyLaunchAreImportedOnNextStartup()
        {
            AppDataManager.Initialize(_root);
            File.WriteAllText(Old("nextendo_known_badges.txt"), "later-copy");
            AppDataManager.Initialize(_root);
            Assert.That(File.ReadAllText(New("nextendo_known_badges.txt")), Is.EqualTo("later-copy"));
            Assert.That(File.Exists(Old("nextendo_known_badges.txt")), Is.False);
        }

        [Test]
        public void LockedUncommittedCredentialCopyCannotBeReportedAsProtected()
        {
            if (!OperatingSystem.IsWindows()) Assert.Ignore("Windows file sharing integration test.");
            string original = "pid=31\nnex_token=nx2.synthetic.signature\nprofile_user_id=synthetic-profile\n";
            File.WriteAllText(Old("nextendo_account.txt"), original);
            FileStream held = null;
            try
            {
                NextendoTextPaths.FaultPoint = step =>
                {
                    if (step != "copy:nextendo_account.txt") return;
                    held = new FileStream(New("nextendo_account.txt"), FileMode.Open, FileAccess.Read, FileShare.Read);
                    throw new IOException("Synthetic interruption.");
                };
                AppDataManager.Initialize(_root);
                typeof(NextendoAccount).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
                Assert.That(NextendoTextPaths.HasPendingCredentialCopy(_root), Is.True);
                NextendoAccount.Save(31, "Synthetic", "SW-1234-5678-9012", "nx2.new.synthetic.signature");
                Assert.That(NextendoAccount.StorageState, Is.EqualTo(NextendoCredentialStorageState.RecoveryRequired));
                Assert.That(NextendoAccount.CredentialPersistenceFailed, Is.True);
                Assert.That(NextendoAccount.Clear(), Is.False);
                Assert.That(File.ReadAllText(Old("nextendo_account.txt")), Is.EqualTo(original));
            }
            finally { held?.Dispose(); }
            NextendoTextPaths.FaultPoint = null;
            AppDataManager.Initialize(_root);
            Assert.That(NextendoTextPaths.HasPendingCredentialCopy(_root), Is.False);
            Assert.That(File.ReadAllText(New("nextendo_account.txt")), Is.EqualTo(original));
            Assert.That(File.Exists(Old("nextendo_account.txt")), Is.False);
        }

        [Test]
        public void ReadonlyOriginalCredentialRequiresCleanupBeforeReportingProtectionComplete()
        {
            if (!OperatingSystem.IsWindows()) Assert.Ignore("Windows readonly-file integration test.");
            string original = "pid=31\nnex_token=nx2.synthetic.signature\n";
            File.WriteAllText(Old("nextendo_account.txt"), original);
            File.SetAttributes(Old("nextendo_account.txt"), FileAttributes.ReadOnly);
            try
            {
                AppDataManager.Initialize(_root);
                Assert.That(NextendoTextPaths.IsOrganized(_root), Is.True);
                Assert.That(NextendoTextPaths.HasPendingCredentialCopy(_root), Is.True);
                typeof(NextendoAccount).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
                Assert.That(NextendoAccount.StorageState, Is.EqualTo(NextendoCredentialStorageState.RecoveryRequired));
                Assert.That(File.ReadAllText(New("nextendo_account.txt")), Is.EqualTo(original));
            }
            finally { File.SetAttributes(Old("nextendo_account.txt"), FileAttributes.Normal); }
            AppDataManager.Initialize(_root);
            Assert.That(NextendoTextPaths.HasPendingCredentialCopy(_root), Is.False);
            Assert.That(File.Exists(Old("nextendo_account.txt")), Is.False);
        }

        [Test]
        public void VersionOneAccountJournalRecoversBeforeTextRelocation()
        {
            if (!OperatingSystem.IsWindows()) Assert.Ignore("DPAPI requires Windows.");
            NextendoAccountStorage.FaultPoint = step => { if (step == "journal") throw new IOException("Synthetic interruption."); };
            Assert.Throws<IOException>(() => NextendoAccountStorage.Execute(_root, plan =>
            {
                plan.Put("nextendo_account.txt", "recovered-account");
                plan.Put("nextendo_profile_links.txt", "31=synthetic-profile", false);
            }));
            NextendoAccountStorage.FaultPoint = null;
            AppDataManager.Initialize(_root);
            Assert.That(NextendoCredentialFile.Read(New("nextendo_account.txt")), Is.EqualTo("recovered-account"));
            Assert.That(NextendoProfileLinks.Read(_root), Is.EqualTo("31=synthetic-profile"));
            Assert.That(File.Exists(Old("nextendo_account.txt")), Is.False);
            Assert.That(NextendoAccountStorage.HasPending(_root), Is.False);
        }

        [Test]
        public void VersionTwoJournalRecoversToOrganizedPaths()
        {
            if (!OperatingSystem.IsWindows()) Assert.Ignore("DPAPI requires Windows.");
            AppDataManager.Initialize(_root);
            NextendoAccountStorage.FaultPoint = step => { if (step == "write:nextendo_account.txt") throw new IOException("Synthetic interruption."); };
            Assert.Throws<IOException>(() => NextendoAccountStorage.Execute(_root, plan =>
            {
                plan.Put("nextendo_account.txt", "recovered-account");
                plan.Put("nextendo_profile_links.txt", "31=synthetic-profile", false);
            }));
            NextendoAccountStorage.FaultPoint = null;
            AppDataManager.Initialize(_root);
            Assert.That(NextendoCredentialFile.Read(New("nextendo_account.txt")), Is.EqualTo("recovered-account"));
            Assert.That(NextendoProfileLinks.Read(_root), Is.EqualTo("31=synthetic-profile"));
            Assert.That(File.Exists(Old("nextendo_account.txt")), Is.False);
        }

        [TestCase("../nextendo_account.txt")]
        [TestCase("nextendo_accounts/31.txt")]
        [TestCase("Config.json")]
        public void NonLooseTextPathsCannotBeRedirected(string name) =>
            Assert.Throws<InvalidDataException>(() => NextendoTextPaths.GetFilePath(name));
    }
}
