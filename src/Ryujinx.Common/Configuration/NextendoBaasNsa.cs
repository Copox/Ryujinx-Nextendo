using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo] Le NSA (NetworkServiceAccountId) que le compte Nextendo présente sur une vraie
    /// Switch : son baasUserID, 16 chiffres hexadécimaux. C'est l'identifiant sous lequel ses amis
    /// Switch le connaissent (friendId de nx-account) et sous lequel ses amis émulateur le
    /// reçoivent (champ « nsa » de /api/friends).
    ///
    /// Pourquoi : Animal Crossing: New Horizons compare le NSA qu'un joueur annonce (session Pia,
    /// en-tête d'une carte) aux amis nn::friends de la console (Friend::GetAccountId) : session en
    /// 0x1c4ae20 et 0x1cbfc40, cartes reçues en 0x1cb6ef0 et 0x1cb8e00 (main 3.0.3). Une Switch
    /// annonce son baasUserID et range ses amis par baasUserID ; Ryujinx annonçait le PID et
    /// rangeait ses amis par PID : Switch et émulateur ne se reconnaissaient jamais comme amis
    /// (meilleurs amis impossibles, cartes écartées). Dans ACNH, Ryujinx annonce donc le
    /// baasUserID et range ses amis par baasUserID, comme une Switch.
    ///
    /// Source : /api/me (champ « nsa », ajouté côté nextendo-account). Gardé sur disque par compte
    /// (nextendo_nsa.txt) : un ACNH lancé hors ligne reste cohérent. Inconnu (site ancien,
    /// injoignable) : 0, et l'émulateur garde le PID partout, comme avant.
    /// NEXTENDO_NSA_BAAS=0 coupe tout.
    /// </summary>
    public static class NextendoBaasNsa
    {
        /// <summary>Animal Crossing: New Horizons.</summary>
        public const ulong AcnhProgramId = 0x01006F8002326000UL;

        private static readonly TimeSpan _retry = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan _askTimeout = TimeSpan.FromSeconds(3);

        private static readonly object _lock = new();
        private static ulong _pid;
        private static ulong _nsa;
        private static long _askedAt = long.MinValue;

        public static bool Enabled => Environment.GetEnvironmentVariable("NEXTENDO_NSA_BAAS") != "0";

        /// <summary>Vrai pour les jeux qui comparent le NSA annoncé à nn::friends et doivent voir le
        /// même NSA qu'une Switch.</summary>
        public static bool TitleUsesBaasNsa(ulong programId) => programId == AcnhProgramId;

        public static bool TitleUsesBaasNsa(string titleId)
            => ulong.TryParse(titleId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong id) && TitleUsesBaasNsa(id);

        private static string FilePath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_nsa.txt");

        /// <summary>
        /// Le baasUserID du compte lié, ou 0. Connu une fois (site ou disque), il ne change plus pour
        /// ce compte pendant la session : l'identité annoncée et la liste d'amis restent d'accord.
        /// </summary>
        public static ulong Get()
        {
            if (!Enabled || !NextendoAccount.IsLinked)
            {
                return 0;
            }

            ulong pid = NextendoAccount.Pid;
            if (pid == 0)
            {
                return 0;
            }

            lock (_lock)
            {
                if (_pid == pid && _nsa != 0)
                {
                    return _nsa;
                }

                if (_pid != pid)
                {
                    _pid = pid;
                    _nsa = ReadFile(pid);
                    _askedAt = long.MinValue;
                    if (_nsa != 0)
                    {
                        return _nsa;
                    }
                }

                if (Environment.TickCount64 - _askedAt < (long)_retry.TotalMilliseconds)
                {
                    return 0;
                }

                _askedAt = Environment.TickCount64;
            }

            ulong asked = Ask();

            lock (_lock)
            {
                if (_pid == pid && _nsa == 0 && asked != 0)
                {
                    _nsa = asked;
                    WriteFile(pid, asked);
                }

                return _pid == pid ? _nsa : 0;
            }
        }

        private static ulong Ask()
        {
            try
            {
                using HttpClient http = new() { Timeout = _askTimeout };
                using HttpRequestMessage req = new(HttpMethod.Get, NextendoEndpoint.BaseUrl().TrimEnd('/') + "/api/me");

                req.Headers.Add("Authorization", "Bearer " + NextendoAccount.NexToken);

                using HttpResponseMessage resp = http.Send(req);
                if (!resp.IsSuccessStatusCode)
                {
                    return 0;
                }

                using Stream body = resp.Content.ReadAsStream();
                using JsonDocument doc = JsonDocument.Parse(body);

                return Parse(doc.RootElement);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>Le « nsa » d'une réponse de /api/me ({"account":{...},"nsa":"0123456789abcdef"}), ou 0.</summary>
        public static ulong Parse(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("nsa", out JsonElement nsa) &&
                nsa.ValueKind == JsonValueKind.String)
            {
                return ParseHex(nsa.GetString());
            }

            return 0;
        }

        /// <summary>16 chiffres hexadécimaux exactement, non nuls, sinon 0.</summary>
        public static ulong ParseHex(string value)
        {
            value = value?.Trim() ?? "";

            return value.Length == 16 &&
                   ulong.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong v)
                ? v
                : 0;
        }

        /// <summary>Le NSA à annoncer : le baasUserID dans les jeux concernés quand il est connu,
        /// sinon le PID (comportement d'avant).</summary>
        public static ulong Choose(ulong programId, ulong pid, ulong baasNsa)
            => TitleUsesBaasNsa(programId) && baasNsa != 0 ? baasNsa : pid;

        private static ulong ReadFile(ulong pid)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return 0;
                }

                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0 && ulong.TryParse(line[..eq].Trim(), out ulong p) && p == pid)
                    {
                        return ParseHex(line[(eq + 1)..]);
                    }
                }
            }
            catch (Exception)
            {
                // fichier illisible : redemandé au site
            }

            return 0;
        }

        private static void WriteFile(ulong pid, ulong nsa)
        {
            try
            {
                string line = $"{pid}={nsa:x16}";
                string[] old = File.Exists(FilePath) ? File.ReadAllLines(FilePath) : [];
                using StreamWriter w = new(FilePath, false);

                foreach (string l in old)
                {
                    int eq = l.IndexOf('=');
                    if (eq > 0 && !(ulong.TryParse(l[..eq].Trim(), out ulong p) && p == pid))
                    {
                        w.WriteLine(l);
                    }
                }

                w.WriteLine(line);
            }
            catch (Exception)
            {
                // pas de cache disque : redemandé à la prochaine session
            }
        }
    }
}
