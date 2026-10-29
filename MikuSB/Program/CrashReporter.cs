using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace MikuSB.MikuSB.Program;

/// <summary>
///     Turns a startup crash into something a double-click user can actually read.
/// </summary>
/// <remarks>
///     When the console window belongs to the apphost (double-click in Explorer) it disappears together
///     with the process, so the stack trace is gone before anyone reads it. This writes a crash log next
///     to the executable, prints it, and pops a message box with the first line plus the reason guess.
///     The dialog can be suppressed with <c>MIKUSB_CRASH_UI=0</c> (used by tests and by service hosts).
/// </remarks>
public static class CrashReporter
{
    private const uint MessageIconError = 0x00000010;
    private const string DialogToggleVariable = "MIKUSB_CRASH_UI";

    public static int Report(Exception exception, string phase)
    {
        var report = BuildReport(exception, phase);
        var logPath = TryWriteLog(report);

        TryWriteConsole(report);
        ShowDialog(exception, phase, logPath);

        return 1;
    }

    private static string BuildReport(Exception exception, string phase)
    {
        var builder = new StringBuilder();
        builder.AppendLine("MikuSB startup failure");
        builder.AppendLine("======================");
        builder.AppendLine($"time     : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");
        builder.AppendLine($"phase    : {phase}");
        builder.AppendLine($"version  : {BuildVersion()}");
        builder.AppendLine($"exe      : {Environment.ProcessPath}");
        builder.AppendLine($"cwd      : {Environment.CurrentDirectory}");
        builder.AppendLine($"base     : {AppContext.BaseDirectory}");
        builder.AppendLine($"temp     : {Path.GetTempPath()}");
        builder.AppendLine($"os       : {RuntimeInformation.OSDescription}");
        builder.AppendLine($"runtime  : {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"console  : {(Console.IsOutputRedirected ? "redirected" : "attached")}");
        builder.AppendLine($"account  : {Environment.UserName}");
        builder.AppendLine();
        builder.AppendLine("hint     : " + GuessHint(exception));
        builder.AppendLine();
        builder.AppendLine("exception:");
        builder.AppendLine(exception.ToString());
        return builder.ToString();
    }

    /// <summary>Build version from <c>version.txt</c>, falling back to the assembly version.</summary>
    private static string BuildVersion()
    {
        var informational = typeof(CrashReporter).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;
        return informational ?? typeof(CrashReporter).Assembly.GetName().Version?.ToString() ?? "unknown";
    }

    /// <summary>
    ///     Maps the exceptions that actually happen on first run to a sentence a player can act on.
    /// </summary>
    private static string GuessHint(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case UnauthorizedAccessException:
                    return "No permission on that path. Unpack the zip into Documents/Desktop instead of Program "
                           + "Files or a read-only drive. (Admin rights are only needed to write the hosts file.)";
                case IOException io when io.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                                          || io.Message.Contains("已存在", StringComparison.Ordinal):
                    return "A file is in the way of a folder the server needs - usually a stray file named "
                           + "Config or Resources next to MikuSB.exe. Delete it and start again.";
                case IOException io when io.Message.Contains("being used", StringComparison.OrdinalIgnoreCase)
                                          || io.Message.Contains("另一个程序", StringComparison.Ordinal):
                    return "A file or port is still held by another MikuSB instance. Close it and start again.";
                case System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.AddressAlreadyInUse }:
                    return "One of the listen ports (21000/21400/21500/18888) is taken. Close the other server "
                           + "or change the ports in Config/Config.json.";
                case IOException io when io.Message.Contains("handle is invalid", StringComparison.OrdinalIgnoreCase)
                                          || io.Message.Contains("句柄无效", StringComparison.Ordinal):
                    return "The process has no usable console. Start MikuSB.cmd instead of MikuSB.exe.";
                case System.Text.Json.JsonException or Newtonsoft.Json.JsonException:
                    return "Config/Config.json is not valid JSON. Restore the backup next to it, or delete the file "
                           + "and let the server recreate it.";
                case FileNotFoundException or DirectoryNotFoundException:
                    return "A required file or folder is missing. Re-extract the release zip and run it from there.";
            }
        }

        return "See the crash log for the full stack trace. If it mentions a port or a path, fix that first.";
    }

    private static string? TryWriteLog(string report)
    {
        foreach (var directory in new[] { Path.Combine(AppContext.BaseDirectory, "logs"), Path.GetTempPath() })
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory,
                    $"crash-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.log");
                File.WriteAllText(path, report, new UTF8Encoding(false));
                return path;
            }
            catch (Exception)
            {
                // Fall through to the next candidate; reporting must never throw again.
            }
        }

        return null;
    }

    private static void TryWriteConsole(string report)
    {
        try
        {
            Console.Error.WriteLine(report);
            Console.Error.Flush();
        }
        catch (Exception)
        {
            // No console and no stderr sink - the log file and the dialog are enough.
        }
    }

    private static void ShowDialog(Exception exception, string phase, string? logPath)
    {
        if (!IsDialogEnabled())
            return;

        var builder = new StringBuilder();
        builder.AppendLine("MikuSB 启动失败 / MikuSB failed to start");
        builder.AppendLine();
        builder.AppendLine($"{phase}: {exception.GetType().Name}: {FirstLine(exception.Message)}");
        builder.AppendLine();
        builder.AppendLine(GuessHint(exception));
        if (logPath is not null)
        {
            builder.AppendLine();
            builder.AppendLine($"日志 / log: {logPath}");
        }

        try
        {
            MessageBoxW(IntPtr.Zero, builder.ToString(), "MikuSB", MessageIconError);
        }
        catch (Exception)
        {
            // No user32 (or no interactive desktop): the log file already has everything.
        }
    }

    private static bool IsDialogEnabled()
    {
        var toggle = Environment.GetEnvironmentVariable(DialogToggleVariable);
        if (!string.IsNullOrEmpty(toggle))
            return toggle is not "0" and not "false" and not "False";

        return Environment.UserInteractive;
    }

    private static string FirstLine(string text)
    {
        var index = text.IndexOfAny(['\r', '\n']);
        return index < 0 ? text : text[..index];
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>Entry point used by tests: reports and returns the exit code.</summary>
    public static int ReportAndExit(Exception exception, string phase)
    {
        var code = Report(exception, phase);
        try
        {
            Debug.Flush();
        }
        catch (Exception)
        {
        }

        return code;
    }
}
