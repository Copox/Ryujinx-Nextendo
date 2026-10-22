using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.FileSystem;
using Ryujinx.HLE.HOS;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Downloads NPLN online patches from the signed service catalog before a game is
    /// loaded, like Citron does: the title index once per session, then the patch of the exact main
    /// build being launched. Documents are verified by NplnPatchCatalog (HLE) and kept on disk as
    /// signed envelopes; without network, the last verified copy keeps working.
    /// </summary>
    public static class NplnPatchCatalogClient
    {
        private static readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(15);
        private static readonly HttpClient _http = CreateClient();
        private static readonly object _lock = new();
        private static readonly Dictionary<ulong, bool> _titles = new();
        private static ulong _indexRevision;
        private static string _indexTag = "";
        private static Task _indexCheck;
        private static DateTime _indexDeadline;

        private static string CacheDirectory => Path.Combine(AppDataManager.BaseDirPath, "service-catalog-v1", "npln-patches");

        private static HttpClient CreateClient()
        {
            HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Ryujinx-Nextendo");

            return client;
        }

        /// <summary>Starts the title index check once per session (call at startup).</summary>
        public static void Start()
        {
            lock (_lock)
            {
                if (_indexCheck != null)
                {
                    return;
                }

                NplnPatchCatalog.CacheDirectory = CacheDirectory;
                _indexDeadline = DateTime.UtcNow.AddSeconds(12);
                _indexCheck = Task.Run(RefreshTitleIndexAsync);
            }
        }

        /// <summary>
        /// Makes sure the verified patch for this game's main build is known before it is loaded.
        /// Never throws: on any failure the game starts with whatever verified patch is cached.
        /// </summary>
        public static async Task PrepareForGameAsync(ApplicationData application, VirtualFileSystem virtualFileSystem)
        {
            try
            {
                Start();
                TimeSpan wait = _indexDeadline - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.WhenAny(_indexCheck, Task.Delay(wait));
                }

                ulong titleId = application.Id;
                if (!IsCompatibleTitle(titleId))
                {
                    return;
                }

                string buildId = await Task.Run(() => ApplicationData.GetMainModuleId(virtualFileSystem,
                    ConfigurationState.Instance.System.IntegrityCheckLevel, application.Path));
                if (buildId.Length != 64)
                {
                    Logger.Warning?.Print(LogClass.Application, "[Nextendo] Cannot check NPLN patch: main build ID unavailable");

                    return;
                }

                NplnPatch cached = NplnPatchCatalog.Cached(titleId, buildId, out ulong revision, out string tag);
                if (cached is { Final: true })
                {
                    Logger.Info?.Print(LogClass.Application, $"[Nextendo] NPLN patch {cached.Id} is final; using verified local copy");

                    return;
                }

                string target = $"{titleId:x16}-{buildId}";
                if (!RefreshDue(target))
                {
                    return;
                }

                await DownloadPatchAsync(titleId, buildId, target, revision, tag);
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] NPLN patch check failed; retaining verified patches: {ex.Message}");
            }
        }

        private static bool IsCompatibleTitle(ulong titleId)
        {
            lock (_lock)
            {
                if (_titles.TryGetValue(titleId, out bool enabled) && enabled)
                {
                    return true;
                }
            }

            return NplnPatchCatalog.HasTitle(titleId);
        }

        private static async Task RefreshTitleIndexAsync()
        {
            try
            {
                LoadTitleIndexCache();

                using HttpRequestMessage request = new(HttpMethod.Get, NextendoServiceCatalog.Canonical + "/npln-title-index");
                request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
                if (_indexTag.Length > 0)
                {
                    request.Headers.TryAddWithoutValidation("If-None-Match", _indexTag);
                }

                using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (response.StatusCode == HttpStatusCode.NotModified && _indexTag.Length > 0 && response.Headers.ETag?.ToString() == _indexTag)
                {
                    Logger.Info?.Print(LogClass.Application, "[Nextendo] NPLN title index unchanged; using verified local index");

                    return;
                }

                response.EnsureSuccessStatusCode();
                string body = await SignedServiceCache.ReadBoundedAsync(response);
                NplnTitleIndex index = NplnPatchCatalog.VerifyTitleIndex(body, DateTimeOffset.UtcNow);
                if (response.Headers.ETag?.ToString() != index.ETag)
                {
                    throw new FormatException("Title index response tag mismatch.");
                }

                RememberTitleIndex(index);
                SaveVerified(Path.Combine(CacheDirectory, "title-index", $"revision-{index.Revision}.json"), body);
                Logger.Info?.Print(LogClass.Application, $"[Nextendo] NPLN title index verified, revision {index.Revision}, {index.Titles.Count} titles");
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] NPLN title index check failed; retaining verified index: {ex.Message}");
            }
        }

        // Replays signed index history in revision order, so a partial list never forgets known titles.
        private static void LoadTitleIndexCache()
        {
            string directory = Path.Combine(CacheDirectory, "title-index");
            if (!Directory.Exists(directory))
            {
                return;
            }

            List<NplnTitleIndex> indexes = new();
            foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    if (new FileInfo(file).Length <= NplnPatchCatalog.MaxDocumentBytes)
                    {
                        indexes.Add(NplnPatchCatalog.VerifyTitleIndex(File.ReadAllText(file), DateTimeOffset.UtcNow, allowExpired: true));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] NPLN title index cache rejected: {ex.Message}");
                }
            }

            foreach (NplnTitleIndex index in indexes.OrderBy(i => i.Revision))
            {
                RememberTitleIndex(index);
            }
        }

        private static void RememberTitleIndex(NplnTitleIndex index)
        {
            lock (_lock)
            {
                if (index.Revision < _indexRevision || (index.Revision == _indexRevision && index.ETag != _indexTag))
                {
                    throw new FormatException("Title index revision rollback or conflict rejected.");
                }

                foreach ((ulong title, bool enabled) in index.Titles)
                {
                    _titles[title] = enabled;
                }

                _indexRevision = index.Revision;
                _indexTag = index.ETag;
            }
        }

        // At most one check per build every 15 minutes, remembered across restarts.
        private static bool RefreshDue(string target)
        {
            string path = Path.Combine(CacheDirectory, "checks", target + ".txt");
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            try
            {
                if (File.Exists(path) && long.TryParse(File.ReadAllText(path).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long checkedAt) &&
                    checkedAt > 0 && checkedAt <= now && now - checkedAt < (long)_refreshInterval.TotalSeconds)
                {
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, now.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] NPLN catalog check time not saved: {ex.Message}");
            }

            return true;
        }

        private static async Task DownloadPatchAsync(ulong titleId, string buildId, string target, ulong cachedRevision, string cachedTag)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"{NextendoServiceCatalog.Canonical}/npln-patches/{titleId:x16}/{buildId}");
            if (!string.IsNullOrEmpty(cachedTag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", cachedTag);
            }

            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode == HttpStatusCode.NotModified && !string.IsNullOrEmpty(cachedTag) && response.Headers.ETag?.ToString() == cachedTag)
            {
                Logger.Info?.Print(LogClass.Application, $"[Nextendo] NPLN patch unchanged; using verified local copy ({target})");

                return;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Logger.Info?.Print(LogClass.Application, $"[Nextendo] No NPLN patch in the catalog for build {buildId}");

                return;
            }

            response.EnsureSuccessStatusCode();
            string body = await SignedServiceCache.ReadBoundedAsync(response);
            NplnPatchManifest manifest = NplnPatchCatalog.VerifyManifest(body, DateTimeOffset.UtcNow);
            if (manifest.Patches.Length != 1 || manifest.Patches[0].TitleId != titleId || manifest.Patches[0].BuildId != buildId ||
                manifest.Patches[0].Module != "main" || string.IsNullOrEmpty(manifest.ETag) || response.Headers.ETag?.ToString() != manifest.ETag)
            {
                throw new FormatException("Patch response does not match the requested build.");
            }

            if (manifest.Revision < cachedRevision)
            {
                throw new FormatException("Patch catalog revision rollback rejected.");
            }

            SaveVerified(Path.Combine(CacheDirectory, "verified", $"{target}-revision-{manifest.Revision}.json"), body);
            NplnPatchCatalog.Remember(manifest);
            Logger.Info?.Print(LogClass.Application, $"[Nextendo] NPLN patch {manifest.Patches[0].Id} v{manifest.Patches[0].Version} downloaded and verified for build {buildId}");
        }

        private static void SaveVerified(string path, string envelope)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, envelope);
                File.Move(temporary, path, true);
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] NPLN patch cache write failed: {ex.Message}");
            }
        }
    }
}
