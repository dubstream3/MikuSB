using MikuSB.Data;
using MikuSB.Database;
using MikuSB.MikuSB.Tool;
using MikuSB.GameServer.Command;
using MikuSB.GameServer.Server;
using MikuSB.Internationalization;
using MikuSB.MikuSB.Update;
using MikuSB.TcpSharp;
using MikuSB.Util;
using System.Globalization;

namespace MikuSB.MikuSB.Program;

public class MikuSB
{
    public static readonly Logger Logger = new("MikuSB");
    public static readonly DatabaseHelper DatabaseHelper = new();
    public static readonly Listener Listener = new();
    public static readonly CommandManager CommandManager = new();

    // for exit signal
    private static readonly CancellationTokenSource _cts = new();
    private static int _exitCode = 0;

    public static async Task Main()
    {
        if (TryCleanHostsFromCommandLine())
            return;

        var time = DateTime.Now;
        IConsole.InitConsole();
        LoaderManager.InitConfig();
        ShowAntiScamWarning();
        if (await UpdateService.TryStartSelfUpdateAsync())
            return;

        RegisterExitEvent();
        LoaderManager.InitCnHosts();
        await LoaderManager.InitSdkServer();
        LoaderManager.InitPacket();

        LoaderManager.InitDatabase();
        if (!DatabaseHelper.LoadAllData)
        {
            var t = Task.Run(() =>
            {
                while (!DatabaseHelper.LoadAllData) // wait for all data to be loaded
                    Thread.Sleep(100);
            });

            await t.WaitAsync(new CancellationToken());

            Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItem", I18NManager.Translate("Word.Database")));
        }

        Logger.Warn(I18NManager.Translate("Server.ServerInfo.WaitForAllDone"));

        await LoaderManager.InitResource();
        ResourceManager.IsLoaded = true;

        HandbookGenerator.GenerateAll();
        var consoleTask = Task.Run(() => LoaderManager.InitCommand(_cts.Token), _cts.Token);

        var elapsed = DateTime.Now - time;
        Logger.Info(I18NManager.Translate("Server.ServerInfo.ServerStarted",
            Math.Round(elapsed.TotalSeconds, 2).ToString(CultureInfo.InvariantCulture)));

        await consoleTask;

        await ProcessExit(Volatile.Read(ref _exitCode));
    }

    /// <summary>
    ///     <c>MikuSB --clean-hosts</c> drops the hosts block and exits without starting the server, because
    ///     starting it would write the block straight back. This is the way out when the server crashed,
    ///     was killed, or refuses to boot with the redirect still in place. It writes to stdout instead of
    ///     going through <see cref="Logger" />: the logger redraws the input line and would throw as soon as
    ///     the output is redirected.
    /// </summary>
    private static bool TryCleanHostsFromCommandLine()
    {
        if (!Environment.GetCommandLineArgs()
                .Any(argument => argument.Equals("--clean-hosts", StringComparison.OrdinalIgnoreCase)))
            return false;

        I18NManager.LoadLanguage(ConfigManager.PeekLanguage(), announce: false);

        if (!HostsFileService.IsSupported)
        {
            Console.WriteLine(I18NManager.Translate("Server.ServerInfo.HostsCleanUnsupported"));
            return true;
        }

        var result = HostsFileService.Restore();
        switch (result.Status)
        {
            case HostsSyncStatus.Applied:
                HostsFileService.FlushDnsCache();
                Console.WriteLine(I18NManager.Translate("Server.ServerInfo.HostsReleased"));
                break;
            case HostsSyncStatus.AlreadyCurrent:
                Console.WriteLine(I18NManager.Translate("Server.ServerInfo.HostsNothingToRemove"));
                break;
            case HostsSyncStatus.NotElevated:
                Console.WriteLine(I18NManager.Translate("Server.ServerInfo.HostsCleanElevated"));
                break;
            default:
                Console.WriteLine(I18NManager.Translate("Server.ServerInfo.HostsRestoreFailed", result.Detail));
                break;
        }

        return true;
    }

    private static void ShowAntiScamWarning()
    {
        Logger.Warn(I18NManager.Translate("Server.ServerInfo.AntiScamSeparator"));
        Logger.Warn(I18NManager.Translate("Server.ServerInfo.AntiScamFree"));
        Logger.Warn(I18NManager.Translate("Server.ServerInfo.AntiScamScam"));
        Logger.Warn(I18NManager.Translate("Server.ServerInfo.AntiScamRefund"));
        Logger.Warn(I18NManager.Translate("Server.ServerInfo.AntiScamDiscord"));
        Logger.Warn(I18NManager.Translate("Server.ServerInfo.AntiScamSeparator"));
    }

    #region Exit

    private static void RegisterExitEvent()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Logger.Info(I18NManager.Translate("Server.ServerInfo.Shutdown"));
            RequestShutdown(0);
        };
        AppDomain.CurrentDomain.UnhandledException += (obj, arg) =>
        {
            Logger.Error(I18NManager.Translate("Server.ServerInfo.UnhandledException", obj.GetType().Name),
                (Exception)arg.ExceptionObject);
            Logger.Info(I18NManager.Translate("Server.ServerInfo.Shutdown"));
            RequestShutdown(1);
        };

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            Logger.Info(I18NManager.Translate("Server.ServerInfo.CancelKeyPressed"));
            eventArgs.Cancel = true;
            RequestShutdown(0);
        };
    }

    private static void RequestShutdown(int exitCode)
    {
        Interlocked.Exchange(ref _exitCode, exitCode);
        _cts.Cancel();
    }

    private static async Task ProcessExit(int exitCode)
    {
        LoaderManager.ReleaseCnHosts();

        SocketListener.Connections.Values.ToList().ForEach(x => x.Stop(true));

        DatabaseHelper.Stop();          // notify stop
        await DatabaseHelper.WaitAsync(); // wait AutoSave thread exit

        DatabaseHelper.SaveDatabase(); // final flush

        Environment.Exit(exitCode);
    }

    # endregion
}
