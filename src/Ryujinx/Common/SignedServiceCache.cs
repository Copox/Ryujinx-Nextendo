using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace Ryujinx.Ava.Common
{
    public sealed class SignedServiceCache
    {
        private sealed record Stored(string Envelope, string ETag, DateTimeOffset NextCheck, DateTimeOffset RetryAt, int Failures, string IdentityEnvelope = null);
        private readonly string _path, _kind, _endpoint;
        private readonly Action<string> _validate;
        private string _envelope, _etag, _identityEnvelope;
        private DateTimeOffset _nextCheck, _retryAt;
        private int _failures;
        public VerifiedServiceDocument Document { get; private set; }
        public VerifiedServiceDocument LastAuthenticatedDocument { get; private set; }
        public string LastError { get; private set; } = "";
        public bool IsValid(DateTimeOffset now) => Document != null && Document.ExpiresAt > now;
        public SignedServiceCache(string path, string kind, string endpoint, Action<string> validate, DateTimeOffset now)
        {
            _path = path;
            _kind = kind;
            _endpoint = endpoint;
            _validate = validate;
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > ServiceCatalogParser.MaxBytes * 2)
                    return;
                Stored stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
                _failures = Math.Clamp(stored.Failures, 0, 4);
                _retryAt = stored.RetryAt <= now.AddMinutes(65) ? stored.RetryAt : default;
                string identityEnvelope = stored.Envelope ?? stored.IdentityEnvelope;
                if (identityEnvelope == null)
                    return;
                VerifiedServiceDocument verified = ServiceCatalogParser.Verify(identityEnvelope, kind, now, allowExpiredForIdentity: true);
                validate(verified.Payload);
                LastAuthenticatedDocument = verified;
                _identityEnvelope = identityEnvelope;
                if (verified.ExpiresAt <= now)
                    return;
                Document = verified;
                _envelope = identityEnvelope;
                _etag = stored.ETag;
                _nextCheck = stored.NextCheck <= now.AddHours(6.5) && stored.NextCheck <= verified.ExpiresAt ? stored.NextCheck : default;
            }
            catch { Document = null; _envelope = null; _etag = null; _retryAt = default; }
        }
        public async Task<bool> RefreshAsync(HttpClient http, DateTimeOffset now, bool manual = false)
        {
            if (!manual && (_retryAt > now || IsValid(now) && _nextCheck > now))
                return false;
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, _endpoint);
                bool conditional = IsValid(now) && !string.IsNullOrEmpty(_etag);
                if (conditional)
                    request.Headers.TryAddWithoutValidation("If-None-Match", _etag);
                using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    if (!conditional || !IsValid(DateTimeOffset.UtcNow))
                        throw new FormatException("Unexpected or expired 304.");
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                    string envelope = await ReadBoundedAsync(response);
                    VerifiedServiceDocument verified = ServiceCatalogParser.Verify(envelope, _kind, DateTimeOffset.UtcNow);
                    _validate(verified.Payload);
                    if ((Document ?? LastAuthenticatedDocument) is VerifiedServiceDocument previous && verified.Revision < previous.Revision)
                        throw new FormatException("Catalog revision rollback.");
                    Document = verified;
                    LastAuthenticatedDocument = verified;
                    _identityEnvelope = envelope;
                    _envelope = envelope;
                    _etag = response.Headers.ETag?.ToString();
                }
                _failures = 0;
                _retryAt = default;
                LastError = "";
                _nextCheck = now.AddMinutes(360 + Random.Shared.Next(-30, 31));
                if (_nextCheck > Document.ExpiresAt)
                    _nextCheck = Document.ExpiresAt;
                Save();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name;
                int[] minutes = { 1, 5, 15, 60 };
                int delay = minutes[Math.Min(_failures, 3)];
                _failures = Math.Min(_failures + 1, 4);
                _retryAt = now.AddSeconds(delay * 60 * (0.9 + Random.Shared.NextDouble() * 0.2));
                if (!IsValid(now))
                { Document = null; _envelope = null; _etag = null; }
                Save();
                return false;
            }
        }
        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                string temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(new Stored(_envelope, _etag, _nextCheck, _retryAt, _failures, _identityEnvelope)));
                File.Move(temp, _path, true);
            }
            catch { /* Disk failure never makes unverified metadata usable. */ }
        }
        public static async Task<string> ReadBoundedAsync(HttpResponseMessage response)
        {
            using Stream stream = await response.Content.ReadAsStreamAsync();
            using MemoryStream buffer = new();
            byte[] chunk = new byte[8192];
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
            int read;
            while ((read = await stream.ReadAsync(chunk, deadline.Token)) != 0)
            { if (buffer.Length + read > ServiceCatalogParser.MaxBytes) throw new FormatException("Response too large."); buffer.Write(chunk, 0, read); }
            return new UTF8Encoding(false, true).GetString(buffer.ToArray());
        }
    }
}
