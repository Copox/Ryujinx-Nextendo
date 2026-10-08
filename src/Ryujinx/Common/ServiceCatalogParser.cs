using NSec.Cryptography;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ryujinx.Ava.Common
{
    public enum ServiceVersionStatus { Accepted, NotAccepted, UnknownInstalledVersion, NotEvaluable, NotApplicable, UnknownCatalog }
    public enum ServiceRequirementsStatus { Satisfied, NotSatisfied, NotEvaluable, NotApplicable, UnknownCatalog }
    public sealed record ServiceListing(string Version, string Protocol, bool Enabled, string Policy, string[] Accepted, string[] Capabilities)
    {
        public ServiceVersionStatus Evaluate(string installed) => Policy switch
        {
            "any" => ServiceVersionStatus.Accepted,
            "exact" when string.IsNullOrWhiteSpace(installed) => ServiceVersionStatus.UnknownInstalledVersion,
            "exact" => Accepted.Contains(installed, StringComparer.Ordinal) ? ServiceVersionStatus.Accepted : ServiceVersionStatus.NotAccepted,
            _ => ServiceVersionStatus.NotEvaluable,
        };
        public bool CanEvaluate => Capabilities.All(ServiceCatalogParser.Capabilities.Contains) && Policy is "exact" or "any";
    }
    public sealed record VerifiedServiceDocument(string Type, long Revision, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, string Payload);
    public sealed record ServiceClientRelease(string Version, long Sequence, string Channel, string Platform, string Architecture, string[] Capabilities, bool Recommended, string Url);
    public static class ServiceCatalogParser
    {
        public const int MaxBytes = 1024 * 1024;
        public static readonly HashSet<string> Capabilities = new(StringComparer.Ordinal) { "versions.exact.v1", "versions.any.v1" };
        private static readonly PublicKey Key = PublicKey.Import(SignatureAlgorithm.Ed25519, Convert.FromBase64String("xI+atR6VesrCH89ZDZD4t3WQLcYOFEhk5F0HLMzgWk4="), KeyBlobFormat.RawPublicKey);
        private static readonly UTF8Encoding Utf8 = new(false, true);
        private static void Unique(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                HashSet<string> names = new(StringComparer.Ordinal);
                foreach (JsonProperty p in element.EnumerateObject())
                { if (!names.Add(p.Name)) throw new FormatException("Duplicate JSON key."); Unique(p.Value); }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
                Unique(item);
        }
        private static JsonDocument Read(string json)
        {
            if (Utf8.GetByteCount(json) > MaxBytes)
                throw new FormatException("Document too large.");
            JsonDocument doc = JsonDocument.Parse(json);
            try
            { Unique(doc.RootElement); return doc; }
            catch { doc.Dispose(); throw; }
        }
        private static string Text(JsonElement element, string field, int max, bool nonempty = true)
        {
            string s = element.GetProperty(field).GetString();
            if (s == null || s.Length > max || (nonempty && string.IsNullOrWhiteSpace(s)) || s.Any(char.IsControl))
                throw new FormatException("Invalid " + field);
            return s;
        }
        private static byte[] Base64(string s) { byte[] b = Convert.FromBase64String(s); if (Convert.ToBase64String(b) != s) throw new FormatException("Noncanonical Base64."); return b; }
        private static DateTimeOffset Date(JsonElement e, string field)
        {
            string raw = Text(e, field, 40);
            if (!raw.EndsWith('Z') || !DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset d))
                throw new FormatException("Invalid UTC date.");
            return d;
        }
        private static string[] Caps(JsonElement e, string field)
        {
            JsonElement a = e.GetProperty(field);
            if (a.GetArrayLength() > 64)
                throw new FormatException("Too many capabilities.");
            string[] result = a.EnumerateArray().Select(x => x.GetString()).ToArray();
            if (result.Any(x => x == null || !Regex.IsMatch(x, "^[a-z0-9._-]{1,64}$")) || result.Distinct(StringComparer.Ordinal).Count() != result.Length)
                throw new FormatException("Invalid capabilities.");
            return result;
        }
        public static VerifiedServiceDocument Verify(string envelope, string expected, DateTimeOffset now, bool allowExpiredForIdentity = false)
        {
            using JsonDocument outer = Read(envelope);
            JsonElement e = outer.RootElement;
            if (Text(e, "key_id", 64) != "catalog-key-1")
                throw new FormatException("Unknown signing key.");
            byte[] payload = Base64(Text(e, "payload", MaxBytes)), sig = Base64(Text(e, "signature", 128));
            if (sig.Length != 64 || !SignatureAlgorithm.Ed25519.Verify(Key, payload, sig))
                throw new FormatException("Invalid catalog signature.");
            string json = Utf8.GetString(payload);
            using JsonDocument inner = Read(json);
            JsonElement d = inner.RootElement;
            if (Text(d, "document_type", 32) != expected || d.GetProperty("schema_version").GetInt32() != 1)
                throw new FormatException("Unsupported document.");
            long revision = d.GetProperty("revision").GetInt64();
            DateTimeOffset issued = Date(d, "issued_at"), expires = Date(d, "expires_at");
            if (revision < 1 || issued > now.AddMinutes(5) || expires <= issued || expires - issued > TimeSpan.FromHours(24) || (!allowExpiredForIdentity && expires <= now))
                throw new FormatException("Invalid document validity.");
            return new(expected, revision, issued, expires, json);
        }
        public static Dictionary<string, ServiceListing> ParseCatalog(string payload)
        {
            using JsonDocument doc = Read(payload);
            JsonElement games = doc.RootElement.GetProperty("games");
            if (games.GetArrayLength() > 10000)
                throw new FormatException("Too many games.");
            Dictionary<string, ServiceListing> result = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement game in games.EnumerateArray())
            {
                // Identity ambiguity invalidates the whole document, not just one entry.
                List<string> ids = new();
                JsonElement array = game.GetProperty("title_ids");
                if (array.GetArrayLength() is < 1 or > 32)
                    throw new FormatException("Invalid title IDs.");
                foreach (JsonElement element in array.EnumerateArray())
                {
                    string id = element.GetString();
                    if (id == null || !Regex.IsMatch(id, "^[0-9a-fA-F]{16}$") || !ulong.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong n) || n == 0 || !seen.Add(id))
                        throw new FormatException("Ambiguous title ID.");
                    ids.Add(id.ToLowerInvariant());
                }
                ServiceListing listing = null;
                try
                {
                    bool enabled = game.GetProperty("enabled").GetBoolean();
                    string[] caps = Caps(game, "required_capabilities");
                    string protocol = game.TryGetProperty("protocol", out _) ? Text(game, "protocol", 32, false) : "";
                    if (game.TryGetProperty("name", out _))
                        Text(game, "name", 256, false);
                    string policy = "", version = "";
                    string[] accepted = Array.Empty<string>();
                    if (enabled || game.TryGetProperty("versions", out _))
                    {
                        JsonElement v = game.GetProperty("versions");
                        policy = Text(v, "policy", 64);
                        if (policy is "exact" or "any" && !caps.Contains("versions." + policy + ".v1"))
                            throw new FormatException("Missing capability.");
                        if (policy == "exact")
                        {
                            JsonElement a = v.GetProperty("accepted");
                            if (a.GetArrayLength() is < 1 or > 64)
                                throw new FormatException("accepted");
                            accepted = a.EnumerateArray().Select(x => x.GetString()).ToArray();
                            if (accepted.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 32 || x.Any(char.IsControl)) || accepted.Distinct(StringComparer.Ordinal).Count() != accepted.Length)
                                throw new FormatException("accepted");
                            version = v.TryGetProperty("recommended", out _) ? Text(v, "recommended", 32) : accepted[0];
                            if (!accepted.Contains(version, StringComparer.Ordinal))
                                throw new FormatException("recommended");
                        }
                        else if (policy == "any" && (v.TryGetProperty("accepted", out _) || v.TryGetProperty("recommended", out _)))
                            throw new FormatException("any");
                    }
                    listing = new(version, protocol, enabled, policy, accepted, caps);
                }
                catch (Exception ex) when (ex is FormatException or InvalidOperationException or KeyNotFoundException) { /* Malformed entry remains unavailable. */ }
                foreach (string id in ids)
                    result.Add(id, listing);
            }
            return result;
        }
        public static ServiceClientRelease[] ParseReleases(string payload)
        {
            using JsonDocument doc = Read(payload);
            JsonElement array = doc.RootElement.GetProperty("releases");
            if (array.GetArrayLength() > 1000)
                throw new FormatException("releases");
            List<ServiceClientRelease> result = new();
            HashSet<long> sequences = new();
            HashSet<string> recommendations = new();
            foreach (JsonElement r in array.EnumerateArray())
            {
                long sequence = r.GetProperty("release_sequence").GetInt64();
                string channel = Text(r, "channel", 64), platform = Text(r, "platform", 64), architecture = Text(r, "architecture", 64);
                bool recommended = r.GetProperty("recommended").GetBoolean();
                string url = Text(r, "release_url", 512);
                if (sequence < 1 || !sequences.Add(sequence) || !url.StartsWith("https://github.com/NextendoNetwork/Ryujinx-Nextendo/releases/", StringComparison.Ordinal) || Date(r, "published_at") > DateTimeOffset.UtcNow || recommended && !recommendations.Add(channel + ":" + platform + ":" + architecture))
                    throw new FormatException("Invalid release.");
                result.Add(new(Text(r, "version", 64), sequence, channel, platform, architecture, Caps(r, "capabilities"), recommended, url));
            }
            return result.ToArray();
        }
        public static ServiceClientRelease Recommend(ServiceClientRelease[] releases, string[] required, string channel, string platform, string architecture) => releases.Where(r => r.Channel == channel && r.Platform == platform && r.Architecture == architecture && required.All(c => r.Capabilities.Contains(c, StringComparer.Ordinal))).OrderByDescending(r => r.Recommended).ThenByDescending(r => r.Sequence).FirstOrDefault();
        public static Dictionary<string, int> ParseCounts(string json)
        {
            using JsonDocument doc = Read(json);
            Dictionary<string, int> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty p in doc.RootElement.GetProperty("counts").EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out int n) && n >= 0)
                result[p.Name] = n;
            return result;
        }
    }
}
