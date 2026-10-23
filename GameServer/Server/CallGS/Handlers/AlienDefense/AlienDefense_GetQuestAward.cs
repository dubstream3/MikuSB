using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.AlienDefense;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.AlienDefense;

// 异星守护 (alien defense / Mow) mission award claim. AlienDefense.GetQuestAward
// (DLC/AlienDefense/AlienDefense.lua:1062-1066) sends the client's pre-filtered claimable list:
//
//     { tbIdList: [20001, ...] }
//
// and the callback (:1068-1072) forwards the response to MowReward.OnRspGetQuestAward, which shows
// ret.sError as a message and otherwise feeds ret.tbAwardList into Item.Gain
// (umg_mow_reward.lua:113-122). Only rows of the current act's NormalMission allowlist are payable;
// unfinished or already-claimed rows are skipped with a log line, mirroring the 弑神斗场 guard.
//
// Request:  { tbIdList: [...] }
// Response: { tbAwardList: [[g, d, p, l, count], ...] }
[CallGSApi("AlienDefense_GetQuestAward")]
public class AlienDefense_GetQuestAward : CallGSHandler<AlienDefenseGetQuestAwardParam>
{
    private static readonly Logger Log = new(nameof(AlienDefense_GetQuestAward));

    protected override async Task<CallGSResult> HandleAsync(
        CallGSContext context,
        AlienDefenseGetQuestAwardParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return CallGSResult.Error("error.BadParam");

        var ids = request.QuestIds ?? [];
        if (ids.Count == 0)
            return CallGSResult.Ok(new JsonObject { ["tbAwardList"] = new JsonArray() });

        var actId = player.Attributes.GetValue(AttrIds.AlienDefense.Gid, AttrIds.AlienDefense.ActivitySid);
        var sync = new NtfSyncPlayer();
        var granted = await AlienDefenseQuestService.ClaimAsync(player, ids, actId, sync);

        return CallGSResult.Ok(new JsonObject { ["tbAwardList"] = granted }, sync);
    }
}

public sealed class AlienDefenseGetQuestAwardParam
{
    [JsonPropertyName("tbIdList")] public List<uint>? QuestIds { get; set; }
}
