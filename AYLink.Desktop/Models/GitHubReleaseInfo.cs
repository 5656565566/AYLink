using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace AYLink.Desktop.Models;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
[method: JsonConstructor]
public sealed class GitHubReleaseInfo()
{
    [JsonProperty("tag_name")]
    public string? TagName { get; set; }

    [JsonProperty("html_url")]
    public string? HtmlUrl { get; set; }
}