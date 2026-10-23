using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using MikuSB.Util;

namespace MikuSB.Loader;

public static class InGameConsoleDownloadService
{
    private static readonly Logger Logger = new("InGameConsoleDownloader");
    private const string Repository = "Kei-Luna/MikuSB-inGame-GUI-Console";
    private const string ReleaseApiUrl = $"https://api.github.com/repos/{Repository}/releases/latest";
    private const string ArchiveName = "MikuSB-InGame-GUI-Console.zip";
    private const string LoaderFileName = "MikuSB-InGame-GUI-Console.Loader.dll";
    private const string ReleaseMarkerFileName = "MikuSB-InGame-GUI-Console.release";
    private const int DownloadTimeoutSeconds = 300;

    private static readonly string[] ManagedFileNames =
    [
        "nethost.dll",
        "MikuSB.InGameConsole.dll",
        "MikuSB.InGameConsole.deps.json",
        "MikuSB.InGameConsole.runtimeconfig.json"
    ];

    public static void EnsurePresent()
    {
        var loaderPath = ResolveLoaderPath();
        var installDirectory = Path.GetDirectoryName(loaderPath)
            ?? throw new InvalidOperationException("Unable to determine the in-game console directory.");
        Directory.CreateDirectory(installDirectory);

        using var client = CreateHttpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(DownloadTimeoutSeconds));
        var release = GetLatestReleaseAsync(client, timeout.Token).GetAwaiter().GetResult();
        var archive = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, ArchiveName, StringComparison.OrdinalIgnoreCase));
        if (archive is null)
            throw new InvalidDataException($"Release {release.TagName} does not contain {ArchiveName}.");

        if (IsCurrentRelease(installDirectory, loaderPath, release.TagName))
            return;

        var temporaryRoot = Path.Combine(Path.GetTempPath(), "MikuSB", "in-game-console", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporaryRoot);
            var archivePath = Path.Combine(temporaryRoot, ArchiveName);
            var stagingDirectory = Path.Combine(temporaryRoot, "staging");

            Logger.Info($"Downloading GUI console release {release.TagName}.");
            DownloadFileAsync(client, archive.DownloadUrl, archivePath, timeout.Token).GetAwaiter().GetResult();
            ExtractArchive(archivePath, stagingDirectory);
            InstallFiles(stagingDirectory, installDirectory, loaderPath);
            File.WriteAllText(Path.Combine(installDirectory, ReleaseMarkerFileName), release.TagName);
            Logger.Info($"GUI console release {release.TagName} installed.");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static string ResolveLoaderPath()
    {
        var configuredPath = ConfigManager.Config.Loader.InGameConsoleLoaderPath;
        if (Path.IsPathRooted(configuredPath))
            return Path.GetFullPath(configuredPath);

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
    }

    private static bool IsCurrentRelease(string installDirectory, string loaderPath, string releaseTag)
    {
        if (!HasRequiredFiles(installDirectory, loaderPath))
            return false;

        var markerPath = Path.Combine(installDirectory, ReleaseMarkerFileName);
        return File.Exists(markerPath)
            && string.Equals(File.ReadAllText(markerPath).Trim(), releaseTag, StringComparison.Ordinal);
    }

    private static bool HasRequiredFiles(string installDirectory, string loaderPath)
    {
        if (!File.Exists(loaderPath))
            return false;

        return ManagedFileNames.All(fileName => File.Exists(Path.Combine(installDirectory, fileName)));
    }

    private static void ExtractArchive(string archivePath, string stagingDirectory)
    {
        Directory.CreateDirectory(stagingDirectory);
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(stagingDirectory, entry.FullName));
            if (!destinationPath.StartsWith(stagingDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Archive entry escapes staging directory: {entry.FullName}");
        }

        ZipFile.ExtractToDirectory(archivePath, stagingDirectory, overwriteFiles: true);
    }

    private static void InstallFiles(string stagingDirectory, string installDirectory, string loaderPath)
    {
        var sourceLoaderPath = Path.Combine(stagingDirectory, LoaderFileName);
        if (!File.Exists(sourceLoaderPath))
            throw new FileNotFoundException("The GUI console archive does not contain the loader DLL.", sourceLoaderPath);

        var targetFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [sourceLoaderPath] = loaderPath
        };
        foreach (var fileName in ManagedFileNames)
            targetFiles[Path.Combine(stagingDirectory, fileName)] = Path.Combine(installDirectory, fileName);

        foreach (var pair in targetFiles)
        {
            if (!File.Exists(pair.Key))
                throw new FileNotFoundException("The GUI console archive is missing a required file.", pair.Key);
            File.Copy(pair.Key, pair.Value, overwrite: true);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("MikuSB-InGameConsoleDownloader", BuildVersion.Current));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static async Task<GitHubReleaseResponse> GetLatestReleaseAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(ReleaseApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<GitHubReleaseResponse>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("GitHub returned an empty release response.");
    }

    private static async Task DownloadFileAsync(
        HttpClient client,
        string downloadUrl,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAssetResponse> Assets { get; set; } = [];
    }

    private sealed class GitHubReleaseAssetResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; set; } = "";
    }
}
