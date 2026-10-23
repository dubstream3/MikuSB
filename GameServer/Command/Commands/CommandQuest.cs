using MikuSB.Enums.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

[CommandInfo("quest", "Complete all quest levels for testing.", "Usage: /quest complete_all <1|0|on|off>", ["q"], [PermEnum.Admin, PermEnum.Support])]
public class CommandQuest : ICommands
{
    [CommandMethod("complete_all")]
    public async ValueTask CompleteAll(CommandArg arg)
    {
        if (!await arg.CheckArgCnt(1))
            return;

        var option = arg.Args[0].ToLowerInvariant();
        var shouldComplete = option switch
        {
            "1" or "on" => true,
            "0" or "off" => false,
            _ => (bool?)null
        };

        if (shouldComplete == null)
        {
            await arg.SendMsg("Usage: /quest complete_all <1|0|on|off>");
            return;
        }

        if (!await arg.CheckOnlineTarget()) return;

        var player = arg.Target!.Player!;
        var result = await player.QuestManager.SetAllLevelsForTestingAsync(shouldComplete.Value);
        await player.SendPacket(CmdIds.NtfSyncAttr, result.Sync);
        var state = shouldComplete.Value ? "completed" : "reset";
        await arg.SendMsg($"Quest levels {state}: {result.LevelCount}. No rewards were granted.");
    }
}
