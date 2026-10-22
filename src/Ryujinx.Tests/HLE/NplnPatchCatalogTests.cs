using NSec.Cryptography;
using NUnit.Framework;
using Ryujinx.HLE.HOS;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ryujinx.Tests.HLE
{
    [NonParallelizable]
    public class NplnPatchCatalogTests
    {
        // Super Mario Party Jamboree 2.3.0 patch as served by nextendo.network on 2026-10-22.
        private const string LiveJamboreeEnvelope = @"{""key_id"":""npln-patches-key-1"",""payload"":""eyJkb2N1bWVudF90eXBlIjoibnBsbl9wYXRjaF9jYXRhbG9nIiwic2NoZW1hX3ZlcnNpb24iOjEsInJldmlzaW9uIjo1LCJpc3N1ZWRfYXQiOiIyMDI2LTEwLTIyVDA1OjAxOjQ1LjQ0OTI1NVoiLCJleHBpcmVzX2F0IjoiMjAyNi0xMC0yM1QwNTowMTo0NS40NDkyNTVaIiwicGF0Y2hfZXRhZyI6IlwiMzNjMmUwN2MwZTJhMTEwZGQwN2RjMTcxYjFiOGZmMzI1OTlmOGJmZTIxMzIwOGY1MTQ2YmY2ZGJkODNkYzYyZFwiIiwicGF0Y2hlcyI6W3siaWQiOiJtcGotMjMwLWNlcnRpZmljYXRlIiwiZW5hYmxlZCI6dHJ1ZSwidGl0bGVfaWQiOiIwMTAwOTY1MDE3MzM4MDAwIiwiZ2FtZV92ZXJzaW9uIjoiMi4zLjAiLCJtb2R1bGUiOiJtYWluIiwiYnVpbGRfaWQiOiJENzBDRTE3MjZDOTc5RTQ1RUQ0MjAwRTlDMkIyM0IyQTI1OEZEQzE4MDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwIiwicGF0Y2hfdmVyc2lvbiI6MSwicmVxdWlyZWRfY2FwYWJpbGl0aWVzIjpbIm5wbG4uaXBzMzIudjEiXSwib2Zmc2V0X2Jhc2lzIjoibnNvX2hlYWRlcl9pbmNsdWRlZCIsImlwczMyX2Jhc2U2NCI6IlNWQlRNeklBclRtOEFBUXFBSUJTUlVWUFJnPT0iLCJzaGEyNTYiOiJhNWUzZTFlMDNhN2I5NGNkMmU0YjU1NjlmOWM2MTg5MzIwMjkxNDAxMzI4YmJiZDVmODg3ODg0MDZkZTI3ZTBhIiwiZ3VhcmRzIjpbeyJvZmZzZXQiOjExMzUyNTA4LCJvcmlnaW5hbF9oZXgiOiJBQUUyNDAzOSIsInBhdGNoZWRfaGV4IjoiMkEwMDgwNTIifV19XX0="",""signature"":""7vvy30TYkX7ahzoNu4tblXqWX+/EfrugZYhVScX3tm5eOP1jJ81BDMzzQ5APnQIXIdHbN0zoe4vFHfETc9gnAw==""}";

        private const string BuildId = "AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899";
        private static readonly DateTimeOffset _now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

        private Key _key;

        [SetUp]
        public void CreateKey() => _key = Key.Create(SignatureAlgorithm.Ed25519);

        [TearDown]
        public void DisposeKey() => _key.Dispose();

        private static byte[] Ips(uint offset, byte[] data)
        {
            byte[] ips = new byte[5 + 6 + data.Length + 4];
            Encoding.ASCII.GetBytes("IPS32").CopyTo(ips, 0);
            ips[5] = (byte)(offset >> 24);
            ips[6] = (byte)(offset >> 16);
            ips[7] = (byte)(offset >> 8);
            ips[8] = (byte)offset;
            ips[9] = (byte)(data.Length >> 8);
            ips[10] = (byte)data.Length;
            data.CopyTo(ips, 11);
            Encoding.ASCII.GetBytes("EEOF").CopyTo(ips, ips.Length - 4);

            return ips;
        }

        private string Envelope(object payload, string keyId = NplnPatchCatalog.KeyId, Key signer = null)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            byte[] signature = SignatureAlgorithm.Ed25519.Sign(signer ?? _key, bytes);

            return JsonSerializer.Serialize(new { key_id = keyId, payload = Convert.ToBase64String(bytes), signature = Convert.ToBase64String(signature) });
        }

        private static object Manifest(byte[] ips, uint guardOffset, string original, string patched, string sha256 = null,
            ulong revision = 3, string titleId = "0100aaaa00000000", bool enabled = true, string expires = "2026-10-23T05:00:00Z")
            => new
            {
                document_type = "npln_patch_catalog",
                schema_version = 1,
                revision,
                issued_at = "2026-10-22T05:00:00Z",
                expires_at = expires,
                patch_etag = "\"" + new string('a', 64) + "\"",
                patches = new[]
                {
                    new
                    {
                        id = "test-patch",
                        enabled,
                        title_id = titleId,
                        module = "main",
                        build_id = BuildId,
                        patch_version = 1,
                        required_capabilities = new[] { NplnPatchCatalog.Capability },
                        offset_basis = "nso_header_included",
                        ips32_base64 = Convert.ToBase64String(ips),
                        sha256 = sha256 ?? Convert.ToHexString(SHA256.HashData(ips)).ToLowerInvariant(),
                        guards = new[] { new { offset = guardOffset, original_hex = original, patched_hex = patched } },
                    },
                },
            };

        private NplnPatchManifest Verify(object manifest) => NplnPatchCatalog.VerifyManifest(Envelope(manifest), _key.PublicKey, _now, false);

        [Test]
        public void LiveCatalogDocumentVerifiesWithTheShippedKey()
        {
            NplnPatchManifest manifest = NplnPatchCatalog.VerifyManifest(LiveJamboreeEnvelope, _now, allowExpired: true);

            Assert.That(manifest.Patches, Has.Length.EqualTo(1));
            Assert.That(manifest.Patches[0].Id, Is.EqualTo("mpj-230-certificate"));
            Assert.That(manifest.Patches[0].TitleId, Is.EqualTo(0x0100965017338000UL));
            Assert.That(manifest.Patches[0].Guards[0].Offset, Is.EqualTo(0xAD39BCU));
        }

        [Test]
        public void SignatureFromAnotherKeyIsRejected()
        {
            using Key other = Key.Create(SignatureAlgorithm.Ed25519);
            string envelope = Envelope(Manifest(Ips(0x200, [1, 2, 3, 4]), 0x200, "AABBCCDD", "01020304"), signer: other);

            Assert.Throws<FormatException>(() => NplnPatchCatalog.VerifyManifest(envelope, _key.PublicKey, _now, false));
        }

        [Test]
        public void UnknownKeyIdIsRejected()
        {
            string envelope = Envelope(Manifest(Ips(0x200, [1, 2, 3, 4]), 0x200, "AABBCCDD", "01020304"), keyId: "catalog-key-1");

            Assert.Throws<FormatException>(() => NplnPatchCatalog.VerifyManifest(envelope, _key.PublicKey, _now, false));
        }

        [Test]
        public void WriteWithoutMatchingGuardIsRejected()
        {
            // The IPS writes 01020304, but the guard declares 05060708.
            Assert.Throws<FormatException>(() => Verify(Manifest(Ips(0x200, [1, 2, 3, 4]), 0x200, "AABBCCDD", "05060708")));
        }

        [Test]
        public void WrongHashIsRejected()
        {
            Assert.Throws<FormatException>(() => Verify(Manifest(Ips(0x200, [1, 2, 3, 4]), 0x200, "AABBCCDD", "01020304", sha256: new string('0', 64))));
        }

        [Test]
        public void ExpiredDocumentIsOnlyAcceptedFromCache()
        {
            string envelope = Envelope(Manifest(Ips(0x200, [1, 2, 3, 4]), 0x200, "AABBCCDD", "01020304", expires: "2026-10-22T06:00:00Z"));

            Assert.Throws<FormatException>(() => NplnPatchCatalog.VerifyManifest(envelope, _key.PublicKey, _now, false));
            Assert.DoesNotThrow(() => NplnPatchCatalog.VerifyManifest(envelope, _key.PublicKey, _now, true));
        }

        [Test]
        public void PatchAppliesOnlyWhenOriginalBytesMatch()
        {
            ulong title = 0x0100AAAA00000001;
            NplnPatchCatalog.Remember(Verify(Manifest(Ips(0x200, [1, 2, 3, 4]), 0x200, "AABBCCDD", "01020304", titleId: "0100aaaa00000001")));

            byte[] matching = new byte[0x200];
            new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }.CopyTo(matching, 0x100);
            Assert.That(NplnPatchCatalog.TryApply(title, "main", BuildId, matching, out bool applied), Is.True);
            Assert.That(applied, Is.True);
            Assert.That(matching.Skip(0x100).Take(4), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));

            byte[] different = new byte[0x200];
            Assert.That(NplnPatchCatalog.TryApply(title, "main", BuildId, different, out applied), Is.True, "the catalog still decides on this build");
            Assert.That(applied, Is.False);
            Assert.That(different.All(b => b == 0), Is.True);

            Assert.That(NplnPatchCatalog.TryApply(title, "subsdk0", BuildId, matching, out _), Is.False);
            Assert.That(NplnPatchCatalog.TryApply(title + 1, "main", BuildId, matching, out _), Is.False);
        }

        [Test]
        public void WithdrawnPatchIsAuthoritativeButNotApplied()
        {
            ulong title = 0x0100AAAA00000002;
            NplnPatchCatalog.Remember(Verify(Manifest(Ips(0x200, [1, 2, 3, 4]), 0x200, "AABBCCDD", "01020304", titleId: "0100aaaa00000002", enabled: false)));

            byte[] program = new byte[0x200];
            new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }.CopyTo(program, 0x100);
            Assert.That(NplnPatchCatalog.TryApply(title, "main", BuildId, program, out bool applied), Is.True);
            Assert.That(applied, Is.False);
            Assert.That(program.Skip(0x100).Take(4), Is.EqualTo(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }));
        }

        [Test]
        public void OlderRevisionNeverReplacesNewer()
        {
            ulong title = 0x0100AAAA00000003;
            NplnPatchCatalog.Remember(Verify(Manifest(Ips(0x200, [1, 2, 3, 4]), 0x200, "AABBCCDD", "01020304", revision: 5, titleId: "0100aaaa00000003")));
            NplnPatchCatalog.Remember(Verify(Manifest(Ips(0x200, [9, 9, 9, 9]), 0x200, "AABBCCDD", "09090909", revision: 4, titleId: "0100aaaa00000003")));

            Assert.That(NplnPatchCatalog.Cached(title, BuildId, out ulong revision, out _).Guards[0].Patched, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(revision, Is.EqualTo(5UL));
        }
    }
}
