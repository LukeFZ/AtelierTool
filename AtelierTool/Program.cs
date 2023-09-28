using System.ComponentModel;
using System.Text.Json;
using AtelierTool;
using MessagePack;
using MessagePack.Formatters;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.PropagateExceptions();

    config.AddCommand<BundleDownloadCommand>("download-bundles")
        .WithDescription("Downloads and decrypts the game bundles.")
        .WithExample("download-bundles")
        .WithExample("download-bundles", "1695186894_yFRlDhTt4xOHNPdw");

    config.AddCommand<BundleDecryptCommand>("decrypt-bundles")
        .WithDescription("Decrypts local bundles given a catalog and the encrypted bundles.");

    config.AddCommand<MasterDataDownloadCommand>("download-masterdata")
        .WithDescription("Downloads and decrypts the game master data.")
        .WithExample("download-masterdata")
        .WithExample("download-masterdata", "1695388425_lqWykRBBn0E2ATNR");

    config.AddCommand<MasterDataDecryptCommand>("decrypt-masterdata")
        .WithDescription("Decrypts a local master data given the version and encrypted file.")
        .WithExample("decrypt-masterdata", "encrypted.masterdata", "1695388425_lqWykRBBn0E2ATNR");

    config.AddCommand<MasterDataExtractCommand>("extract-masterdata")
        .WithDescription("Extracts a local master data into seperate JSON files.");
});

await app.RunAsync(args);

internal sealed class BundleDownloadCommand : AsyncCommand<BundleDownloadCommand.Settings>
{
    public enum Platform
    {
        Android,
        iOS
    }

    public sealed class Settings : CommandSettings
    {
        [Description("Asset version to download. Obtains the latest version if not specified.")]
        [CommandArgument(0, "[version]")]
        public string? AssetVersion { get; set; }

        [Description("Platform to download assets for")]
        [CommandOption("-p|--platform")]
        [DefaultValue(Platform.Android)]
        public Platform AssetPlatform { get; init; }

        [Description("Path to store the assets into.")]
        [CommandOption("-o|--output")]
        [DefaultValue("output")]
        public string OutputPath { get; init; } = null!;

        [Description("Amount of files to download concurrently.")]
        [CommandOption("-c|--concurrent")]
        [DefaultValue(16)]
        public int ConcurrentCount { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (settings.AssetVersion == null)
        {
            ConsoleLogger.WriteInfoLine("Asset version not specified, obtaining latest version from server.");

            using var client = new ApiClient();
            var (assetVer, _) = await client.GetVersionsAsync();
            if (assetVer != "")
            {
                ConsoleLogger.WriteInfoLine("Obtained latest asset version [green bold]successfully.[/]");
                settings.AssetVersion = assetVer;
            }
            else
            {
                ConsoleLogger.WriteErrLine("Failed to retrieve latest master data version.");
                return -1;
            }
        }

        var fullOutputPath = Path.GetFullPath(settings.OutputPath);
        Directory.CreateDirectory(fullOutputPath);

        var url = $"https://asset.resleriana.jp/asset/{settings.AssetVersion}/{settings.AssetPlatform}/";

        ConsoleLogger.WriteInfoLine($"Downloading catalog for version {settings.AssetVersion}.");
        var catalog = await Catalog.LoadFromVersion(url, fullOutputPath);
        ConsoleLogger.WriteInfoLine("Downloaded catalog [green bold]successfully.[/]");

        ConsoleLogger.WriteInfoLine($"Total asset count: {catalog.FileCatalog.Bundles.Count}");
        ConsoleLogger.WriteInfoLine($"Downloading assets. (Concurrent count: {settings.ConcurrentCount})");

        var downloader = new Downloader(catalog, settings.OutputPath, settings.ConcurrentCount, url);

        await downloader.Download();

        return 0;
    }
}

internal sealed class BundleDecryptCommand : AsyncCommand<BundleDecryptCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("Path to the bundle catalog.")]
        [CommandArgument(0, "<catalog path>")]
        public string CatalogPath { get; init; } = null!;

        [Description("Path to the bundle directory.")]
        [CommandArgument(1, "<bundle directory>")]
        public string BundlesPath { get; init; } = null!;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var catalogData = await File.ReadAllTextAsync(settings.CatalogPath);
        var catalog = JsonSerializer.Deserialize<Catalog>(catalogData, new JsonSerializerOptions {PropertyNamingPolicy = new UnderscoreNamingPolicy()});
        if (catalog == null)
        {
            ConsoleLogger.WriteErrLine("Failed to parse bundle catalog.");
            return -1;
        }

        foreach (var bundle in catalog.FileCatalog.Bundles)
        {
            var path = Path.Join(settings.BundlesPath, bundle.RelativePath);
            if (!File.Exists(path))
                continue;

            var assetData = await File.ReadAllBytesAsync(path);
            var decStream = BundleCrypto.DecryptBundle(assetData, bundle);

            await using var fs = File.OpenWrite(path);
            await decStream.CopyToAsync(fs);

            ConsoleLogger.WriteInfoLine($"[green bold]Successfully[/] decrypted bundle [white bold]{bundle.BundleName}[/].");
        }

        return 0;
    }

    public override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (!File.Exists(settings.CatalogPath))
            return ValidationResult.Error("Catalog file not found.");

        if (!Directory.Exists(settings.BundlesPath))
            return ValidationResult.Error("Bundles directory not found.");

        return base.Validate(context, settings);
    }
}

internal sealed class MasterDataDownloadCommand : AsyncCommand<MasterDataDownloadCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("Master data version to download. Obtains the latest version if not specified.")]
        [CommandArgument(0, "[version]")]
        public string? MasterDataVersion { get; set; }

        [Description("Path to store the master data into.")]
        [CommandOption("-o|--output")]
        [DefaultValue("downloaded.masterdata")]
        public string OutputPath { get; init; } = null!;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (settings.MasterDataVersion == null)
        {
            ConsoleLogger.WriteInfoLine("Master data version not specified, obtaining latest version from server.");

            using var client = new ApiClient();
            var (_, masterDataVer) = await client.GetVersionsAsync();
            if (masterDataVer != "")
            {
                ConsoleLogger.WriteInfoLine("Obtained latest master data version [green bold]successfully.[/]");
                settings.MasterDataVersion = masterDataVer;
            }
            else
            {
                ConsoleLogger.WriteErrLine("Failed to retrieve latest master data version.");
                return -1;
            }
        }

        var url = $"https://asset.resleriana.jp/master_data/{settings.MasterDataVersion}";

        ConsoleLogger.WriteInfoLine($"Downloading master data for version {settings.MasterDataVersion}.");

        using var httpClient = new HttpClient();
        var encrypted = await httpClient.GetByteArrayAsync(url);

        ConsoleLogger.WriteInfoLine("Downloaded master data [green bold]successfully.[/]");

        var decrypted = MasterDataCrypto.DecryptMasterData(encrypted, settings.MasterDataVersion);

        await File.WriteAllBytesAsync(settings.OutputPath, decrypted);  

        return 0;
    }

    public override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (Directory.Exists(settings.OutputPath))
            return ValidationResult.Error("Output path is a directory.");

        return base.Validate(context, settings);
    }
}

internal sealed class MasterDataDecryptCommand : AsyncCommand<MasterDataDecryptCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("Path to the encrypted masterdata.")]
        [CommandArgument(0, "<masterdata path>")]
        public string MasterDataPath { get; init; } = null!;

        [Description("Version of the master data.")]
        [CommandArgument(1, "<version>")]
        public string MasterDataVersion { get; init; } = null!;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var encryptedMasterData = await File.ReadAllBytesAsync(settings.MasterDataPath);

        var decrypted = MasterDataCrypto.DecryptMasterData(encryptedMasterData, settings.MasterDataVersion);

        await File.WriteAllBytesAsync(settings.MasterDataPath, decrypted);

        ConsoleLogger.WriteInfoLine("Decrypted master data.");

        return 0;
    }

    public override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (!File.Exists(settings.MasterDataPath))
            return ValidationResult.Error("Master data file not found.");

        return base.Validate(context, settings);
    }
}

internal sealed class MasterDataExtractCommand : Command<MasterDataExtractCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("Path to the masterdata")]
        [CommandArgument(0, "<masterdata path>")]
        public string MasterDataPath { get; init; } = null!;

        [Description("Path to the output directory.")]
        [CommandOption("-o|--output")]
        [DefaultValue("masterdata_output")]
        public string OutputPath { get; set; } = null!;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        Directory.CreateDirectory(settings.OutputPath);

        var md = File.ReadAllBytes(settings.MasterDataPath);

        var reader = new MessagePackReader(md);
        var hd = new DictionaryFormatter<string, (int, int)>();
        var header = hd.Deserialize(ref reader, MessagePackSerializerOptions.Standard);

        var rawTable = md.AsMemory((int)reader.Consumed);

        var msgpackSettings = MessagePackSerializer.DefaultOptions.WithCompression(MessagePackCompression.Lz4Block);

        foreach (var (name, (offset, size)) in header)
        {
            Console.WriteLine($"dumping {name} @ 0x{offset:x8} (0x{size:x8})");

            var tableJson = MessagePackSerializer.ConvertToJson(rawTable.Slice(offset, size), msgpackSettings);

            File.WriteAllText(Path.Join(settings.OutputPath, name + ".json"), tableJson);
        }

        return 0;
    }

    public override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (!File.Exists(settings.MasterDataPath))
            return ValidationResult.Error("The provided master data does not exist.");

        if (File.Exists(settings.OutputPath))
            return ValidationResult.Error("The output directory is a file.");

        return base.Validate(context, settings);
    }
}