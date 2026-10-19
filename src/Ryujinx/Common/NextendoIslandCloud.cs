using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using static Ryujinx.Common.Configuration.NextendoIslandSync;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Île d'Animal Crossing: New Horizons dans le cloud (synchro stricte). Une seule
    /// île par compte côté serveur, sans copie. Règle : la plus récente est proposée, mais rien
    /// n'est jamais remplacé (ni le cloud, ni l'île de cet émulateur) sans que le joueur l'ait
    /// choisi. Le serveur refuse en 409 tout envoi qui ne s'appuie pas sur sa révision actuelle.
    /// </summary>
    public static class NextendoIslandCloud
    {
        public sealed class CloudMeta
        {
            public bool Exists;
            public ulong Rev;
            public long Size;
            public DateTime UpdatedAtUtc;
            public string Device = "";
            public string SessionDevice = "";
            public DateTime SessionAtUtc;
        }

        /// <summary>Envoi refusé en cours de partie (île modifiée ailleurs) : on demande à la fin.</summary>
        public static CloudMeta ConflictPending { get; private set; }

        private static string Tid(ApplicationData app) => app.IdString.ToLowerInvariant();

        private static HttpClient Client(int seconds)
        {
            HttpClient http = new() { Timeout = TimeSpan.FromSeconds(seconds) };
            NextendoApi.AddAppHeader(http);
            http.DefaultRequestHeaders.Add("Authorization", "Bearer " + NextendoAccount.NexToken);
            http.DefaultRequestHeaders.Add("X-Nextendo-Device", DeviceId);
            return http;
        }

        private static string Url(ApplicationData app, string suffix = "") => $"{NextendoEndpoint.BaseUrl()}/api/save/{Tid(app)}{suffix}";

        private static DateTime ParseTime(JsonElement e, string name)
        {
            return e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
                && DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t)
                ? t : DateTime.MinValue;
        }

        private static CloudMeta ParseMeta(JsonElement e)
        {
            CloudMeta m = new()
            {
                Exists = e.TryGetProperty("exists", out JsonElement ex) && ex.ValueKind == JsonValueKind.True,
                Rev = e.TryGetProperty("rev", out JsonElement r) && r.TryGetUInt64(out ulong rv) ? rv : 0,
                Size = e.TryGetProperty("size", out JsonElement s) && s.TryGetInt64(out long sz) ? sz : 0,
                UpdatedAtUtc = ParseTime(e, "updatedAt"),
                Device = e.TryGetProperty("device", out JsonElement d) ? d.GetString() ?? "" : "",
            };
            if (e.TryGetProperty("session", out JsonElement sess) && sess.ValueKind == JsonValueKind.Object)
            {
                m.SessionDevice = sess.TryGetProperty("device", out JsonElement sd) ? sd.GetString() ?? "" : "";
                m.SessionAtUtc = ParseTime(sess, "at");
            }

            return m;
        }

        public static async Task<CloudMeta> GetMetaAsync(ApplicationData app)
        {
            try
            {
                using HttpClient http = Client(20);
                HttpResponseMessage resp = await http.GetAsync(Url(app, "/meta"));
                if (!resp.IsSuccessStatusCode)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] île : méta HTTP {(int)resp.StatusCode}");
                    return null;
                }

                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                return ParseMeta(doc.RootElement);
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] île : méta illisible ({ex.Message})");
                return null;
            }
        }

        /// <summary>Télécharge l'île et l'installe ; l'état retient la révision téléchargée.</summary>
        public static async Task<bool> DownloadAndApplyAsync(ApplicationData app)
        {
            try
            {
                using HttpClient http = Client(120);
                HttpResponseMessage resp = await http.GetAsync(Url(app));
                if (resp.StatusCode == HttpStatusCode.NoContent || !resp.IsSuccessStatusCode)
                {
                    return false;
                }

                ulong rev = ulong.TryParse(resp.Headers.TryGetValues("X-Save-Rev", out var v) ? v.FirstOrDefault() : null, out ulong r) ? r : 0;
                byte[] zip = await resp.Content.ReadAsByteArrayAsync();
                if (zip.Length == 0 || !ApplicationHelper.ImportNextendoSave(app.Id, app.ControlHolder, zip))
                {
                    return false;
                }

                SaveState(NextendoAccount.Pid, app.Id, new SyncState { Rev = rev, LocalHash = Fingerprint(ApplicationHelper.NextendoSaveContentDir(app.Id)) });
                Logger.Info?.Print(LogClass.Application, $"[Nextendo] île : rev {rev} téléchargée ({zip.Length} o)");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] île : téléchargement en échec ({ex.Message})");
                return false;
            }
        }

        public enum PushResult { Sent, Unchanged, Conflict, NotLinked, Failed }

        /// <summary>
        /// Envoie l'île locale en s'appuyant sur <paramref name="baseRev"/> (null = la révision de
        /// l'état local). Sans état (jamais lié ici, ou « Pas maintenant »), rien n'est envoyé.
        /// </summary>
        public static async Task<PushResult> PushAsync(ApplicationData app, ulong? baseRev = null)
        {
            if (!ApplicationHelper.IsNextendoProfileActive() || NextendoAccount.IsGuest || string.IsNullOrEmpty(NextendoAccount.NexToken))
            {
                return PushResult.NotLinked;
            }

            SyncState state = LoadState(NextendoAccount.Pid, app.Id);
            if (state == null && baseRev == null)
            {
                return PushResult.NotLinked;
            }

            string dir = ApplicationHelper.NextendoSaveContentDir(app.Id);
            if (dir == null)
            {
                return PushResult.Unchanged;
            }

            try
            {
                string hash = Fingerprint(dir);
                if (baseRev == null && hash == state.LocalHash)
                {
                    return PushResult.Unchanged;
                }

                byte[] zip = ZipDirectory(dir);
                using HttpClient http = Client(180);
                using ByteArrayContent content = new(zip);
                using HttpRequestMessage req = new(HttpMethod.Put, Url(app)) { Content = content };
                req.Headers.Add("X-Save-Base-Rev", (baseRev ?? state.Rev).ToString(CultureInfo.InvariantCulture));
                HttpResponseMessage resp = await http.SendAsync(req);
                string body = await resp.Content.ReadAsStringAsync();

                if (resp.StatusCode == HttpStatusCode.Conflict)
                {
                    using JsonDocument doc = JsonDocument.Parse(body);
                    ConflictPending = doc.RootElement.TryGetProperty("cloud", out JsonElement c) ? ParseMeta(c) : new CloudMeta { Exists = true };
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] île : CONFLIT, le cloud est en rev {ConflictPending.Rev} ({ConflictPending.Device})");
                    return PushResult.Conflict;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] île : envoi refusé HTTP {(int)resp.StatusCode} {body}");
                    return PushResult.Failed;
                }

                using JsonDocument ok = JsonDocument.Parse(body);
                ulong rev = ok.RootElement.GetProperty("rev").GetUInt64();
                SaveState(NextendoAccount.Pid, app.Id, new SyncState { Rev = rev, LocalHash = hash });
                ConflictPending = null;
                Logger.Info?.Print(LogClass.Application, $"[Nextendo] île : rev {rev} envoyée ({zip.Length} o)");
                return PushResult.Sent;
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] île : envoi en échec ({ex.Message})");
                return PushResult.Failed;
            }
        }

        /// <summary>« Je joue » (au lancement, puis à chaque envoi périodique).</summary>
        public static async Task AnnounceAsync(ApplicationData app)
        {
            try
            {
                using HttpClient http = Client(10);
                await http.PostAsync(Url(app, "/session"), null);
            }
            catch (Exception ex)
            {
                Logger.Debug?.Print(LogClass.Application, $"[Nextendo] île : session non annoncée ({ex.Message})");
            }
        }

        public static async Task EndSessionAsync(ApplicationData app)
        {
            try
            {
                using HttpClient http = Client(10);
                await http.DeleteAsync(Url(app, "/session"));
            }
            catch (Exception ex)
            {
                Logger.Debug?.Print(LogClass.Application, $"[Nextendo] île : fin de session non envoyée ({ex.Message})");
            }
        }

        private static string When(DateTime utc) => utc == DateTime.MinValue ? "?" : utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

        private static string Where(string device)
        {
            if (string.IsNullOrEmpty(device))
            {
                return "?";
            }

            if (device == DeviceId)
            {
                return LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_IslandThisDevice];
            }

            return device.Split(':')[0] switch
            {
                "switch" => "Nintendo Switch",
                "citron" => "Citron",
                "ryujinx" => "Ryujinx (" + device[(device.IndexOf(':') + 1)..] + ")",
                _ => device,
            };
        }

        private static DateTime LocalWriteUtc(string dir)
        {
            return dir == null ? DateTime.MinValue
                : Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        }

        private static Task<UserResult> Ask(string body, string yes, string no)
        {
            return ContentDialogHelper.CreateConfirmationDialog(
                LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_IslandTitle], body, yes, no, "Nextendo Network", iconSymbol: 0xE753);
        }

        /// <summary>
        /// Avant le lancement. Rend false si le joueur annule (île ouverte ailleurs). Toute
        /// question posée ici a deux issues explicites ; aucune ne remplace quoi que ce soit en silence.
        /// </summary>
        public static async Task<bool> PrepareLaunchAsync(ApplicationData app)
        {
            ConflictPending = null;
            if (!ApplicationHelper.IsNextendoProfileActive() || NextendoAccount.IsGuest || string.IsNullOrEmpty(NextendoAccount.NexToken))
            {
                return true;
            }

            CloudMeta cloud = await GetMetaAsync(app);
            if (cloud == null)
            {
                return true; // serveur injoignable : on joue, l'envoi conditionnel protégera le cloud
            }

            // Question a : île ouverte sur un autre appareil il y a moins de 10 min.
            if (!string.IsNullOrEmpty(cloud.SessionDevice) && cloud.SessionDevice != DeviceId)
            {
                int minutes = (int)Math.Max(0, (DateTime.UtcNow - cloud.SessionAtUtc).TotalMinutes);
                UserResult go = await Ask(
                    LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_IslandOpenElsewhereFormat, Where(cloud.SessionDevice), minutes),
                    LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_OnlineLaunchAnyway],
                    LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_OnlineCancel]);
                if (go != UserResult.Yes)
                {
                    return false;
                }
            }

            string dir = ApplicationHelper.NextendoSaveContentDir(app.Id);
            string localHash = Fingerprint(dir);
            SyncState state = LoadState(NextendoAccount.Pid, app.Id);
            LaunchAction action = Decide(cloud.Exists, cloud.Rev, dir != null, localHash, state);
            Logger.Info?.Print(LogClass.Application, $"[Nextendo] île : cloud rev={cloud.Rev} exists={cloud.Exists} local={(dir != null)} état={(state == null ? "aucun" : state.Rev.ToString())} -> {action}");

            switch (action)
            {
                case LaunchAction.OfferFirstUpload:
                    // Question c : l'île de cet émulateur se lie au compte SEULEMENT si le joueur le dit.
                    if (await Ask(LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_IslandFirstUploadFormat, app.Name),
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_IslandLinkBtn],
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_IslandNotNowBtn]) == UserResult.Yes)
                    {
                        await PushAsync(app, baseRev: 0);
                    }
                    break;

                case LaunchAction.OfferDownloadEmptyLocal:
                case LaunchAction.OfferDownloadCloudNewer:
                    if (await Ask(LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_IslandDownloadFormat, When(cloud.UpdatedAtUtc), Where(cloud.Device)),
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_CloudSaveDownloadBtn],
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_IslandKeepLocalBtn]) == UserResult.Yes)
                    {
                        await DownloadAndApplyAsync(app);
                    }
                    break;

                case LaunchAction.Conflict:
                    // Question b : deux îles différentes. La plus récente est proposée en premier.
                    DateTime localUtc = LocalWriteUtc(dir);
                    bool cloudNewer = CloudIsNewer(cloud.UpdatedAtUtc, localUtc);
                    string body = LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_IslandConflictFormat,
                        When(cloud.UpdatedAtUtc), Where(cloud.Device), When(localUtc),
                        LocaleManager.Instance[cloudNewer ? LocaleKeys.Dialog_Nextendo_IslandKeepCloudBtn : LocaleKeys.Dialog_Nextendo_IslandKeepLocalBtn]);
                    string cloudBtn = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_IslandKeepCloudBtn];
                    string localBtn = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_IslandKeepLocalBtn];
                    // Trois issues : la plus récente (bouton principal), l'autre, ou Annuler (rien ne
                    // change et le jeu ne se lance pas). Fermer la fenêtre vaut Annuler.
                    UserResult choice = await ContentDialogHelper.CreateDeniableConfirmationDialog(
                        LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_IslandTitle], body,
                        cloudNewer ? cloudBtn : localBtn, cloudNewer ? localBtn : cloudBtn,
                        LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_OnlineCancel], "Nextendo Network");
                    if (choice != UserResult.Yes && choice != UserResult.No)
                    {
                        return false;
                    }

                    bool keepCloud = (choice == UserResult.Yes) == cloudNewer;

                    if (keepCloud)
                    {
                        await DownloadAndApplyAsync(app);
                    }
                    else
                    {
                        await PushAsync(app, baseRev: cloud.Rev);
                    }
                    break;
            }

            if (LoadState(NextendoAccount.Pid, app.Id) != null)
            {
                await AnnounceAsync(app);
            }

            return true;
        }

        /// <summary>Envoi périodique pendant la partie : jamais de question, un conflit attend la fin.</summary>
        public static async Task PeriodicAsync(ApplicationData app)
        {
            if (ConflictPending != null || LoadState(NextendoAccount.Pid, app.Id) == null)
            {
                return;
            }

            await PushAsync(app);
            await AnnounceAsync(app);
        }

        /// <summary>Fin de partie : dernier envoi, puis question si l'île a changé ailleurs entre-temps.</summary>
        public static async Task FinishAsync(ApplicationData app, bool canAsk)
        {
            if (LoadState(NextendoAccount.Pid, app.Id) != null && ConflictPending == null)
            {
                await PushAsync(app);
            }

            CloudMeta c = ConflictPending;
            if (c != null && canAsk)
            {
                UserResult r = await Ask(
                    LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_IslandConflictExitFormat, Where(c.Device), When(c.UpdatedAtUtc)),
                    LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_IslandReplaceCloudBtn],
                    LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_IslandNotNowBtn]);
                if (r == UserResult.Yes)
                {
                    await PushAsync(app, baseRev: c.Rev);
                }
            }

            ConflictPending = null;
            await EndSessionAsync(app);
        }
    }
}
