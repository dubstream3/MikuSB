using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

// Standard achievement claim (Achievement/Achievement.lua:824 GetReward -> :846
// `me:CallGS("Achievement_GetReward", json.encode({nId = nId}))` after UI.ShowConnection()).
//
// The client pre-checks IsReceive/IsFinished locally and its callback (:1132-1140) closes the
// connection overlay and hands tbParam to the panel, which reads tbParam.tbRewards
// ([ [G, D, P, L, Count], ... ]) exactly like the DLC board does (AchievementDLC_GetReward).
//
// Rows live in achievement/achievement.json (group 2). Achievement.GetExtraReward (:793, the
// per-chapter bonus whose Group is GROUP_ExtraReward = 5) uses the same api, so ids are resolved in
// the standard table first and then in the activity quest table (group 7) for safety.
//
// Request:  { nId }
// Response: { tbRewards: [ [G, D, P, L, Count], ... ] }
[CallGSApi("Achievement_GetReward")]
public class Achievement_GetReward : CallGSHandler<AchievementGetRewardParam>
{
    protected override async Task<CallGSResult> HandleAsync(
        CallGSContext context,
        AchievementGetRewardParam request)
    {
        var player = context.Connection.Player!;
        if (request.Id == 0)
            return CallGSResult.Error("error.BadParam");

        uint groupId;
        uint checkValue;
        IReadOnlyList<IReadOnlyList<uint>> rewards;
        if (GameData.AchievementData.TryGetValue(request.Id, out var achievement))
        {
            groupId = AchievementClaimService.NormalGroupId;
            checkValue = achievement.CheckValue;
            rewards = AchievementClaimService.ReadRewardRows(achievement.Rewards);
        }
        else if (GameData.ActivityQuestData.TryGetValue(request.Id, out var quest))
        {
            groupId = AchievementClaimService.QuestGroupId;
            checkValue = quest.CheckValue;
            rewards = AchievementClaimService.ReadRewardRows(quest.Rewards);
        }
        else
        {
            return CallGSResult.Error("error.BadParam");
        }

        if (AchievementClaimService.IsClaimed(player, groupId, request.Id))
            return CallGSResult.Error("achievement.RewardGeted");

        if (!AchievementClaimService.IsFinished(player, groupId, request.Id, checkValue))
            return CallGSResult.Error("achievement.NotFinished");

        var sync = new NtfSyncPlayer();
        var granted = await AchievementClaimService.TryClaimAsync(
            player, groupId, request.Id, checkValue, rewards, sync);

        var payload = new JsonObject { ["tbRewards"] = granted ?? new JsonArray() };
        return CallGSResult.Ok(payload, sync);
    }
}

public sealed class AchievementGetRewardParam
{
    [JsonPropertyName("nId")] public uint Id { get; set; }
}
