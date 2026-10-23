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

    public static IReadOnlyList<(string Address, string Host)> Entries { get; } =
    [
        ("127.0.0.1", "js2sdk.xoyo.com"),
        ("127.0.0.1", "sh-jxsj.xgsdk.com"),
        ("127.0.0.1", "sh-qrcode.xgsdk.com"),
        ("127.0.0.1", "xgdata.xgsdk.com"),
        ("127.0.0.1", "gpm-conf.testplus.cn")
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

    public static string BuildBlock()
    {
        var builder = new StringBuilder();
        builder.AppendLine(BeginMarker);
        builder.AppendLine("# Managed by MikuSB; removed again when the server shuts down cleanly.");
        foreach (var (address, host) in Entries)
            builder.Append(address).Append('\t').Append(host).AppendLine();
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
            var desired = BuildBlock().ReplaceLineEndings(newLine);

            if (GetManagedBlock(lines, newLine) == desired)
                return HostsSyncResult.AlreadyCurrent();

            var stripped = StripManaged(lines);
            var builder = new StringBuilder();
            if (stripped.Count > 0)
            {
                builder.Append(string.Join(newLine, stripped).TrimEnd('\r', '\n'));
                builder.Append(newLine).Append(newLine);
            }

            builder.Append(desired.ReplaceLineEndings(newLine)).Append(newLine);
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
            if (GetManagedBlock(lines, newLine) is null)
                return HostsSyncResult.AlreadyCurrent();

            var stripped = StripManaged(lines);
            var text = stripped.Count == 0
                ? ""
                : string.Join(newLine, stripped).TrimEnd('\r', '\n') + newLine;

            File.WriteAllText(HostsPath, text, new UTF8Encoding(false));
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

    private static bool IsBegin(string line) => line.Trim().Equals(BeginMarker, StringComparison.Ordinal);

    private static bool IsEnd(string line) => line.Trim().Equals(EndMarker, StringComparison.Ordinal);

    private static string? GetManagedBlock(List<string> lines, string newLine)
    {
        var begin = lines.FindIndex(IsBegin);
        if (begin < 0)
            return null;

        var end = lines.FindIndex(begin + 1, IsEnd);
        if (end < 0)
            return null;

        return string.Join(newLine, lines.Skip(begin).Take(end - begin + 1));
    }

    private static List<string> StripManaged(List<string> lines)
    {
        var begin = lines.FindIndex(IsBegin);
        if (begin < 0)
            return lines;

        var end = lines.FindIndex(begin + 1, IsEnd);
        if (end < 0)
            return lines;

        var result = lines.Take(begin).ToList();
        var rest = lines.Skip(end + 1).ToList();
        while (rest.Count > 0 && string.IsNullOrWhiteSpace(rest[0]))
            rest.RemoveAt(0);

        result.AddRange(rest);
        return result;
    }
}
