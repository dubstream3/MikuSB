using MikuSB.Enums.Player;
using MikuSB.Internationalization;
using MikuSB.Loader;
using MikuSB.Util;

namespace MikuSB.GameServer.Command.Commands;

[CommandInfo("game", "Game.Command.Game.Desc", "Game.Command.Game.Usage", [], [PermEnum.Admin, PermEnum.Support])]
public class CommandGame : ICommands
{
    private static readonly Logger Logger = new("CommandManager");

    [CommandDefault]
    public async ValueTask Launch(CommandArg arg)
    {
        try
        {
            var pid = GameLaunchService.Launch(arg.Args.ToArray());
            var message = I18NManager.Translate("Game.Command.Game.Started", pid.ToString());
            Logger.Info(message);
            await arg.SendMsg(message);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to launch game.", ex);
            await arg.SendMsg(I18NManager.Translate("Game.Command.Game.Failed", ex.Message));
        }
    }
}
