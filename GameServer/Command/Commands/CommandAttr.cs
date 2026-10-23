using MikuSB.Enums.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Command.Commands;

// Direct lever for debugging client-side state that is driven by a single attribute, e.g.
// the DLC entry point reads (gid 15, sid 1) -> /attr set 15 1 35 @4
[CommandInfo("attr", "Read or write a player attribute.", "Usage: /attr set <gid> <sid> <value> [@uid] | /attr get <gid> <sid> [@uid]", ["a"], [PermEnum.Admin, PermEnum.Support])]
public class CommandAttr : ICommands
{
    [CommandMethod("set")]
    public async ValueTask Set(CommandArg arg)
    {
        if (!await arg.CheckArgCnt(3))
            return;

        var gid = (uint)arg.GetInt(0);
        var sid = (uint)arg.GetInt(1);
        var value = (uint)arg.GetInt(2);

        if (!await arg.CheckOnlineTarget()) return;

        var player = arg.Target!.Player!;
        var attr = player.Attributes.Set(gid, sid, value);
        var sync = new NtfSyncPlayer();
        player.Attributes.SyncTo(sync, attr);
        await player.SendPacket(CmdIds.NtfSyncAttr, sync);
        await arg.SendMsg($"Attribute ({gid},{sid}) = {value} for uid={player.Data.Uid}.");
    }

    [CommandMethod("get")]
    public async ValueTask Get(CommandArg arg)
    {
        if (!await arg.CheckArgCnt(2))
            return;

        var gid = (uint)arg.GetInt(0);
        var sid = (uint)arg.GetInt(1);

        if (!await arg.CheckOnlineTarget()) return;

        var player = arg.Target!.Player!;
        await arg.SendMsg($"Attribute ({gid},{sid}) = {player.Attributes.GetValue(gid, sid)} for uid={player.Data.Uid}.");
    }
}
