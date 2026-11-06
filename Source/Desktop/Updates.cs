using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace SubWake;

/// <summary>A newer release on GitHub.</summary>
sealed record UpdateInfo(string Version, string Url);

/// <summary>
/// The update check: once a day (and on request) it asks GitHub's public API for the latest release.
/// It sends nothing but the request itself (with "SubWake/version" as the user agent) and installs nothing.
/// </summary>
static class Updates
{
    public static readonly TimeSpan Every = TimeSpan.FromHours(24);
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);

    /// <summary>"owner/repo", set by the GitHubRepo property in the .csproj.</summary>
    public static string Repo { get; } =
        typeof(Updates).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "GitHubRepo")?.Value ?? "";

    public static string ReleasesUrl => $"https://github.com/{Repo}/releases/latest";

    /// <summary>"v2.1.0", "2.1", "v2.1.0-beta" → 2.1.0. Null if it isn't a version.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string clean = text.Trim().TrimStart('v', 'V');
        int cut = clean.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) clean = clean[..cut];
        if (!System.Version.TryParse(clean, out var version)) return null;
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    public static bool IsNewer(string? latest, string current) =>
        ParseVersion(latest) is Version l && ParseVersion(current) is Version c && l > c;

    /// <summary>An automatic check is due after a day, or if the clock went back.</summary>
    public static bool Due(DateTime? last, DateTime now) => last is not DateTime l || now - l >= Every || l > now;

    /// <summary>Only release pages on GitHub are opened from the update banner.</summary>
    public static string SafeUrl(string? url, string repo) =>
        url != null && url.StartsWith($"https://github.com/{repo}/", StringComparison.OrdinalIgnoreCase) ? url : $"https://github.com/{repo}/releases/latest";

    public static async Task<UpdateInfo?> LatestAsync(string current)
    {
        if (!Repo.Contains('/')) throw new InvalidOperationException("No GitHub repository is set for update checks.");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SubWake", current));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;   // No release yet.
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string? tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        string? url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : null;
        if (!IsNewer(tag, current)) return null;
        return new UpdateInfo(ParseVersion(tag)!.ToString(3), SafeUrl(url, Repo));
    }
}
