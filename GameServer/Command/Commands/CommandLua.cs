using System.Text.Json.Nodes;
using MikuSB.Enums.Player;
using MikuSB.GameServer.Server.CallGS;

namespace MikuSB.GameServer.Command.Commands;

// Remote-controls the connected client through the s2c "GM" channel. The client implements that
// script as assert(load(tbParam.code))() (Script/Misc/GM.lua), so whatever we send here runs as
// real Lua inside the game. Handy because synthesised mouse input is fragile in exclusive
// fullscreen: the client can open a UI or finish a level on command instead of by pixel clicking.
//
//   /lua UI.Open('Dlc35Main')            -- open a widget
//   /lua GM.LevelVictory(GetPlayerController())  -- finish the running level
[CommandInfo("lua", "Run Lua inside the connected client.", "Usage: /lua <lua code> [@uid]", ["cl"], [PermEnum.Admin, PermEnum.Support])]
public class CommandLua : ICommands
{
    [CommandDefault]
    public async ValueTask Run(CommandArg arg)
    {
        if (arg.Args.Count == 0)
        {
            await arg.SendMsg("Usage: /lua <lua code> [@uid]");
            return;
        }

        if (!await arg.CheckOnlineTarget())
            return;

        // Args are space-split, so re-join them: the code itself may contain spaces.
        var code = string.Join(' ', arg.Args);
        var payload = new JsonObject { ["code"] = code }.ToJsonString();

        await CallGSRouter.SendScript(arg.Target!, "GM", payload);
        await arg.SendMsg($"Sent {code.Length} char(s) of Lua to uid={arg.Target!.Player!.Data.Uid}.");
    }
}
