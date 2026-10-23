using System.Net.Http.Headers;

namespace MikuSB.Util;

public static class PatchDownloadService
{
    private static readonly Logger Logger = new("PatchDownloader");
    private const string PatchRelativePath = @"Patch\MikuSB-Patch.dll";
    private const string PatchDownloadUrl = "https://github.com/Kei-Luna/MikuSB-Patch/releases/download/MikuSB-Patch/MikuSB-Patch.dll";
    private const int DownloadTimeoutSeconds = 60;

    public static void EnsurePatchPresent()
    {
        var patchPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, PatchRelativePath));
        if (File.Exists(patchPath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(patchPath)!);
        Logger.Warn($"Patch DLL not found. Downloading to {patchPath}.");

        using var client = CreateHttpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DownloadTimeoutSeconds));
        using var response = client.GetAsync(PatchDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token)
            .GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        using var source = response.Content.ReadAsStreamAsync(cts.Token).GetAwaiter().GetResult();
        using var destination = File.Create(patchPath);
        source.CopyTo(destination);

        Logger.Info("Patch DLL download completed.");
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("MikuSB-PatchDownloader", BuildVersion.Current));

        return client;
    }
}
