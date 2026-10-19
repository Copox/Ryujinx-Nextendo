using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// Pays du compte Nextendo (code ISO à deux lettres) que le service de compte rend aux jeux dans
    /// les informations du compte Nintendo (NasUserBaseForApplication, 4 octets en 0x20).
    ///
    /// Mario Kart 8 Deluxe le lit par nn::account::CachedNintendoAccountInfo::GetCountry (le SDK lit
    /// une chaîne de 4 octets au plus en 0x20 de la structure), le range aux octets 0 et 1 de son
    /// profil de 132 octets (Ranking 4) et l'envoie aux autres joueurs, qui affichent le drapeau.
    /// Sans lui, le profil part avec 00 00 : aucun drapeau (38 % des profils du serveur le 18/10).
    ///
    /// Source : le pays du compte Nextendo (Account.Country de nextendo-account), lu sur /api/me
    /// (champ account.country). Le joueur le choisit à l'inscription et peut le changer ensuite.
    /// C'est le même champ que nx-account met dans le profil Nintendo Account (users/me) des
    /// vraies consoles, par /internal/identity : un compte a le même drapeau sur console et sur
    /// émulateur. Compte sans pays : champ vide, aucun drapeau (comme nx-account, jamais un pays
    /// inventé). NEXTENDO_PAYS_COMPTE=0 coupe tout : le champ redevient vide, comme avant.
    /// </summary>
    public static class NextendoCountry
    {
        private static readonly TimeSpan _knownTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan _unknownTtl = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan _firstAskTimeout = TimeSpan.FromSeconds(3);

        private static readonly object _lock = new();
        private static string _country;
        private static ulong _countryPid;
        private static long _countryAt;
        private static int _asking;

        /// <summary>Faux si NEXTENDO_PAYS_COMPTE vaut 0.</summary>
        public static bool Enabled => Environment.GetEnvironmentVariable("NEXTENDO_PAYS_COMPTE") != "0";

        /// <summary>
        /// Le pays du compte lié, ou "" (pas de compte, coupé, inconnu, site injoignable). Un pays
        /// connu est rendu tout de suite et redemandé en arrière-plan quand il a vieilli ; seule la
        /// toute première demande attend le site, 3 s au plus.
        /// </summary>
        public static string Get()
        {
            if (!Enabled || !NextendoAccount.IsLinked)
            {
                return "";
            }

            ulong pid = NextendoAccount.Pid;

            lock (_lock)
            {
                if (_country != null && _countryPid == pid)
                {
                    TimeSpan ttl = _country.Length != 0 ? _knownTtl : _unknownTtl;
                    if (Environment.TickCount64 - _countryAt >= (long)ttl.TotalMilliseconds)
                    {
                        Prepare();
                    }

                    return _country;
                }
            }

            return Ask(pid, _firstAskTimeout);
        }

        /// <summary>Demande le pays en avance, sans que personne l'attende.</summary>
        public static void Prepare()
        {
            if (!Enabled || !NextendoAccount.IsLinked || Interlocked.Exchange(ref _asking, 1) != 0)
            {
                return;
            }

            ulong pid = NextendoAccount.Pid;

            Task.Run(() =>
            {
                try
                {
                    Ask(pid, TimeSpan.FromSeconds(5));
                }
                finally
                {
                    Interlocked.Exchange(ref _asking, 0);
                }
            });
        }

        private static string Ask(ulong pid, TimeSpan timeout)
        {
            string country = null;

            try
            {
                using HttpClient http = new() { Timeout = timeout };
                using HttpRequestMessage req = new(HttpMethod.Get, NextendoEndpoint.BaseUrl().TrimEnd('/') + "/api/me");

                req.Headers.Add("Authorization", "Bearer " + NextendoAccount.NexToken);

                using HttpResponseMessage resp = http.Send(req);
                if (resp.IsSuccessStatusCode)
                {
                    using System.IO.Stream body = resp.Content.ReadAsStream();
                    using JsonDocument doc = JsonDocument.Parse(body);

                    country = Parse(doc.RootElement);
                }
            }
            catch (Exception)
            {
                // Site injoignable : on garde le dernier pays connu de ce compte.
            }

            lock (_lock)
            {
                if (country == null && _countryPid == pid && !string.IsNullOrEmpty(_country))
                {
                    country = _country;
                }

                _country = country ?? "";
                _countryPid = pid;
                _countryAt = Environment.TickCount64;

                return _country;
            }
        }

        /// <summary>Le pays d'une réponse de /api/me ({"account":{"country":"FR",...}}), ou "".</summary>
        public static string Parse(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("account", out JsonElement account) &&
                account.ValueKind == JsonValueKind.Object &&
                account.TryGetProperty("country", out JsonElement country) &&
                country.ValueKind == JsonValueKind.String)
            {
                return Normalize(country.GetString());
            }

            return "";
        }

        /// <summary>Deux lettres ASCII en majuscules, sinon "" (jamais autre chose dans la structure).</summary>
        public static string Normalize(string value)
        {
            if (value == null)
            {
                return "";
            }

            value = value.Trim();
            if (value.Length != 2)
            {
                return "";
            }

            char a = char.ToUpperInvariant(value[0]);
            char b = char.ToUpperInvariant(value[1]);

            return a is >= 'A' and <= 'Z' && b is >= 'A' and <= 'Z' ? new string(new[] { a, b }) : "";
        }
    }
}
