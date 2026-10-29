using Microsoft.AspNetCore.Components;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Enums.Packet;
using MikuSB.GameServer.Command;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server;
using MikuSB.GameServer.Server.CallGS;
using MikuSB.GameServer.Server.Packet;
using MikuSB.Internationalization;
using MikuSB.MikuSB.Tool;
using MikuSB.MikuSB.Update;
using MikuSB.Proto;
using MikuSB.SdkServer;
using MikuSB.TcpSharp;
using MikuSB.Util;
using MikuSB.Util.Security;
using System.Reflection;

namespace MikuSB.MikuSB.Program;

public class LoaderManager : MikuSB
{
    public static void InitConfig()
    {
        // Initialize log
        var logDir = ConfigManager.Config.Path.LogPath;
        var logFile = new FileInfo(Path.Combine(logDir, "Server.log"));
        logFile.Directory?.Create();

        if (logFile.Exists)
        {
            // Read start time from first log line, fall back to file creation time
            DateTime logStartTime;
            try
            {
                var firstLine = File.ReadLines(logFile.FullName).FirstOrDefault() ?? "";
                // Format: [HH:mm:ss] ...
                var timeStr = firstLine.Length >= 10 ? firstLine[1..9] : "";
                var dateStr = logFile.CreationTime.ToString("yyyy-MM-dd");
                logStartTime = DateTime.TryParse($"{dateStr} {timeStr}", out var parsed)
                    ? parsed
                    : logFile.CreationTime;
            }
            catch
            {
                logStartTime = logFile.CreationTime;
            }

            var backupName = $"Server-backup-{logStartTime:yyyy.MM.dd-HH.mm.ss}.log";
            var backupFile = new FileInfo(Path.Combine(logDir, backupName));
            logFile.MoveTo(backupFile.FullName, overwrite: true);
        }

        Logger.SetLogFile(new FileInfo(Path.Combine(logDir, "Server.log")));

        // Localize the boot banner before its first line is printed. The language lives in Config.json,
        // which LoadConfig() only reads further down, so peek at the file instead of waiting for it.
        I18NManager.LoadLanguage(ConfigManager.PeekLanguage(), announce: false);

        // Init all directories
        try
        {
            ConfigManager.InitDirectories();
        }
        catch (Exception e)
        {
            Logger.Error(I18NManager.Translate("Server.ServerInfo.FailedToLoadItem", I18NManager.Translate("Word.Config")), e);
            Console.ReadLine();
            return;
        }

        // Starting the server
        Logger.Info(I18NManager.Translate("Server.ServerInfo.StartingServer"));
        Logger.Info(I18NManager.Translate("Server.ServerInfo.BuildVersion", BuildVersion.Current));

        // Load the config
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.Config")));
        try
        {
            ConfigManager.LoadConfig();
        }
        catch (Exception e)
        {
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToLoadItem", I18NManager.Translate("Word.Config")), e);
            Console.ReadLine();
            return;
        }

        // Load the language
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.Language")));
        try
        {
            I18NManager.LoadLanguage();
        }
        catch (Exception e)
        {
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToLoadItem", I18NManager.Translate("Word.Language")), e);
            Console.ReadLine();
            return;
        }
    }

    private static bool IsSpecificChannel()
    {
        return (ConfigManager.Config.Loader.Arguments ?? [])
            .Any(x => x.Contains("jinshan", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Points the specific-channel SDK host names at this machine for as long as the server runs.
    ///     Needs an elevated process; when that is missing we keep going and only warn.
    /// </summary>
    public static void InitCnHosts()
    {
        if (!HostsFileService.IsSupported)
            return;

        if (!ConfigManager.Config.Loader.ManageCnHosts)
        {
            Logger.Info(I18NManager.Translate("Server.ServerInfo.HostsDisabled"));
            return;
        }

        if (!IsSpecificChannel())
        {
            Logger.Info(I18NManager.Translate("Server.ServerInfo.HostsSkipped"));
            return;
        }

        var result = HostsFileService.Ensure();
        switch (result.Status)
        {
            case HostsSyncStatus.Applied:
                Logger.Info(I18NManager.Translate("Server.ServerInfo.HostsApplied", HostsFileService.HostsPath));
                break;
            case HostsSyncStatus.AlreadyCurrent:
                break;
            case HostsSyncStatus.NotElevated:
                Logger.Warn(I18NManager.Translate("Server.ServerInfo.HostsNotElevated"));
                break;
            default:
                Logger.Warn(I18NManager.Translate("Server.ServerInfo.HostsFailed", result.Detail));
                break;
        }
    }

    /// <summary>
    ///     Drops the managed block on the way out. This deliberately ignores <c>Loader.ManageCnHosts</c>:
    ///     the switch decides whether a block may be written, and leaving one behind because the switch
    ///     was flipped in the meantime would keep every SDK host name on this machine.
    /// </summary>
    public static void ReleaseCnHosts()
    {
        if (!HostsFileService.IsSupported)
            return;

        var result = HostsFileService.Restore();
        switch (result.Status)
        {
            case HostsSyncStatus.Applied:
                Logger.Info(I18NManager.Translate("Server.ServerInfo.HostsReleased"));
                break;
            case HostsSyncStatus.Failed:
                Logger.Warn(I18NManager.Translate("Server.ServerInfo.HostsRestoreFailed", result.Detail));
                break;
        }
    }

    public static void InitDatabase()
    {
        // Initialize the database
        try
        {
            _ = Task.Run(DatabaseHelper.Initialize); // do not wait

            while (!DatabaseHelper.LoadAccount) Thread.Sleep(100);

            Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItem",
                I18NManager.Translate("Word.DatabaseAccount")));
        }
        catch (Exception e)
        {
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToLoadItem", I18NManager.Translate("Word.Database")), e);
            Console.ReadLine();
            return;
        }
    }

    public static async Task InitSdkServer()
    {
        SdkServer.SdkServer.Start([]);
        Logger.Info(I18NManager.Translate("Server.ServerInfo.ServerRunning", I18NManager.Translate("Word.Dispatch"),
            ConfigManager.Config.HttpServer.GetDisplayAddress()));

        //KcpListener.BaseConnection = typeof(Connection);
        //KcpListener.StartListener();
        SocketListener.BaseConnection = typeof(Connection);
        SocketListener.StartListener();

        await Task.CompletedTask;
    }

    public static void InitPacket()
    {
        // get opcode from CmdIds
        var opcodes = typeof(CmdIds).GetFields().Where(x => x.FieldType == typeof(int)).ToList();
        foreach (var opcode in opcodes)
        {
            var name = opcode.Name;
            var value = (int)opcode.GetValue(null)!;
            SocketConnection.LogMap.TryAdd(value, name);
        }

        HandlerManager.Init();
        CallGSRouter.Init();
    }

    public static async Task InitResource()
    {
        // Init custom files
        Logger.Info(I18NManager.Translate("Server.ServerInfo.GeneratingItem", I18NManager.Translate("Word.CustomData")));
        try
        {
            await AssemblyGenerater.LoadCustomData(Assembly.GetExecutingAssembly());
        }
        catch (Exception e)
        {
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToLoadItem", I18NManager.Translate("Word.CustomData")), e);
            Console.ReadLine();
            return;
        }

        // Load the game data
        try
        {
            await UpdateService.EnsureResourcesPresentAsync();
            Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.GameData")));
            ResourceManager.LoadGameData();
        }
        catch (Exception e)
        {
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToLoadItem", I18NManager.Translate("Word.GameData")), e);
            Console.ReadLine();
            return;
        }
    }

    public static async Task InitCommand(CancellationToken exitToken)
    {
        // Register the command handlers
        try
        {
            CommandManager.RegisterCommands();
        }
        catch (Exception e)
        {
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToInitializeItem",
                    I18NManager.Translate("Word.Command")), e);
            Console.ReadLine();
            return;
        }
        IConsole.OnConsoleExcuteCommand += CommandExecutor.ConsoleExcuteCommand;
        CommandExecutor.OnRunCommand += (sender, e) => { _ = CommandManager.HandleCommand(e, sender); };
        InGameConsoleBridge.ExecuteCommandAsync = InGameConsoleCommandService.ExecuteAsync;

        // Admin panel data. Same reason as the line above: SdkServer only references Common, so the panel
        // cannot touch sockets or the command table directly and goes through these delegates instead.
        InGameConsoleBridge.GetOnlinePlayers = SnapshotOnlinePlayers;
        InGameConsoleBridge.GetCommands = DescribeCommands;
        InGameConsoleBridge.GetCurrencyCatalog = CurrencyService.GetCatalog;
        // DlcRuntimeOverrides.EffectiveDlcId already folds "runtime pin wins, otherwise Config.json" into one
        // value, so the panel never has to re-implement that precedence.
        InGameConsoleBridge.GetServedDlc = () => new ServedDlcInfo(
            DlcRuntimeOverrides.EffectiveDlcId,
            DlcRuntimeOverrides.HasRuntimePin,
            ConfigManager.Config.ServerOption.PinDlcActivityId != 0);

        await IConsole.ListenConsole(exitToken);
    }

    /// <summary>
    /// Player roster for the admin panel. SocketListener.Connections is a SortedList the network threads
    /// mutate without a lock, so a disconnect can race this enumeration; a partial view beats failing the
    /// request, and the next call is consistent again.
    /// </summary>
    private static IReadOnlyList<OnlinePlayerInfo> SnapshotOnlinePlayers()
    {
        var players = new List<OnlinePlayerInfo>();
        try
        {
            foreach (var socket in SocketListener.Connections.Values.ToList())
            {
                if (socket is not Connection connection) continue;
                if (connection.State != SessionStateEnum.ACTIVE) continue;
                if (connection.Player is not { } player) continue;

                players.Add(new OnlinePlayerInfo(player.Uid, player.Data.Name ?? "",
                    player.Data.Level, connection.RemoteEndPoint?.ToString() ?? ""));
            }
        }
        catch (InvalidOperationException)
        {
            // Collection was modified mid-enumeration: the snapshot collected so far is still correct.
        }

        return players.OrderBy(info => info.Uid).ToArray();
    }

    private static IReadOnlyList<CommandDescriptor> DescribeCommands() =>
        CommandManager.CommandInfo.Values
            .Select(info => new CommandDescriptor(info.Name, info.Usage, info.Description, info.Alias,
                info.Perm.Select(perm => perm.ToString()).ToArray()))
            .OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal)
            .ToArray();
}