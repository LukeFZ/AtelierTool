using System.ComponentModel;
using AtelierTool;
using Spectre.Console.Cli;
using static DownloadCommand;

var app = new CommandApp();

app.Configure(config =>
{
    config.AddCommand<DownloadCommand>("download")
        .WithDescription("Downloads and decrypts the assets given a version.")
        .WithExample("download", "1695186894_yFRlDhTt4xOHNPdw");
});

await app.RunAsync(args);

internal sealed class DownloadCommand : AsyncCommand<Settings>
{
    public enum Platform
    {
        Android,
        iOS
    }

    public sealed class Settings : CommandSettings
    {
        [Description("Asset version to download")]
        [CommandArgument(0, "<version>")]
        public string AssetVersion { get; init; } = null!;

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