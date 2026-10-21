using Ryujinx.Common.Logging;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo] Ce que fait olsc chez Nintendo, contre notre serveur : l'île archivée du compte
    /// Nextendo sur /api/save/&lt;titre&gt; (un emplacement, la dernière envoyée gagne, restauration
    /// permise d'office). Utilisé par olsc:u (HLE) pendant la partie et, comme le service système
    /// de la Switch (« l'archivage se fait quand vous ne jouez pas »), après la fin du jeu.
    /// </summary>
    public static class NextendoIslandCloud
    {
        public enum Outcome
        {
            Ok,
            Unavailable,
            NetworkError,
            Refused,
            NoLocalSave,
            InvalidIsland,
            Unchanged,
            Deferred,
        }

        private static string Url(ulong titleId, string suffix = "") => $"{NextendoEndpoint.BaseUrl()}/api/save/{titleId:x16}{suffix}";

        /// <summary>Compte Nextendo lié, pas invité, en ligne permis, sur les serveurs Nextendo.</summary>
        public static bool Usable =>
            NextendoAccount.IsLinked && !NextendoAccount.IsGuest && !NextendoAccount.OnlineBlocked
            && !string.IsNullOrEmpty(NextendoAccount.NexToken) && !NextendoServerOverride.HorsNextendo;

        private static HttpClient Client(int seconds)
        {
            HttpClient http = new() { Timeout = TimeSpan.FromSeconds(seconds) };
            http.DefaultRequestHeaders.Add("Authorization", "Bearer " + NextendoAccount.NexToken);
            http.DefaultRequestHeaders.Add("X-Nextendo-Device", NextendoIslandSync.DeviceId);
            if (!string.IsNullOrEmpty(NextendoIslandSync.ClientId))
            {
                http.DefaultRequestHeaders.Add("X-Nextendo-Client-Id", NextendoIslandSync.ClientId);
            }

            return http;
        }

        private static Outcome FromStatus(HttpStatusCode status) => (int)status >= 500 ? Outcome.NetworkError : Outcome.Refused;

        /// <summary>État de l'île du compte.</summary>
        public static async Task<(Outcome, NextendoIslandSync.CloudIsland)> GetMetaAsync(ulong titleId)
        {
            if (!Usable)
            {
                return (Outcome.Unavailable, null);
            }

            try
            {
                using HttpClient http = Client(20);
                using HttpResponseMessage resp = await http.GetAsync(Url(titleId, "/meta"));
                if (!resp.IsSuccessStatusCode)
                {
                    Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] island: meta HTTP {(int)resp.StatusCode}");
                    return (FromStatus(resp.StatusCode), null);
                }

                return (Outcome.Ok, NextendoIslandSync.ParseMeta(await resp.Content.ReadAsStringAsync()));
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] island: meta failed ({ex.Message})");
                return (Outcome.NetworkError, null);
            }
        }

        /// <summary>Met le cache d'information (GetSaveDataBackupInfoCache) à l'image du serveur.</summary>
        public static void StoreCache(string key, NextendoIslandSync.CloudIsland island)
        {
            NextendoIslandSync.Update(key, e =>
            {
                e.HasCache = NextendoIslandSync.IsRestorable(island);
                e.CacheId = e.HasCache ? island.Rev : 0;
                e.CacheTime = e.HasCache ? island.PosixTime : 0;
                e.CacheCustom = e.HasCache ? Convert.ToBase64String(island.Custom) : "";
            });
        }

        public static byte[] DecodeCustom(string b64)
        {
            try
            {
                byte[] raw = string.IsNullOrEmpty(b64) ? [] : Convert.FromBase64String(b64);
                return raw.Length <= NextendoIslandSync.CustomDataSize ? raw : [];
            }
            catch (FormatException)
            {
                return [];
            }
        }

        private static bool HasFiles(string dir) => Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any();

        /// <summary>Le contenu validé (« 0 ») d'une sauvegarde, sinon celui de travail (« 1 »), ou null.</summary>
        public static string ContentDir(string saveRoot)
        {
            if (string.IsNullOrEmpty(saveRoot))
            {
                return null;
            }

            foreach (string sub in new[] { "0", "1" })
            {
                string dir = Path.Combine(saveRoot, sub);
                if (HasFiles(dir))
                {
                    return dir;
                }
            }

            return null;
        }

        /// <summary>
        /// Envoie l'île de la sauvegarde <paramref name="saveRoot"/> avec les données du jeu du profil.
        /// <paramref name="onlyIfChanged"/> : l'envoi d'après-partie n'envoie pas une île inchangée.
        /// </summary>
        // Un seul envoi à la fois : un envoi plus ancien ne peut pas finir après un plus récent.
        private static readonly SemaphoreSlim _gate = new(1, 1);

        /// <summary>
        /// Vrai quand le jeu de ce titre tourne : l'envoi d'après-partie n'est alors pas lancé
        /// (le jeu écrit sa sauvegarde ; l'envoi suivra sa fin). Posé par l'interface.
        /// </summary>
        public static Func<ulong, bool> IsGameRunning { get; set; } = _ => false;

        public static async Task<Outcome> UploadAsync(ulong titleId, string saveRoot, string key, bool onlyIfChanged)
        {
            await _gate.WaitAsync();
            try
            {
                return await UploadLockedAsync(titleId, saveRoot, key, onlyIfChanged);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Comme le service système de la Switch, l'archivage se fait hors du jeu. L'envoi est
        /// d'abord marqué « en attente » sur le disque : une fermeture de l'émulateur avant la fin
        /// le laisse en attente, et il repart au lancement suivant (<see cref="RunPendingAsync"/>).
        /// </summary>
        public static void MarkPending(string key, string saveRoot)
        {
            NextendoIslandSync.Update(key, e =>
            {
                e.PendingUpload = true;
                e.PendingSaveRoot = saveRoot ?? "";
            });
        }

        /// <summary>Envoie l'île d'un profil marqué en attente, puis lève la marque si c'est fait.</summary>
        public static async Task<Outcome> UploadPendingAsync(ulong titleId, string saveRoot, string key)
        {
            if (IsGameRunning(titleId))
            {
                return Outcome.Deferred;
            }

            Outcome outcome = await UploadAsync(titleId, saveRoot, key, onlyIfChanged: true);
            if (outcome is Outcome.Ok or Outcome.Unchanged or Outcome.NoLocalSave)
            {
                NextendoIslandSync.Update(key, e => e.PendingUpload = false);
            }

            return outcome;
        }

        /// <summary>Au lancement de l'émulateur : relance les envois restés en attente.</summary>
        public static async Task RunPendingAsync()
        {
            foreach ((string key, NextendoIslandSync.OlscEntry entry) in NextendoIslandSync.AllPending())
            {
                if (!ulong.TryParse(key.Split(':')[0], System.Globalization.NumberStyles.HexNumber, null, out ulong titleId) || !NextendoIslandSync.IsStrict(titleId))
                {
                    continue;
                }

                Outcome outcome = await UploadPendingAsync(titleId, entry.PendingSaveRoot, key);
                Logger.Info?.Print(LogClass.ServiceOlsc, $"[Nextendo] island: pending backup {outcome}");
            }
        }

        private static async Task<Outcome> UploadLockedAsync(ulong titleId, string saveRoot, string key, bool onlyIfChanged)
        {
            if (!Usable)
            {
                return Outcome.Unavailable;
            }

            if (onlyIfChanged && IsGameRunning(titleId))
            {
                return Outcome.Deferred;
            }

            string dir = ContentDir(saveRoot);
            if (dir == null)
            {
                return Outcome.NoLocalSave;
            }

            NextendoIslandSync.OlscEntry entry = NextendoIslandSync.Get(key);
            string hash = NextendoIslandSync.Fingerprint(dir);
            if (onlyIfChanged && hash.Length > 0 && hash == entry.LastUploadedHash)
            {
                return Outcome.Unchanged;
            }

            // Instantané en mémoire, vérifié : une sauvegarde qui change pendant la lecture n'est pas envoyée.
            byte[] zip = NextendoIslandSync.ZipDirectory(dir);
            if (NextendoIslandSync.Fingerprint(dir) != hash)
            {
                return Outcome.Deferred;
            }

            byte[] custom = DecodeCustom(entry.Custom);

            try
            {
                using HttpClient http = Client(180);
                using ByteArrayContent content = new(zip);
                using HttpRequestMessage request = new(HttpMethod.Put, Url(titleId)) { Content = content };
                request.Headers.Add("X-Save-Device", NextendoIslandSync.DeviceId);
                if (custom.Length > 0)
                {
                    request.Headers.Add("X-Save-Custom", Convert.ToBase64String(custom));
                }

                using HttpResponseMessage resp = await http.SendAsync(request);
                string body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] island: upload refused HTTP {(int)resp.StatusCode} {body}");
                    return FromStatus(resp.StatusCode);
                }

                NextendoIslandSync.CloudIsland sent = NextendoIslandSync.ParseMeta(body);
                sent.Exists = true;
                sent.Custom = custom;
                if (sent.PosixTime == 0)
                {
                    sent.PosixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }

                StoreCache(key, sent);
                NextendoIslandSync.Update(key, e => e.LastUploadedHash = hash);
                Logger.Info?.Print(LogClass.ServiceOlsc, $"[Nextendo] island: rev {sent.Rev} uploaded ({zip.Length} bytes)");

                return Outcome.Ok;
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] island: upload failed ({ex.Message})");
                return Outcome.NetworkError;
            }
        }

        /// <summary>
        /// Restauration : l'île du compte remplace celle de la sauvegarde <paramref name="saveRoot"/>.
        /// L'île de cette machine est d'abord gardée (5 dernières, nextendo_island_backups) ; un échec
        /// en cours de route remet l'île d'avant.
        /// </summary>
        public static async Task<Outcome> DownloadAsync(ulong titleId, string saveRoot, string key)
        {
            await _gate.WaitAsync();
            try
            {
                return await DownloadLockedAsync(titleId, saveRoot, key);
            }
            finally
            {
                _gate.Release();
            }
        }

        private static async Task<Outcome> DownloadLockedAsync(ulong titleId, string saveRoot, string key)
        {
            if (!Usable)
            {
                return Outcome.Unavailable;
            }

            if (string.IsNullOrEmpty(saveRoot) || !Directory.Exists(saveRoot))
            {
                return Outcome.NoLocalSave;
            }

            byte[] zip;
            ulong rev;
            try
            {
                using HttpClient http = Client(180);
                using HttpResponseMessage resp = await http.GetAsync(Url(titleId));
                if (resp.StatusCode == HttpStatusCode.NoContent || !resp.IsSuccessStatusCode)
                {
                    Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] island: download HTTP {(int)resp.StatusCode}");
                    return resp.StatusCode == HttpStatusCode.NoContent ? Outcome.Refused : FromStatus(resp.StatusCode);
                }

                zip = await resp.Content.ReadAsByteArrayAsync();
                rev = ulong.TryParse(resp.Headers.TryGetValues("X-Save-Rev", out var v) ? v.FirstOrDefault() : null, out ulong r) ? r : 0;
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] island: download failed ({ex.Message})");
                return Outcome.NetworkError;
            }

            string backups = Path.Combine(AppDataManager.BaseDirPath ?? ".", "nextendo_island_backups");
            Outcome replaced = NextendoIslandSync.ReplaceIsland(saveRoot, zip, backups, $"{titleId:x16}");
            if (replaced != Outcome.Ok)
            {
                Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] island: restore not applied ({replaced}), local island kept");
                return replaced;
            }

            string dir0 = Path.Combine(saveRoot, "0");

            // L'île restaurée est celle du cloud : pas la peine de la renvoyer à la fin de la partie.
            string restoredHash = NextendoIslandSync.Fingerprint(dir0);
            NextendoIslandSync.Update(key, e =>
            {
                e.LastUploadedHash = restoredHash;
                e.PendingUpload = false;
            });

            (Outcome metaRc, NextendoIslandSync.CloudIsland island) = await GetMetaAsync(titleId);
            if (metaRc == Outcome.Ok)
            {
                StoreCache(key, island);
            }

            Logger.Info?.Print(LogClass.ServiceOlsc, $"[Nextendo] island: rev {rev} restored ({zip.Length} bytes)");

            return Outcome.Ok;
        }
    }
}
