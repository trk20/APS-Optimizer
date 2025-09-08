using System.IO.Compression;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;

namespace APS_Optimizer_V3.Services;

public class CryptoMiniSatDownloader
{
    private const string GITHUB_API_URL = "https://api.github.com/repos/msoos/cryptominisat/releases/latest";
    private const string WINDOWS_EXE_NAME = "cryptominisat5.exe";
    private const string LINUX_EXE_NAME = "cryptominisat5";

    public static async Task<string> EnsureCryptoMiniSatAvailable()
    {
        bool isWindows = OperatingSystem.IsWindows();
        bool isLinux = OperatingSystem.IsLinux();
        if (!isWindows && !isLinux)
        {
            throw new PlatformNotSupportedException("Only Windows and Linux are supported for automatic CryptoMiniSat download.");
        }

        var arch = RuntimeInformation.ProcessArchitecture;
        string linuxArchSegment = arch switch
        {
            Architecture.X64 => "linux-amd64",
            Architecture.Arm64 => "linux-arm64",
            _ => throw new PlatformNotSupportedException($"Unsupported architecture: {arch}")
        };

        // 2. Allow override via environment variable
        var overridePath = Environment.GetEnvironmentVariable("CRYPTOMINISAT_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath!;
        }

        var appDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
                          ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cryptoMiniSatDir = Path.Combine(appDirectory, "CryptoMiniSat");
        Directory.CreateDirectory(cryptoMiniSatDir);

        string exeName = isWindows ? WINDOWS_EXE_NAME : LINUX_EXE_NAME;
        var exePath = Path.Combine(cryptoMiniSatDir, exeName);

        if (File.Exists(exePath))
        {
            return exePath;
        }

        try
        {
            Console.WriteLine($"CryptoMiniSat not found, downloading for {(isWindows ? "Windows" : "Linux")} {arch}...");
            await DownloadAndExtractCryptoMiniSat(exePath, linuxArchSegment, isWindows, isLinux, exeName);
            return exePath;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to download CryptoMiniSat: {ex.Message}", ex);
        }
    }

    private static async Task DownloadAndExtractCryptoMiniSat(string targetExePath, string linuxArchSegment, bool isWindows, bool isLinux, string exeName)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "trk20/APS-Optimizer");
        httpClient.Timeout = TimeSpan.FromMinutes(5); // 5 minute timeout

        try
        {
            Console.WriteLine("Getting latest release info from GitHub...");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var releaseJson = await httpClient.GetStringAsync(GITHUB_API_URL, cts.Token);
            var releaseInfo = JToken.Parse(releaseJson).ToObject<GitHubRelease>();
            if (releaseInfo?.Assets == null || !releaseInfo.Assets.Any())
            {
                throw new InvalidOperationException("No assets found in latest release");
            }

            Console.WriteLine($"Found {releaseInfo.Assets.Length} assets in latest release");

            GitHubAsset? chosenAsset = null;
            if (isWindows)
            {
                chosenAsset = releaseInfo.Assets.FirstOrDefault(a => !string.IsNullOrEmpty(a.Name) && a.Name.Contains("win", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            }
            else if (isLinux)
            {
                chosenAsset = releaseInfo.Assets.FirstOrDefault(a => !string.IsNullOrEmpty(a.Name) && a.Name.Contains(linuxArchSegment, StringComparison.OrdinalIgnoreCase) && a.Name.Contains("linux", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            }

            if (chosenAsset == null)
            {
                var assetNames = string.Join(", ", releaseInfo.Assets.Select(a => a.Name));
                throw new InvalidOperationException($"No suitable CryptoMiniSat asset found for platform. Available: {assetNames}");
            }

            Console.WriteLine($"Selected asset: {chosenAsset.Name}");
            Console.WriteLine($"Downloading from: {chosenAsset.Browser_download_url}");

            using var downloadCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var bytes = await httpClient.GetByteArrayAsync(chosenAsset.Browser_download_url!, downloadCts.Token);
            using var rawStream = new MemoryStream(bytes);

            await ExtractExecutableFromArchive(rawStream, chosenAsset.Name!, targetExePath, exeName);

            if (OperatingSystem.IsLinux())
            {
                TryMarkExecutable(targetExePath);
            }

            Console.WriteLine($"Successfully extracted {exeName} to {targetExePath}");
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

    private static async Task ExtractExecutableFromArchive(Stream archiveStream, string assetName, string targetExePath, string exeName)
    {
        if (!assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unexpected archive format '{assetName}'. Only .zip assets are supported.");
        }

        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            if (string.Equals(entry.Name, exeName, StringComparison.OrdinalIgnoreCase) ||
                (OperatingSystem.IsWindows() && entry.Name.Equals(WINDOWS_EXE_NAME, StringComparison.OrdinalIgnoreCase)))
            {
                using var exeStream = entry.Open();
                using var fileStream = File.Create(targetExePath);
                await exeStream.CopyToAsync(fileStream);
                return;
            }
        }
        throw new InvalidOperationException($"Executable {exeName} not found inside archive {assetName}.");
    }

    private static void TryMarkExecutable(string path)
    {
        try
        {
#if NET7_0_OR_GREATER
            // Grant rwx for user, rx for group/other
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                       UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                       UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
#endif
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to mark {path} as executable: {ex.Message}");
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