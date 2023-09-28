using System.Net.Http.Json;

namespace AtelierTool;

public class ApiClient : IDisposable
{
    private readonly HttpClient _client;

    public ApiClient()
    {
        _client = new HttpClient();
        _client.BaseAddress = new Uri("https://gacha.lukefz.xyz/atelier/");
    }

    public async Task<(string AssetVersion, string MasterDataVersion)> GetVersionsAsync()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "version");

        var resp = await _client.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var data = await resp.Content.ReadFromJsonAsync<AssetVersionSet>();
        return data == null 
            ? ("", "")
            : (data.AssetVersion, data.MasterDataVersion);
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    internal record AssetVersionSet(string AssetVersion, string MasterDataVersion, DateTimeOffset LastUpdated,
        DateTimeOffset LastChecked);
}