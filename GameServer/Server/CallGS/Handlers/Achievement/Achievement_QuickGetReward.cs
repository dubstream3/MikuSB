using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Achievement;

// One-key achievement claim (Achievement/Achievement.lua:851 QuickGetReward -> :913
// `me:CallGS("Achievement_QuickGetReward", json.encode({tbIdList, nType, tbPointList?}))`).
//
// The client builds tbIdList from its own config walk (every row in that group that is in its time
// window, has its PreID satisfied and is finished-but-unclaimed), so the server only has to claim the
// ids it recognises. The callback (:1143-1167) flattens tbParam.tbRewards (an array per id) into one
// list for the reward popup, so the reply shape is an array of row-lists.
//
// Request:  { tbIdList: [id, ...], nType: <group>, tbPointList?: [...] }
// Response: { tbRewards: [ [ [G,D,P,L,Count], ... ], ... ] }
[CallGSApi("Achievement_QuickGetReward")]
public class Achievement_QuickGetReward : CallGSHandler<AchievementQuickGetRewardParam>
{
    /// <summary>Defensive cap — the client's list is a few dozen ids at most.</summary>
    private const int MaxIds = 200;

    protected override async Task<CallGSResult> HandleAsync(
        CallGSContext context,
        AchievementQuickGetRewardParam request)
    {
        var player = context.Connection.Player!;
        if (request.IdList == null || request.IdList.Count == 0)
            return CallGSResult.Ok(new JsonObject { ["tbRewards"] = new JsonArray() });

        var sync = new NtfSyncPlayer();
        var allRewards = new JsonArray();
        var claimed = 0;

        foreach (var id in request.IdList)
        {
            if (claimed >= MaxIds)
                break;

            uint groupId;
            uint checkValue;
            IReadOnlyList<IReadOnlyList<uint>> rewards;
            if (GameData.AchievementData.TryGetValue(id, out var achievement))
            {
                groupId = AchievementClaimService.NormalGroupId;
                checkValue = achievement.CheckValue;
                rewards = AchievementClaimService.ReadRewardRows(achievement.Rewards);
            }
            else if (GameData.ActivityQuestData.TryGetValue(id, out var quest))
            {
                groupId = AchievementClaimService.QuestGroupId;
                checkValue = quest.CheckValue;
                rewards = AchievementClaimService.ReadRewardRows(quest.Rewards);
            }
            else
            {
                continue;
            }

            var granted = await AchievementClaimService.TryClaimAsync(
                player, groupId, id, checkValue, rewards, sync);
            if (granted == null)
                continue;

            allRewards.Add(granted);
            claimed++;
        }

        if (claimed > 0)
            DatabaseHelper.SaveDatabaseType(player.Data);

        var payload = new JsonObject { ["tbRewards"] = allRewards };
        return CallGSResult.Ok(payload, sync);
    }
}

public sealed class AchievementQuickGetRewardParam
{
    [JsonPropertyName("tbIdList")] public List<uint>? IdList { get; set; }

    [JsonPropertyName("nType")] public uint Type { get; set; }
}
