using NSec.Cryptography;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.Loaders.Executables;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ryujinx.HLE.HOS
{
    public sealed record NplnPatchGuard(uint Offset, byte[] Original, byte[] Patched);

    public sealed record NplnPatch(string Id, ulong TitleId, string BuildId, string Module, ulong Version, bool Enabled, bool Final, byte[] Ips, NplnPatchGuard[] Guards);

    public sealed record NplnPatchManifest(ulong Revision, DateTimeOffset ExpiresAt, string ETag, NplnPatch[] Patches);

    public sealed record NplnTitleIndex(ulong Revision, DateTimeOffset ExpiresAt, string ETag, Dictionary<ulong, bool> Titles);

    /// <summary>
    /// [Nextendo] NPLN online patches from the signed service catalog
    /// (https://nextendo.network/service-catalog/v1/npln-*), the same documents Citron uses.
    /// This class only verifies documents and applies verified patches; it never touches the network.
    /// The frontend downloads documents before a game is loaded and hands them over through
    /// <see cref="Remember"/>, or points <see cref="CacheDirectory"/> at its verified on-disk copies.
    /// </summary>
    public static class NplnPatchCatalog
    {
        public const int MaxDocumentBytes = 1024 * 1024;
        public const string KeyId = "npln-patches-key-1";
        public const string Capability = "npln.ips32.v1";

        // Public half of npln-patches-key-1. Only documents signed by its private key are accepted.
        private static readonly PublicKey _key = PublicKey.Import(SignatureAlgorithm.Ed25519,
            Convert.FromBase64String("LAI+Pi+GZtj3p4DpNM5ORBraw1cbZCVSMFgUmAVWNeA="), KeyBlobFormat.RawPublicKey);

        private static readonly UTF8Encoding _utf8 = new(false, true);
        private static readonly object _lock = new();
        private static readonly Dictionary<(ulong, string, string), (ulong Revision, NplnPatch Patch, string ETag)> _patches = new();
        private static string _cacheDirectory;
        private static bool _cacheLoaded;

        /// <summary>
        /// Directory holding verified envelopes (verified/*.json). Every file is verified again
        /// when it is read; an unsigned or altered file is ignored.
        /// </summary>
        public static string CacheDirectory
        {
            get => _cacheDirectory;
            set
            {
                lock (_lock)
                {
                    _cacheDirectory = value;
                    _cacheLoaded = false;
                }
            }
        }

        public static NplnTitleIndex VerifyTitleIndex(string envelope, DateTimeOffset now, bool allowExpired = false)
            => VerifyTitleIndex(envelope, _key, now, allowExpired);

        public static NplnPatchManifest VerifyManifest(string envelope, DateTimeOffset now, bool allowExpired = false)
            => VerifyManifest(envelope, _key, now, allowExpired);

        internal static NplnTitleIndex VerifyTitleIndex(string envelope, PublicKey key, DateTimeOffset now, bool allowExpired)
        {
            using JsonDocument doc = VerifyDocument(envelope, key, "npln_title_index", now, allowExpired);
            JsonElement root = doc.RootElement;
            string tag = root.GetProperty("index_etag").GetString();
            ValidateTag(tag);

            JsonElement entries = root.GetProperty("titles");
            if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 10000)
            {
                throw new FormatException("Invalid title index entries.");
            }

            Dictionary<ulong, bool> titles = new();
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                ulong titleId = TitleId(entry.GetProperty("title_id").GetString());
                JsonElement enabled = entry.GetProperty("enabled");
                if (enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || !titles.TryAdd(titleId, enabled.GetBoolean()))
                {
                    throw new FormatException("Invalid or duplicate title index target.");
                }
            }

            return new(root.GetProperty("revision").GetUInt64(), Date(root, "expires_at"), tag, titles);
        }

        internal static NplnPatchManifest VerifyManifest(string envelope, PublicKey key, DateTimeOffset now, bool allowExpired)
        {
            using JsonDocument doc = VerifyDocument(envelope, key, "npln_patch_catalog", now, allowExpired);
            JsonElement root = doc.RootElement;
            string tag = root.TryGetProperty("patch_etag", out JsonElement tagElement) ? tagElement.GetString() : "";
            if (!string.IsNullOrEmpty(tag))
            {
                ValidateTag(tag);
            }

            JsonElement entries = root.GetProperty("patches");
            if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 256)
            {
                throw new FormatException("Invalid patch catalog entries.");
            }

            HashSet<(ulong, string, string)> seen = new();
            List<NplnPatch> patches = new();
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                ulong titleId = TitleId(entry.GetProperty("title_id").GetString());
                string buildId = entry.GetProperty("build_id").GetString() ?? "";
                Hex(buildId, 32);
                if (buildId.Length != 64)
                {
                    throw new FormatException("Invalid patch target.");
                }

                JsonElement capabilities = entry.GetProperty("required_capabilities");
                if (entry.GetProperty("offset_basis").GetString() != "nso_header_included" ||
                    capabilities.ValueKind != JsonValueKind.Array || capabilities.GetArrayLength() != 1 ||
                    capabilities[0].GetString() != Capability)
                {
                    throw new FormatException("Unsupported patch capability.");
                }

                string id = entry.GetProperty("id").GetString() ?? "";
                string module = entry.GetProperty("module").GetString() ?? "";
                ulong version = entry.GetProperty("patch_version").GetUInt64();
                if (id.Length is 0 or > 80 || module != "main" || version == 0 ||
                    !seen.Add((titleId, buildId.ToUpperInvariant(), module)))
                {
                    throw new FormatException("Invalid or duplicate patch identity.");
                }

                byte[] ips = Base64(entry.GetProperty("ips32_base64").GetString());
                if (!SHA256.HashData(ips).AsSpan().SequenceEqual(Hex(entry.GetProperty("sha256").GetString(), 32)))
                {
                    throw new FormatException("Invalid patch hash.");
                }

                JsonElement guardEntries = entry.GetProperty("guards");
                if (guardEntries.ValueKind != JsonValueKind.Array || guardEntries.GetArrayLength() is 0 or > 256)
                {
                    throw new FormatException("Invalid patch guards.");
                }

                NplnPatchGuard[] guards = guardEntries.EnumerateArray()
                    .Select(g => new NplnPatchGuard(g.GetProperty("offset").GetUInt32(),
                        Hex(g.GetProperty("original_hex").GetString(), 4096),
                        Hex(g.GetProperty("patched_hex").GetString(), 4096)))
                    .ToArray();

                NplnPatch patch = new(id, titleId, buildId.ToUpperInvariant(), module, version,
                    entry.GetProperty("enabled").GetBoolean(),
                    entry.TryGetProperty("final", out JsonElement final) && final.GetBoolean(),
                    ips, guards);
                ValidateRecords(patch);
                patches.Add(patch);
            }

            return new(root.GetProperty("revision").GetUInt64(), Date(root, "expires_at"), tag, patches.ToArray());
        }

        /// <summary>
        /// Keeps the patches of a verified manifest. A lower revision never replaces a higher one,
        /// and catalog expiry only controls refreshes, not how long a verified patch stays usable.
        /// </summary>
        public static void Remember(NplnPatchManifest manifest)
        {
            lock (_lock)
            {
                foreach (NplnPatch patch in manifest.Patches)
                {
                    (ulong, string, string) key = (patch.TitleId, patch.BuildId, patch.Module);
                    if (!_patches.TryGetValue(key, out var previous) || previous.Revision < manifest.Revision ||
                        (previous.Revision == manifest.Revision && (!string.IsNullOrEmpty(manifest.ETag) || string.IsNullOrEmpty(previous.ETag))))
                    {
                        _patches[key] = (manifest.Revision, patch, manifest.ETag);
                    }
                }
            }
        }

        public static bool HasTitle(ulong titleId)
        {
            lock (_lock)
            {
                LoadCache();

                return _patches.Keys.Any(key => key.Item1 == titleId);
            }
        }

        public static NplnPatch Cached(ulong titleId, string buildId, out ulong revision, out string etag)
        {
            lock (_lock)
            {
                LoadCache();

                if (_patches.TryGetValue((titleId, buildId.ToUpperInvariant(), "main"), out var stored))
                {
                    revision = stored.Revision;
                    etag = stored.ETag;

                    return stored.Patch;
                }

                revision = 0;
                etag = "";

                return null;
            }
        }

        /// <summary>
        /// Applies the catalog patch for this NSO when the original bytes at every guard match.
        /// Returns true when the catalog has an entry for this build (applied or not), so callers
        /// never fall back to other online patches for a build the catalog decides on.
        /// </summary>
        internal static bool TryApply(ulong titleId, NsoExecutable nso, out bool applied)
            => TryApply(titleId, nso.Name, Convert.ToHexString(nso.BuildId), nso.Program, out applied);

        internal static bool TryApply(ulong titleId, string module, string buildId, byte[] program, out bool applied)
        {
            applied = false;
            if (module != "main")
            {
                return false;
            }

            NplnPatch patch = Cached(titleId, buildId, out ulong revision, out _);
            if (patch == null)
            {
                return false;
            }

            if (!patch.Enabled)
            {
                Logger.Warning?.Print(LogClass.ModLoader, $"[Nextendo] NPLN patch {patch.Id} withdrawn by the signed catalog");

                return true;
            }

            // Guard offsets include the 0x100-byte NSO header, which Program does not contain.
            foreach (NplnPatchGuard guard in patch.Guards)
            {
                long offset = (long)guard.Offset - 0x100;
                if (offset < 0 || offset + guard.Original.Length > program.Length ||
                    !program.AsSpan((int)offset, guard.Original.Length).SequenceEqual(guard.Original))
                {
                    Logger.Error?.Print(LogClass.ModLoader, $"[Nextendo] NPLN patch {patch.Id} rejected: original instructions do not match");

                    return true;
                }
            }

            // Verification proved that the IPS32 records are exactly the guards' patched bytes.
            foreach (NplnPatchGuard guard in patch.Guards)
            {
                guard.Patched.CopyTo(program.AsSpan((int)(guard.Offset - 0x100)));
            }

            applied = true;
            Logger.Info?.Print(LogClass.ModLoader, $"[Nextendo] NPLN: catalog patch {patch.Id} version {patch.Version} (revision {revision}) applied to build {buildId.TrimEnd('0')}");

            return true;
        }

        private static void LoadCache()
        {
            if (_cacheLoaded || string.IsNullOrEmpty(_cacheDirectory))
            {
                return;
            }

            _cacheLoaded = true;
            string directory = Path.Combine(_cacheDirectory, "verified");
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    if (new FileInfo(file).Length > MaxDocumentBytes)
                    {
                        continue;
                    }

                    Remember(VerifyManifest(File.ReadAllText(file), DateTimeOffset.UtcNow, allowExpired: true));
                }
                catch (Exception ex)
                {
                    Logger.Warning?.Print(LogClass.ModLoader, $"[Nextendo] NPLN patch cache entry rejected ({Path.GetFileName(file)}): {ex.Message}");
                }
            }
        }

        private static JsonDocument VerifyDocument(string envelope, PublicKey key, string type, DateTimeOffset now, bool allowExpired)
        {
            if (envelope == null || _utf8.GetByteCount(envelope) > MaxDocumentBytes)
            {
                throw new FormatException("Patch catalog too large.");
            }

            byte[] payload;
            using (JsonDocument outer = JsonDocument.Parse(envelope))
            {
                JsonElement e = outer.RootElement;
                if (e.GetProperty("key_id").GetString() != KeyId)
                {
                    throw new FormatException("Unknown patch signing key.");
                }

                payload = Base64(e.GetProperty("payload").GetString());
                byte[] signature = Base64(e.GetProperty("signature").GetString());
                if (signature.Length != 64 || !SignatureAlgorithm.Ed25519.Verify(key, payload, signature))
                {
                    throw new FormatException("Invalid patch catalog signature.");
                }
            }

            JsonDocument doc = JsonDocument.Parse(payload);
            try
            {
                JsonElement d = doc.RootElement;
                JsonElement revision = d.GetProperty("revision");
                if (d.GetProperty("document_type").GetString() != type || d.GetProperty("schema_version").GetInt32() != 1 ||
                    !revision.TryGetUInt64(out ulong value) || value == 0)
                {
                    throw new FormatException("Unsupported patch catalog.");
                }

                DateTimeOffset issued = Date(d, "issued_at"), expires = Date(d, "expires_at");
                if (issued > now.AddMinutes(5) || expires <= issued || expires - issued > TimeSpan.FromDays(1) ||
                    (!allowExpired && expires <= now))
                {
                    throw new FormatException("Patch catalog outside its validity period.");
                }

                return doc;
            }
            catch
            {
                doc.Dispose();
                throw;
            }
        }

        // Every IPS32 write must be one guard, in order, with exactly the guard's patched bytes.
        private static void ValidateRecords(NplnPatch patch)
        {
            byte[] b = patch.Ips;
            if (b.Length is < 15 or > 65536 || Encoding.ASCII.GetString(b, 0, 5) != "IPS32" ||
                Encoding.ASCII.GetString(b, b.Length - 4, 4) != "EEOF")
            {
                throw new FormatException("Invalid IPS32 patch.");
            }

            int position = 5;
            ulong previousEnd = 0;
            foreach (NplnPatchGuard guard in patch.Guards)
            {
                if (position + 6 > b.Length - 4)
                {
                    throw new FormatException("Truncated IPS32 patch.");
                }

                uint offset = (uint)(b[position] << 24 | b[position + 1] << 16 | b[position + 2] << 8 | b[position + 3]);
                int length = b[position + 4] << 8 | b[position + 5];
                position += 6;
                if (length == 0 || offset < 0x100 || offset < previousEnd || (ulong)offset + (ulong)length > 512UL * 1024 * 1024 ||
                    position + length > b.Length - 4 || offset != guard.Offset ||
                    length != guard.Original.Length || length != guard.Patched.Length ||
                    !b.AsSpan(position, length).SequenceEqual(guard.Patched))
                {
                    throw new FormatException("IPS32 write does not match its guard.");
                }

                position += length;
                previousEnd = (ulong)offset + (ulong)length;
            }

            if (position != b.Length - 4)
            {
                throw new FormatException("Unprotected IPS32 write.");
            }
        }

        private static ulong TitleId(string text)
        {
            if (text is not { Length: 16 } || !ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong titleId) || titleId == 0)
            {
                throw new FormatException("Invalid title ID.");
            }

            return titleId;
        }

        private static void ValidateTag(string tag)
        {
            if (tag is not { Length: 66 } || tag[0] != '"' || tag[^1] != '"')
            {
                throw new FormatException("Invalid signed tag.");
            }

            Hex(tag[1..^1], 32);
        }

        private static byte[] Hex(string text, int maxBytes)
        {
            if (string.IsNullOrEmpty(text) || text.Length % 2 != 0 || text.Length / 2 > maxBytes)
            {
                throw new FormatException("Invalid hex bytes.");
            }

            return Convert.FromHexString(text);
        }

        private static byte[] Base64(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > MaxDocumentBytes * 2 || text.Length % 4 != 0)
            {
                throw new FormatException("Invalid Base64.");
            }

            return Convert.FromBase64String(text);
        }

        private static DateTimeOffset Date(JsonElement element, string field)
        {
            string raw = element.GetProperty(field).GetString();
            if (raw is not { Length: >= 20 } || !raw.EndsWith('Z') ||
                !DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset date))
            {
                throw new FormatException("Invalid patch catalog timestamp.");
            }

            return date;
        }
    }
}
