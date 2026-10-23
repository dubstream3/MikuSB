using MikuSB.Enums.Player;
using MikuSB.Internationalization;
using MikuSB.Util.Extensions;

namespace MikuSB.GameServer.Command.Commands;

[CommandInfo("help", "Game.Command.Help.Desc", "Game.Command.Help.Usage", ["h"], [PermEnum.Support, PermEnum.Trial])]
public class CommandHelp : ICommands
{
    [CommandDefault]
    public async static ValueTask Help(CommandArg arg)
    {
        if (arg.Args.Count == 1)
        {
            var cmd = arg.Args[0];
            if (CommandManager.CommandInfo == null || !CommandManager.CommandInfo.TryGetValue(cmd, out var command))
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.CommandNotFound"));
                return;
            }

            var msg =
                $"/{command.Name} - {I18NManager.Translate(command.Description)}\n{I18NManager.Translate(command.Usage)}";
            if (command.Alias.Length > 0)
                msg +=
                    $"\n{I18NManager.Translate("Game.Command.Help.CommandAlias")} {command.Alias.ToList().ToArrayString()}";
            if (command.Perm != null)
                msg += $"\n{I18NManager.Translate("Game.Command.Help.CommandPermission")} {string.Join(", ", command.Perm.Select(perm => perm.ToString()))}";

            await arg.SendMsg(msg + "\n");
            return;
        }
        else
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Help.Commands"));
            if (CommandManager.CommandInfo == null) return;

            foreach (var command in CommandManager.CommandInfo.Values)
            {
                var msg =
                    $"/{command.Name} - {I18NManager.Translate(command.Description)}\n{I18NManager.Translate(command.Usage)}";
                if (command.Alias.Length > 0)
                    msg +=
                        $"\n{I18NManager.Translate("Game.Command.Help.CommandAlias")} {command.Alias.ToList().ToArrayString()}";

                if (command.Perm != null)
                    msg += $"\n{I18NManager.Translate("Game.Command.Help.CommandPermission")} {string.Join(", ", command.Perm.Select(perm => perm.ToString()))}";
                await arg.SendMsg(msg + "\n");
            }
        }
    }
}