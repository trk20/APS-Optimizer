using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace APS_Optimizer_V3.Services;

public class CryptoMiniSatDownloader
{
    private const string GITHUB_API_URL = "https://api.github.com/repos/msoos/cryptominisat/releases/latest";
    private const string EXE_NAME = "cryptominisat5.exe";

    public static async Task<string> EnsureCryptoMiniSatAvailable()
    {
        // Use the application's actual deployment directory instead of AppData
        var appDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
                          ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cryptoMiniSatDir = Path.Combine(appDirectory, "CryptoMiniSat");
        Directory.CreateDirectory(cryptoMiniSatDir);

        var exePath = Path.Combine(cryptoMiniSatDir, EXE_NAME);

        if (File.Exists(exePath))
        {
            return exePath;
        }

        try
        {
            Console.WriteLine("CryptoMiniSat not found, downloading...");
            await DownloadAndExtractCryptoMiniSat(exePath);
            return exePath;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to download CryptoMiniSat: {ex.Message}", ex);
        }
    }

    private static async Task DownloadAndExtractCryptoMiniSat(string targetExePath)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "trk20/APS-Optimizer");
        httpClient.Timeout = TimeSpan.FromMinutes(5); // 5 minute timeout

        try
        {
            Console.WriteLine("Getting latest release info from GitHub...");

            // Get latest release info
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var releaseJson = await httpClient.GetStringAsync(GITHUB_API_URL, cts.Token);

            var releaseInfo = JToken.Parse(releaseJson)
                .ToObject<GitHubRelease>();

            if (releaseInfo?.Assets == null || !releaseInfo.Assets.Any())
            {
                throw new InvalidOperationException("No assets found in latest release");
            }

            Console.WriteLine($"Found {releaseInfo.Assets.Length} assets in latest release");

            // Find Windows zip asset
            var windowsAsset = releaseInfo.Assets.FirstOrDefault(a =>
                !string.IsNullOrEmpty(a.Name) &&
                a.Name.Contains("win", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            if (windowsAsset == null)
            {
                var assetNames = string.Join(", ", releaseInfo.Assets.Select(a => a.Name));
                throw new InvalidOperationException($"No Windows zip file found in latest release. Available assets: {assetNames}");
            }

            Console.WriteLine($"Found Windows asset: {windowsAsset.Name}");
            Console.WriteLine($"Downloading from: {windowsAsset.Browser_download_url}");

            // Download zip file
            using var downloadCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var zipBytes = await httpClient.GetByteArrayAsync(windowsAsset.Browser_download_url, downloadCts.Token);

            //Console.WriteLine($"Downloaded {zipBytes.Length} bytes");

            // Extract exe from zip
            using var zipStream = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            //Console.WriteLine($"Zip contains {archive.Entries.Count} entries:");
            foreach (var entry in archive.Entries)
            {
                Console.WriteLine($"  - {entry.FullName}");
            }

            // Find exe file in zip
            var exeEntry = archive.Entries.FirstOrDefault(e =>
                e.Name.Equals(EXE_NAME, StringComparison.OrdinalIgnoreCase) ||
                e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (exeEntry == null)
            {
                var entryNames = string.Join(", ", archive.Entries.Select(e => e.Name));
                throw new InvalidOperationException($"No executable file found in {windowsAsset.Name}. Archive contains: {entryNames}");
            }

            Console.WriteLine($"Found executable: {exeEntry.Name}");

            // Extract exe to target path
            using var exeStream = exeEntry.Open();
            using var fileStream = File.Create(targetExePath);
            await exeStream.CopyToAsync(fileStream);

            Console.WriteLine($"Successfully extracted {exeEntry.Name} to {targetExePath}");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            throw new InvalidOperationException("Download timed out. Please check your internet connection.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"HTTP error downloading CryptoMiniSat: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during download: {ex.Message}");
            throw;
        }
    }
}

public class GitHubRelease
{
    public GitHubAsset[]? Assets { get; set; }
}

public class GitHubAsset
{
    public string? Name { get; set; }
    public string? Browser_download_url { get; set; }
}