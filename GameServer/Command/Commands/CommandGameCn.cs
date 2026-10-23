using MikuSB.Enums.Player;
using MikuSB.Internationalization;
using MikuSB.Loader;
using MikuSB.Util;

namespace MikuSB.GameServer.Command.Commands;

[CommandInfo("game-cn", "Game.Command.GameCn.Desc", "Game.Command.GameCn.Usage", [],
    [PermEnum.Admin, PermEnum.Support])]
public class CommandGameCn : ICommands
{
    private static readonly Logger Logger = new("CommandManager");

    [CommandDefault]
    public async ValueTask Launch(CommandArg arg)
    {
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
            var pid = GameLaunchService.LaunchWithoutPatch(arg.Args.ToArray());
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
}
