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
    /// [Nextendo] Île d'Animal Crossing: New Horizons dans le cloud, règle de Nintendo :
    /// un emplacement par compte, la dernière île envoyée remplace la précédente ; l'île de cet
    /// émulateur n'est envoyée qu'une fois liée (« Lier cette île », l'équivalent de l'activation de
    /// l'archivage dans le jeu, que l'olsc:u de l'émulateur ne transmet pas) ; la restauration n'est
    /// proposée que si le joueur l'a débloquée sur le site (chez Nintendo : le support).
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
            public bool RestoreUnlocked;
        }

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

        private static bool Usable() =>
            ApplicationHelper.IsNextendoProfileActive() && !NextendoAccount.IsGuest && !string.IsNullOrEmpty(NextendoAccount.NexToken);

        private static DateTime ParseTime(JsonElement e, string name)
        {
            return e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
                && DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t)
                ? t : DateTime.MinValue;
        }

        private static CloudMeta ParseMeta(JsonElement e)
        {
            return new CloudMeta
            {
                Exists = e.TryGetProperty("exists", out JsonElement ex) && ex.ValueKind == JsonValueKind.True,
                Rev = e.TryGetProperty("rev", out JsonElement r) && r.TryGetUInt64(out ulong rv) ? rv : 0,
                Size = e.TryGetProperty("size", out JsonElement s) && s.TryGetInt64(out long sz) ? sz : 0,
                UpdatedAtUtc = ParseTime(e, "updatedAt"),
                Device = e.TryGetProperty("device", out JsonElement d) ? d.GetString() ?? "" : "",
                RestoreUnlocked = ParseTime(e, "restaurationJusqua") > DateTime.UtcNow,
            };
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

        /// <summary>
        /// Restauration : télécharge l'île (le serveur exige le déblocage du site et le consomme),
        /// l'installe, et la lie à cette installation.
        /// </summary>
        public static async Task<bool> RestoreAsync(ApplicationData app)
        {
            try
            {
                using HttpClient http = Client(120);
                HttpResponseMessage resp = await http.GetAsync(Url(app));
                if (resp.StatusCode == HttpStatusCode.Forbidden)
                {
                    Logger.Warning?.Print(LogClass.Application, "[Nextendo] île : restauration verrouillée (à débloquer sur le site)");
                    return false;
                }

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

                SaveState(NextendoAccount.Pid, app.Id, new SyncState { Linked = true, Rev = rev, LocalHash = Fingerprint(ApplicationHelper.NextendoSaveContentDir(app.Id)) });
                Logger.Info?.Print(LogClass.Application, $"[Nextendo] île : rev {rev} restaurée ({zip.Length} o)");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] île : restauration en échec ({ex.Message})");
                return false;
            }
        }

        public enum PushResult { Sent, Unchanged, NotLinked, Failed }

        /// <summary>
        /// Envoie l'île locale si elle est liée et a changé. Le serveur remplace l'île du compte
        /// (un emplacement, le dernier envoi gagne).
        /// </summary>
        public static async Task<PushResult> PushAsync(ApplicationData app, bool link = false)
        {
            if (!Usable())
            {
                return PushResult.NotLinked;
            }

            SyncState state = LoadState(NextendoAccount.Pid, app.Id);
            if (!link && (state == null || !state.Linked))
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
                if (!link && hash == state.LocalHash)
                {
                    return PushResult.Unchanged;
                }

                byte[] zip = ZipDirectory(dir);
                using HttpClient http = Client(180);
                using ByteArrayContent content = new(zip);
                HttpResponseMessage resp = await http.PutAsync(Url(app), content);
                string body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] île : envoi refusé HTTP {(int)resp.StatusCode} {body}");
                    return PushResult.Failed;
                }

                using JsonDocument ok = JsonDocument.Parse(body);
                ulong rev = ok.RootElement.TryGetProperty("rev", out JsonElement r) && r.TryGetUInt64(out ulong rv) ? rv : 0;
                SaveState(NextendoAccount.Pid, app.Id, new SyncState { Linked = true, Rev = rev, LocalHash = hash });
                Logger.Info?.Print(LogClass.Application, $"[Nextendo] île : rev {rev} envoyée ({zip.Length} o)");
                return PushResult.Sent;
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] île : envoi en échec ({ex.Message})");
                return PushResult.Failed;
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

        private static Task<UserResult> Ask(string body, string yes, string no)
        {
            return ContentDialogHelper.CreateConfirmationDialog(
                LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_IslandTitle], body, yes, no, "Nextendo Network", iconSymbol: 0xE753);
        }

        /// <summary>
        /// Avant le lancement. Ne bloque jamais le jeu : au pire une question à deux issues, et
        /// « Pas maintenant » ne touche à rien.
        /// </summary>
        public static async Task<bool> PrepareLaunchAsync(ApplicationData app)
        {
            if (!Usable())
            {
                return true;
            }

            CloudMeta cloud = await GetMetaAsync(app);
            if (cloud == null)
            {
                return true; // serveur injoignable : on joue, l'envoi suivra si l'île est liée
            }

            string dir = ApplicationHelper.NextendoSaveContentDir(app.Id);
            SyncState state = LoadState(NextendoAccount.Pid, app.Id);
            LaunchAction action = Decide(cloud.Exists, cloud.RestoreUnlocked, dir != null, state);
            Logger.Info?.Print(LogClass.Application, $"[Nextendo] île : cloud rev={cloud.Rev} exists={cloud.Exists} restauration={cloud.RestoreUnlocked} local={(dir != null)} liée={(state?.Linked == true)} -> {action}");

            switch (action)
            {
                case LaunchAction.OfferRestore:
                    if (await Ask(LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_IslandRestoreFormat, When(cloud.UpdatedAtUtc), Where(cloud.Device)),
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_IslandRestoreBtn],
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_IslandNotNowBtn]) == UserResult.Yes)
                    {
                        await RestoreAsync(app);
                    }
                    break;

                case LaunchAction.OfferLink:
                    string body = cloud.Exists
                        ? LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_IslandLinkReplaceFormat, When(cloud.UpdatedAtUtc), Where(cloud.Device), app.Name)
                        : LocaleManager.GetFormatted(LocaleKeys.Dialog_Nextendo_IslandLinkFormat, app.Name);
                    if (await Ask(body,
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_IslandLinkBtn],
                            LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_IslandNotNowBtn]) == UserResult.Yes)
                    {
                        await PushAsync(app, link: true);
                    }
                    break;
            }

            return true;
        }

        /// <summary>Envoi périodique pendant la partie : seulement si l'île est liée et a changé.</summary>
        public static Task PeriodicAsync(ApplicationData app) => PushAsync(app);

        /// <summary>Fin de partie : dernier envoi si l'île est liée.</summary>
        public static Task FinishAsync(ApplicationData app, bool canAsk) => PushAsync(app);
    }
}
