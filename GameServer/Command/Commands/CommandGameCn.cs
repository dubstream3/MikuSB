using MikuSB.Enums.Player;
using MikuSB.Internationalization;
using MikuSB.Loader;
using MikuSB.Util;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
///     Starts the specific-channel (CN) client. Two routes reach the same server: the hosts block points the
///     seven SDK domains at this machine and the client starts untouched (<c>hosts</c>, the default), or the CN
///     build of the patch DLL is injected and the hosts file is left alone (<c>dll</c>). The mode is decided by
///     <c>-dll</c>/<c>-hosts</c> on the command line, falling back to <c>Loader.CnLaunchMode</c>.
/// </summary>
[CommandInfo("game-cn", "Game.Command.GameCn.Desc", "Game.Command.GameCn.Usage", [],
    [PermEnum.Admin, PermEnum.Support])]
public class CommandGameCn : ICommands
{
    private static readonly Logger Logger = new("CommandManager");

    [CommandDefault]
    public async ValueTask Launch(CommandArg arg)
    {
        if (ResolveMode(arg) == "dll")
        {
            await LaunchWithDll(arg);
            return;
        }

        if (!HostsFileService.IsSupported)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.GameCn.Unsupported"));
            return;
        }

        var sync = HostsFileService.Ensure();
        switch (sync.Status)
        {
            case HostsSyncStatus.NotElevated:
                await arg.SendMsg(I18NManager.Translate("Game.Command.GameCn.NeedAdmin",
                    HostsFileService.HostsPath, HostsFileService.BuildBlock()));
                return;
            case HostsSyncStatus.Failed:
                await arg.SendMsg(I18NManager.Translate("Game.Command.GameCn.HostsFailed", sync.Detail));
                return;
            case HostsSyncStatus.Applied:
                Logger.Info($"Updated the hosts file: {HostsFileService.HostsPath}");
                await arg.SendMsg(
                    I18NManager.Translate("Game.Command.GameCn.HostsUpdated", HostsFileService.HostsPath));
                break;
            default:
                await arg.SendMsg(I18NManager.Translate("Game.Command.GameCn.HostsCurrent"));
                break;
        }

        // The resolver caches the old answers for a while; drop them so the client sees the new records.
        HostsFileService.FlushDnsCache();

        try
        {
            var pid = GameLaunchService.LaunchWithoutPatch(GameArguments(arg));
            var message = I18NManager.Translate("Game.Command.GameCn.Started", pid.ToString());
            Logger.Info(message);
            await arg.SendMsg(message);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to launch the specific-channel client.", ex);
            await arg.SendMsg(I18NManager.Translate("Game.Command.GameCn.Failed", ex.Message));
        }
    }

    /// <summary>
    ///     Injects the CN patch DLL instead of touching hosts. No elevation is needed and no hosts entry is
    ///     written, which is what makes this route usable on a machine where the hosts file cannot be edited.
    /// </summary>
    private static async ValueTask LaunchWithDll(CommandArg arg)
    {
        var mode = I18NManager.Translate("Game.Command.GameCn.DllMode");
        Logger.Info($"[game-cn] dll mode: injecting {ConfigManager.Config.Loader.CnPatchPath}");

        try
        {
            var pid = GameLaunchService.LaunchCnWithPatch(GameArguments(arg));
            var message = I18NManager.Translate("Game.Command.GameCn.DllStarted", pid.ToString());
            Logger.Info(message);
            await arg.SendMsg(mode);
            await arg.SendMsg(message);
        }
        catch (FileNotFoundException ex)
        {
            Logger.Error("CN patch DLL is missing.", ex);
            await arg.SendMsg(I18NManager.Translate("Game.Command.GameCn.DllMissing", ex.Message));
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to launch the specific-channel client with the patch DLL.", ex);
            await arg.SendMsg(I18NManager.Translate("Game.Command.GameCn.DllFailed", ex.Message));
        }
    }

    /// <summary>
    ///     <c>-dll</c> / <c>-hosts</c> win over the configured default. Both tokens are parsed out of
    ///     <see cref="CommandArg.Attributes" />: the leading dash is stripped there, and the raw tokens stay in
    ///     <see cref="CommandArg.Args" /> (see <see cref="GameArguments" />, which drops them again).
    /// </summary>
    private static string ResolveMode(CommandArg arg)
    {
        foreach (var attribute in arg.Attributes)
        {
            if (attribute.Equals("dll", StringComparison.OrdinalIgnoreCase))
            {
                return "dll";
            }

            if (attribute.Equals("hosts", StringComparison.OrdinalIgnoreCase))
            {
                return "hosts";
            }
        }

        return string.Equals(ConfigManager.Config.Loader.CnLaunchMode, "dll", StringComparison.OrdinalIgnoreCase)
            ? "dll"
            : "hosts";
    }

    /// <summary>
    ///     The client must never see the route switches. <see cref="CommandArg.Args" /> keeps the raw
    ///     <c>-dll</c>/<c>-hosts</c> token (only the dash is stripped into <see cref="CommandArg.Attributes" />),
    ///     so forwarding <c>Args</c> verbatim handed the game a switch it does not know.
    /// </summary>
    private static string[] GameArguments(CommandArg arg)
    {
        var arguments = new List<string>(arg.Args.Count);
        foreach (var value in arg.Args)
        {
            if (value.Equals("-dll", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("-hosts", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            arguments.Add(value);
        }

        return arguments.ToArray();
    }
}
