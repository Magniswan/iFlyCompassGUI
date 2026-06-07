using System.Net.Http.Json;
using iFlyCompassGUI.Models;

namespace iFlyCompassGUI.Helpers;

public static class GitHubApiHelper
{
    private static string GetApiBase(string repoUrl)
    {
        // Convert https://github.com/owner/repo to https://api.github.com/repos/owner/repo
        if (repoUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            var path = repoUrl["https://github.com/".Length..].TrimEnd('/');
            return $"https://api.github.com/repos/{path}";
        }
        return repoUrl;
    }

    public static async Task<ReleaseInfo?> GetLatestReleaseAsync(HttpClient httpClient, string repoUrl)
    {
        var apiBase = GetApiBase(repoUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{apiBase}/releases/latest");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        var response = await httpClient.SendAsync(request);

        if ((int)response.StatusCode == 403 && response.Headers.Contains("X-RateLimit-Remaining"))
        {
            var resetHeader = response.Headers.GetValues("X-RateLimit-Reset").FirstOrDefault();
            var resetTime = resetHeader != null
                ? DateTimeOffset.FromUnixTimeSeconds(long.Parse(resetHeader)).ToLocalTime().ToString("HH:mm")
                : "稍后";
            throw new HttpRequestException($"GitHub API 请求次数已达上限，请在 {resetTime} 后再试。");
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<GitHubReleaseResponse>();
        if (json == null) return null;

        return new ReleaseInfo
        {
            TagName = json.tag_name,
            Name = json.name,
            Body = json.body,
            ZipballUrl = json.zipball_url,
            TarballUrl = json.tarball_url,
            PublishedAt = json.published_at
        };
    }

    public static async Task<string?> GetReleaseAssetUrlAsync(HttpClient httpClient, string repoUrl, string tagName, string assetName)
    {
        var apiBase = GetApiBase(repoUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{apiBase}/releases/tags/{tagName}");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        var response = await httpClient.SendAsync(request);

        if ((int)response.StatusCode == 403 && response.Headers.Contains("X-RateLimit-Remaining"))
        {
            var resetHeader = response.Headers.GetValues("X-RateLimit-Reset").FirstOrDefault();
            var resetTime = resetHeader != null
                ? DateTimeOffset.FromUnixTimeSeconds(long.Parse(resetHeader)).ToLocalTime().ToString("HH:mm")
                : "稍后";
            throw new HttpRequestException($"GitHub API 请求次数已达上限，请在 {resetTime} 后再试。");
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<GitHubReleaseDetailResponse>();
        if (json?.assets == null) return null;

        // If assetName starts with ".", treat it as an extension and find by suffix match
        if (assetName.StartsWith("."))
        {
            var asset = json.assets.FirstOrDefault(a => a.name.EndsWith(assetName, StringComparison.OrdinalIgnoreCase));
            return asset?.browser_download_url;
        }

        var exactAsset = json.assets.FirstOrDefault(a => a.name.Equals(assetName, StringComparison.OrdinalIgnoreCase));
        return exactAsset?.browser_download_url;
    }

    private class GitHubReleaseResponse
    {
        public string tag_name { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string body { get; set; } = string.Empty;
        public string zipball_url { get; set; } = string.Empty;
        public string tarball_url { get; set; } = string.Empty;
        public DateTime published_at { get; set; }
    }

    private class GitHubReleaseDetailResponse
    {
        public string tag_name { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string body { get; set; } = string.Empty;
        public List<GitHubAsset> assets { get; set; } = [];
    }

    private class GitHubAsset
    {
        public string name { get; set; } = string.Empty;
        public string browser_download_url { get; set; } = string.Empty;
    }
}
