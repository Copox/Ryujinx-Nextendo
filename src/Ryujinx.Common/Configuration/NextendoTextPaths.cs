using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Ryujinx.Common.Configuration
{
    /// <summary>Groups loose Nextendo text files without moving emulator directories or account copies.</summary>
    public static class NextendoTextPaths
    {
        private const string ReadyName = "text-layout-v1.json";
        private const string PendingName = "text-layout-pending.json";
        private const string ConflictPrefix = "nextendo-text-conflict-protected-v1\n";
        private const string PortableConflictPrefix = "nextendo-text-conflict-v1\n";
        private const int MaxConflictTextBytes = 4 * 1024 * 1024;
        private static readonly byte[] ConflictPurpose = Encoding.UTF8.GetBytes("Nextendo/Ryujinx/TextConflict/v1");
        private static readonly object _sync = new();
        private sealed record Layout(string Root, bool Organized, bool PendingCredentialCopy = false);
        internal sealed record Item(string Name, string Hash, bool DestinationExisted);
        internal sealed record Manifest(int Version, List<Item> Files, string Id = null);
        private static Layout _layout;
        internal static Action<string> FaultPoint;

        public static string GetFilePath(string name) => GetFilePath(AppDataManager.BaseDirPath ?? ".", name);

        internal static string GetFilePath(string root, string name) => IsOrganized(root)
            ? OrganizedPath(root, name) : Path.Combine(root, ValidateName(name));

        internal static bool IsOrganized(string root)
        {
            Layout layout = Volatile.Read(ref _layout);
            return layout != null && layout.Organized && string.Equals(layout.Root, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
        }

        internal static bool HasPendingCredentialCopy(string root)
        {
            Layout layout = Volatile.Read(ref _layout);
            return layout != null && layout.PendingCredentialCopy && string.Equals(layout.Root, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
        }

        internal static string OrganizedPath(string root, string name) => Path.Combine(root, "system", "nextendo", ValidateName(name));

        private static string ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name) ||
                !name.StartsWith("nextendo_", StringComparison.Ordinal) || !name.EndsWith(".txt", StringComparison.Ordinal) ||
                name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')))
                throw new InvalidDataException("Invalid Nextendo text filename.");
            return name;
        }

        internal static void Initialize(string root)
        {
            lock (_sync)
            {
                root = Path.GetFullPath(root);
                string folder = Path.Combine(root, "system", "nextendo");
                string ready = Path.Combine(folder, ReadyName);
                string pending = Path.Combine(folder, PendingName);
                bool organized = false;
                bool planCommitted = false;
                bool readingManifest = false;
                Manifest currentPlan = null;
                Volatile.Write(ref _layout, new Layout(root, false));
                try
                {
                    using FileStream storageLock = NextendoAccountStorage.Acquire(root);
                    NextendoAccountStorage.RecoverLocked(root);
                    RejectLink(Path.Combine(root, "system"));
                    RejectLink(folder);
                    Directory.CreateDirectory(folder);
                    // A damaged commit marker must not redirect readers to the obsolete root.
                    organized = File.Exists(ready);
                    readingManifest = organized;
                    Manifest committed = organized ? ReadManifest(ready) : new(1, []);
                    readingManifest = false;
                    Volatile.Write(ref _layout, new Layout(root, organized));
                    if (File.Exists(pending))
                    {
                        readingManifest = true;
                        currentPlan = ReadManifest(pending);
                        readingManifest = false;
                        if (organized && committed.Id != null && currentPlan.Id == committed.Id)
                        {
                            Cleanup(root, committed);
                            File.Delete(pending);
                            currentPlan = null;
                        }
                        else if (SourcesChanged(root, currentPlan))
                        {
                            RetireChangedPlan(root, currentPlan);
                            File.Delete(pending);
                            currentPlan = null;
                        }
                    }
                    if (organized) Cleanup(root, committed);
                    bool conflicts = false;
                    Manifest plan = currentPlan ?? Prepare(root, out conflicts);
                    if ((!organized && conflicts) || (organized && plan.Files.Count == 0))
                    {
                        Volatile.Write(ref _layout, new Layout(root, organized, HasSensitiveCopy(root, organized)));
                        if (conflicts) LogDeferred();
                        return;
                    }
                    currentPlan = plan;
                    if (!File.Exists(pending)) SaveManifest(pending, plan);
                    FaultPoint?.Invoke("prepared");
                    foreach (Item item in plan.Files)
                    {
                        string source = Path.Combine(root, item.Name);
                        string destination = OrganizedPath(root, item.Name);
                        RejectLink(source);
                        RejectLink(destination);
                        if (Hash(source) != item.Hash) throw new IOException("A Nextendo text source changed during organization.");
                        CopyVerified(source, destination, item);
                        FaultPoint?.Invoke("copy:" + item.Name);
                    }
                    foreach (Item item in plan.Files)
                    {
                        if (Hash(Path.Combine(root, item.Name)) != item.Hash || Hash(OrganizedPath(root, item.Name)) != item.Hash)
                            throw new IOException("Nextendo text verification failed.");
                    }
                    Manifest combined = new(1, committed.Files.Concat(plan.Files).GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToList(), plan.Id);
                    SaveManifest(ready, combined);
                    organized = planCommitted = true;
                    Volatile.Write(ref _layout, new Layout(root, true));
                    FaultPoint?.Invoke("committed");
                    Cleanup(root, combined);
                    File.Delete(pending);
                    Volatile.Write(ref _layout, new Layout(root, true, HasSensitiveCopy(root, true)));
                    if (conflicts || HasSensitiveCopy(root, true)) LogDeferred();
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
                    CryptographicException or FormatException or PlatformNotSupportedException or ArgumentException or NotSupportedException or System.Security.SecurityException)
                {
                    if (!planCommitted && currentPlan != null) AbortUncommitted(root, pending, currentPlan);
                    bool ambiguous = readingManifest || File.Exists(ready) && !organized;
                    Volatile.Write(ref _layout, new Layout(root, organized, ambiguous || HasSensitiveCopy(root, organized)));
                    LogDeferred();
                }
            }
        }

        private static void LogDeferred() => Logger.Warning?.Print(LogClass.Application,
            "[Nextendo] Organizing text files was deferred. Original files were retained where recovery still needs them.");

        private static bool HasSensitiveCopy(string root, bool organized)
        {
            foreach (string name in new[] { "nextendo_account.txt", "nextendo_profile_links.txt" })
            {
                string path = organized ? Path.Combine(root, name) : OrganizedPath(root, name);
                try { File.GetAttributes(path); return true; }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return true; }
            }
            return false;
        }

        // Account writers call this while holding the shared storage lock. Another process may
        // have committed the layout after this process originally deferred initialization.
        internal static void RefreshForWrite(string root)
        {
            string ready = Path.Combine(root, "system", "nextendo", ReadyName);
            if (File.Exists(ready))
            {
                RejectLink(Path.Combine(root, "system"));
                RejectLink(Path.Combine(root, "system", "nextendo"));
                ReadManifest(ready);
                Volatile.Write(ref _layout, new Layout(Path.GetFullPath(root), true, HasSensitiveCopy(root, true)));
            }
            else if (IsOrganized(root)) throw new IOException("The committed text layout changed. Restart before writing account data.");
            if (HasPendingCredentialCopy(root)) throw new IOException("A sensitive text copy still requires recovery.");
        }

        private static bool SourcesChanged(string root, Manifest plan)
        {
            bool changed = false;
            foreach (Item item in plan.Files)
            {
                string source = Path.Combine(root, item.Name);
                RejectLink(source);
                try
                {
                    if (Hash(source) != item.Hash) changed = true;
                }
                // A confirmed deletion can be a sign-out by an older build. Do not restore it.
                catch (FileNotFoundException) { changed = true; }
                catch (DirectoryNotFoundException) { changed = true; }
            }
            return changed;
        }

        private static void RetireChangedPlan(string root, Manifest plan)
        {
            // Before commit the root remains authoritative. Preserve staging bytes separately
            // before retrying from the current originals; sensitive Windows archives use DPAPI.
            foreach (Item item in plan.Files.Where(i => !i.DestinationExisted))
            {
                string destination = OrganizedPath(root, item.Name);
                RejectLink(destination);
                if (!File.Exists(destination)) continue;
                bool sensitive = item.Name is "nextendo_account.txt" or "nextendo_profile_links.txt";
                string hash = Hash(destination);
                if (!sensitive)
                {
                    // Preserve large ordinary TXT files without buffering or encoding their bytes.
                    string rawArchive = destination + "." + hash + ".raw.conflict";
                    RejectLink(rawArchive);
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    if (File.Exists(rawArchive))
                    {
                        if (Hash(rawArchive) != hash || Hash(destination) != hash)
                            throw new IOException("Text conflict archive verification failed.");
                        File.Delete(destination);
                    }
                    else File.Move(destination, rawArchive);
                    FaultPoint?.Invoke("archive:" + item.Name);
                    continue;
                }
                if (new FileInfo(destination).Length > MaxConflictTextBytes / 4 * 3)
                    throw new InvalidDataException("A sensitive text conflict exceeds its preservation limit.");
                byte[] content = File.ReadAllBytes(destination);
                if (Convert.ToHexString(SHA256.HashData(content)) != hash)
                    throw new IOException("A staging copy changed during preservation.");
                string archive = destination + "." + hash + ".conflict";
                RejectLink(archive);
                if (!File.Exists(archive))
                {
                    byte[] bytes = sensitive && OperatingSystem.IsWindows()
                        ? NextendoCredentialFile.Encode(Convert.ToBase64String(content), MaxConflictTextBytes, ConflictPrefix, ConflictPurpose)
                        : Encoding.UTF8.GetBytes(PortableConflictPrefix + Convert.ToBase64String(content));
                    NextendoCredentialFile.WriteBytes(archive, bytes);
                }
                if (!ReadConflict(archive).AsSpan().SequenceEqual(content)) throw new IOException("Text conflict archive verification failed.");
                FaultPoint?.Invoke("archive:" + item.Name);
                if (Hash(destination) != hash) throw new IOException("A staging copy changed during preservation.");
                File.Delete(destination);
            }
        }

        internal static byte[] ReadConflict(string path)
        {
            if (new FileInfo(path).Length > (MaxConflictTextBytes + 4096L) * 2) throw new InvalidDataException("A text conflict archive exceeds its limit.");
            using StreamReader reader = new(path, Encoding.UTF8);
            string header = reader.ReadLine();
            if (header == ConflictPrefix.TrimEnd('\n'))
                return Convert.FromBase64String(NextendoCredentialFile.Read(path, MaxConflictTextBytes, ConflictPrefix, ConflictPurpose, allowPlaintext: false));
            if (header == PortableConflictPrefix.TrimEnd('\n')) return Convert.FromBase64String(reader.ReadToEnd());
            throw new InvalidDataException("Unrecognized text conflict archive.");
        }

        private static void AbortUncommitted(string root, string pending, Manifest plan)
        {
            bool clean = true;
            foreach (Item item in plan.Files.Where(i => !i.DestinationExisted))
            {
                string source = Path.Combine(root, item.Name);
                string destination = OrganizedPath(root, item.Name);
                try
                {
                    RejectLink(source);
                    RejectLink(destination);
                    if (!File.Exists(destination)) continue;
                    if (!File.Exists(source) || Hash(source) != item.Hash || !IsMatchingPrefix(source, destination))
                    {
                        clean = false;
                        continue;
                    }
                    // Delete only our uncommitted, byte-verified copy. The original is intact.
                    File.Delete(destination);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    clean = false;
                }
            }
            if (clean)
            {
                try { File.Delete(pending); }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException) { }
            }
        }

        private static bool IsMatchingPrefix(string source, string destination)
        {
            using FileStream input = File.OpenRead(source);
            using FileStream copy = File.OpenRead(destination);
            if (copy.Length > input.Length) return false;
            byte[] left = new byte[65536], right = new byte[65536];
            while (copy.Position < copy.Length)
            {
                int count = (int)Math.Min(left.Length, copy.Length - copy.Position);
                input.ReadExactly(left.AsSpan(0, count));
                copy.ReadExactly(right.AsSpan(0, count));
                if (!left.AsSpan(0, count).SequenceEqual(right.AsSpan(0, count))) return false;
            }
            return true;
        }

        private static Manifest Prepare(string root, out bool conflicts)
        {
            List<Item> items = [];
            conflicts = false;
            int count = 0;
            foreach (string source in Directory.EnumerateFiles(root, "nextendo_*.txt", SearchOption.TopDirectoryOnly))
            {
                if (++count > 4096) throw new InvalidDataException("Too many Nextendo text files.");
                try
                {
                    string name = ValidateName(Path.GetFileName(source));
                    string destination = OrganizedPath(root, name);
                    RejectLink(source);
                    RejectLink(destination);
                    string hash = Hash(source);
                    bool exists = File.Exists(destination);
                    if (exists && Hash(destination) != hash) { conflicts = true; continue; }
                    items.Add(new(name, hash, exists));
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    conflicts = true;
                }
            }
            return new(1, items, Guid.NewGuid().ToString("N"));
        }

        private static void CopyVerified(string source, string destination, Item item)
        {
            if (File.Exists(destination) && Hash(destination) == item.Hash)
            {
                File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
                return;
            }
            if (item.DestinationExisted) throw new IOException("A Nextendo text destination changed.");
            FileStreamOptions options = new() { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream output = new(destination, options))
            {
                // Resume an owned partial copy only when every existing byte matches the source.
                if (output.Length > input.Length) throw new IOException("Conflicting partial Nextendo text copy.");
                long existing = output.Length;
                byte[] left = new byte[65536], right = new byte[65536];
                while (output.Position < existing)
                {
                    int count = (int)Math.Min(left.Length, existing - output.Position);
                    input.ReadExactly(left.AsSpan(0, count));
                    output.ReadExactly(right.AsSpan(0, count));
                    if (!left.AsSpan(0, count).SequenceEqual(right.AsSpan(0, count))) throw new IOException("Conflicting partial Nextendo text copy.");
                }
                input.CopyTo(output);
                output.Flush(true);
            }
            if (Hash(destination) != item.Hash) throw new IOException("Nextendo text copy verification failed.");
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
        }

        private static void Cleanup(string root, Manifest manifest)
        {
            foreach (Item item in manifest.Files)
            {
                try
                {
                    string original = Path.Combine(root, item.Name);
                    string destination = OrganizedPath(root, item.Name);
                    RejectLink(original);
                    RejectLink(destination);
                    if (File.Exists(original) && File.Exists(destination) && Hash(original) == item.Hash && Hash(destination) == item.Hash)
                    {
                        File.Delete(original);
                        FaultPoint?.Invoke("cleanup:" + item.Name);
                    }
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    LogDeferred();
                }
            }
        }

        private static Manifest ReadManifest(string path)
        {
            RejectLink(path);
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Nextendo text manifest exceeds its limit.");
            Manifest manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path));
            if (manifest == null || manifest.Version != 1 || manifest.Files == null || manifest.Files.Count > 4096 || manifest.Files.Any(i => i == null) ||
                manifest.Files.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Count)
                throw new InvalidDataException("Invalid Nextendo text manifest.");
            foreach (Item item in manifest.Files)
            {
                ValidateName(item.Name);
                if (item.Hash == null || item.Hash.Length != 64 || !item.Hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid Nextendo text fingerprint.");
            }
            return manifest;
        }

        private static void SaveManifest(string path, Manifest manifest)
        {
            RejectLink(path);
            NextendoCredentialFile.WriteBytes(path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest)));
        }

        private static string Hash(string path)
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static void RejectLink(string path)
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked Nextendo text paths are unsupported.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
