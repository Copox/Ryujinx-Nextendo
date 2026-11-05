using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Ryujinx.Common.Configuration
{
    internal static class NextendoProfileLinks
    {
        internal const string Name = "nextendo_profile_links.txt";
        internal const string StateName = "nextendo_account_status.dat";
        internal const string Prefix = "nextendo-profile-links-protected-v1\n";
        internal const string ProtectedKey = "_profile_links_protected";
        internal const string SnapshotKey = "_profile_links_snapshot";
        internal const int MaxStateBytes = 512 * 1024;
        private static readonly byte[] _purpose = Encoding.UTF8.GetBytes("Nextendo/Ryujinx/ProfileLinks/v1");
        private sealed record Problem(string Root, NextendoCredentialStorageState State);
        private static Problem _problem;

        internal static NextendoCredentialStorageState Issue(string root)
        {
            Problem problem = Volatile.Read(ref _problem);
            return problem != null && string.Equals(problem.Root, root, StringComparison.OrdinalIgnoreCase)
                ? problem.State : NextendoCredentialStorageState.None;
        }

        private static void SetIssue(string root, NextendoCredentialStorageState state) => Volatile.Write(ref _problem, new(root, state));
        internal static void OnCommitted(string root) => SetIssue(root, NextendoCredentialStorageState.None);

        internal static string ReadState(string root)
        {
            string path = Path.Combine(root, StateName);
            return File.Exists(path) ? NextendoCredentialFile.Read(path, MaxStateBytes,
                validatePlaintext: OperatingSystem.IsWindows() ? IsLegacyState : null) : null;
        }

        private static bool IsLegacyState(string text)
        {
            // Non-Windows state contains only credential fingerprints and rejection reasons.
            // Never accept a plaintext protection marker or recovery snapshot as legacy state.
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string line in text.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                int equals = line.IndexOf('=');
                if (equals != 64 || !line[..equals].All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F') || !seen.Add(line[..equals])) return false;
                string value = line[(equals + 1)..].Trim();
                if (value != nameof(NextendoCredentialStorageState.Rejected) && value != nameof(NextendoCredentialStorageState.IdentityMismatch)) return false;
            }
            return true;
        }

        internal static Dictionary<string, string> Fields(string text)
        {
            Dictionary<string, string> fields = [];
            foreach (string line in (text ?? "").Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) fields[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
            return fields;
        }

        internal static string Canonicalize(string text)
        {
            if (Encoding.UTF8.GetByteCount(text ?? "") > NextendoCredentialFile.MaxPlaintextBytes)
                throw new InvalidDataException("Profile links exceed their size limit.");
            Dictionary<string, string> links = [];
            foreach (string line in (text ?? "").Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0 || !ulong.TryParse(line[..eq].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong pid) || pid == 0)
                    throw new InvalidDataException("Invalid profile association.");
                string profile = line[(eq + 1)..].Trim();
                if (profile.Length == 0 || profile.Length > 128 || profile.Contains('\r') || profile.Contains('='))
                    throw new InvalidDataException("Invalid local profile identifier.");
                string key = pid.ToString(CultureInfo.InvariantCulture);
                if (links.TryGetValue(key, out string previous) && !string.Equals(previous, profile, StringComparison.OrdinalIgnoreCase))
                    throw new ConflictException();
                links[key] = profile;
            }
            return string.Join("\n", links.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value));
        }

        internal static byte[] Encode(string text) => NextendoCredentialFile.Encode(Canonicalize(text), prefix: Prefix, entropy: _purpose);

        internal static string Read(string root) => Read(root, Path.Combine(root, Name), ReadState(root));

        internal static string Read(string root, string path, string stateText)
        {
            Dictionary<string, string> state = Fields(stateText);
            string expected = null;
            if (state.GetValueOrDefault(ProtectedKey) == "1")
            {
                if (!state.TryGetValue(SnapshotKey, out string snapshot) || snapshot.Length > 4 * ((NextendoCredentialFile.MaxPlaintextBytes + 2) / 3))
                    throw new InvalidDataException("Protected profile-link recovery data is missing or oversized.");
                try { expected = Canonicalize(new UTF8Encoding(false, true).GetString(Convert.FromBase64String(snapshot))); }
                catch (Exception exception) when (exception is ArgumentException or FormatException)
                { throw new InvalidDataException("Invalid protected profile-link recovery data."); }
            }

            string actual = null;
            bool protectedFormat = false;
            try
            {
                if (File.Exists(path))
                {
                    actual = Canonicalize(NextendoCredentialFile.Read(path, prefix: Prefix, entropy: _purpose, allowPlaintext: expected == null));
                    protectedFormat = HasProtectedHeader(path);
                }
            }
            catch (Exception exception) when (expected != null && exception is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException or FormatException)
            {
                // The authenticated recovery snapshot is authoritative, not an edited file.
            }
            if (expected != null)
            {
                if (actual != expected || !protectedFormat) SetIssue(root, NextendoCredentialStorageState.RecoveryRequired);
                return expected;
            }
            string merged = MergeProtectedAccounts(root, actual);
            return actual == null && merged.Length == 0 ? null : merged;
        }

        private static bool HasProtectedHeader(string path)
        {
            using StreamReader reader = new(path, Encoding.UTF8);
            return reader.ReadLine() == Prefix.TrimEnd('\n');
        }

        private static string MergeProtectedAccounts(string root, string text)
        {
            Dictionary<string, string> links = Fields(text);
            string copies = Path.Combine(root, "nextendo_accounts");
            IEnumerable<string> paths = Directory.Exists(copies) ? Directory.GetFiles(copies, "*.txt") : [];
            paths = paths.Append(Path.Combine(root, "nextendo_account.txt"));
            foreach (string path in paths)
            {
                if (!File.Exists(path)) continue;
                if (new FileInfo(path).Length > (NextendoCredentialFile.MaxPlaintextBytes + 4096L) * 2) continue;
                using (StreamReader reader = new(path, Encoding.UTF8))
                {
                    if (reader.ReadLine() != NextendoCredentialFile.Prefix.TrimEnd('\n')) continue;
                }
                Dictionary<string, string> account;
                try { account = Fields(NextendoCredentialFile.Read(path)); }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException or FormatException or PlatformNotSupportedException) { continue; }
                string pid = account.GetValueOrDefault("pid", "");
                string profile = account.GetValueOrDefault("profile_user_id", "");
                if (string.IsNullOrEmpty(profile) || !ulong.TryParse(pid, out ulong number) || number == 0) continue;
                pid = number.ToString(CultureInfo.InvariantCulture);
                if (links.TryGetValue(pid, out string linked) && !string.Equals(profile, linked, StringComparison.OrdinalIgnoreCase))
                    throw new ConflictException();
                links[pid] = profile;
            }
            return Canonicalize(string.Join("\n", links.Select(p => p.Key + "=" + p.Value)));
        }

        internal static void Initialize(string root)
        {
            OnCommitted(root);
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                string path = Path.Combine(root, Name);
                if (!File.Exists(path) && !File.Exists(Path.Combine(root, StateName)) &&
                    !File.Exists(Path.Combine(root, "nextendo_account.txt")) && !Directory.Exists(Path.Combine(root, "nextendo_accounts"))) return;
                NextendoAccountStorage.Execute(root, plan =>
                {
                    string status = plan.Read(StateName);
                    string statusPath = Path.Combine(root, StateName);
                    if (status != null && !File.ReadAllText(statusPath).StartsWith(NextendoCredentialFile.Prefix, StringComparison.Ordinal))
                        plan.Put(StateName, status);
                    string links = plan.Read(Name);
                    if (links == null) return;
                    Dictionary<string, string> state = Fields(plan.Read(StateName));
                    if (state.GetValueOrDefault(ProtectedKey) == "1" && Issue(root) == NextendoCredentialStorageState.None && HasProtectedHeader(path)) return;
                    plan.Put(Name, links);
                });
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException or FormatException or PlatformNotSupportedException)
            {
                SetIssue(root, exception is ConflictException ? NextendoCredentialStorageState.Conflict :
                    NextendoAccountStorage.HasPending(root) ? NextendoCredentialStorageState.RecoveryRequired : NextendoCredentialStorageState.Failed);
                Logger.Warning?.Print(LogClass.Application, "[Nextendo] Protected profile associations could not be completed. Existing files were retained.");
            }
        }

        internal sealed class ConflictException : IOException { }
    }
}
