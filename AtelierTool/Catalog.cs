using System.Text.Json;

namespace AtelierTool;

public record Catalog(FileCatalog FileCatalog, string MainAssetLabel, string UniqueBuildId, int Version, List<string> MainAssetBundles)
{
    public static async Task<Catalog> LoadFromVersion(string baseUrl, string outputPath)
    {
        using var client = new HttpClient();
        var catalog = await client.GetAsync($"{baseUrl}catalog.json");
        catalog.EnsureSuccessStatusCode();

        var catalogData = await catalog.Content.ReadAsStringAsync();
        await File.WriteAllTextAsync(Path.Join(outputPath, "catalog.json"), catalogData);

        return JsonSerializer.Deserialize<Catalog>(catalogData, new JsonSerializerOptions() {PropertyNamingPolicy = new UnderscoreNamingPolicy()})
               ?? throw new InvalidOperationException("Failed to deserialize remote catalog.");
    }
}

public record FileCatalog(List<Bundle> Bundles);

public record Bundle(string RelativePath, string BundleName, string Hash, long Crc, int FileSize, string FileMd5, int Compression, string UserData);