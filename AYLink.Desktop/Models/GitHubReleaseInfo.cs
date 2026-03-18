using System.Text.Json.Serialization;

namespace AYLink.Desktop.Models;

public sealed class GitHubReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
}