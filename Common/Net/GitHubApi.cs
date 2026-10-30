using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using MikuSB.Util;

namespace MikuSB.Net;

/// <summary>
///     One asset attached to a GitHub release, as returned by the releases API.
/// </summary>
public sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

/// <summary>
///     The subset of a GitHub release this server cares about.
/// </summary>
public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = [];
}

public enum GitHubDownloadFailure
{
    /// <summary>The attempt never produced an HTTP response inside the header budget.</summary>
    NoResponse,

    /// <summary>The body stopped moving for a whole progress window.</summary>
    Stalled,

    /// <summary>The overall per-source deadline expired.</summary>
    TimedOut,

    /// <summary>The source answered with a non-success status.</summary>
    Http,

    /// <summary>The source answered 200 but sent no bytes.</summary>
    Empty,

    /// <summary>Anything else (DNS, TLS, socket).</summary>
    Other
}

/// <summary>
///     Carries the reason a single download source was dropped, so the caller can keep its own
///     localized wording instead of parsing exception messages.
/// </summary>
public sealed class GitHubDownloadException : Exception
{
    public GitHubDownloadException(GitHubDownloadFailure failure, string host, string message, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
        Host = host;
    }

    public GitHubDownloadFailure Failure { get; }
    public string Host { get; }
}

/// <summary>
///     Everything the download loop needs to report back. Callbacks are optional: the caller decides
///     how a mirror attempt or a progress tick should be phrased.
/// </summary>
public sealed class GitHubDownloadOptions
{
    public int TimeoutSeconds { get; init; } = 300;
    public int HeaderTimeoutSeconds { get; init; } = 10;
    public int WindowSeconds { get; init; } = 10;
    public long WindowBytes { get; init; } = 1024 * 1024;
    public int DirectAttempts { get; init; } = 1;

    /// <summary>(host, isMirror, index, total) before an attempt starts.</summary>
    public Action<string, bool, int, int>? OnAttempt { get; init; }

    /// <summary>(host, bytes, totalBytes) roughly every two seconds; total is 0 when unknown.</summary>
    public Action<string, long, long>? OnProgress { get; init; }

    /// <summary>(host, reason) when a source is dropped.</summary>
    public Action<string, string>? OnAttemptFailed { get; init; }

    /// <summary>(host, isMirror) once a source delivered the whole file.</summary>
    public Action<string, bool>? OnSucceeded { get; init; }
}

/// <summary>
///     The single place that talks to GitHub. Release lookups, the resource archive, the patch DLL and
///     the in-game console bundle all go through here, so the mirror list and the stall watchdog apply
///     everywhere instead of only to whichever downloads happened to grow their own copy.
/// </summary>
public static class GitHubApi
{
    private static readonly Logger Logger = new("GitHub");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Lazy<string[]> LazyMirrors = new(ResolveMirrors);

    /// <summary>
    ///     Reverse proxies that serve <c>https://&lt;mirror&gt;/https://github.com/...</c> verbatim.
    ///     Override with <c>MIKUSB_GITHUB_MIRRORS</c> (semicolon separated); the older
    ///     <c>MIKUSB_RESOURCE_MIRRORS</c> name still works so existing deployments keep their list.
    /// </summary>
    public static IReadOnlyList<string> Mirrors => LazyMirrors.Value;

    /// <summary>
    ///     api.github.com, unchanged. The release lookup is only a fallback for repositories we do not
    ///     control (the in-game console bundle); our own update check reads version.txt instead, which
    ///     needs no API quota and no token.
    /// </summary>
    private const string ApiBaseUrl = "https://api.github.com";

    public static HttpClient CreateClient(string productName)
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(productName, BuildVersion.Current));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>
    ///     The direct URL first (when <paramref name="directAttempts" /> is 1), then every mirror, which
    ///     is exactly the order the download loop walks.
    /// </summary>
    public static List<string> Candidates(string url, int directAttempts = 1)
    {
        var candidates = new List<string>(Math.Max(0, directAttempts) + Mirrors.Count);
        for (var attempt = 0; attempt < directAttempts; attempt++)
            candidates.Add(url);

        foreach (var mirror in Mirrors)
            candidates.Add(mirror + url);

        return candidates;
    }

    /// <summary>
    ///     Latest published release, or null when no source could answer. A 404/401/403 from the first
    ///     source that reached GitHub is reported through <paramref name="onUnavailable" /> and ends the
    ///     search: mirrors proxy the same API, so they would only repeat the answer.
    /// </summary>
    public static async Task<GitHubRelease?> GetLatestReleaseAsync(
        string owner,
        string repository,
        HttpClient client,
        TimeSpan timeout,
        Action<string, string>? onUnavailable = null,
        Action<string, string>? onSourceFailed = null,
        CancellationToken cancellationToken = default)
    {
        var apiUrl = $"{ApiBaseUrl}/repos/{owner}/{repository}/releases/latest";

        foreach (var candidate in Candidates(apiUrl))
        {
            var host = new Uri(candidate).Host;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);
                using var response = await client.GetAsync(candidate, timeoutCts.Token);

                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden)
                {
                    onUnavailable?.Invoke(host, response.StatusCode.ToString());
                    return null;
                }

                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
                var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, timeoutCts.Token);
                if (release != null)
                    return release;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                onSourceFailed?.Invoke(host, ex.Message);
            }
        }

        return null;
    }

    /// <summary>
    ///     Downloads <paramref name="url" /> to <paramref name="destinationPath" />, walking the direct
    ///     source and then the mirrors. Each source gets <see cref="GitHubDownloadOptions.TimeoutSeconds" />
    ///     overall and has to move <see cref="GitHubDownloadOptions.WindowBytes" /> inside every
    ///     <see cref="GitHubDownloadOptions.WindowSeconds" /> window, which also catches a connection that
    ///     answers the handshake and then goes silent.
    /// </summary>
    public static async Task DownloadAsync(
        string url,
        string destinationPath,
        GitHubDownloadOptions options,
        HttpClient? client = null,
        CancellationToken cancellationToken = default)
    {
        var owned = client == null;
        client ??= CreateClient("MikuSB-Downloader");

        try
        {
            var candidates = Candidates(url, options.DirectAttempts);
            Exception? lastError = null;

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var host = new Uri(candidate).Host;
                var isMirror = index >= options.DirectAttempts;
                options.OnAttempt?.Invoke(host, isMirror, index, candidates.Count);

                try
                {
                    await DownloadOnceAsync(client, candidate, destinationPath, options, host, cancellationToken);
                    options.OnSucceeded?.Invoke(host, isMirror);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    options.OnAttemptFailed?.Invoke(host, ex.Message);
                }
            }

            throw lastError ?? new IOException($"Failed to download {url}.");
        }
        finally
        {
            if (owned)
                client.Dispose();
        }
    }

    /// <summary>
    ///     Fetches a small text file (version.txt) through the mirror list. No temp file and no progress
    ///     plumbing: a version string is a few bytes, so this walks the candidates until one answers.
    /// </summary>
    public static async Task<string?> ReadTextAsync(
        string url,
        GitHubDownloadOptions? options = null,
        HttpClient? client = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new GitHubDownloadOptions();
        var owned = client == null;
        client ??= CreateClient("MikuSB-Updater");

        try
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
            foreach (var candidate in Candidates(url, options.DirectAttempts))
            {
                var host = new Uri(candidate).Host;
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(timeout);
                    using var response = await client.GetAsync(candidate, HttpCompletionOption.ResponseContentRead,
                        timeoutCts.Token);
                    if (!response.IsSuccessStatusCode)
                        throw new GitHubDownloadException(GitHubDownloadFailure.Http, host,
                            $"{host} answered {(int)response.StatusCode} {response.StatusCode}.");

                    var text = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                    if (string.IsNullOrWhiteSpace(text))
                        throw new GitHubDownloadException(GitHubDownloadFailure.Empty, host, $"{host} returned an empty file.");

                    options.OnSucceeded?.Invoke(host, true);
                    return text;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    options.OnAttemptFailed?.Invoke(host, ex.Message);
                    Logger.Debug($"Could not fetch {candidate}: {ex.Message}");
                }
            }

            return null;
        }
        finally
        {
            if (owned)
                client.Dispose();
        }
    }

    private static async Task DownloadOnceAsync(
        HttpClient client,
        string downloadUrl,
        string destinationPath,
        GitHubDownloadOptions options,
        string host,
        CancellationToken cancellationToken)
    {
        var timeoutSeconds = Math.Max(1, options.TimeoutSeconds);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);
        using var watchdogStop = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);

        var progress = new ProgressState(host);
        var buffer = ArrayPool<byte>.Shared.Rent(81_920);
        Task? watchdog = null;

        try
        {
            using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(attemptCts.Token);
            headerCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.HeaderTimeoutSeconds)));

            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, headerCts.Token);
            }
            catch (OperationCanceledException) when (!timeoutCts.IsCancellationRequested
                                                      && !cancellationToken.IsCancellationRequested)
            {
                throw new GitHubDownloadException(GitHubDownloadFailure.NoResponse, host,
                    $"{host} sent no response within {options.HeaderTimeoutSeconds}s.");
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                    throw new GitHubDownloadException(GitHubDownloadFailure.Http, host,
                        $"{host} answered {(int)response.StatusCode} {response.StatusCode}.");

                progress.TotalBytes = response.Content.Headers.ContentLength ?? 0;
                progress.Restart();
                if (options.OnProgress != null)
                    options.OnProgress(host, 0, progress.TotalBytes);

                watchdog = WatchAsync(progress, options, attemptCts, watchdogStop.Token);

                await using var source = await response.Content.ReadAsStreamAsync(attemptCts.Token);
                await using var destination = File.Create(destinationPath);

                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(), attemptCts.Token)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), attemptCts.Token);
                    progress.Add(read);
                    options.OnProgress?.Invoke(host, progress.Bytes, progress.TotalBytes);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new GitHubDownloadException(GitHubDownloadFailure.TimedOut, host,
                $"{host} did not finish within {timeoutSeconds}s.");
        }
        catch (OperationCanceledException)
        {
            throw new GitHubDownloadException(progress.StallKind, host,
                progress.Stall ?? $"{host} stopped sending data.");
        }
        finally
        {
            watchdogStop.Cancel();
            ArrayPool<byte>.Shared.Return(buffer);
            if (watchdog != null)
            {
                try
                {
                    await watchdog;
                }
                catch (OperationCanceledException)
                {
                    // Expected: the watchdog ends on the stop token.
                }
            }
        }

        if (progress.Bytes == 0)
            throw new GitHubDownloadException(GitHubDownloadFailure.Empty, host, $"{host} returned an empty body.");
    }

    private static async Task WatchAsync(
        ProgressState progress,
        GitHubDownloadOptions options,
        CancellationTokenSource attemptCts,
        CancellationToken stopToken)
    {
        var lastBytes = 0L;
        try
        {
            while (!stopToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.WindowSeconds)), stopToken);

                var current = progress.Bytes;
                var windowBytes = current - lastBytes;
                if (windowBytes < options.WindowBytes)
                {
                    progress.StallKind = GitHubDownloadFailure.Stalled;
                    progress.Stall =
                        $"{progress.Host} moved only {FormatSize(windowBytes)} in {options.WindowSeconds}s "
                        + $"(needs {FormatSize(options.WindowBytes)}).";
                    attemptCts.Cancel();
                    return;
                }

                lastBytes = current;
            }
        }
        catch (OperationCanceledException)
        {
            // Finished or unwinding; the caller already has the outcome.
        }
        catch (ObjectDisposedException)
        {
            // Same as above.
        }
    }

    private static string FormatSize(long bytes)
        => bytes >= 1024L * 1024
            ? (bytes / 1024d / 1024d).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " MB"
            : (bytes / 1024d).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " KB";

    private sealed class ProgressState(string host)
    {
        private readonly Stopwatch _stopwatch = new();
        private long _bytes;

        public string Host { get; } = host;
        public long TotalBytes { get; set; }
        public long Bytes => Interlocked.Read(ref _bytes);
        public string? Stall { get; set; }
        public GitHubDownloadFailure StallKind { get; set; } = GitHubDownloadFailure.Stalled;

        public void Restart() => _stopwatch.Restart();

        public void Add(int count) => Interlocked.Add(ref _bytes, count);
    }

    private static string[] ResolveMirrors()
    {
        var configured = Environment.GetEnvironmentVariable("MIKUSB_GITHUB_MIRRORS")
                         ?? Environment.GetEnvironmentVariable("MIKUSB_RESOURCE_MIRRORS");
        var mirrors = configured?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (mirrors is not { Length: > 0 })
            mirrors = DefaultMirrors;

        var resolved = mirrors.Select(mirror => mirror.EndsWith('/') ? mirror : mirror + "/").ToArray();
        Logger.Debug($"GitHub mirrors: {resolved.Length}");
        return resolved;
    }

    /// <summary>
    ///     Probed from the maintainer's machine on 2026-10-24 through a direct connection: each entry
    ///     completed a full 34 MB archive with a valid zip header. The ~30 sites dropped from the
    ///     original list were dead, DNS-invalid, rate limited (429) or returned a non-archive body.
    /// </summary>
    private static readonly string[] DefaultMirrors =
    [
        "https://git.669966.xyz/",
        "https://fastgit.cc/",
        "https://github.ednovas.xyz/",
        "https://cdn.akaere.online/",
        "https://gh-proxy.com/",
        "https://ghproxy.imciel.com/",
        "https://ghpxy.hwinzniej.top/",
        "https://git.yylx.win/",
        "https://gh.catmak.name/",
        "https://tvv.tw/"
    ];
}
