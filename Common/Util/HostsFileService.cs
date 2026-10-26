using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace MikuSB.Util;

public enum HostsSyncStatus
{
    Applied,
    AlreadyCurrent,
    NotElevated,
    Unsupported,
    Failed
}

public sealed class HostsSyncResult
{
    private HostsSyncResult(HostsSyncStatus status, string detail = "")
    {
        Status = status;
        Detail = detail;
    }

    public HostsSyncStatus Status { get; }
    public string Detail { get; }
    public bool Ok => Status is HostsSyncStatus.Applied or HostsSyncStatus.AlreadyCurrent;

    public static HostsSyncResult Applied() => new(HostsSyncStatus.Applied);
    public static HostsSyncResult AlreadyCurrent() => new(HostsSyncStatus.AlreadyCurrent);
    public static readonly HostsSyncResult NotElevated = new(HostsSyncStatus.NotElevated);
    public static readonly HostsSyncResult Unsupported = new(HostsSyncStatus.Unsupported);
    public static HostsSyncResult Failed(string detail) => new(HostsSyncStatus.Failed, detail);
}

/// <summary>
///     Keeps the specific-channel SDK host names pointed at this machine while the server runs.
///     Everything lives inside a fenced block, so entries the user added by hand are never touched.
/// </summary>
public static class HostsFileService
{
    private const string BeginMarker = "# >>> MikuSB specific channel (managed) >>>";
    private const string EndMarker = "# <<< MikuSB specific channel <<<";
    private const string RedirectSuffix = "# MikuSB-CN-REDIRECT";

    // Blocks written by the first generation of the redirect script (hosts-cn-sdk.ps1) use their own
    // fence text, and they are still around on machines that were set up before the server managed the
    // file itself. Restore() has to recognise them, otherwise the cleanup claims there is nothing to do.
    private const string LegacyBeginMarker = "# MikuSB-CN-REDIRECT begin";
    private const string LegacyEndMarker = "# MikuSB-CN-REDIRECT end";

    /// <summary>
    ///     Every name the client has to resolve locally before it can log in. Missing one of them
    ///     either stalls the handshake or leaves the client without a server list, so the list lives
    ///     in one place and is written verbatim.
    /// </summary>
    public static IReadOnlyList<(string Address, string Host)> Entries { get; } =
    [
        ("127.0.0.1", "sh-jxsj.xgsdk.com"),
        ("127.0.0.1", "xgdata.xgsdk.com"),
        ("127.0.0.1", "js2sdk.xoyo.com"),
        ("127.0.0.1", "sh-qrcode.xgsdk.com"),
        ("127.0.0.1", "activity.xgsdk.com"),
        ("127.0.0.1", "gpm-conf.testplus.cn"),
        ("127.0.0.1", "cbjq-serverlist.xoyocdn.com")
    ];

    public static bool IsSupported => OperatingSystem.IsWindows();

    public static string HostsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    public static bool IsElevated
    {
        get
        {
            if (!IsSupported)
                return false;

            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public static string BuildBlock(IEnumerable<(string Address, string Host)>? entries = null)
    {
        var rows = (entries ?? Entries).ToList();
        var builder = new StringBuilder();
        builder.AppendLine(BeginMarker);
        builder.AppendLine("# Managed by MikuSB; removed again when the server shuts down cleanly.");
        foreach (var (address, host) in rows)
            builder.Append(address).Append(' ').Append(host).Append(' ').Append(RedirectSuffix).AppendLine();
        builder.Append(EndMarker);
        return builder.ToString();
    }

    /// <summary>
    ///     Writes the managed block (idempotent). Throws nothing: callers decide what to report.
    /// </summary>
    public static HostsSyncResult Ensure()
    {
        if (!IsSupported)
            return HostsSyncResult.Unsupported;

        try
        {
            var original = File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : "";
            if (HasDecodingDamage(original))
                return HostsSyncResult.Failed("the hosts file is not valid UTF-8; refusing to rewrite it");

            var newLine = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = SplitLines(original);
            var outside = StripManaged(lines);
            var existing = ManagedLines(lines);

            // Whatever the user already redirected by hand counts as done: writing a second copy
            // would only make the file harder to read, and removing it afterwards would be rude.
            var mapped = MappedHosts(outside);
            var wanted = Entries.Where(entry => !mapped.Contains(entry.Host)).ToList();

            if (wanted.Count == 0)
            {
                if (existing.Count == 0)
                    return HostsSyncResult.AlreadyCurrent();

                File.WriteAllText(HostsPath, Join(outside, newLine), new UTF8Encoding(false));
                return HostsSyncResult.Applied();
            }

            var expected = wanted.Select(entry => $"{entry.Address} {entry.Host} {RedirectSuffix}");
            if (NormalizeManaged(existing).SequenceEqual(expected))
                return HostsSyncResult.AlreadyCurrent();

            var desired = BuildBlock(wanted).ReplaceLineEndings(newLine);

            var builder = new StringBuilder();
            if (outside.Count > 0)
            {
                builder.Append(string.Join(newLine, outside).TrimEnd('\r', '\n'));
                builder.Append(newLine).Append(newLine);
            }

            builder.Append(desired).Append(newLine);
            File.WriteAllText(HostsPath, builder.ToString(), new UTF8Encoding(false));
            return HostsSyncResult.Applied();
        }
        catch (UnauthorizedAccessException)
        {
            return HostsSyncResult.NotElevated;
        }
        catch (Exception e)
        {
            return HostsSyncResult.Failed(e.Message);
        }
    }

    /// <summary>
    ///     Removes the managed block, restoring the file to what it was before the server touched it.
    /// </summary>
    public static HostsSyncResult Restore()
    {
        if (!IsSupported)
            return HostsSyncResult.Unsupported;

        try
        {
            if (!File.Exists(HostsPath))
                return HostsSyncResult.AlreadyCurrent();

            var original = File.ReadAllText(HostsPath);
            if (HasDecodingDamage(original))
                return HostsSyncResult.AlreadyCurrent();

            var newLine = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = SplitLines(original);
            var stripped = StripManaged(lines);
            if (stripped.Count == lines.Count)
                return HostsSyncResult.AlreadyCurrent();

            File.WriteAllText(HostsPath, Join(stripped, newLine), new UTF8Encoding(false));
            return HostsSyncResult.Applied();
        }
        catch (UnauthorizedAccessException)
        {
            return HostsSyncResult.NotElevated;
        }
        catch (Exception e)
        {
            return HostsSyncResult.Failed(e.Message);
        }
    }

    /// <summary>
    ///     Drops the resolver cache so entries written a second ago take effect for the next launch.
    /// </summary>
    public static void FlushDnsCache()
    {
        if (!IsSupported)
            return;

        try
        {
            using var process = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            process?.WaitForExit(5000);
        }
        catch
        {
            // best effort only
        }
    }

    private static bool HasDecodingDamage(string text) => text.Contains('\uFFFD');

    private static List<string> SplitLines(string text) =>
        text.Length == 0 ? [] : text.ReplaceLineEndings("\n").Split('\n').ToList();

    private static bool IsBegin(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Equals(BeginMarker, StringComparison.Ordinal) ||
               trimmed.StartsWith(LegacyBeginMarker, StringComparison.Ordinal);
    }

    private static bool IsEnd(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Equals(EndMarker, StringComparison.Ordinal) ||
               trimmed.Equals(LegacyEndMarker, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Collects the names that already point at this machine, ignoring our own block.
    /// </summary>
    private static HashSet<string> MappedHosts(IEnumerable<string> lines)
    {
        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var comment = line.IndexOf('#');
            var body = comment >= 0 ? line[..comment] : line;
            var parts = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0] == "127.0.0.1")
                mapped.Add(parts[1]);
        }

        return mapped;
    }

    /// <summary>
    ///     Serialises host lines back out; an empty file stays empty instead of becoming a lone newline.
    /// </summary>
    private static string Join(List<string> lines, string newLine)
    {
        var text = string.Join(newLine, lines).TrimEnd('\r', '\n');
        return text.Length == 0 ? "" : text + newLine;
    }

    /// <summary>
    ///     The lines that live between the fence markers, whichever generation of markers they use.
    /// </summary>
    private static List<string> ManagedLines(List<string> lines)
    {
        var result = new List<string>();
        var inside = false;
        foreach (var line in lines)
        {
            if (!inside && IsBegin(line))
            {
                inside = true;
                continue;
            }

            if (!inside)
                continue;

            if (IsEnd(line))
            {
                inside = false;
                continue;
            }

            result.Add(line);
        }

        return result;
    }

    /// <summary>
    ///     Drops every fenced block (all generations), plus any redirect line whose fence was removed by
    ///     hand, and keeps everything else in order.
    /// </summary>
    private static List<string> StripManaged(List<string> lines)
    {
        var result = new List<string>(lines.Count);
        var inside = false;
        foreach (var line in lines)
        {
            if (!inside && IsBegin(line))
            {
                inside = true;
                continue;
            }

            if (inside)
            {
                if (IsEnd(line))
                    inside = false;
                continue;
            }

            if (line.TrimEnd().EndsWith(RedirectSuffix, StringComparison.Ordinal))
                continue;

            result.Add(line);
        }

        return result;
    }

    /// <summary>
    ///     What <see cref="Ensure" /> compares against: comments and blank lines dropped, so the two
    ///     marker generations and the entries they carry can be compared line by line.
    /// </summary>
    private static IEnumerable<string> NormalizeManaged(IEnumerable<string> lines) =>
        lines.Select(line => line.TrimEnd())
            .Where(line => line.Length > 0 && line[0] != '#');
}
