using System.Collections;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using MikuSB.Util;

namespace MikuSB.Loader;

public static class GameLaunchService
{
    public static int Launch(params string[] extraGameArguments)
    {
        ConfigManager.LoadConfig();
        if (ConfigManager.Config.Loader.EnableInGameConsole
            && ConfigManager.Config.Loader.AutoUpdateInGameConsole)
        {
            InGameConsoleDownloadService.EnsurePresent();
        }
        PatchDownloadService.EnsurePatchPresent();
        var options = LaunchOptions.FromConfig(extraGameArguments);
        return Launch(options, true);
    }

    /// <summary>
    ///     Starts the client without loading the patch DLLs. Channels that are redirected through
    ///     hosts records do not need an injected patch, and this path must not download one either.
    /// </summary>
    public static int LaunchWithoutPatch(params string[] extraGameArguments)
    {
        ConfigManager.LoadConfig();
        if (ConfigManager.Config.Loader.EnableInGameConsole
            && ConfigManager.Config.Loader.AutoUpdateInGameConsole)
        {
            InGameConsoleDownloadService.EnsurePresent();
        }

        var options = LaunchOptions.FromConfig(extraGameArguments);
        return Launch(options, false);
    }

    public static int Launch(LaunchOptions options)
    {
        return Launch(options, true);
    }

    public static int Launch(LaunchOptions options, bool injectPatch)
    {
        var startupInfo = new STARTUPINFOW
        {
            cb = Marshal.SizeOf<STARTUPINFOW>()
        };

        var commandLine = BuildCommandLine(options.GamePath, options.GameArguments);
        var workingDirectory = options.WorkingDirectory ?? Path.GetDirectoryName(options.GamePath)
            ?? throw new InvalidOperationException("Unable to determine working directory.");

        var environment = BuildEnvironmentBlock(options.EnvironmentVariables);
        try
        {
            if (!CreateProcessW(
                    lpApplicationName: options.GamePath,
                    lpCommandLine: commandLine,
                    lpProcessAttributes: IntPtr.Zero,
                    lpThreadAttributes: IntPtr.Zero,
                    bInheritHandles: false,
                    dwCreationFlags: (injectPatch ? CreationFlags.CREATE_SUSPENDED : 0) |
                                     CreationFlags.CREATE_UNICODE_ENVIRONMENT,
                    lpEnvironment: environment,
                    lpCurrentDirectory: workingDirectory,
                    lpStartupInfo: ref startupInfo,
                    lpProcessInformation: out var processInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create game process.");
            }

            try
            {
                if (injectPatch)
                {
                    foreach (var patchPath in options.PatchPaths)
                        InjectDll(processInfo.hProcess, patchPath);

                    if (ResumeThread(processInfo.hThread) == uint.MaxValue)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to resume game process.");
                }

                return processInfo.dwProcessId;
            }
            finally
            {
                CloseHandle(processInfo.hThread);
                CloseHandle(processInfo.hProcess);
            }
        }
        finally
        {
            if (environment != IntPtr.Zero)
                Marshal.FreeHGlobal(environment);
        }
    }

    private static void InjectDll(IntPtr processHandle, string dllPath)
    {
        var dllBytes = Encoding.Unicode.GetBytes(dllPath + '\0');
        var remoteBuffer = VirtualAllocEx(
            processHandle,
            IntPtr.Zero,
            (nuint)dllBytes.Length,
            AllocationType.Commit | AllocationType.Reserve,
            MemoryProtection.ReadWrite);

        if (remoteBuffer == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAllocEx failed.");

        try
        {
            if (!WriteProcessMemory(processHandle, remoteBuffer, dllBytes, dllBytes.Length, out var written) ||
                written.ToInt64() != dllBytes.Length)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteProcessMemory failed.");
            }

            var kernel32 = GetModuleHandleW("kernel32.dll");
            if (kernel32 == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetModuleHandleW(kernel32.dll) failed.");

            var loadLibrary = GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibrary == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetProcAddress(LoadLibraryW) failed.");

            var remoteThread = CreateRemoteThread(
                processHandle,
                IntPtr.Zero,
                0,
                loadLibrary,
                remoteBuffer,
                0,
                out _);

            if (remoteThread == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRemoteThread failed.");

            try
            {
                var waitResult = WaitForSingleObject(remoteThread, 10_000);
                if (waitResult != 0)
                    throw new Win32Exception($"Remote LoadLibraryW timed out or failed: {waitResult}");
            }
            finally
            {
                CloseHandle(remoteThread);
            }
        }
        finally
        {
            VirtualFreeEx(processHandle, remoteBuffer, 0, FreeType.Release);
        }
    }

    private static string BuildCommandLine(string exePath, IReadOnlyList<string> gameArgs)
    {
        var parts = new List<string> { Quote(exePath) };
        parts.AddRange(gameArgs.Select(Quote));
        return string.Join(' ', parts);
    }

    private static IntPtr BuildEnvironmentBlock(IReadOnlyDictionary<string, string> variables)
    {
        if (variables.Count == 0)
            return IntPtr.Zero;

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            merged[(string)entry.Key] = entry.Value?.ToString() ?? string.Empty;

        foreach (var pair in variables)
            merged[pair.Key] = pair.Value;

        var payload = string.Join('\0', merged.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Key}={x.Value}")) + "\0\0";

        return Marshal.StringToHGlobalUni(payload);
    }

    private static string Quote(string value)
    {
        if (value.Length == 0)
            return "\"\"";

        if (!value.Any(char.IsWhiteSpace) && !value.Contains('"'))
            return value;

        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    [Flags]
    private enum CreationFlags : uint
    {
        CREATE_SUSPENDED = 0x00000004,
        CREATE_UNICODE_ENVIRONMENT = 0x00000400
    }

    [Flags]
    private enum AllocationType : uint
    {
        Commit = 0x1000,
        Reserve = 0x2000
    }

    [Flags]
    private enum MemoryProtection : uint
    {
        ReadWrite = 0x04
    }

    [Flags]
    private enum FreeType : uint
    {
        Release = 0x8000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOW
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        CreationFlags dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOW lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        nuint dwSize,
        AllocationType flAllocationType,
        MemoryProtection flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        nuint dwSize,
        FreeType dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        int nSize,
        out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(
        IntPtr hProcess,
        IntPtr lpThreadAttributes,
        nuint dwStackSize,
        IntPtr lpStartAddress,
        IntPtr lpParameter,
        uint dwCreationFlags,
        out int lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}

public sealed class LaunchOptions
{
    public required string GamePath { get; init; }
    public required IReadOnlyList<string> PatchPaths { get; init; }
    public string? WorkingDirectory { get; init; }
    public required IReadOnlyList<string> GameArguments { get; init; }
    public required IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; }

    public static LaunchOptions FromConfig(IEnumerable<string>? extraGameArguments = null)
    {
        var config = ConfigManager.Config;
        var serverBaseDirectory = AppContext.BaseDirectory;
        var gamePath = ResolvePath(config.Loader.GamePath, AppContext.BaseDirectory);
        var configuredPatchPaths = (config.Loader.PatchPaths ?? []).ToList();
        if (config.Loader.EnableInGameConsole
            && !configuredPatchPaths.Contains(config.Loader.InGameConsoleLoaderPath, StringComparer.OrdinalIgnoreCase))
        {
            configuredPatchPaths.Add(config.Loader.InGameConsoleLoaderPath);
        }

        var patchPaths = ResolvePatchPaths(configuredPatchPaths, serverBaseDirectory);
        var gameArgs = new List<string>(config.Loader.Arguments ?? []);
        if (extraGameArguments is not null)
            gameArgs.AddRange(extraGameArguments.Where(x => !string.IsNullOrWhiteSpace(x)));
        gameArgs = EnsureUserDirArgument(gameArgs, serverBaseDirectory);
        PersistResolvedArgumentsIfChanged(config, gameArgs);

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (config.Loader.SetAllProxy && config.Proxy.Enabled)
            env["ALL_PROXY"] = $"socks5h://127.0.0.1:{config.Proxy.Port}";

        if (string.IsNullOrWhiteSpace(gamePath))
            throw new InvalidOperationException("Loader.GamePath is not configured.");
        if (!File.Exists(gamePath))
            throw new FileNotFoundException("Game executable not found.", gamePath);
        if (patchPaths.Count == 0)
            throw new InvalidOperationException("At least one patch path is required.");

        foreach (var patchPath in patchPaths)
        {
            if (!File.Exists(patchPath))
                throw new FileNotFoundException("Patch DLL not found.", patchPath);
        }

        var workingDirectory = Path.GetDirectoryName(gamePath);
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException($"Working directory not found: {workingDirectory}");

        return new LaunchOptions
        {
            GamePath = Path.GetFullPath(gamePath),
            PatchPaths = patchPaths,
            WorkingDirectory = Path.GetFullPath(workingDirectory),
            GameArguments = gameArgs,
            EnvironmentVariables = env
        };
    }

    private static List<string> EnsureUserDirArgument(List<string> gameArgs, string baseDirectory)
    {
        // Verified 2026-10-05: Game.exe hangs inside GameBase.dll early init (44 MB, 54 modules,
        // no UE4 modules, no window, no log, exits after ~2.5 min) whenever -userdir points at a
        // path other than the game's own root. The official launcher passes
        // -userdir="<GameDir>" (see the working Game.log "LogInit: Command Line:").
        // An explicitly configured -userdir therefore wins; only add a fallback when it is absent.
        var existingIndex = gameArgs.FindIndex(x => x.StartsWith("-userdir=", StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            return gameArgs;

        var userDataDirectory = Path.GetFullPath(Path.Combine(baseDirectory, "Client_User_Data"));
        Directory.CreateDirectory(userDataDirectory);
        gameArgs.Add($"-userdir={userDataDirectory}");

        return gameArgs;
    }

    private static void PersistResolvedArgumentsIfChanged(Configuration.ConfigContainer config, List<string> gameArgs)
    {
        var currentArgs = config.Loader.Arguments ?? [];
        if (currentArgs.SequenceEqual(gameArgs, StringComparer.Ordinal))
            return;

        config.Loader.Arguments = gameArgs.ToArray();
        ConfigManager.SaveConfig();
    }

    private static string? ResolvePath(string? value, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return Path.IsPathRooted(value)
            ? value
            : Path.GetFullPath(Path.Combine(baseDirectory, value));
    }

    private static List<string> ResolvePatchPaths(IEnumerable<string>? values, string baseDirectory)
    {
        var result = new List<string>();
        if (values is null)
            return result;

        foreach (var value in values)
        {
            var resolved = ResolvePath(value, baseDirectory);
            if (!string.IsNullOrWhiteSpace(resolved))
                result.Add(resolved);
        }

        return result;
    }
}
