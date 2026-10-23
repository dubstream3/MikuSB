using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.FarmWar;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

// FarmWarActivity.Req_GetQuestAward (FarmWarActivity.lua:906-923). umg_farmwar_mission.lua:72 feeds
// tbData.tbAwardList straight into Item.Gain, so the reply must be ONE FLAT array of
// [G, D, P, L, Count] rows. The ids are the period task board (farmwarout.json TaskID ->
// achievement/activity_quest.json 20400..20434); the claim bit is bit0 of group 7, the same layout
// AchievementClaimService uses.
[CallGSApi("FarmWarLogic_GetQuestAward")]
public class FarmWarLogic_GetQuestAward : CallGSHandler
{
    private static readonly Logger Log = new(nameof(FarmWarLogic_GetQuestAward));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var request = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject
                      ?? new JsonObject();

        var act = FarmWarActivityService.CurrentAct(player);
        var sync = new NtfSyncPlayer();
        var granted = new JsonArray();
        var claimed = 0;

        foreach (var taskId in ReadIds(request["tbIdList"]))
        {
            if (act == null || !act.TaskIds.Contains(taskId))
            {
                Log.Warn($"FarmWar GetQuestAward: task {taskId} is not on the current board.");
                continue;
            }

            var attr = player.Attributes.Get(AttrIds.Achievement.QuestGroupId, taskId);
            var bits = attr?.Val ?? 0;
            if ((bits & 1) != 0)
                continue;

            // bit0 is the claim flag, bits >= 1 carry the task progress written by
            // FarmWarSettlement (AchievementClaimService layout). No progress means the task was
            // never advanced on this account, so a forged claim must not pay out.
            if ((bits & ~1u) == 0)
            {
                Log.Warn($"FarmWar GetQuestAward: task {taskId} has no progress (bits={bits}).");
                continue;
            }

            if (!GameData.ActivityQuestData.TryGetValue(taskId, out var quest))
                continue;

            var rows = AchievementClaimService.ReadRewardRows(quest.Rewards);
            var reward = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);

            var claimAttr = player.Attributes.Set(
                AttrIds.Achievement.QuestGroupId, taskId, bits | 1);
            player.Attributes.SyncTo(sync, claimAttr);

            claimed++;
            foreach (var row in reward)
                granted.Add(row?.DeepClone());
        }

        DatabaseHelper.SaveDatabaseType(player.Data);

        Log.Info($"FarmWar GetQuestAward tasks=[{string.Join(",", ReadIds(request["tbIdList"]))}] " +
                 $"claimed={claimed} rows={granted.Count} for uid={player.Uid}.");

        return CallGSResult.Ok(new JsonObject
        {
            ["tbAwardList"] = granted
        }.ToJsonString(), sync, "FarmWarLogic_GetQuestAward");
    }

    private static List<uint> ReadIds(JsonNode? node)
    {
        var ids = new List<uint>();
        if (node is not JsonArray array)
            return ids;

        foreach (var item in array)
        {
            var value = item is JsonObject json ? ReadUint(json["nId"]) : ReadUint(item);
            if (value != 0 && !ids.Contains(value))
                ids.Add(value);
        }

        return ids;
    }

    private static uint ReadUint(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<uint>(out var parsed))
            return parsed;

        return uint.TryParse(node?.ToString(), out var text) ? text : 0;
    }
}
