using Avalonia.Threading;
using Ryujinx.Ava.Systems.AppLibrary;
using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
namespace Ryujinx.Ava.Common
{
    public static class NextendoServiceCatalog
    {
        public const string Canonical = "https://nextendo.network/service-catalog/v1";
        private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
        private static ApplicationLibrary _library;
        private static SignedServiceCache _catalog, _releases;
        private static Timer _timer;
        private static Func<bool> _visible;
        private static string _baseUrl;
        private static Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
        private static DateTimeOffset _countsAt, _nextCounts, _manualAt;
        private static readonly SemaphoreSlim RefreshLock = new(1, 1);
        public static void Start(ApplicationLibrary library, Func<bool> visible = null)
        {
            if (_timer != null)
                return;
            _library = library;
            _visible = visible ?? (() => true);
            _baseUrl = Canonical;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string dir = Path.Combine(AppDataManager.BaseDirPath, "service-catalog-v1");
            _catalog = new(Path.Combine(dir, "catalog.cache.json"), "game_catalog", _baseUrl + "/game-catalog", p => ServiceCatalogParser.ParseCatalog(p), now);
            _releases = new(Path.Combine(dir, "releases.cache.json"), "client_releases", _baseUrl + "/client-releases", p => ServiceCatalogParser.ParseReleases(p), now);
            _timer = new Timer(_ => _ = RefreshAsync(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        }
        // A manual action may call this; repeated clicks are coalesced and limited.
        public static Task RefreshNowAsync() => RefreshAsync(true);
        // Launch consumes a verified snapshot. Normal launches do not bypass next_check.
        public static async Task<ServiceLaunchDecision> EvaluateLaunchAsync(ApplicationData application)
        {
            if (application.IsNextendoCompatible)
                return ServiceLaunchDecision.Unmanaged;
            if (_catalog == null)
                return ServiceLaunchDecision.Unavailable;
            if (!_catalog.IsValid(DateTimeOffset.UtcNow))
                await RefreshAsync();
            await RefreshLock.WaitAsync();
            try
            {
                bool valid = _catalog.IsValid(DateTimeOffset.UtcNow);
                VerifiedServiceDocument identity = valid ? _catalog.Document : _catalog.LastAuthenticatedDocument;
                bool found = false;
                ServiceListing listing = null;
                if (identity != null)
                    found = ServiceCatalogParser.ParseCatalog(identity.Payload).TryGetValue(application.IdBaseString, out listing);
                return ServiceLaunchPolicy.Evaluate(found || application.WasListedOnService, valid, listing, application.Version);
            }
            finally { RefreshLock.Release(); }
        }
        public static async Task RefreshAsync(bool manual = false)
        {
            if (_library == null)
                return;
            if (manual)
                await RefreshLock.WaitAsync();
            else if (!await RefreshLock.WaitAsync(0))
                return;
            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (manual)
                { if (now - _manualAt < TimeSpan.FromMinutes(1)) return; _manualAt = now; }
                await _catalog.RefreshAsync(Http, now, manual);
                await _releases.RefreshAsync(Http, now, manual);
                Dictionary<string, ServiceListing> listings = _catalog.IsValid(DateTimeOffset.UtcNow) ? ServiceCatalogParser.ParseCatalog(_catalog.Document.Payload) : new(StringComparer.OrdinalIgnoreCase);
                ServiceClientRelease[] releases = _releases.IsValid(DateTimeOffset.UtcNow) ? ServiceCatalogParser.ParseReleases(_releases.Document.Payload) : null;
                bool needed = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_visible())
                        return false;
                    foreach (ApplicationData app in _library.Applications.Items)
                    if (!app.IsNextendoCompatible && listings.TryGetValue(app.IdBaseString, out ServiceListing listing) && listing?.Enabled == true)
                        return true;
                    return false;
                });
                if (needed && now >= _nextCounts)
                {
                    _nextCounts = now.AddSeconds(30);
                    try
                    { using HttpResponseMessage response = await Http.GetAsync(_baseUrl + "/online-counts", HttpCompletionOption.ResponseHeadersRead); response.EnsureSuccessStatusCode(); _counts = ServiceCatalogParser.ParseCounts(await SignedServiceCache.ReadBoundedAsync(response)); _countsAt = DateTimeOffset.UtcNow; }
                    catch { /* Missing/stale counts are unknown. */ }
                }
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    bool valid = _catalog.IsValid(DateTimeOffset.UtcNow), fresh = DateTimeOffset.UtcNow - _countsAt <= TimeSpan.FromSeconds(60);
                    string platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
                    string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
                    string channel = ReleaseInformation.IsCanaryBuild ? "canary" : "stable";
                    foreach (ApplicationData app in _library.Applications.Items)
                    {
                        if (app.IsNextendoCompatible)
                            continue; // Never reintegrate built-in titles.
                        listings.TryGetValue(app.IdBaseString, out ServiceListing listing);
                        int? count = fresh && _counts.TryGetValue(app.IdBaseString, out int n) ? n : null;
                        app.ApplyServiceAvailability(listing, count);
                        app.ApplyServiceRequirements(valid, releases, channel, platform, arch);
                    }
                });
            }
            catch { /* Optional metadata must never block startup. */ }
            finally { RefreshLock.Release(); }
        }
    }
}
