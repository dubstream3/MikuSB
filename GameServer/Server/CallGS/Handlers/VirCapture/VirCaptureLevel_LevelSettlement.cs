using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

// 虚境探索 (vir capture) 探索结束上报.
//
// Client origin: VirCapture:Req_LevelSettlement (DLC/VirCapture/VirCapture.lua:2318-2334, api declared at
// :85) sends {nLevelID, nSeed, tbLog, tbKill, tbMonster} through Reconnect.Send_SettleInfo (:2329), so it
// arrives as Chapter_DealLevelSettlement with sCmd = "VirCaptureLevel_LevelSettlement". It is the report
// of a *normal* exploration exit (VirCapture.lua:785 NoramlExit); giving up sends no settlement at all
// (VirCapture.lua:777 ManualExit).
//
// This api is the low-gap case of the settlement family: the run's real state is already persisted while
// the player plays (VirCaptureLevel_SaveCapture = captures/exp, VirCaptureLevel_SaveFightData = point
// state, VirCaptureLevel_SavePos = position, all in attribute group 128), and the reply callback
// (VirCapture.lua:2336-2343) reads no field at all - it closes the overlay, runs the end-exploration
// callback and clears the local kill table. So the echo fallback the api used to hit was already
// functionally correct; this branch exists so the api is observable in the log, is validated early
// instead of being answered blindly, and no longer depends on the generic echo path.
//
// Deliberately no business write: neither the client (VirCapture.lua:220-330 reads only act id, exp,
// level, bag counters and group 128 slots/points) nor the group 128 layout
// (VirCaptureStateHelper.cs:10-33) has any "this capture map was cleared" slot to record. Inventing one
// would be a slot the client never reads.
internal static class VirCaptureLevelSettlement
{
    private static readonly Logger Logger = new("VirCapture");

    public static ValueTask<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        // The client names the map as nLevelID (not nID, which is what the sibling tower api uses).
        var levelId = ReadUint(tbParam, "nLevelID");
        if (levelId == 0 || !GameData.VirCaptureCaptureRegionData.ContainsKey(levelId))
        {
            Logger.Info(
                $"Vir capture exploration report for uid={player.Uid} named level {levelId}, which is not " +
                "a capture region; nothing written.");
            return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>(
                (new JsonObject { ["sErr"] = "error.BadParam" }, null));
        }

        var seed = ReadUint(tbParam, "nSeed");
        Logger.Info(
            $"Vir capture exploration level {levelId} (seed={seed}) reported by uid={player.Uid}; run " +
            "state was already persisted by SaveCapture/SaveFightData/SavePos, so this is observation only.");

        return ValueTask.FromResult<(JsonNode, NtfSyncPlayer?)>((new JsonObject(), null));
    }

    private static uint ReadUint(JsonNode? node, string key)
    {
        if (node is not JsonObject obj)
            return 0;

        var raw = obj[key]?.ToString().Trim().Trim('[', ']', '"');
        return uint.TryParse(raw, out var value) ? value : 0;
    }
}
