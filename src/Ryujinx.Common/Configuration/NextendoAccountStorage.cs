using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ryujinx.Common.Configuration
{
    // A durable redo journal (encrypted on Windows) groups account files and profile links. Recovery rolls
    // forward before loading any account. Legacy builds sharing the same directory are unsupported.
    internal static class NextendoAccountStorage
    {
        private const string JournalName = "nextendo_account_transaction.dat";
        private const int MaxJournalBytes = 8 * 1024 * 1024;
        internal const string PortableJournalPrefix = "nextendo-portable-journal-v1\n";
        internal static Action<string> FaultPoint;
        internal sealed record Entry(string Path, string BeforeHash, byte[] Data);
        internal sealed record Journal(int Version, List<Entry> Entries);

        internal sealed class Plan(string root, bool organized)
        {
            private readonly Dictionary<string, string> _before = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, byte[]> _updates = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> _texts = new(StringComparer.OrdinalIgnoreCase);

            internal string Read(string relative)
            {
                string path = Resolve(root, relative, organized);
                _before.TryAdd(relative, Hash(path));
                if (relative == NextendoProfileLinks.Name)
                    return NextendoProfileLinks.Read(root, path, Read(NextendoProfileLinks.StateName));
                if (relative == NextendoProfileLinks.StateName) return NextendoProfileLinks.ReadState(root);
                return File.Exists(path) ? NextendoCredentialFile.Read(path,
                    relative == NextendoProfileLinks.StateName ? NextendoProfileLinks.MaxStateBytes : NextendoCredentialFile.MaxPlaintextBytes,
                    allowPlaintext: relative != NextendoProfileLinks.StateName || !OperatingSystem.IsWindows()) : null;
            }

            internal void Put(string relative, string text, bool protect = true)
            {
                Read(relative);
                if (relative == NextendoProfileLinks.Name && text != null) text = NextendoProfileLinks.Canonicalize(text);
                _texts[relative] = text;
                _updates[relative] = text == null ? null : relative == NextendoProfileLinks.Name && OperatingSystem.IsWindows()
                    ? NextendoProfileLinks.Encode(text) : protect && OperatingSystem.IsWindows() ? NextendoCredentialFile.Encode(text,
                        relative == NextendoProfileLinks.StateName ? NextendoProfileLinks.MaxStateBytes : NextendoCredentialFile.MaxPlaintextBytes)
                    : System.Text.Encoding.UTF8.GetBytes(text);
            }

            internal List<Entry> Entries
            {
                get
                {
                    if (OperatingSystem.IsWindows() && _texts.TryGetValue(NextendoProfileLinks.Name, out string links) && links != null)
                    {
                        // Persist the map and its encrypted recovery snapshot in the same transaction.
                        string original = Read(NextendoProfileLinks.StateName);
                        Dictionary<string, string> state = NextendoProfileLinks.Fields(_texts.GetValueOrDefault(NextendoProfileLinks.StateName, original));
                        state[NextendoProfileLinks.ProtectedKey] = "1";
                        state[NextendoProfileLinks.SnapshotKey] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(links));
                        Put(NextendoProfileLinks.StateName, string.Join("\n", state.Select(p => p.Key + "=" + p.Value)));
                    }
                    return _updates.Select(p => new Entry(p.Key, _before[p.Key], p.Value)).ToList();
                }
            }
            internal void CheckReads()
            {
                foreach (var pair in _before)
                {
                    if (Hash(Resolve(root, pair.Key, organized)) != pair.Value) throw new IOException("Account files changed while preparing storage.");
                }
            }
        }

        internal static bool HasPending(string root) => File.Exists(Path.Combine(root, JournalName));

        internal static FileStream Acquire(string root)
        {
            Directory.CreateDirectory(root);
            return new FileStream(Path.Combine(root, "nextendo_account_storage.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        internal static void Recover(string root)
        {
            if (!HasPending(root)) return;
            using FileStream storageLock = Acquire(root);
            RecoverLocked(root);
        }

        internal static void Execute(string root, Action<Plan> prepare)
        {
            using FileStream storageLock = Acquire(root);
            if (HasPending(root)) throw new IOException("Account recovery requires restarting the application.");
            NextendoTextPaths.RefreshForWrite(root);
            bool organized = NextendoTextPaths.IsOrganized(root);
            Plan plan = new(root, organized);
            prepare(plan);
            List<Entry> entries = plan.Entries;
            if (entries.Count == 0) return;
            plan.CheckReads();
            Journal journal = new(organized ? 2 : 1, entries);
            FaultPoint?.Invoke("prepared");
            if (OperatingSystem.IsWindows())
            {
                NextendoCredentialFile.Write(Path.Combine(root, JournalName), JsonSerializer.Serialize(journal), MaxJournalBytes);
            }
            else
            {
                NextendoCredentialFile.WriteBytes(Path.Combine(root, JournalName), EncodePortableJournal(journal));
            }
            FaultPoint?.Invoke("journal");
            Apply(root, journal);
            FaultPoint?.Invoke("committed");
            if (HasPending(root)) File.Delete(Path.Combine(root, JournalName));
            if (entries.Any(e => e.Path == NextendoProfileLinks.Name && e.Data != null)) NextendoProfileLinks.OnCommitted(root);
        }

        internal static void RecoverLocked(string root)
        {
            string path = Path.Combine(root, JournalName);
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > (MaxJournalBytes + 4096L) * 2) throw new InvalidDataException("Account journal exceeds its size limit.");
            string raw = File.ReadAllText(path);
            string json;
            if (raw.StartsWith(NextendoCredentialFile.Prefix, StringComparison.Ordinal))
                json = NextendoCredentialFile.Read(path, MaxJournalBytes, allowPlaintext: false);
            else if (raw.StartsWith(PortableJournalPrefix, StringComparison.Ordinal))
            {
                json = raw[PortableJournalPrefix.Length..];
                if (Encoding.UTF8.GetByteCount(json) > MaxJournalBytes) throw new InvalidDataException("Portable journal exceeds its size limit.");
            }
            else throw new InvalidDataException("Unrecognized account journal.");
            Journal journal;
            try { journal = JsonSerializer.Deserialize<Journal>(json); }
            catch (JsonException exception) { throw new InvalidDataException("Invalid account journal.", exception); }
            Apply(root, journal);
            File.Delete(path);
        }

        internal static byte[] EncodePortableJournal(Journal journal)
        {
            string json = JsonSerializer.Serialize(journal);
            if (Encoding.UTF8.GetByteCount(json) > MaxJournalBytes) throw new InvalidDataException("Portable journal exceeds its size limit.");
            return Encoding.UTF8.GetBytes(PortableJournalPrefix + json);
        }

        private static void Apply(string root, Journal journal)
        {
            if (journal == null || journal.Version is not (1 or 2) || journal.Entries == null || journal.Entries.Count > 4096 || journal.Entries.Any(e => e == null) ||
                journal.Entries.Select(e => e.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Entries.Count)
                throw new InvalidDataException("Invalid account journal.");

            // Check every destination before applying any entry. Never overwrite a newer external edit.
            foreach (Entry entry in journal.Entries)
            {
                string current = Hash(Resolve(root, entry.Path, journal.Version == 2));
                string desired = entry.Data == null ? null : Convert.ToHexString(SHA256.HashData(entry.Data));
                if (current != entry.BeforeHash && current != desired) throw new IOException("Account recovery conflicts with an external change.");
            }

            foreach (Entry entry in journal.Entries)
            {
                string path = Resolve(root, entry.Path, journal.Version == 2);
                if (entry.Data == null) File.Delete(path);
                else NextendoCredentialFile.WriteBytes(path, entry.Data);
                FaultPoint?.Invoke("write:" + entry.Path);
            }
        }

        private static string Hash(string path)
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > MaxJournalBytes) throw new InvalidDataException("Account storage file exceeds its size limit.");
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static string Resolve(string root, string relative, bool organized)
        {
            if (string.IsNullOrEmpty(relative)) throw new InvalidDataException("Invalid account storage path.");
            bool accountCopy = relative.StartsWith("nextendo_accounts/", StringComparison.Ordinal) && relative.EndsWith(".txt", StringComparison.Ordinal) &&
                relative[18..^4].Length > 0 && relative[18..^4].All(c => c >= '0' && c <= '9');
            if (relative != "nextendo_account.txt" && relative != "nextendo_profile_links.txt" && relative != "nextendo_account_status.dat" && !accountCopy)
                throw new InvalidDataException("Invalid account storage path.");
            bool looseText = relative is "nextendo_account.txt" or "nextendo_profile_links.txt";
            string path = organized && looseText ? NextendoTextPaths.OrganizedPath(root, relative)
                : Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            // Refuse symlink/junction destinations; a journal is limited to this data directory.
            if ((File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) ||
                ((accountCopy || (organized && looseText)) && Directory.Exists(Path.GetDirectoryName(path)) && (File.GetAttributes(Path.GetDirectoryName(path)) & FileAttributes.ReparsePoint) != 0) ||
                (organized && looseText && Directory.Exists(Path.Combine(root, "system")) && (File.GetAttributes(Path.Combine(root, "system")) & FileAttributes.ReparsePoint) != 0))
                throw new IOException("Account storage links are unsupported.");
            return path;
        }
    }
}
