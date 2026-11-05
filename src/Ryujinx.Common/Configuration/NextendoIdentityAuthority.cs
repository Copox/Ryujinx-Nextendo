using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ryujinx.Common.Configuration
{
    public sealed record NextendoIdentitySnapshot(ulong Pid, string Token, string ProfileUserId, long Generation)
    {
        public override string ToString() => $"NextendoIdentitySnapshot {{ Pid = {Pid}, Generation = {Generation} }}";
    }

    internal sealed record NextendoVerifiedIdentity(ulong Pid, string Username, string FriendCode, bool IsGuest);

    internal sealed class NextendoIdentityAuthority
    {
        private readonly HttpClient _client;

        internal NextendoIdentityAuthority(HttpMessageHandler handler = null)
        {
            _client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(5),
                MaxResponseContentBufferSize = 2 * 1024 * 1024,
            };
        }

        internal async Task<(NextendoVerifiedIdentity Identity, bool Rejected)> ResolveAsync(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 2048)
            {
                return (null, true);
            }
            foreach (char character in token)
                if (character <= ' ' || character >= 127) return (null, true);

            try
            {
                // Only the account authority may resolve identity. Never follow token-selected URLs or redirects.
                using HttpRequestMessage request = new(HttpMethod.Get, NextendoEndpoint.Canonical + "/api/me");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.UserAgent.ParseAdd("Nextendo-Ryujinx/AccountIdentity");
                using HttpResponseMessage response = await _client.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return (null, response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden);
                }

                using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false),
                    new JsonDocumentOptions { MaxDepth = 24 });
                JsonElement account = document.RootElement.GetProperty("account");
                JsonElement id = account.GetProperty("pid");
                string decimalId = id.ValueKind == JsonValueKind.String ? id.GetString() : id.GetRawText();
                if (!ulong.TryParse(decimalId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong pid) || pid == 0 ||
                    pid.ToString(CultureInfo.InvariantCulture) != decimalId)
                {
                    return (null, false);
                }

                string username = account.GetProperty("username").GetString();
                string friendCode = account.GetProperty("friend_code").GetString();
                if (string.IsNullOrWhiteSpace(username) || username.Length > 64 || username.Contains('\n') || username.Contains('\r') ||
                    friendCode == null || friendCode.Length != 17 || !friendCode.StartsWith("SW-", StringComparison.Ordinal))
                {
                    return (null, false);
                }

                for (int i = 3; i < friendCode.Length; i++)
                {
                    if (i == 7 || i == 12 ? friendCode[i] != '-' : friendCode[i] < '0' || friendCode[i] > '9')
                    {
                        return (null, false);
                    }
                }

                bool isGuest = account.TryGetProperty("email", out JsonElement email) && email.ValueKind == JsonValueKind.String &&
                    (email.GetString() ?? "").EndsWith("@nextendo.local", StringComparison.OrdinalIgnoreCase);
                return (new NextendoVerifiedIdentity(pid, username, friendCode, isGuest), false);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or
                JsonException or InvalidOperationException or FormatException or System.Collections.Generic.KeyNotFoundException)
            {
                // Errors must not contain credentials or turn an unavailable authority into an authorization.
                return (null, false);
            }
        }
    }
}
