#!/usr/bin/env python3
# [Nextendo] Release-time bake, driven by CI secrets/env — never committed with real values.
#
# The open-source tree resolves the Nextendo server addresses from environment variables and
# falls back to loopback (see DnsMitmResolver / NextendoNetworkCheck), and ships ReleaseInformation
# with %%RYUJINX_...%% placeholders. Official builds run this once, on a throwaway CI checkout, to
# bake the real addresses in and stamp the version. Addresses come from the NEXTENDO_SERVER_IP /
# NEXTENDO_NAT_IP secrets, never from this file.
#
# Usage: NEXTENDO_SERVER_IP=... NEXTENDO_NAT_IP=... bake_release.py <version> <git_hash>
import os, io, sys

server = os.environ["NEXTENDO_SERVER_IP"].strip()
nat = os.environ["NEXTENDO_NAT_IP"].strip()
# nncs NAT-check responders: default to the backend IPs (single-host setup) but can be set
# independently, so the NAT check stays put when the account/game backend moves (e.g. onto nx1).
nncs1 = os.environ.get("NEXTENDO_NNCS1_IP", server).strip()
nncs2 = os.environ.get("NEXTENDO_NNCS2_IP", nat).strip()
# The registered Nextendo Developers client_id for the official Ryujinx build (public PKCE
# client, no secret possible by design — see NextendoAppSecrets.cs). Gitignored like the file
# itself: a source checkout never carries the real value.
client_id = os.environ["NEXTENDO_OFFICIAL_CLIENT_ID"].strip()
# Ed25519 private key (base64, 32-byte seed) matching the public key registered server-side for
# client_id. Signs every request (NextendoAttestation.cs) so a client_id copied out of a packet
# capture can't be replayed as this build -- only forging a NEW signature needs this key.
official_private_key = os.environ["NEXTENDO_OFFICIAL_PRIVATE_KEY"].strip()
version = sys.argv[1]
git_hash = sys.argv[2][:7]

# Two-phase on purpose: EVERY pattern is checked before ANY file is written.
# A partial bake used to be possible -- one file rewritten, the next pattern missing, the run
# aborted -- and the resulting tree still builds: ReleaseInformation keeps its %% placeholders,
# so IsValid is false and the version/update gates are silently inactive in a shipped build.
# Better to touch nothing at all than to leave that behind.
_pending = []

def patch(rel, old, new):
    with io.open(rel, encoding="utf-8") as f:
        s = f.read()
    n = s.count(old)
    if n != 1:
        raise SystemExit(f"BAKE FAIL {rel}: expected exactly 1 match, found {n}")
    _pending.append((rel, s.replace(old, new)))

def write_new(rel, content):
    _pending.append((rel, content))

_pending_deletes = []

def delete_existing(rel):
    if not os.path.isfile(rel):
        raise SystemExit(f"BAKE FAIL {rel}: expected to exist, not found")
    _pending_deletes.append(rel)

def commit():
    for rel, s in _pending:
        with io.open(rel, "w", encoding="utf-8", newline="") as f:
            f.write(s)
        print(f"  baked {rel}")
    for rel in _pending_deletes:
        os.remove(rel)
        print(f"  removed {rel}")

# 1) DnsMitmResolver: real server IPs instead of the loopback fallback
patch("src/Ryujinx.HLE/HOS/Services/Sockets/Sfdnsres/Proxy/DnsMitmResolver.cs",
"""            string value = Environment.GetEnvironmentVariable(envVar);

            if (!IPAddress.TryParse(value, out IPAddress address))
            {
                Logger.Warning?.PrintMsg(LogClass.ServiceBsd, $"DnsMitmResolver: {envVar} not set, falling back to loopback — Nintendo hosts will resolve to 127.0.0.1");

                return IPAddress.Loopback;
            }

            return address;""",
f"""            string value = Environment.GetEnvironmentVariable(envVar);

            if (IPAddress.TryParse(value, out IPAddress address))
            {{
                return address;
            }}

            return IPAddress.Parse(envVar == "NEXTENDO_NAT_IP" ? "{nat}" : "{server}");""")

# 2) NextendoNetworkCheck: real nncs responders, DECOUPLED from the backend IP (bakes their own
#    fallback so a build whose backend points at nx1 still probes the two real NAT responders)
patch("src/Ryujinx/Common/NextendoNetworkCheck.cs",
'''        private static readonly string Nncs1Configure =
            Environment.GetEnvironmentVariable("NEXTENDO_NNCS1_IP")
            ?? Environment.GetEnvironmentVariable("NEXTENDO_SERVER_IP") ?? "127.0.0.1";
        private static readonly string Nncs2Configure =
            Environment.GetEnvironmentVariable("NEXTENDO_NNCS2_IP")
            ?? Environment.GetEnvironmentVariable("NEXTENDO_NAT_IP") ?? "127.0.0.1";''',
f'''        private static readonly string Nncs1Configure =
            Environment.GetEnvironmentVariable("NEXTENDO_NNCS1_IP")
            ?? Environment.GetEnvironmentVariable("NEXTENDO_SERVER_IP") ?? "{nncs1}";
        private static readonly string Nncs2Configure =
            Environment.GetEnvironmentVariable("NEXTENDO_NNCS2_IP")
            ?? Environment.GetEnvironmentVariable("NEXTENDO_NAT_IP") ?? "{nncs2}";''')

# 3) ReleaseInformation: stamp version/channel/config so IsValid is true (gates active)
patch("src/Ryujinx.Common/ReleaseInformation.cs",
'''        private const string BuildVersion = "%%RYUJINX_BUILD_VERSION%%";
        private const string BuildGitHash = "%%RYUJINX_BUILD_GIT_HASH%%";
        private const string ReleaseChannelName = "%%RYUJINX_TARGET_RELEASE_CHANNEL_NAME%%";
        private const string ConfigFileName = "%%RYUJINX_CONFIG_FILE_NAME%%";''',
f'''        private const string BuildVersion = "{version}";
        private const string BuildGitHash = "{git_hash}";
        private const string ReleaseChannelName = "release";
        private const string ConfigFileName = "Config.json";''')

# 4) NextendoAppSecrets: gitignored, never checked out at all -- write it fresh rather than
#    patching a placeholder (see .gitignore comment next to the real path). The dev-build
#    template defines the SAME class for an unbaked checkout (empty AppToken) -- it has to go,
#    or the two collide as a duplicate type definition the moment the real file exists.
delete_existing("src/Ryujinx/Common/NextendoAppSecrets.template.cs")
write_new("src/Ryujinx/Common/NextendoAppSecrets.cs", f'''namespace Ryujinx.Ava.Common
{{
    /// <summary>
    /// [Nextendo] The application identity that proves to nextendo.network THIS BUILD is a
    /// registered Ryujinx-Nextendo client, created via https://nextendo.network/developers,
    /// as opposed to the player's own login token (NextendoAccount.NexToken), which only proves
    /// WHO is playing, not WHICH client is asking. Ryujinx is a public PKCE client by design (no
    /// secret it could keep), so the client_id itself is the credential here, exactly like any
    /// third-party desktop app registered the same way -- no special bypass for being "official".
    ///
    /// Baked by bake_release.py from the NEXTENDO_OFFICIAL_CLIENT_ID / NEXTENDO_OFFICIAL_PRIVATE_KEY
    /// CI secrets. This file is gitignored on purpose: a source checkout never carries the real
    /// values.
    /// </summary>
    public static class NextendoAppSecrets
    {{
        public const string AppToken = "{client_id}";
        public const string AppPrivateKeyB64 = "{official_private_key}";
    }}
}}
''')

commit()
print("BAKE OK")
