using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Server.Packet.Recv.Login;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.AlienDefense;

// 异星守护 (alien defense / Mow) entry gate. AlienDefense.CheckOpenAct
// (DLC/AlienDefense/AlienDefense.lua:790-795) sends no parameters:
//
//     me:CallGS('AlienDefense_CheckOpenAct')
//
// and the callback (:797-824) reads { bOpen, nId, nStartTime, nEndTime }: it patches the pushed
// window into AlienDefenseConfig.tbTimeList[nId], plays the act's PlotID once, then opens MowMain.
// Without bOpen the client just tips ui.TxtNotOpen.
//
// This handler answers the player's own click only: the same s2c name used to be pushed unsolicited
// at login and on every DLC page entry, which made the client walk into MowMain without being asked
// (that callback opens the UI) and left it stacked under the DLC page. The DLC button now gets its
// window from the AlienDefenseLevelWindows client patch instead, so this stays a pure answer.
// The act id comes from the login seed (143,1), falling back to the pinned DLC's Mow entry.
//
// Request:  {}
// Response: { bOpen, nId, nStartTime, nEndTime }
[CallGSApi("AlienDefense_CheckOpenAct")]
public class AlienDefense_CheckOpenAct : CallGSHandler<AlienDefenseCheckOpenActParam>
{
    private static readonly Logger Log = new(nameof(AlienDefense_CheckOpenAct));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, AlienDefenseCheckOpenActParam request)
    {
        var player = context.Connection.Player;
        var uid = player?.Uid ?? 0;

        // Entering a level group is gated client-side by the per level windows of the local CSV
        // (umg_mow_main.lua:180-191), which still hold the dead act 4 dates. Re-stamp them before the
        // response opens MowMain; AlienDefense.lua:7 has required AlienDefenseLevel by the time this
        // request is sent, so the table is guaranteed to exist here.
        await HandlerReqLogin.SendClientPatch(
            context.Connection, nameof(DlcClientPatches.AlienDefenseLevelWindows), DlcClientPatches.AlienDefenseLevelWindows);

        var actId = player != null
            ? player.Attributes.GetValue(AttrIds.AlienDefense.Gid, AttrIds.AlienDefense.ActivitySid)
            : 0;
        if (actId == 0)
            actId = DlcRuntimeOverrides.SelectAlienDefenseActId();

        if (actId == 0)
        {
            Log.Warn($"Alien defense open check with no act for uid={uid}.");
            return CallGSResult.Ok(new JsonObject { ["bOpen"] = false });
        }

        var (start, end) = DlcRuntimeOverrides.Window();
        var payload = DlcRuntimeOverrides.BuildAlienDefenseTimePayload(actId, start, end);

        Log.Info($"Alien defense open check by uid={uid} (act={actId}).");
        return CallGSResult.Ok(payload);
    }
}

public sealed class AlienDefenseCheckOpenActParam
{
}
