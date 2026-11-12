using MikuSB.Net;

namespace MikuSB.Util;

public static class PatchDownloadService
{
    private static readonly Logger Logger = new("PatchDownloader");
    private const string PatchRelativePath = @"Patch\MikuSB-Patch.dll";
    private const string CnPatchRelativePath = @"Patch\MikuSB-Patch-cn.dll";
    private const int DownloadTimeoutSeconds = 120;

    // Both channel builds are assets of the same release: a fresh install gets the global and the CN DLL
    // whichever region the operator picks later, so the launcher never has to say "put the DLL here".
    // MIKUSB_PATCH_URL / MIKUSB_CN_PATCH_URL override the whole list for one channel.
    private static readonly string[] PatchDownloadUrls = BuildUrlList("MikuSB-Patch.dll", "MIKUSB_PATCH_URL");
    private static readonly string[] CnPatchDownloadUrls = BuildUrlList("MikuSB-Patch-cn.dll", "MIKUSB_CN_PATCH_URL");

    private static string[] BuildUrlList(string asset, string overrideVariable)
    {
        var overridden = Environment.GetEnvironmentVariable(overrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
            return [overridden];

        return
        [
            $"https://github.com/dubstream3/MikuSB/releases/download/MikuSB-Patch/{asset}",
            $"https://github.com/dubstream3/MikuSB-Patch/releases/download/MikuSB-Patch/{asset}",
            $"https://github.com/Kei-Luna/MikuSB-Patch/releases/download/MikuSB-Patch/{asset}",
        ];
    }

    /// <summary>
    /// Makes sure both channel builds sit next to the executable. Called when the resource archive is
    /// fetched (so the DLLs always arrive together with the resources, never as a "please put it here"
    /// dialog) and again right before the game is launched.
    /// </summary>
    public static void EnsureAllPresent()
    {
        EnsurePatchPresent();
        EnsureCnPatchPresent();
    }

    /// <summary>Global-channel build, injected by the loader and by the CN path when it falls back to it.</summary>
    public static bool EnsurePatchPresent() => EnsurePresent(PatchRelativePath, PatchDownloadUrls, "global");

    /// <summary>CN-channel build, injected by <c>/game-cn -dll</c> and by <c>Loader.CnLaunchMode=dll</c>.</summary>
    public static bool EnsureCnPatchPresent() => EnsurePresent(CnPatchRelativePath, CnPatchDownloadUrls, "cn");

    private static bool EnsurePresent(string relativePath, string[] urls, string channel)
    {
        var patchPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath));
        if (File.Exists(patchPath))
            return true;

        Directory.CreateDirectory(Path.GetDirectoryName(patchPath)!);
        Logger.Warn($"Patch DLL ({channel}) not found. Downloading to {patchPath}.");

        // Same mirror list and stall watchdog as the resource archive: a bare github.com URL is
        // unusable on the networks this project targets.
        var options = new GitHubDownloadOptions
        {
            TimeoutSeconds = DownloadTimeoutSeconds,
            OnAttempt = (host, isMirror, index, total) =>
                Logger.Info(isMirror
                    ? $"Patch DLL ({channel}): trying mirror {host} ({index}/{total - 1})."
                    : $"Patch DLL ({channel}): trying {host} (direct)."),
            OnAttemptFailed = (host, message) => Logger.Warn($"Patch DLL ({channel}) from {host} failed: {message}"),
            OnSucceeded = (host, isMirror) => Logger.Info(isMirror
                ? $"Patch DLL ({channel}) downloaded through mirror {host}."
                : $"Patch DLL ({channel}) downloaded from {host}.")
        };

        Exception? last = null;
        foreach (var url in urls)
        {
            try
            {
                GitHubApi.DownloadAsync(url, patchPath, options).GetAwaiter().GetResult();
                if (File.Exists(patchPath))
                {
                    Logger.Info($"Patch DLL ({channel}) download completed from {url}.");
                    return true;
                }
            }
            catch (Exception e)
            {
                last = e;
                Logger.Warn($"Patch DLL ({channel}) download from {url} failed: {e.Message}");
            }
        }

        Logger.Error($"Every patch DLL source failed for the {channel} channel.", last);
        return false;
    }
}
