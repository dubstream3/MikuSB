using MikuSB.Enums.Player;
using MikuSB.Internationalization;
using MikuSB.Util;

namespace MikuSB.GameServer.Command.Commands;

/// <summary>
///     Removes the fenced block <c>/game-cn</c> writes into the hosts file. The server also drops the
///     block when it shuts down cleanly, but a crash, a kill or a machine that never sees a clean exit
///     leaves it behind, and the block keeps every SDK host name on this machine until it is gone.
///     The same job is available without a running server through <c>MikuSB --clean-hosts</c>.
/// </summary>
[CommandInfo("hosts-clean", "Game.Command.HostsClean.Desc", "Game.Command.HostsClean.Usage",
    ["game-cn-clean", "clean-hosts"], [PermEnum.Admin, PermEnum.Support])]
public class CommandHostsClean : ICommands
{
    private static readonly Logger Logger = new("CommandManager");

    [CommandDefault]
    public async ValueTask Run(CommandArg arg)
    {
        if (!HostsFileService.IsSupported)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.HostsClean.Unsupported"));
            return;
        }

        var result = HostsFileService.Restore();
        switch (result.Status)
        {
            case HostsSyncStatus.Applied:
                // The resolver keeps the old answers for a while, so a client started right after the
                // cleanup would still reach this machine.
                HostsFileService.FlushDnsCache();
                var message = I18NManager.Translate("Game.Command.HostsClean.Removed", HostsFileService.HostsPath);
                Logger.Info(message);
                await arg.SendMsg(message);
                break;
            case HostsSyncStatus.AlreadyCurrent:
                await arg.SendMsg(
                    I18NManager.Translate("Game.Command.HostsClean.NothingToRemove", HostsFileService.HostsPath));
                break;
            case HostsSyncStatus.NotElevated:
                await arg.SendMsg(
                    I18NManager.Translate("Game.Command.HostsClean.NeedAdmin", HostsFileService.HostsPath));
                break;
            case HostsSyncStatus.Failed:
                await arg.SendMsg(I18NManager.Translate("Game.Command.HostsClean.Failed", result.Detail));
                break;
            default:
                await arg.SendMsg(I18NManager.Translate("Game.Command.HostsClean.Unsupported"));
                break;
        }
    }
}
