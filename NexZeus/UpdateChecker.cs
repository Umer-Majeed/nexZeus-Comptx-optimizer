using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NexZeus
{
    /// <summary>
    /// Checks GitHub Releases for a newer NexZeus version and, if found,
    /// asks the user whether to open the download page.
    ///
    /// SETUP: set RepoOwner / RepoName below to your actual GitHub repo
    /// (the one you publish .exe / installer releases to). If you don't
    /// use GitHub Releases, point CheckUrl-building logic at whatever
    /// endpoint you do use instead — the compare/prompt logic below
    /// doesn't care where the version string came from.
    /// </summary>
    public static class UpdateChecker
    {
        private const string RepoOwner = "YOUR_GITHUB_USERNAME";   // TODO: fill in
        private const string RepoName = "NexZeus";                 // TODO: fill in

        private static string ApiUrl => $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

        private class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("html_url")]
            public string? HtmlUrl { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }

        /// <summary>
        /// Silently checks for an update. Returns null if up to date, unreachable,
        /// not configured, or on any error — this must never throw or block startup.
        /// </summary>
        public static async Task<(string version, string url)?> CheckForUpdateAsync()
        {
            if (RepoOwner == "YOUR_GITHUB_USERNAME")
            {
                // Not configured yet — skip silently instead of spamming errors.
                return null;
            }

            try
            {
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(6);
                // GitHub API requires a User-Agent header or it rejects the request.
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NexZeus", GetCurrentVersion().ToString()));
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                string json = await http.GetStringAsync(ApiUrl);
                var release = JsonSerializer.Deserialize<GitHubRelease>(json);

                if (release?.TagName == null || release.HtmlUrl == null)
                    return null;

                string cleanTag = release.TagName.TrimStart('v', 'V');
                if (!Version.TryParse(cleanTag, out var remoteVersion))
                    return null;

                var current = GetCurrentVersion();

                if (remoteVersion > current)
                    return (cleanTag, release.HtmlUrl);

                return null;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Update check failed (non-fatal): " + ex.Message);
                return null;
            }
        }

        public static Version GetCurrentVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
        }

        /// <summary>Runs the check and, if a newer version exists, shows a themed prompt offering to open the download page.</summary>
        public static async Task CheckAndPromptAsync(System.Windows.Window? owner)
        {
            var update = await CheckForUpdateAsync();
            if (update == null) return;

            bool openPage = ThemedMessageBox.Show(
                owner,
                $"A new version of NexZeus is available: v{update.Value.version}\n" +
                $"You're currently running v{GetCurrentVersion()}.\n\n" +
                "Open the download page now?",
                "Update Available",
                ThemedMessageBoxIcon.Question);

            if (openPage)
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = update.Value.url,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "Opening update download page");
                }
            }
        }
    }
}