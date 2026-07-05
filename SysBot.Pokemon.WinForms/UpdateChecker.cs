using Newtonsoft.Json;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SysBot.Pokemon.WinForms
{
    public class UpdateChecker
    {
        // Auto-update is DISABLED (see FetchLatestReleaseAsync). These are kept only so the type
        // compiles; nothing contacts an external account. This is PKM-Universe's own build,
        // deployed manually — no third-party repo can push code into the live bots.
        private const string RepositoryOwner = "PokemonLover8888";
        private const string RepositoryName = "PKM-Universe-Bot";

        // Reuse HttpClient for better performance and socket management
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static UpdateChecker()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PKM-Universe-Bot");
        }

        public static async Task<(bool UpdateAvailable, bool UpdateRequired, string NewVersion)> CheckForUpdatesAsync(bool forceShow = false, bool showDialog = true)
        {
            ReleaseInfo? latestRelease = await FetchLatestReleaseAsync();

            bool updateAvailable = latestRelease != null && latestRelease.TagName != TradeBot.Version;
            bool updateRequired = latestRelease?.Prerelease == false && IsUpdateRequired(latestRelease?.Body);
            string? newVersion = latestRelease?.TagName;

            // Only show dialog if explicitly requested via forceShow (manual check)
            if (forceShow && showDialog)
            {
                var updateForm = new UpdateForm(updateRequired, newVersion ?? "", updateAvailable);
                updateForm.ShowDialog();
            }

            return (updateAvailable, updateRequired, newVersion ?? string.Empty);
        }

        public static async Task<string> FetchChangelogAsync()
        {
            ReleaseInfo? latestRelease = await FetchLatestReleaseAsync();
            return latestRelease?.Body ?? "Failed to fetch the latest release information.";
        }

        public static async Task<string?> FetchDownloadUrlAsync()
        {
            ReleaseInfo? latestRelease = await FetchLatestReleaseAsync();
            if (latestRelease?.Assets == null)
                return null;

            return latestRelease.Assets
            .FirstOrDefault(a => a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
            ?.BrowserDownloadUrl;
        }

        private static async Task<ReleaseInfo?> FetchLatestReleaseAsync()
        {
            // AUTO-UPDATE DISABLED. PKM-Universe Bot is built and deployed manually, so the bot must
            // never fetch releases from any external GitHub account. Returning null means "no update
            // available" across CheckForUpdatesAsync / FetchChangelogAsync / FetchDownloadUrlAsync,
            // with no network call at all. To re-enable later, restore the api.github.com fetch and
            // point RepositoryOwner/RepositoryName at your OWN repo.
            await Task.CompletedTask;
            return null;
        }

        private static bool IsUpdateRequired(string? changelogBody)
        {
            return !string.IsNullOrWhiteSpace(changelogBody) &&
                   changelogBody.Contains("Required = Yes", StringComparison.OrdinalIgnoreCase);
        }

        private class ReleaseInfo
        {
            [JsonProperty("tag_name")]
            public string? TagName { get; set; }

            [JsonProperty("prerelease")]
            public bool Prerelease { get; set; }

            [JsonProperty("assets")]
            public List<AssetInfo>? Assets { get; set; }

            [JsonProperty("body")]
            public string? Body { get; set; }
        }

        private class AssetInfo
        {
            [JsonProperty("name")]
            public string? Name { get; set; }

            [JsonProperty("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }
        }
    }
}
