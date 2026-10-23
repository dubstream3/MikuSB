using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.WorldBoss;

// 世界BOSS task reward claim (Challenge/WorldBoss/WorldBoss.lua:781-797). The panel collects the
// finished rows client side and sends { tbTaskID = [...] } behind a connection overlay; the callback
// feeds tbParam.tbAward straight into Item.Gain, so the reply has to be ONE FLAT array of
// [G, D, P, L, Count] rows (a nested per task array would be indexed as numbers by Item.Gain).
//
// The rows live in achievement/activity_quest.json (ids 21001..21020) and are packed with the
// achievement bit layout in group 7, which AchievementClaimService already owns; a row that is
// already claimed or not finished is simply skipped, so the button stays enabled but grants nothing.
[CallGSApi("WorldBossLogic_GetReward")]
public class WorldBossLogic_GetReward : CallGSHandler
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var request = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject
                      ?? new JsonObject();

        var taskIds = ReadTaskIds(request["tbTaskID"]);
        var sync = new NtfSyncPlayer();
        var granted = new JsonArray();
        var claimed = 0;

        foreach (var taskId in taskIds)
        {
            if (!GameData.ActivityQuestData.TryGetValue(taskId, out var quest))
            {
                Logger.GetByClassName().Warn($"WorldBoss GetReward: no activity_quest row {taskId}.");
                continue;
            }

            var rows = AchievementClaimService.ReadRewardRows(quest.Rewards);
            var reward = await AchievementClaimService.TryClaimAsync(
                player, AttrIds.Achievement.QuestGroupId, taskId, quest.CheckValue, rows, sync);

            if (reward == null)
                continue;

            claimed++;
            foreach (var row in reward)
                granted.Add(row?.DeepClone());
        }

        Logger.GetByClassName().Info(
            $"WorldBoss GetReward tasks=[{string.Join(",", taskIds)}] claimed={claimed} " +
            $"rows={granted.Count} for uid={player.Uid}.");

        return CallGSResult.Ok(new JsonObject
        {
            ["tbAward"] = granted
        }.ToJsonString(), sync, "WorldBossLogic_GetReward");
    }

    private static List<uint> ReadTaskIds(JsonNode? node)
    {
        var ids = new List<uint>();
        if (node is not JsonArray array)
            return ids;

        foreach (var item in array)
        {
            var id = item is JsonObject json ? ReadUint(json["nId"]) : ReadUint(item);
            if (id != 0 && !ids.Contains(id))
                ids.Add(id);
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
