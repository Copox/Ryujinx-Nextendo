using LibHac;
using LibHac.Fs;
using LibHac.Fs.Shim;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.FileSystem;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Path = System.IO.Path;
using TimeSpan = System.TimeSpan;

namespace Ryujinx.HLE.HOS.Services.Olsc
{
    /// <summary>
    /// [Nextendo] Ce que fait olsc chez Nintendo, contre notre serveur : l'île archivée du compte
    /// Nextendo (un emplacement, la dernière envoyée gagne, restauration permise d'office) sur
    /// /api/save/&lt;titre&gt;. Seul le jeu déclenche un envoi ou une restauration, depuis son menu.
    /// </summary>
    static class NextendoOlscCloud
    {
        private static string Tid(ulong titleId) => titleId.ToString("x16");

        private static string Url(ulong titleId, string suffix = "") => $"{NextendoEndpoint.BaseUrl()}/api/save/{Tid(titleId)}{suffix}";

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

        private static ResultCode FromStatus(HttpStatusCode status)
        {
            return (int)status >= 500 ? ResultCode.NextendoNetworkError : ResultCode.NextendoServerRefused;
        }

        /// <summary>État de l'île du compte, ou null avec la raison.</summary>
        public static async Task<(ResultCode, NextendoIslandSync.CloudIsland)> GetMetaAsync(ulong titleId)
        {
            if (!Usable)
            {
                return (ResultCode.NextendoUnavailable, null);
            }

            try
            {
                using HttpClient http = Client(20);
                using HttpResponseMessage resp = await http.GetAsync(Url(titleId, "/meta"));
                if (!resp.IsSuccessStatusCode)
                {
                    Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: meta HTTP {(int)resp.StatusCode}");
                    return (FromStatus(resp.StatusCode), null);
                }

                return (ResultCode.Success, NextendoIslandSync.ParseMeta(await resp.Content.ReadAsStringAsync()));
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: meta failed ({ex.Message})");
                return (ResultCode.NextendoNetworkError, null);
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

        // ---- sauvegarde Device du jeu en cours -----------------------------------------------

        /// <summary>Dossier racine de la sauvegarde Device du jeu (nand/user/save/&lt;id&gt;), ou null.</summary>
        public static string FindDeviceSaveRoot(Switch device, ulong titleId)
        {
            try
            {
                HorizonClient client = device.System.LibHacHorizonManager.RyujinxClient;
                SaveDataFilter filter = SaveDataFilter.Make(titleId, SaveDataType.Device, default, default, default);
                Result rc = client.Fs.FindSaveDataWithFilter(out SaveDataInfo info, SaveDataSpaceId.User, in filter);
                if (rc.IsFailure())
                {
                    return null;
                }

                string root = Path.Combine(VirtualFileSystem.GetNandPath(), "user", "save", info.SaveDataId.ToString("x16"));
                return Directory.Exists(root) ? root : null;
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: device save lookup failed ({ex.Message})");
                return null;
            }
        }

        private static bool HasFiles(string dir) => Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any();

        /// <summary>Le contenu validé (« 0 »), sinon celui de travail (« 1 »).</summary>
        private static string ContentDir(string root)
        {
            foreach (string sub in new[] { "0", "1" })
            {
                string dir = Path.Combine(root, sub);
                if (HasFiles(dir))
                {
                    return dir;
                }
            }

            return null;
        }

        // ---- envoi ---------------------------------------------------------------------------

        public static async Task<ResultCode> UploadAsync(Switch device, ulong titleId, string key)
        {
            if (!Usable)
            {
                return ResultCode.NextendoUnavailable;
            }

            string root = FindDeviceSaveRoot(device, titleId);
            string dir = root == null ? null : ContentDir(root);
            if (dir == null)
            {
                return ResultCode.NextendoNoLocalSave;
            }

            byte[] zip = NextendoIslandSync.ZipDirectory(dir);
            byte[] custom = DecodeCustom(NextendoIslandSync.Get(key).Custom);

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
                    Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: upload refused HTTP {(int)resp.StatusCode} {body}");
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
                Logger.Info?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: island rev {sent.Rev} uploaded ({zip.Length} bytes)");

                return ResultCode.Success;
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: upload failed ({ex.Message})");
                return ResultCode.NextendoNetworkError;
            }
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

        // ---- restauration --------------------------------------------------------------------

        public static async Task<ResultCode> DownloadAsync(Switch device, ulong titleId, string key)
        {
            if (!Usable)
            {
                return ResultCode.NextendoUnavailable;
            }

            string root = FindDeviceSaveRoot(device, titleId);
            if (root == null)
            {
                return ResultCode.NextendoNoLocalSave;
            }

            byte[] zip;
            ulong rev;
            try
            {
                using HttpClient http = Client(180);
                using HttpResponseMessage resp = await http.GetAsync(Url(titleId));
                if (resp.StatusCode == HttpStatusCode.NoContent || !resp.IsSuccessStatusCode)
                {
                    Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: download HTTP {(int)resp.StatusCode}");
                    return resp.StatusCode == HttpStatusCode.NoContent ? ResultCode.NextendoServerRefused : FromStatus(resp.StatusCode);
                }

                zip = await resp.Content.ReadAsByteArrayAsync();
                rev = ulong.TryParse(resp.Headers.TryGetValues("X-Save-Rev", out var v) ? v.FirstOrDefault() : null, out ulong r) ? r : 0;
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: download failed ({ex.Message})");
                return ResultCode.NextendoNetworkError;
            }

            string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
            string fresh = Path.Combine(root, "nx_restore_new");
            string old0 = Path.Combine(root, "nx_restore_old0");
            string old1 = Path.Combine(root, "nx_restore_old1");

            try
            {
                foreach (string leftover in new[] { fresh, old0, old1 })
                {
                    if (Directory.Exists(leftover))
                    {
                        Directory.Delete(leftover, true);
                    }
                }

                NextendoIslandSync.ExtractIsland(zip, fresh);
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: downloaded island refused ({ex.Message})");
                TryDelete(fresh);
                return ResultCode.NextendoInvalidIsland;
            }

            // L'île de cette machine est gardée avant d'être remplacée (les 5 dernières).
            string current = ContentDir(root);
            if (current != null)
            {
                try
                {
                    string backups = Path.Combine(AppDataManager.BaseDirPath, "nextendo_island_backups");
                    Directory.CreateDirectory(backups);
                    File.WriteAllBytes(Path.Combine(backups, $"{Tid(titleId)}-{stamp}.zip"), NextendoIslandSync.ZipDirectory(current));
                    foreach (string extra in Directory.GetFiles(backups, $"{Tid(titleId)}-*.zip").OrderByDescending(f => f, StringComparer.Ordinal).Skip(5))
                    {
                        File.Delete(extra);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: local island backup failed ({ex.Message})");
                    TryDelete(fresh);
                    return ResultCode.NextendoNoLocalSave;
                }
            }

            string dir0 = Path.Combine(root, "0");
            string dir1 = Path.Combine(root, "1");
            bool had0 = Directory.Exists(dir0);
            bool had1 = Directory.Exists(dir1);

            try
            {
                if (had0)
                {
                    Directory.Move(dir0, old0);
                }

                if (had1)
                {
                    Directory.Move(dir1, old1);
                }

                Directory.Move(fresh, dir0);
                if (had1)
                {
                    CopyDirectory(dir0, dir1);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: island restore failed, local island kept ({ex.Message})");
                try
                {
                    if (Directory.Exists(old0))
                    {
                        TryDelete(dir0);
                        Directory.Move(old0, dir0);
                    }

                    if (Directory.Exists(old1))
                    {
                        TryDelete(dir1);
                        Directory.Move(old1, dir1);
                    }
                }
                catch (Exception rollback)
                {
                    Logger.Error?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: rollback failed ({rollback.Message}), copy kept in nextendo_island_backups");
                }

                TryDelete(fresh);
                return ResultCode.NextendoNoLocalSave;
            }

            TryDelete(old0);
            TryDelete(old1);

            (ResultCode metaRc, NextendoIslandSync.CloudIsland island) = await GetMetaAsync(titleId);
            if (metaRc == ResultCode.Success)
            {
                StoreCache(key, island);
            }

            Logger.Info?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: island rev {rev} restored ({zip.Length} bytes)");

            return ResultCode.Success;
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(dst, Path.GetRelativePath(src, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, overwrite: true);
            }
        }

        private static void TryDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (Exception)
            {
                // un reste sans effet : effacé au prochain essai
            }
        }
    }
}
