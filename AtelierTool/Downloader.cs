using System.Collections.Concurrent;
using System.Security.Cryptography;
using Spectre.Console;

namespace AtelierTool;

public class Downloader : IDisposable
{
    private readonly Catalog _catalog;
    private readonly string _outputPath;
    private readonly HttpClient _client;

    private readonly ConcurrentBag<Bundle> _failedBundles;
    private SemaphoreSlim _semaphore;
    private long _finishedAssetCount;

    public Downloader(Catalog catalog, string outputPath, int concurrent, string url)
    {
        _catalog = catalog;
        _outputPath = outputPath;
        _semaphore = new SemaphoreSlim(concurrent, concurrent);
        _failedBundles = new ConcurrentBag<Bundle>();

        _client = new HttpClient();
        _client.BaseAddress = new Uri(url);
        _client.Timeout = TimeSpan.FromMinutes(5);

        _finishedAssetCount = 0;
    }

    public async Task Download()
    {
        var bundles = _catalog.FileCatalog.Bundles;

        foreach (var dir in bundles
                     .Where(x => x.RelativePath.Contains('/'))
                     .Select(x => Path.GetDirectoryName(x.RelativePath))
                     .Distinct())
        {
            Directory.CreateDirectory(Path.Join(_outputPath, dir));
        }

        if (bundles.Any(x => File.Exists(Path.Join(_outputPath, x.RelativePath))))
        {
            ConsoleLogger.WriteInfoLine("Checking already downloaded assets. This might take a long time.");
            AnsiConsole.Progress()
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn())
                .Start(ctx =>
                {
                    var localBundles = bundles.Where(x => File.Exists(Path.Join(_outputPath, x.RelativePath))).ToList();

                    var task = ctx.AddTask("Verifying downloaded assets", autoStart: false, maxValue: localBundles.Count);

                    var threadPool = localBundles
                        .AsParallel()
                        .WithDegreeOfParallelism(_semaphore.CurrentCount)
                        .Select(asset => VerifyFile(asset, Path.Join(_outputPath, asset.RelativePath)))
                        .ToList();

                    var lastChecked = 0L;

                    task.StartTask();
                    while (lastChecked != localBundles.Count)
                    {
                        var current = Interlocked.Read(ref _finishedAssetCount);
                        if (current != lastChecked)
                        {
                            task.Increment(current - lastChecked);
                        }

                        lastChecked = current;
                    }

                    task.StopTask();
                });

            foreach (var bundle in _failedBundles)
                bundles.Remove(bundle);
        }

        var downloadBundles = bundles;

        while (downloadBundles.Count > 0)
        {
            _finishedAssetCount = 0;
            _failedBundles.Clear();

            ConsoleLogger.WriteInfoLine("Starting asset download.");
            await AnsiConsole.Progress()
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("Downloading assets", autoStart: false, maxValue: downloadBundles.Count);

                    var threadPool = downloadBundles
                        .AsParallel()
                        .WithDegreeOfParallelism(_semaphore.CurrentCount)
                        .Select(asset => DownloadFile(asset, Path.Join(_outputPath, asset.RelativePath)))
                        .ToList();

                    var lastChecked = 0L;

                    task.StartTask();
                    while (lastChecked != downloadBundles.Count)
                    {
                        var current = Interlocked.Read(ref _finishedAssetCount);
                        if (current != lastChecked)
                        {
                            task.Increment(current - lastChecked);
                        }

                        lastChecked = current;
                        await Task.Delay(10);
                    }

                    task.StopTask();
                });

            if (!_failedBundles.IsEmpty)
            {
                ConsoleLogger.WriteInfoLine("Some bundles failed to download. Retrying them.");
                downloadBundles = _failedBundles.ToList();

                _semaphore = new SemaphoreSlim(1, 1);
            }
            else
            {
                break;
            }
        }

        ConsoleLogger.WriteInfoLine("Downloaded all bundles [green bold]successfully.[/]");
    }

    private async Task VerifyFile(Bundle bundle, string path)
    {
        await _semaphore.WaitAsync();

        await using var fs = File.OpenRead(path);

        var expectedSize = bundle.Compression != 3 ? bundle.FileSize : bundle.FileSize - Header.Size - Header.HashSize;
        if (fs.Length == expectedSize)
        {
            _failedBundles.Add(bundle);

            //var hash = await MD5.HashDataAsync(fs);
            //if (hash.SequenceEqual(Convert.FromBase64String(bundle.FileMd5)))
            //{
            //   _failedBundles.Add(bundle);
            //}
        }

        Interlocked.Add(ref _finishedAssetCount, 1);

        _semaphore.Release();
    }

    private async Task DownloadFile(Bundle bundle, string path)
    {
        await _semaphore.WaitAsync();

        try
        {
            var bundleData = await _client.GetByteArrayAsync(bundle.RelativePath);

            await using var decStream = bundle.Compression != 3 
                ? new MemoryStream(bundleData) 
                : BundleCrypto.DecryptBundle(bundleData, bundle);

            _semaphore.Release();

            await using var fs = File.OpenWrite(path);
            await decStream.CopyToAsync(fs);

            Interlocked.Increment(ref _finishedAssetCount);
        }
        catch (Exception ex)
        {
            await Console.Out.WriteLineAsync($"Failed to download bundle {bundle.RelativePath}. This download will be retried later. Reason: {ex}");

            _failedBundles.Add(bundle);
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}