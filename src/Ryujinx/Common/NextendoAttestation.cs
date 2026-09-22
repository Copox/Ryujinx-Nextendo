using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using System;
using System.Text;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Signs every server request with the build's Ed25519 private key
    /// (NextendoAppSecrets.AppPrivateKeyB64), so nextendo.network can tell "this request comes
    /// from a genuine Ryujinx-Nextendo build" from "this request merely knows the client_id" --
    /// the latter is trivially copyable out of a packet capture (Wireshark, a proxy), the former
    /// is not, since each signature is bound to a timestamp and a single-use nonce.
    ///
    /// Header shape: "X-Nextendo-Client: {client_id}.{unix_ts}.{nonce}.{signature_base64url}",
    /// signing exactly "{client_id}.{unix_ts}.{nonce}" -- see verifyClientAttestation server-side
    /// (oauth.go) for the matching check.
    /// </summary>
    public static class NextendoAttestation
    {
        private static readonly Lazy<Ed25519PrivateKeyParameters> PrivateKey = new(() =>
        {
            byte[] raw = Convert.FromBase64String(NextendoAppSecrets.AppPrivateKeyB64);
            return new Ed25519PrivateKeyParameters(raw, 0);
        });

        /// <summary>
        /// Builds the signed header value for right now. Returns null when this build has no
        /// private key baked in (a plain source checkout) -- callers should simply omit the
        /// header in that case, same as an unbaked AppToken already does.
        /// </summary>
        public static string BuildHeaderValue()
        {
            if (string.IsNullOrEmpty(NextendoAppSecrets.AppPrivateKeyB64))
            {
                return null;
            }

            string clientId = NextendoAppSecrets.AppToken;
            string ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            string nonce = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
            string message = $"{clientId}.{ts}.{nonce}";

            var signer = new Ed25519Signer();
            signer.Init(true, PrivateKey.Value);
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            signer.BlockUpdate(messageBytes, 0, messageBytes.Length);
            byte[] signature = signer.GenerateSignature();

            string sigB64Url = Convert.ToBase64String(signature)
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            return $"{message}.{sigB64Url}";
        }
    }
}
