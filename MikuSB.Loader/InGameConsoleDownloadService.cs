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

    /// <summary>
    ///     Budget for the "which release is current" lookup. It runs before the game window exists, so it
    ///     must never be the reason a launch feels stuck: eight of the ten mirrors refuse the API, and a
    ///     slow one used to consume the whole download timeout.
    /// </summary>
    private const int ApiLookupTimeoutSeconds = 12;

    /// <summary>
    ///     How long a completed update check stays trusted. The console changes rarely, and skipping the
    ///     lookup keeps our unauthenticated GitHub quota (60/h per IP, shared on most networks) for the
    ///     things that actually need it.
    /// </summary>
    private const int UpdateCheckIntervalHours = 6;

    /// <summary>Marker text for "installed from <see cref="LatestArchiveUrl" />, tag unknown".</summary>
    private const string UnknownTagMarker = "latest";

    private static readonly string[] ManagedFileNames =
    [
        "nethost.dll",
        "MikuSB.InGameConsole.dll",
        "MikuSB.InGameConsole.deps.json",
        "MikuSB.InGameConsole.runtimeconfig.json"
    ];

    /// <summary>
    ///     The release asset behind GitHub's "latest" redirect. Unlike <c>api.github.com</c> this path is
    ///     served by the mirrors (measured 2026-11: 7 of 10, with no quota), which is the whole reason the
    ///     console can still be installed when the API answers 403.
    /// </summary>
    private static string LatestArchiveUrl =>
        $"https://github.com/{RepositoryOwner}/{RepositoryName}/releases/latest/download/{ArchiveName}";

    public static void EnsurePresent()
    {
        var loaderPath = ResolveLoaderPath();
        var installDirectory = Path.GetDirectoryName(loaderPath)
            ?? throw new InvalidOperationException("Unable to determine the in-game console directory.");
        Directory.CreateDirectory(installDirectory);

        var installed = HasRequiredFiles(installDirectory, loaderPath);
        if (installed && HasFreshUpdateCheck(installDirectory))
            return;

        using var client = GitHubApi.CreateClient("MikuSB-InGameConsoleDownloader");
        using var overall = new CancellationTokenSource(TimeSpan.FromSeconds(DownloadTimeoutSeconds));

        var release = TryGetLatestRelease(client, overall.Token);
        var releaseTag = release?.TagName;
        string downloadUrl;

        if (release is null)
        {
            if (installed)
            {
                // The lookup failed but the console is already on disk: an unreachable update check is
                // not a reason to refuse to start the game.
                Logger.Warn(
                    $"Could not read the latest {RepositoryName} release; keeping the installed copy.");
                return;
            }

            // First install and no API answer. The asset URL needs no quota and works through the
            // mirrors, so the console is still installed instead of failing the whole launch.
            downloadUrl = LatestArchiveUrl;
            releaseTag = UnknownTagMarker;
            Logger.Warn(
                $"Could not read the latest {RepositoryName} release; "
                + $"installing from {LatestArchiveUrl} instead.");
        }
        else
        {
            var asset = release.Assets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, ArchiveName, StringComparison.OrdinalIgnoreCase));
            if (asset is null)
            {
                if (installed)
                {
                    Logger.Warn($"Release {release.TagName} does not contain {ArchiveName}; keeping the installed copy.");
                    return;
                }

                downloadUrl = LatestArchiveUrl;
                releaseTag = UnknownTagMarker;
                Logger.Warn($"Release {release.TagName} does not contain {ArchiveName}; trying {LatestArchiveUrl}.");
            }
            else
            {
                if (IsCurrentRelease(installDirectory, loaderPath, release.TagName))
                {
                    TouchUpdateCheck(installDirectory);
                    return;
                }

                downloadUrl = asset.DownloadUrl;
            }
        }

        var temporaryRoot = Path.Combine(Path.GetTempPath(), "MikuSB", "in-game-console", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporaryRoot);
            var archivePath = Path.Combine(temporaryRoot, ArchiveName);
            var stagingDirectory = Path.Combine(temporaryRoot, "staging");

            Logger.Info($"Downloading GUI console release {releaseTag}.");
            GitHubApi.DownloadAsync(downloadUrl, archivePath, new GitHubDownloadOptions
            {
                TimeoutSeconds = DownloadTimeoutSeconds,
                OnAttempt = (host, isMirror, index, total) => Logger.Info(
                    isMirror
                        ? $"GUI console: trying mirror {host} ({index}/{total - 1})."
                        : $"GUI console: trying {host} (direct)."),
                OnAttemptFailed = (host, message) => Logger.Warn($"GUI console from {host} failed: {message}")
            }, client, overall.Token).GetAwaiter().GetResult();
            ExtractArchive(archivePath, stagingDirectory);
            InstallFiles(stagingDirectory, installDirectory, loaderPath);
            File.WriteAllText(Path.Combine(installDirectory, ReleaseMarkerFileName), releaseTag!);
            Logger.Info($"GUI console release {releaseTag} installed.");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    /// <summary>
    ///     Asks for the current release with its own short deadline, so a mirror that hangs cannot eat the
    ///     download budget. Returns null for every kind of "no answer" - including a timeout.
    /// </summary>
    private static GitHubRelease? TryGetLatestRelease(HttpClient client, CancellationToken outer)
    {
        try
        {
            using var lookup = CancellationTokenSource.CreateLinkedTokenSource(outer);
            lookup.CancelAfter(TimeSpan.FromSeconds(ApiLookupTimeoutSeconds));
            return GitHubApi
                .GetLatestReleaseAsync(RepositoryOwner, RepositoryName, client,
                    TimeSpan.FromSeconds(ApiLookupTimeoutSeconds),
                    onUnavailable: (host, status) =>
                        Logger.Warn($"GUI console release lookup refused by {host}: {status}."),
                    onSourceFailed: (host, message) =>
                        Logger.Warn($"GUI console release lookup from {host} failed: {message}"),
                    cancellationToken: lookup.Token)
                .GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            Logger.Warn($"GUI console release lookup timed out after {ApiLookupTimeoutSeconds}s.");
            return null;
        }
    }

    private static bool HasFreshUpdateCheck(string installDirectory)
    {
        var markerPath = Path.Combine(installDirectory, ReleaseMarkerFileName);
        if (!File.Exists(markerPath))
            return false;

        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(markerPath);
        return age >= TimeSpan.Zero && age < TimeSpan.FromHours(UpdateCheckIntervalHours);
    }

    /// <summary>
    ///     Records "this copy was verified as current" without rewriting the tag, so the next few launches
    ///     can skip the lookup entirely.
    /// </summary>
    private static void TouchUpdateCheck(string installDirectory)
    {
        try
        {
            File.SetLastWriteTimeUtc(Path.Combine(installDirectory, ReleaseMarkerFileName), DateTime.UtcNow);
        }
        catch (Exception)
        {
            // A read-only install is simply re-checked next launch.
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
