using MikuSB.Net;

namespace MikuSB.Util;

public static class PatchDownloadService
{
    private static readonly Logger Logger = new("PatchDownloader");
    private const string PatchRelativePath = @"Patch\MikuSB-Patch.dll";
    private const int DownloadTimeoutSeconds = 120;

    // Public builds fetch the DLL from this project's own public repository release; the legacy
    // Kei-Luna location stays behind it because that namespace has been answering HTTP 504. An
    // internal (dev) build ships one locally instead, so the first File.Exists short-circuits both.
    // MIKUSB_PATCH_URL overrides the whole list.
    private static readonly string[] PatchDownloadUrls = BuildUrlList();

    private static string[] BuildUrlList()
    {
        var overridden = Environment.GetEnvironmentVariable("MIKUSB_PATCH_URL");
        if (!string.IsNullOrWhiteSpace(overridden))
            return [overridden];

        return
        [
            "https://github.com/dubstream3/MikuSB/releases/download/MikuSB-Patch/MikuSB-Patch.dll",
            "https://github.com/dubstream3/MikuSB-Patch/releases/download/MikuSB-Patch/MikuSB-Patch.dll",
            "https://github.com/Kei-Luna/MikuSB-Patch/releases/download/MikuSB-Patch/MikuSB-Patch.dll",
        ];
    }

    /// <summary>
    /// Makes sure Patch/MikuSB-Patch.dll exists next to the executable. Called when the resource
    /// archive is fetched (so the DLL always arrives together with the resources, never as a "please
    /// put it here" dialog) and again right before the game is launched.
    /// </summary>
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

        Exception? last = null;
        foreach (var url in PatchDownloadUrls)
        {
            try
            {
                GitHubApi.DownloadAsync(url, patchPath, options).GetAwaiter().GetResult();
                if (File.Exists(patchPath))
                {
                    Logger.Info($"Patch DLL download completed from {url}.");
                    return;
                }
            }
            catch (Exception e)
            {
                last = e;
                Logger.Warn($"Patch DLL download from {url} failed: {e.Message}");
            }
        }

        Logger.Error("Every patch DLL source failed. The launcher cannot inject without Patch/MikuSB-Patch.dll.", last);
    }
}
