using MikuSB.Net;

namespace MikuSB.Util;

public static class PatchDownloadService
{
    private static readonly Logger Logger = new("PatchDownloader");
    private const string PatchRelativePath = @"Patch\MikuSB-Patch.dll";
    private const int DownloadTimeoutSeconds = 120;

    // Overridable so a fork or a LAN mirror can serve its own build without a recompile.
    private static readonly string PatchDownloadUrl =
        Environment.GetEnvironmentVariable("MIKUSB_PATCH_URL")
        ?? "https://github.com/Kei-Luna/MikuSB-Patch/releases/download/MikuSB-Patch/MikuSB-Patch.dll";

    public static void EnsurePatchPresent()
    {
        var patchPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, PatchRelativePath));
        if (File.Exists(patchPath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(patchPath)!);
        Logger.Warn($"Patch DLL not found. Downloading to {patchPath}.");

        // Same mirror list and stall watchdog as the resource archive: a bare github.com URL is
        // unusable on the networks this project targets.
        var options = new GitHubDownloadOptions
        {
            TimeoutSeconds = DownloadTimeoutSeconds,
            OnAttempt = (host, isMirror, index, total) =>
                Logger.Info(isMirror
                    ? $"Patch DLL: trying mirror {host} ({index}/{total - 1})."
                    : $"Patch DLL: trying {host} (direct)."),
            OnAttemptFailed = (host, message) => Logger.Warn($"Patch DLL from {host} failed: {message}"),
            OnSucceeded = (host, isMirror) => Logger.Info(isMirror
                ? $"Patch DLL downloaded through mirror {host}."
                : $"Patch DLL downloaded from {host}.")
        };

        GitHubApi.DownloadAsync(PatchDownloadUrl, patchPath, options).GetAwaiter().GetResult();
        Logger.Info("Patch DLL download completed.");
    }
}
