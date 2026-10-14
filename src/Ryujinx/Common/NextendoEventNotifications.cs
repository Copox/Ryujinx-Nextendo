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

        /// <summary>The console running, if one is (set by the window).</summary>
        public static Func<Ryujinx.HLE.Switch> Console { get; set; }

        // The controller in use is another one: its picture in the HOME Menu, at once.
        private static void ControllerPicture(string name)
        {
            Ryujinx.HLE.HOS.Services.Sdb.Pl.ControllerStyle style = Ryujinx.HLE.HOS.Services.Sdb.Pl.ControllerButtonFont.StyleOf(name);

            Task.Run(() =>
            {
                try
                {
                    Ryujinx.HLE.HOS.NextendoControllerPictures.Show(Console?.Invoke(), style);
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.Application, $"[Nextendo] controller picture left as it is: {e.Message}");
                }
            });
        }

        // A controller plugged in: the console's "Joy-Con attached" effect on the sides of the
        // screen, in the colour of the controller (green for an Xbox one, blue for a PlayStation
        // one, their own for Joy-Con, grey for the others).
        private const uint RailEffectTag = 0x178A;

        private static void ControllerPlugged(string name)
        {
            ControllerPicture(name);
            RailEffect(name, 0);
        }

        // The same on the way out: the console's "Joy-Con detached" effect, then the picture and
        // the symbols of a controller still there.
        private static void ControllerUnplugged(string name)
        {
            RailEffect(name, 1);
            ControllerPictureGone(name);
        }

        private static void RailEffect(string name, byte gone)
        {
            Ryujinx.Input.SDL3.JoyConColors colors = Ryujinx.Input.SDL3.JoyConColors.Of(name);

            if (NextendoHomeMenuVanilla.Enabled || !Ryujinx.HLE.HOS.HomeMenuStatus.Running)
            {
                return;
            }

            string n = (name ?? "").ToLowerInvariant();
            bool left = n.Contains("joy-con (l)");
            bool right = n.Contains("joy-con (r)");

            uint color = Ryujinx.HLE.HOS.Services.Sdb.Pl.ControllerButtonFont.StyleOf(name) switch
            {
                Ryujinx.HLE.HOS.Services.Sdb.Pl.ControllerStyle.Xbox => 0x107C10,
                Ryujinx.HLE.HOS.Services.Sdb.Pl.ControllerStyle.PlayStation4 or Ryujinx.HLE.HOS.Services.Sdb.Pl.ControllerStyle.PlayStation5 => 0x0070D1,
                _ => left ? colors?.Body ?? 0x0AB9E6 : right ? colors?.Body ?? 0xFF3C28 : 0x828282,
            };

            for (int side = 0; side < 2; side++)
            {
                if ((side == 0 && right) || (side == 1 && left))
                {
                    continue;
                }

                // the colour (red, green, blue, opaque), then the side and the event (0: attached)
                byte[] data = new byte[0x48];

                data[0] = (byte)(color >> 16);
                data[1] = (byte)(color >> 8);
                data[2] = (byte)color;
                data[3] = 0xFF;
                data[0x38] = (byte)side;
                data[0x40] = gone;

                Ryujinx.Horizon.Ovln.OvlnNotifications.PostNow(RailEffectTag, data);
            }

            Logger.Info?.Print(LogClass.Application, $"[Nextendo] controller {(gone == 0 ? "plugged in" : "unplugged")} ({name}): effect in #{color:X6}");
        }

        // The one gone was the one shown: the one of a controller still there, or the console's.
        private static void ControllerPictureGone(string name)
        {
            string left = null;

            foreach (string other in Ryujinx.Input.SDL3.SDL3GamepadDriver.NextendoControllerNames)
            {
                left ??= other;
            }

            ControllerPicture(left);
        }

        public static void Initialize(Action requestScreenshot = null)
        {
            Ryujinx.Input.SDL3.SDL3GamepadDriver.NextendoControllerConnected += ControllerPlugged;
            Ryujinx.Input.SDL3.SDL3GamepadDriver.NextendoControllerDisconnected += ControllerUnplugged;

            Ryujinx.Input.SDL3.SDL3GamepadDriver.NextendoControllerDisconnected += ControllerDisconnected;
            Ryujinx.Input.SDL3.SDL3GamepadDriver.NextendoControllerBatteryLow += ControllerBatteryLow;
            Ryujinx.Input.SDL3.SDL3GamepadDriver.NextendoControllerConnected += ControllerConnected;

            _pcBatteryTimer = new Timer(_ => CheckPcBattery(), null, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(1));

            _badgeTimer = new Timer(_ => _ = CheckBadgesAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10));
        }

        private static void ControllerDisconnected(string name) =>
            NextendoConsoleToasts.Show(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NotifControllerDisconnected], name, null, PictogramOf(name) + "+flair_disconnected");

        private static void ControllerConnected(string name) =>
            NextendoConsoleToasts.Show(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NotifControllerConnected], name, null, PictogramOf(name));

        // ---- The computer's battery (a laptop): low, as the console says it ----

        private static Timer _pcBatteryTimer;
        private static bool _pcBatteryTold;

        private static void CheckPcBattery()
        {
            Ryujinx.HLE.HOS.Services.Ptm.Psm.HostBattery.State state = Ryujinx.HLE.HOS.Services.Ptm.Psm.HostBattery.Read();

            if (state.Plugged || state.Percent > 20)
            {
                _pcBatteryTold = false;

                return;
            }

            if (state.Percent <= 15 && !_pcBatteryTold)
            {
                _pcBatteryTold = true;
                PcBatteryLow(state.Percent);
            }
        }

        /// <summary>The Nextendo server lost (false) or back (true).</summary>
        public static void ServerConnection(bool back) =>
            NextendoConsoleToasts.Show(
                LocaleManager.Instance[back ? LocaleKeys.Dialog_Nextendo_NotifServerBack : LocaleKeys.Dialog_Nextendo_NotifServerLost],
                LocaleManager.Instance[back ? LocaleKeys.Dialog_Nextendo_NotifServerBackText : LocaleKeys.Dialog_Nextendo_NotifServerLostText],
                Ryujinx.Common.EmbeddedResources.Read("Ryujinx/Assets.UIImages.Logo_Nextendo.png"));

        private static void PcBatteryLow(int percent) =>
            NextendoConsoleToasts.Show(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NotifPcBatteryLow],
                string.Format(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NotifPcBatteryText], percent), null, "fit:flair_battery");

        private static void ControllerBatteryLow(string name, int percent) =>
            NextendoConsoleToasts.Show(LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_NotifBatteryLow], $"{name} ({percent} %)", null, PictogramOf(name) + "+flair_battery");

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
