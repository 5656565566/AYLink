using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AYLink.Desktop.Models;

namespace AYLink.Desktop.Services;

public sealed class UpdateService
{
    private static readonly Uri LatestReleaseUri =
        new("https://api.github.com/repos/5656565566/AYLink/releases/latest");

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AYLink");
        return client;
    }

    public async Task<GitHubReleaseInfo?> GetLatestReleaseAsync()
    {
        using var response = await HttpClient.GetAsync(LatestReleaseUri);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<GitHubReleaseInfo>(stream);
    }
}