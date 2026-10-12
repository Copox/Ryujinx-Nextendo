using Ryujinx.Ava.Common.Locale;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Notifications of the emulator, drawn like the console's: a controller gone or
    /// low on battery, a screenshot saved, a game added to the library, a Nextendo badge earned.
    /// </summary>
    public static class NextendoEventNotifications
    {
        // The pictogram of a controller, from the name the system gives it (Assets/ControllerIcons).
        private static string PictogramOf(string name)
        {
            string n = name.ToLowerInvariant();

            if (n.Contains("xbox"))
            {
                return "controller_xbox";
            }

            if (n.Contains("dualsense") || n.Contains("ps5"))
            {
                return "controller_ps5";
            }

            if (n.Contains("dualshock") || n.Contains("ps4") || n.Contains("playstation"))
            {
                return "controller_ps4";
            }

            return "controller_pro";
        }

        public static void Initialize(Action requestScreenshot = null)
        {
            Ryujinx.Input.SDL3.SDL3GamepadDriver.NextendoControllerDisconnected += ControllerDisconnected;
            Ryujinx.Input.SDL3.SDL3GamepadDriver.NextendoControllerBatteryLow += ControllerBatteryLow;

            _badgeTimer = new Timer(_ => _ = CheckBadgesAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10));

            // Tests: each notification in turn, through the same code as the real events.
            if (Environment.GetEnvironmentVariable("NEXTENDO_NOTIF_TEST") == "1")
            {
                Task.Run(async () =>
                {
                    await Task.Delay(25000);
                    ControllerDisconnected("Xbox Wireless Controller");
                    await Task.Delay(7000);
                    ControllerDisconnected("PS4 Controller");
                    await Task.Delay(7000);
                    ControllerDisconnected("DualSense Wireless Controller");
                    await Task.Delay(7000);
                    ControllerDisconnected("Nintendo Switch Pro Controller");
                    await Task.Delay(7000);
                    ControllerBatteryLow("DualSense Wireless Controller", 12);
                    await Task.Delay(7000);
                    BadgesSeen("notif-test", ["donor"], NextendoEndpoint.BaseUrl().TrimEnd('/'));
                    BadgesSeen("notif-test", ["donor", "booster_3"], NextendoEndpoint.BaseUrl().TrimEnd('/'));
                    await Task.Delay(7000);
                    requestScreenshot?.Invoke();
                });
            }
        }

        private static void ControllerDisconnected(string name) =>
            NextendoConsoleToasts.Show(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NotifControllerDisconnected], name, null, PictogramOf(name));

        private static void ControllerBatteryLow(string name, int percent) =>
            NextendoConsoleToasts.Show(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NotifBatteryLow], $"{name} ({percent} %)", null, "battery_low");

        /// <summary>A screenshot was saved: its picture as the icon.</summary>
        public static void ScreenshotSaved(byte[] png) =>
            NextendoConsoleToasts.Show(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NotifScreenshotSaved],
                LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NotifScreenshotText], png);

        // ---- Games added to the library ----

        private static string KnownTitlesPath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_known_titles.txt");

        /// <summary>
        /// The games of the library: those never seen before are told (the first time, they are
        /// only remembered: an emulator just installed would announce its whole library).
        /// </summary>
        public static void LibraryLoaded(IEnumerable<(ulong Id, string Name, byte[] Icon)> titles)
        {
            // Two loads ending together would tell the same game twice.
            lock (_knownTitlesLock)
            {
                TellAdded(titles);
            }
        }

        private static readonly object _knownTitlesLock = new();

        private static void TellAdded(IEnumerable<(ulong Id, string Name, byte[] Icon)> titles)
        {
            List<(ulong Id, string Name, byte[] Icon)> list = titles.Where(t => t.Id != 0).ToList();
            HashSet<string> known;
            bool first = !File.Exists(KnownTitlesPath);

            try
            {
                known = first ? [] : File.ReadAllLines(KnownTitlesPath).Where(l => l.Length > 0).ToHashSet();
            }
            catch (Exception)
            {
                return;
            }

            List<(ulong Id, string Name, byte[] Icon)> added = list.Where(t => !known.Contains(t.Id.ToString("X16"))).ToList();

            if (added.Count == 0 && !first)
            {
                return;
            }

            try
            {
                File.WriteAllLines(KnownTitlesPath, known.Concat(added.Select(t => t.Id.ToString("X16"))).Distinct());
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo][notif] known games not kept: {e.Message}");
            }

            if (first)
            {
                return;
            }

            foreach ((ulong id, string name, byte[] icon) in added)
            {
                Logger.Info?.Print(LogClass.Application, $"[Nextendo][notif] game added: {name} ({id:X16})");

                NextendoConsoleToasts.Show(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NotifGameAdded], name, icon);
            }
        }

        // ---- Nextendo badges ----

        private static Timer _badgeTimer;
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

        private static string KnownBadgesPath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_known_badges.txt");

        // The badges of the account (/api/me): one not seen before is told, with its icon from the
        // website. The first time they are only remembered.
        private static async Task CheckBadgesAsync()
        {
            if (!NextendoAccount.IsLinked || string.IsNullOrEmpty(NextendoAccount.NexToken))
            {
                return;
            }

            try
            {
                string baseUrl = NextendoEndpoint.BaseUrl().TrimEnd('/');

                using HttpRequestMessage req = new(HttpMethod.Get, baseUrl + "/api/me");
                req.Headers.Add("Authorization", "Bearer " + NextendoAccount.NexToken);

                using HttpResponseMessage resp = await _http.SendAsync(req);

                if (!resp.IsSuccessStatusCode)
                {
                    return;
                }

                using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

                if (!doc.RootElement.TryGetProperty("account", out JsonElement account) || !account.TryGetProperty("badges", out JsonElement badges) || badges.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                string pid = account.TryGetProperty("pid", out JsonElement p) ? p.ToString() : "";
                List<string> now = badges.EnumerateArray().Select(b => b.GetString() ?? "").Where(b => b.Length > 0).ToList();

                BadgesSeen(pid, now, baseUrl);
            }
            catch (Exception e)
            {
                Logger.Debug?.Print(LogClass.Application, $"[Nextendo][notif] badges not read: {e.Message}");
            }
        }

        /// <summary>The badges of the account now: the new ones are told.</summary>
        public static void BadgesSeen(string pid, List<string> now, string baseUrl)
        {
            Dictionary<string, string> kept = [];

            try
            {
                if (File.Exists(KnownBadgesPath))
                {
                    foreach (string line in File.ReadAllLines(KnownBadgesPath))
                    {
                        int at = line.IndexOf('=');

                        if (at > 0)
                        {
                            kept[line[..at]] = line[(at + 1)..];
                        }
                    }
                }
            }
            catch (Exception)
            {
                return;
            }

            bool first = !kept.TryGetValue(pid, out string before);
            HashSet<string> old = (before ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

            kept[pid] = string.Join(",", now);

            try
            {
                File.WriteAllLines(KnownBadgesPath, kept.Select(k => $"{k.Key}={k.Value}"));
            }
            catch (Exception)
            {
                // told again next time
            }

            if (first)
            {
                return;
            }

            foreach (string badge in now.Where(b => !old.Contains(b)))
            {
                (string icon, string name) = BadgeOf(badge);

                byte[] picture = null;

                try
                {
                    picture = _http.GetByteArrayAsync($"{baseUrl}/assets/img/badges/{icon}.png").GetAwaiter().GetResult();
                }
                catch (Exception)
                {
                    // the card goes without it
                }

                Logger.Info?.Print(LogClass.Application, $"[Nextendo][notif] badge earned: {badge}");

                NextendoConsoleToasts.Show(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NotifBadgeEarned], name, picture);
            }
        }

        // A badge of the server: its icon on the website and its name.
        private static (string Icon, string Name) BadgeOf(string badge)
        {
            if (badge.StartsWith("booster", StringComparison.Ordinal))
            {
                string tier = badge.Contains('_') ? badge[(badge.IndexOf('_') + 1)..] : "1";

                return ("boost_" + tier, LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_BadgeBooster]);
            }

            return badge switch
            {
                "admin" => ("admin", LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_BadgeAdmin]),
                "team" => ("team", LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_BadgeTeam]),
                "verified_dev" => ("dev_verifie", LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_BadgeVerifiedDev]),
                "donor" => ("donateur", LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_BadgeDonor]),
                "anniversary_1" => ("anniversaire", LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_BadgeAnniversary]),
                _ => ("membre", badge),
            };
        }
    }
}
