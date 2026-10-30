using System.IO.Compression;
using MikuSB.Net;
using MikuSB.Util;

namespace MikuSB.Loader;

public static class InGameConsoleDownloadService
{
    private static readonly Logger Logger = new("InGameConsoleDownloader");
    private const string RepositoryOwner = "Kei-Luna";
    private const string RepositoryName = "MikuSB-inGame-GUI-Console";
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

        using var client = GitHubApi.CreateClient("MikuSB-InGameConsoleDownloader");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(DownloadTimeoutSeconds));
        var release = GitHubApi
            .GetLatestReleaseAsync(RepositoryOwner, RepositoryName, client,
                TimeSpan.FromSeconds(DownloadTimeoutSeconds),
                cancellationToken: timeout.Token)
            .GetAwaiter().GetResult()
            ?? throw new InvalidDataException(
                $"Could not read the latest {RepositoryName} release from GitHub or any mirror.");
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
            GitHubApi.DownloadAsync(archive.DownloadUrl, archivePath, new GitHubDownloadOptions
            {
                TimeoutSeconds = DownloadTimeoutSeconds,
                OnAttempt = (host, isMirror, index, total) => Logger.Info(
                    isMirror
                        ? $"GUI console: trying mirror {host} ({index}/{total - 1})."
                        : $"GUI console: trying {host} (direct)."),
                OnAttemptFailed = (host, message) => Logger.Warn($"GUI console from {host} failed: {message}")
            }, client, timeout.Token).GetAwaiter().GetResult();
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
}
