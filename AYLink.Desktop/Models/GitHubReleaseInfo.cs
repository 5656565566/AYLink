using Newtonsoft.Json;

namespace AYLink.Desktop.Models;

public sealed class GitHubReleaseInfo
{
    [JsonProperty("tag_name")]
    public string? TagName { get; set; }

    [JsonProperty("html_url")]
    public string? HtmlUrl { get; set; }
}