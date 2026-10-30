using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using MikuSB.Util;

namespace MikuSB.SdkServer.Utils;

/// <summary>
/// Everything the first-run wizard needs, kept away from the controller so it can be reasoned about (and
/// probed from the console) on its own.
///
/// Two client families are supported and they are redirected in completely different ways:
/// <list type="bullet">
/// <item><c>cn</c> (jinshan channel): the client resolves its SDK / server-list hosts through DNS, so a
/// hosts file redirect is enough — no DLL is injected.</item>
/// <item><c>global</c>: the client pins/seals its traffic, so the only supported route is the upstream one —
/// launch through MikuSB.Loader with <c>Patch\MikuSB-Patch.dll</c> injected and <c>ALL_PROXY</c> pointing at
/// the local SOCKS5 proxy. The wizard only fills in the paths for that flow.</item>
/// </list>
/// </summary>
public static class SetupService
{
    /// <summary>Trailing comment used to recognise (and later rewrite) the block we own in the hosts file.</summary>
    public const string HostsMarker = "# MikuSB-CN-REDIRECT";

    /// <summary>
    /// Hosts entries the CN client resolves before it can reach a server. The last one is the endpoint the
    /// client actually uses to learn its login/game server address.
    /// </summary>
    public static readonly string[] CnDomains =
    [
        "sh-jxsj.xgsdk.com",
        "xgdata.xgsdk.com",
        "js2sdk.xoyo.com",
        "sh-qrcode.xgsdk.com",
        "activity.xgsdk.com",
        "gpm-conf.testplus.cn",
        "cbjq-serverlist.xoyocdn.com"
    ];

    public sealed record PathCheck(string Key, bool Ok, string Detail);

    /// <summary>Result of looking at whatever the operator typed into the client-path box.</summary>
    public sealed record PathProbe(
        bool Valid,
        string Input,
        string Exe,
        string Install,
        string UserDir,
        string Paks,
        IReadOnlyList<PathCheck> Checks);

    public sealed record HostsState(bool Readable, string Path, bool Managed, IReadOnlyList<string> Entries, string Error);

    public sealed record RegionOption(string Id, string Loader, string Note);

    // ---------------------------------------------------------------------------------------------
    // Path probing
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves what the operator typed (install folder, <c>data\game</c>, <c>Win64</c> or the exe itself) into
    /// a concrete launch path, and reports every check separately so the UI can show what is wrong instead of
    /// just "invalid".
    /// </summary>
    public static PathProbe Probe(string? input)
    {
        var raw = (input ?? string.Empty).Trim().Trim('"');
        var checks = new List<PathCheck>();

        if (raw.Length == 0)
        {
            checks.Add(new PathCheck("input", false, "客户端路径为空"));
            return new PathProbe(false, raw, "", "", "", "", checks);
        }

        string expanded;
        try
        {
            expanded = Environment.ExpandEnvironmentVariables(raw);
        }
        catch (Exception)
        {
            expanded = raw;
        }

        var exe = ResolveExe(expanded);
        checks.Add(exe.Length > 0
            ? new PathCheck("locate", true, exe)
            : new PathCheck("locate", false, "这个路径下找不到 Game.exe（Binaries\\Win64\\Game.exe）"));

        var userDir = exe.Length > 0 ? ResolveUserDir(exe) : "";
        var install = exe.Length > 0 ? ResolveInstall(userDir) : "";

        checks.Add(exe.Length > 0 && File.Exists(exe)
            ? new PathCheck("exe", true, Path.GetFileName(exe))
            : new PathCheck("exe", false, "Game.exe 不存在"));

        var nameOk = exe.Length > 0 && string.Equals(Path.GetFileName(exe), "Game.exe", StringComparison.OrdinalIgnoreCase);
        checks.Add(nameOk
            ? new PathCheck("name", true, "Game.exe")
            : new PathCheck("name", false, "可执行文件名不是 Game.exe，游戏可能不是尘白禁区"));

        var userOk = userDir.Length > 0 && Directory.Exists(userDir);
        checks.Add(userOk
            ? new PathCheck("userdir", true, userDir)
            : new PathCheck("userdir", false, "找不到 data\\game 目录（-userdir 需要它）"));

        var paks = userOk ? Path.Combine(userDir, "Game", "Content", "Paks") : "";
        var paksOk = paks.Length > 0 && Directory.Exists(paks);
        checks.Add(paksOk
            ? new PathCheck("paks", true, CountPaks(paks))
            : new PathCheck("paks", false, "缺少 Game\\Content\\Paks（游戏资源未安装或路径不对）"));

        var installOk = install.Length > 0 && Directory.Exists(Path.Combine(install, "data", "game"));
        checks.Add(installOk
            ? new PathCheck("install", true, install)
            : new PathCheck("install", false, "找不到安装根目录下的 data\\game"));

        var valid = File.Exists(exe) && nameOk && paksOk;
        return new PathProbe(valid, raw, exe, install, userDir, paks, checks);
    }

    private static string CountPaks(string paks)
    {
        try
        {
            var count = Directory.EnumerateFiles(paks, "*.pak", SearchOption.AllDirectories).Take(2).Count();
            return count > 0 ? $"已安装 pak（{paks}）" : $"目录存在但没有 .pak（{paks}）";
        }
        catch (Exception)
        {
            return paks;
        }
    }

    private static string ResolveExe(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }

            if (!Directory.Exists(path))
            {
                return "";
            }

            // Accept the install root, data\game, the Win64 folder or the Binaries folder: every one of
            // those is something a player may reasonably paste.
            string[] relative =
            [
                Path.Combine("Game", "Binaries", "Win64", "Game.exe"),
                Path.Combine("data", "game", "Game", "Binaries", "Win64", "Game.exe"),
                Path.Combine("game", "Game", "Binaries", "Win64", "Game.exe"),
                Path.Combine("Binaries", "Win64", "Game.exe"),
                "Game.exe"
            ];

            foreach (var rel in relative)
            {
                var candidate = Path.Combine(path, rel);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            // Last resort: one level of subfolders (covers "...\Snowbreak\Game\..." layouts).
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                foreach (var rel in relative)
                {
                    var candidate = Path.Combine(dir, rel);
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Unreadable input is reported as "not found" by the caller.
        }

        return "";
    }

    /// <summary><c>-userdir</c> must be the folder that contains <c>Game\Content</c> — i.e. <c>data\game</c>.</summary>
    private static string ResolveUserDir(string exe)
    {
        try
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(exe) ?? "");
            for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Game", "Content", "Paks")))
                {
                    return dir.FullName;
                }
            }

            // Fall back to the layout the client ships with: <install>\data\game\Game\Binaries\Win64.
            var marker = Path.Combine("data", "game") + Path.DirectorySeparatorChar;
            var index = exe.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                return Path.GetFullPath(Path.Combine(exe[..(index + marker.Length)], "."));
            }
        }
        catch (Exception)
        {
        }

        return "";
    }

    private static string ResolveInstall(string userDir)
    {
        try
        {
            // userDir is <install>\data\game, so the install root is two levels up.
            var dir = new DirectoryInfo(userDir);
            var install = dir.Parent?.Parent;
            return install?.FullName ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>Cheap auto-detect: one level of folders on each fixed drive, looking for the client layout.</summary>
    public static IReadOnlyList<string> Suggest()
    {
        var found = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
                {
                    continue;
                }

                foreach (var dir in Directory.EnumerateDirectories(drive.RootDirectory.FullName))
                {
                    var exe = ResolveExe(dir);
                    if (exe.Length > 0 && File.Exists(exe))
                    {
                        found.Add(exe);
                    }
                }
            }
            catch (Exception)
            {
                // A drive that refuses enumeration is simply not a candidate.
            }
        }

        return found.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
    }

    // ---------------------------------------------------------------------------------------------
    // hosts file
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// hosts file this build maintains. <c>MIKUSB_HOSTS_PATH</c> overrides it so the rewrite logic can be
    /// exercised against a copy instead of the machine's real file.
    /// </summary>
    public static string HostsPath =>
        Environment.GetEnvironmentVariable("MIKUSB_HOSTS_PATH") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", "etc", "hosts");

    public static HostsState ReadHosts()
    {
        try
        {
            var text = File.ReadAllText(HostsPath, HostsEncoding);
            var entries = text.Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => CnDomains.Any(domain => line.Contains(domain, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var managed = text.Contains(HostsMarker, StringComparison.Ordinal);
            return new HostsState(true, HostsPath, managed, entries, "");
        }
        catch (Exception ex)
        {
            return new HostsState(false, HostsPath, false, [], ex.Message);
        }
    }

    /// <summary>
    /// Latin-1 is used as a byte-transparent codec on purpose: the hosts file on Chinese Windows is GBK and
    /// carries unrelated entries with Chinese comments, and a wrong codec would either throw (no CodePages
    /// provider is registered here) or silently rewrite bytes we do not own. Every byte round-trips unchanged.
    /// </summary>
    private static Encoding HostsEncoding => Encoding.Latin1;

    /// <summary>
    /// Rewrites our block in the hosts file. Returns a human readable outcome; when the current process is not
    /// elevated the rewrite is retried through an elevated PowerShell (one UAC prompt) and then verified by
    /// reading the file back — a declined prompt is reported as such, never as success.
    /// </summary>
    public static string WriteHosts(bool enable, string targetIp)
    {
        targetIp = string.IsNullOrWhiteSpace(targetIp) ? "127.0.0.1" : targetIp.Trim();
        if (!IPAddress.TryParse(targetIp, out _))
        {
            return "无效的地址：" + targetIp;
        }

        try
        {
            return WriteHostsDirect(enable, targetIp);
        }
        catch (UnauthorizedAccessException)
        {
            // Fall through to the elevated retry.
        }
        catch (IOException)
        {
            // The hosts file can be locked by a listener even for an elevated process; try the retry too.
        }

        return WriteHostsElevated(enable, targetIp);
    }

    private static string WriteHostsDirect(bool enable, string targetIp)
    {
        var encoding = HostsEncoding;
        var lines = File.ReadAllText(HostsPath, encoding).Replace("\r\n", "\n").Split('\n').ToList();

        var kept = lines.Where(line => !IsOurs(line)).ToList();
        while (kept.Count > 0 && kept[^1].Length == 0)
        {
            kept.RemoveAt(kept.Count - 1);
        }

        if (enable)
        {
            kept.Add("");
            kept.Add($"{HostsMarker} begin (managed by the MikuSB admin panel)");
            kept.AddRange(CnDomains.Select(domain => $"{targetIp} {domain} {HostsMarker}"));
            kept.Add($"{HostsMarker} end");
        }

        var payload = string.Join("\r\n", kept) + "\r\n";
        var item = new FileInfo(HostsPath);
        var wasReadOnly = item.IsReadOnly;
        try
        {
            if (wasReadOnly)
            {
                item.IsReadOnly = false;
            }

            File.WriteAllText(HostsPath, payload, encoding);
        }
        finally
        {
            if (wasReadOnly)
            {
                try
                {
                    new FileInfo(HostsPath).IsReadOnly = true;
                }
                catch (Exception)
                {
                }
            }
        }

        var state = ReadHosts();
        return enable
            ? $"hosts 已写入 {state.Entries.Count} 条（{targetIp}）"
            : "hosts 中的 MikuSB 条目已移除";
    }

    private static bool IsOurs(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.Contains(HostsMarker, StringComparison.Ordinal))
        {
            return true;
        }

        var withoutComment = trimmed.Split('#')[0];
        return CnDomains.Any(domain => Regex.IsMatch(withoutComment,
            $@"(^|\s){Regex.Escape(domain)}(\s|$)", RegexOptions.IgnoreCase));
    }

    private static string WriteHostsElevated(bool enable, string targetIp)
    {
        var script = Path.Combine(ConfigManager.Config.Path.LogPath, "hosts-cn-sdk.ps1");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(script)!);
            File.WriteAllText(script, BuildHostsScript(), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            return "无法生成提权脚本：" + ex.Message;
        }

        var arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Mode {(enable ? "add" : "remove")} -TargetIp {targetIp}";
        try
        {
            var startInfo = new ProcessStartInfo("powershell.exe", arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return "无法启动提权进程";
            }

            process.WaitForExit();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return "已取消管理员授权，hosts 未修改";
        }
        catch (Exception ex)
        {
            return "提权失败：" + ex.Message;
        }

        var state = ReadHosts();
        if (enable && state.Entries.Count < CnDomains.Length)
        {
            return $"提权进程已结束，但 hosts 只看到 {state.Entries.Count} 条，可能被安全软件拦下";
        }

        return enable
            ? $"hosts 已写入 {state.Entries.Count} 条（{targetIp}）"
            : "hosts 中的 MikuSB 条目已移除";
    }

    /// <summary>
    /// True when this process can write the hosts file without a UAC prompt. <c>MIKUSB_ELEVATED=0|1</c> overrides
    /// the detection: it exists so the panel's "running as administrator" paths can be exercised without a real
    /// UAC prompt, and as an escape hatch if the check ever lies on an exotic setup.
    /// </summary>
    public static bool IsElevated()
    {
        switch (Environment.GetEnvironmentVariable("MIKUSB_ELEVATED"))
        {
            case "1":
            case "true":
                return true;
            case "0":
            case "false":
                return false;
        }

        if (!OperatingSystem.IsWindows())
            return true;

        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Outcome of a hosts sync. <c>Status</c> is a code the panel localises (applied / already / removed / clear
    /// / needs_admin / failed); <c>Message</c> is the detail from <see cref="WriteHosts"/>, also logged.
    /// </summary>
    public sealed record HostsSync(string Status, string Message, IReadOnlyList<string> Entries);

    /// <summary>
    /// Make the hosts block match <paramref name="targetIp"/>. The setup wizard calls this from its "write
    /// config" step, so the button only reports a problem when the block is still wrong afterwards.
    /// </summary>
    public static HostsSync SyncHosts(string targetIp)
    {
        targetIp = string.IsNullOrWhiteSpace(targetIp) ? "127.0.0.1" : targetIp.Trim();
        if (!IPAddress.TryParse(targetIp, out _))
            return new HostsSync("failed", "无效的地址：" + targetIp, ReadHosts().Entries);

        var before = ReadHosts();
        if (IsBlockCurrent(before, targetIp))
            return new HostsSync("already", $"hosts 已是最新（{targetIp}）", before.Entries);

        var message = WriteHosts(true, targetIp);
        var after = ReadHosts();
        if (IsBlockCurrent(after, targetIp))
            return new HostsSync("applied", message, after.Entries);

        return new HostsSync(after.Readable ? "needs_admin" : "failed", message, after.Entries);
    }

    /// <summary>Drop our hosts block. Same elevation policy as <see cref="SyncHosts"/>.</summary>
    public static HostsSync ClearHosts()
    {
        var before = ReadHosts();
        if (before.Entries.Count == 0)
            return new HostsSync("clear", "hosts 里没有 MikuSB 的条目", before.Entries);

        var message = WriteHosts(false, "127.0.0.1");
        var after = ReadHosts();
        return after.Entries.Count == 0
            ? new HostsSync("removed", message, after.Entries)
            : new HostsSync("needs_admin", message, after.Entries);
    }

    private static bool IsBlockCurrent(HostsState state, string targetIp)
    {
        if (state.Entries.Count != CnDomains.Length)
            return false;

        return state.Entries.All(line => line.TrimStart().StartsWith(targetIp, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildHostsScript()
    {
        var domains = string.Join(",\r\n    ", CnDomains.Select(domain => $"'{domain}'"));
        return $$"""
# Generated by the MikuSB admin panel setup wizard; safe to delete.
# Rewrites the {{HostsMarker}} block in the hosts file (needs Administrator).
param(
    [Parameter(Mandatory = $true)][ValidateSet('add', 'remove')][string]$Mode,
    [string]$TargetIp = '127.0.0.1'
)

$hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$marker = '{{HostsMarker}}'
$domains = @(
    {{domains}}
)

$encoding = [System.Text.Encoding]::GetEncoding(28591)  # Latin-1: byte-transparent, keeps the GBK comments intact
$lines = @([System.IO.File]::ReadAllLines($hostsPath, $encoding))
$pattern = '^\s*(0\.0\.0\.0|127\.0\.0\.1|::1|([0-9]{1,3}\.){3}[0-9]{1,3})?\s*(' + (($domains | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')\s*(#.*)?$'
$kept = @($lines | Where-Object { -not $_.Contains($marker) -and -not ($_ -match $pattern) })
while ($kept.Count -gt 0 -and $kept[-1].Trim().Length -eq 0) { $kept = @($kept[0..($kept.Count - 2)]) }

$out = @($kept)
if ($Mode -eq 'add') {
    $out += ''
    $out += "$marker begin (managed by the MikuSB admin panel)"
    foreach ($d in $domains) { $out += "$TargetIp $d $marker" }
    $out += "$marker end"
}

$item = Get-Item -LiteralPath $hostsPath
$wasReadOnly = $item.IsReadOnly
if ($wasReadOnly) { $item.IsReadOnly = $false }

$payload = ($out -join "`r`n") + "`r`n"
$strategies = @(
    { $tmp = Join-Path $env:TEMP 'hosts-mikusb.tmp'; [System.IO.File]::WriteAllText($tmp, $payload, $encoding); [System.IO.File]::Move($tmp, $hostsPath, $true) },
    { [System.IO.File]::WriteAllText($hostsPath, $payload, $encoding) }
)

$written = $false
foreach ($strategy in $strategies) {
    try { & $strategy; $written = $true; break } catch { }
}

if ($wasReadOnly) { (Get-Item -LiteralPath $hostsPath).IsReadOnly = $true }
if (-not $written) { exit 1 }
exit 0
""";
    }

    // ---------------------------------------------------------------------------------------------
    // region presets
    // ---------------------------------------------------------------------------------------------

    public static IReadOnlyList<RegionOption> Regions { get; } =
    [
        new("cn", "hosts",
            "国服（jinshan）：客户端自己启动，面板只把 7 个 SDK 域名指向本机，不注入 DLL。"),
        new("global", "loader",
            "国际服：沿用上游 Loader 注入 MikuSB-Patch.dll + 本地 SOCKS5，面板只代填路径与启动参数。")
    ];

    public static string NormalizeRegion(string? region) =>
        string.Equals(region, "cn", StringComparison.OrdinalIgnoreCase) ? "cn" : "global";

    /// <summary>
    /// What region the config currently describes. An existing install has no <c>Loader.Region</c> yet, so the
    /// launch arguments are the only honest source: only the CN build is started with <c>-ChannelID=jinshan</c>.
    /// </summary>
    public static string ResolveRegion(string? stored, IEnumerable<string>? arguments)
    {
        if (!string.IsNullOrWhiteSpace(stored))
        {
            return NormalizeRegion(stored);
        }

        return (arguments ?? []).Any(arg => arg.Contains("jinshan", StringComparison.OrdinalIgnoreCase))
            ? "cn"
            : "global";
    }

    /// <summary>Launch arguments for a region, with <c>-userdir</c> pinned to the resolved folder.</summary>
    public static string[] BuildArguments(string region, string userDir)
    {
        var args = new List<string> { "-FeatureLevelES31" };
        if (NormalizeRegion(region) == "cn")
        {
            args.Add("-ChannelID=jinshan");
            args.Add("-gclid=CBJQ_setup");
        }
        else
        {
            args.Add("-channelid=seasun");
            args.Add("-NoSplash");
        }

        if (!string.IsNullOrWhiteSpace(userDir))
        {
            args.Add($"-userdir={userDir}");
        }

        return args.ToArray();
    }

    /// <summary>LAN addresses a player on another machine could use; loopback-only setups fall back to 127.0.0.1.</summary>
    public static IReadOnlyList<string> LocalAddresses()
    {
        var addresses = new List<string> { "127.0.0.1" };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(unicast.Address))
                    {
                        continue;
                    }

                    var text = unicast.Address.ToString();
                    if (!addresses.Contains(text))
                    {
                        addresses.Add(text);
                    }
                }
            }
        }
        catch (Exception)
        {
        }

        return addresses;
    }
}
