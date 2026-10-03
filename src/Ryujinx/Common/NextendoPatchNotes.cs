using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] "What's new" popup, shown once per version on the first launch of a new build.
    ///
    /// A flag file records the version whose notes were last shown. When the running version differs
    /// (comparing the base version, ignoring any "+githash" suffix so a rebuild of the same release
    /// doesn't re-trigger it) the condensed notes are shown and the flag is updated. Deleting
    /// nextendo_patchnote_version re-shows them.
    /// </summary>
    public static class NextendoPatchNotes
    {
        private const string FlagFileName = "nextendo_patchnote_version";
        private const string LatestReleaseUrl = "https://api.github.com/repos/NextendoNetwork/Ryujinx-Nextendo/releases/latest";

        private static string FlagPath => Path.Combine(AppDataManager.BaseDirPath, FlagFileName);

        // The base version (before any "+githash"), so the note isn't re-shown just because the
        // hash changed within the same release.
        private static string CurrentVersion()
        {
            string v = Ryujinx.Common.ReleaseInformation.Version ?? "";
            int plus = v.IndexOf('+');

            return plus >= 0 ? v[..plus] : v;
        }

        /// <summary>True when the running version's notes haven't been shown on this install yet.</summary>
        public static bool ShouldShow()
        {
            try
            {
                string cur = CurrentVersion();
                if (string.IsNullOrEmpty(cur))
                {
                    return false;
                }

                string seen = File.Exists(FlagPath) ? File.ReadAllText(FlagPath).Trim() : "";

                return seen != cur;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Records the running version as "notes seen", so they won't show again until the next
        /// release. Also seeds the flag on a fresh install (no "what's new" the very first time,
        /// right after the setup wizard).
        /// </summary>
        public static void MarkShown()
        {
            try
            {
                File.WriteAllText(FlagPath, CurrentVersion());
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.Application, $"[Nextendo] could not write patch-note flag: {ex.Message}");
            }
        }

        private const uint Accent = 0xFF3EE8C8;
        private const uint Divider = 0x33808080;

        /// <summary>Shows the condensed patch notes as a modal dialog, then marks them seen.</summary>
        public static async Task ShowAsync()
        {
            try
            {
                (string title, string body, string url)? release = await FetchLatestReleaseAsync();
                ContentDialog dialog = new()
                {
                    Title = release?.title ?? LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_PatchNoteTitle],
                    Content = release is { } latest ? BuildReleaseBody(latest.body, latest.url) : BuildBody(),
                    CloseButtonText = LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_PatchNoteButton],
                };

                await ContentDialogHelper.ShowAsync(dialog);
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.UI, $"[Nextendo] patch-note popup failed: {ex.Message}");
            }
            finally
            {
                MarkShown();
            }
        }

        private static async Task<(string title, string body, string url)?> FetchLatestReleaseAsync()
        {
            try
            {
                using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(8) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Ryujinx-Nextendo");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                using HttpResponseMessage response = await http.GetAsync(LatestReleaseUrl);
                response.EnsureSuccessStatusCode();
                using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                JsonElement root = document.RootElement;
                string tag = root.TryGetProperty("tag_name", out JsonElement tagElement) ? tagElement.GetString() ?? "" : "";
                string name = root.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? "" : "";
                string body = root.TryGetProperty("body", out JsonElement bodyElement) ? bodyElement.GetString() ?? "" : "";
                string url = root.TryGetProperty("html_url", out JsonElement urlElement) ? urlElement.GetString() ?? "" : "";
                string title = string.IsNullOrWhiteSpace(name) ? $"What's New: {tag}" : $"What's New: {name}";

                return string.IsNullOrWhiteSpace(body) ? null : (title, body, url);
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.UI, $"[Nextendo] could not load GitHub release notes: {ex.Message}");
                return null;
            }
        }

        private static Control BuildReleaseBody(string markdown, string releaseUrl)
        {
            SolidColorBrush accent = new(Accent);
            SolidColorBrush divider = new(Divider);
            StackPanel root = new() { Margin = new Thickness(2, 0, 2, 0), Spacing = 4 };
            bool firstSection = true;

            foreach (string line in markdown.Replace("\r", "").Split('\n'))
            {
                string text = line.Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                if (text.StartsWith("## ", StringComparison.Ordinal) || text.StartsWith("### ", StringComparison.Ordinal))
                {
                    string heading = text.TrimStart('#', ' ').Trim();
                    root.Children.Add(BuildVersionHeader(heading, firstSection, accent, divider));
                    firstSection = false;
                }
                else
                {
                    if (text.StartsWith("- ", StringComparison.Ordinal) || text.StartsWith("* ", StringComparison.Ordinal))
                    {
                        text = text[2..].Trim();
                    }
                    root.Children.Add(BuildBullet(text, accent));
                }
            }

            if (Uri.TryCreate(releaseUrl, UriKind.Absolute, out Uri uri) && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            {
                Button link = new()
                {
                    Content = "View release on GitHub",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 8, 0, 0),
                };
                link.Click += (_, _) => Ryujinx.Common.Helper.OpenHelper.OpenUrl(uri.ToString());
                root.Children.Add(link);
            }

            return new ScrollViewer
            {
                Content = root,
                MaxHeight = 560,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            };
        }

        /// <summary>
        /// Builds the notes panel. The localized body uses one line per feature; a line starting with
        /// "## " opens a new version section (its remainder is the version label), any other non-empty
        /// line is a feature bullet. Bodies without "## " markers render as a single section.
        /// </summary>
        private static ScrollViewer BuildBody()
        {
            SolidColorBrush accent = new(Accent);
            SolidColorBrush divider = new(Divider);

            StackPanel root = new() { Margin = new Thickness(2, 0, 2, 0) };
            bool firstSection = true;

            foreach (string line in LocaleManager.Instance[LocaleKeys.Dialog_Nextendo_PatchNoteBody].Split('\n'))
            {
                string trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (trimmed.StartsWith("## ", StringComparison.Ordinal))
                {
                    root.Children.Add(BuildVersionHeader(trimmed[3..].Trim(), firstSection, accent, divider));
                    firstSection = false;
                }
                else
                {
                    string bullet = trimmed.StartsWith("•", StringComparison.Ordinal) ? trimmed[1..].Trim() : trimmed;
                    root.Children.Add(BuildBullet(bullet, accent));
                }
            }

            return new ScrollViewer
            {
                Content = root,
                MaxHeight = 420,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            };
        }

        private static StackPanel BuildVersionHeader(string version, bool first, IBrush accent, IBrush divider)
        {
            TextBlock label = new()
            {
                Text = version,
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                Foreground = accent,
                Margin = new Thickness(0, first ? 0 : 14, 0, 2),
            };

            Border line = new()
            {
                Height = 1,
                Background = divider,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 8),
            };

            StackPanel header = new();
            header.Children.Add(label);
            header.Children.Add(line);

            return header;
        }

        private static Grid BuildBullet(string text, IBrush accent)
        {
            TextBlock glyph = new()
            {
                Text = "•",
                FontSize = 14,
                Foreground = accent,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0),
            };

            TextBlock body = new()
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                LineHeight = 20,
            };

            Grid row = new()
            {
                Margin = new Thickness(0, 0, 0, 7),
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(18)),
                    new ColumnDefinition(GridLength.Star),
                },
            };

            Grid.SetColumn(glyph, 0);
            Grid.SetColumn(body, 1);
            row.Children.Add(glyph);
            row.Children.Add(body);

            return row;
        }
    }
}
