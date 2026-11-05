using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Ryujinx.Common.Configuration
{
    internal static class NextendoCredentialFile
    {
        internal const string Prefix = "nextendo-protected-v1\n";
        internal const int MaxPlaintextBytes = 128 * 1024;
        private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("Nextendo/Ryujinx/Account/v1");

        internal static string Read(string path, int maxPlaintextBytes = MaxPlaintextBytes, string prefix = Prefix, byte[] entropy = null, bool allowPlaintext = true,
            Func<string, bool> validatePlaintext = null)
        {
            // Allow DPAPI and base64 overhead without increasing the accepted plaintext size.
            if (new FileInfo(path).Length > (maxPlaintextBytes + 4096L) * 2)
            {
                throw new InvalidDataException("Account file exceeds its size limit.");
            }

            string text = File.ReadAllText(path);
            if (!text.StartsWith(prefix, StringComparison.Ordinal))
            {
                if (!allowPlaintext) throw new CryptographicException("Protected storage format is required.");
                if (Encoding.UTF8.GetByteCount(text) > maxPlaintextBytes) throw new InvalidDataException("Account text exceeds its size limit.");
                if (validatePlaintext != null && !validatePlaintext(text)) throw new InvalidDataException("Unrecognized legacy account state.");
                return text;
            }

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("This credential belongs to a Windows user.");
            }

            byte[] plaintext = ProtectedData.Unprotect(Convert.FromBase64String(text[prefix.Length..]), entropy ?? _entropy, DataProtectionScope.CurrentUser);
            try
            {
                if (plaintext.Length > maxPlaintextBytes) throw new InvalidDataException("Account text exceeds its size limit.");
                return Encoding.UTF8.GetString(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        internal static byte[] Encode(string text, int maxPlaintextBytes = MaxPlaintextBytes, string prefix = Prefix, byte[] entropy = null)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Persistent credentials require a supported operating-system vault.");
            }

            byte[] plaintext = Encoding.UTF8.GetBytes(text);
            if (plaintext.Length > maxPlaintextBytes) throw new InvalidDataException("Account text exceeds its size limit.");
            byte[] ciphertext;
            try
            {
                ciphertext = ProtectedData.Protect(plaintext, entropy ?? _entropy, DataProtectionScope.CurrentUser);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            byte[] verified = ProtectedData.Unprotect(ciphertext, entropy ?? _entropy, DataProtectionScope.CurrentUser);
            try
            {
                if (Encoding.UTF8.GetString(verified) != text) throw new CryptographicException("Credential verification failed.");
            }
            finally { CryptographicOperations.ZeroMemory(verified); }
            return Encoding.UTF8.GetBytes(prefix + Convert.ToBase64String(ciphertext));
        }

        internal static void Write(string path, string text, int maxPlaintextBytes = MaxPlaintextBytes)
        {
            byte[] data = Encode(text, maxPlaintextBytes);
            WriteBytes(path, data, temporary =>
            {
                if (Read(temporary, maxPlaintextBytes) != text) throw new CryptographicException("Credential verification failed.");
            });
        }

        internal static void WriteBytes(string path, byte[] data, Action<string> verify = null)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, ".nextendo-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                FileStreamOptions options = new() { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (FileStream stream = new(temporary, options))
                {
                    stream.Write(data);
                    stream.Flush(true);
                }

                verify?.Invoke(temporary);

                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }
}
