using System;
using System.Threading.Tasks;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo, HOME Menu work] What the console's "link an account" needs from the emulator:
    /// the Nextendo sign-in in the browser, and the account's picture. Set by the emulator side.
    /// </summary>
    public static class NextendoAccountLink
    {
        /// <summary>Signs in to Nextendo in the browser; (ok, error).</summary>
        public static Func<Task<(bool ok, string error)>> SignIn { get; set; }

        /// <summary>The picture of the signed-in account (JPEG), or null.</summary>
        public static Func<byte[]> Picture { get; set; }

        /// <summary>The sign-in page of the console was answered by a Nextendo sign-in.</summary>
        public static bool SignedIn { get; set; }

        private static string _email;
        private static long _emailAt = long.MinValue / 2;

        /// <summary>
        /// The e-mail address of the Nextendo account (/api/me), kept ten minutes; "" when it is
        /// not known. Shown by the user page of the HOME Menu, as a console shows the address of
        /// its Nintendo Account.
        /// </summary>
        public static string Email()
        {
            if (!NextendoAccount.IsLinked)
            {
                return "";
            }

            if (_email != null && Environment.TickCount64 - _emailAt < 10 * 60 * 1000)
            {
                return _email;
            }

            // The page that shows it waits for this answer: a known e-mail is given at once and
            // asked again behind (it does not change), the first one is asked ahead (Prepare).
            if (_email != null)
            {
                Prepare();

                return _email;
            }

            return Ask();
        }

        private static int _asking;

        /// <summary>Asks the e-mail ahead, without anybody waiting for it.</summary>
        public static void Prepare()
        {
            if (!NextendoAccount.IsLinked || (_email != null && Environment.TickCount64 - _emailAt < 10 * 60 * 1000) ||
                System.Threading.Interlocked.Exchange(ref _asking, 1) != 0)
            {
                return;
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    Ask();
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _asking, 0);
                }
            });
        }

        private static string Ask()
        {
            try
            {
                using System.Net.Http.HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
                using System.Net.Http.HttpRequestMessage req = new(System.Net.Http.HttpMethod.Get, NextendoEndpoint.BaseUrl().TrimEnd('/') + "/api/me");

                req.Headers.Add("Authorization", "Bearer " + NextendoAccount.NexToken);

                using System.Net.Http.HttpResponseMessage resp = http.Send(req);
                using System.IO.Stream body = resp.Content.ReadAsStream();
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(body);

                _email = doc.RootElement.TryGetProperty("account", out System.Text.Json.JsonElement account) &&
                         account.TryGetProperty("email", out System.Text.Json.JsonElement email) ? email.GetString() ?? "" : "";
                _emailAt = Environment.TickCount64;
            }
            catch (Exception)
            {
                _email ??= "";
            }

            return _email;
        }

        /// <summary>Set by the last import: the account already had its user on this emulator.</summary>
        public static bool AlreadyRegistered { get; set; }
    }
}
