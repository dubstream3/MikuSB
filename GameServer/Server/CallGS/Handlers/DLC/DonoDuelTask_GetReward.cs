using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// Client side origin: DonoDuelTask:DoReqGetReward (DLC/DonoDuel/DonoDuelTask.lua:1017-1048) sends
// {nId}; the callback (DonoDuelTask.lua:1050-1070) hands the whole answer to ShowMissionReward1Pop,
// which reads tbParam.tbRewards.tbItems into Item.Gain and then tbParam.tbRewards.tbCardId
// (DonoDuelTask.lua:610-657). Claiming is recorded as bit1 of attribute (186, nId).
// Request:  { "nId": <missionId> }
// Response: { "nId": <missionId>, "tbRewards": { "tbItems": [[g,d,p,l,count], ...],
//                                                 "tbCardId": [<bag block id>, ...] } }
// tbCardId is not optional: Reward3 is where the mission's cards live (mission 101 alone hands out
// cards 1/5/9) and the third popup is the only place the player ever sees them.
[CallGSApi("DonoDuelTask_GetReward")]
public class DonoDuelTask_GetReward : CallGSHandler
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var id = DonoDuelTaskState.ReadMissionId(param);
        if (id == 0 || !GameData.DonoDuelStoryTaskData.TryGetValue(id, out var row))
            return CallGSResult.Ok("{}");

        // Already claimed: answer with the id only so the client still refreshes its npc state.
        if (DonoDuelTaskState.IsReceived(player, id))
            return CallGSResult.Ok(new JsonObject { ["nId"] = id });

        var sync = new NtfSyncPlayer();
        var rows = DonoDuelTaskState.ReadRewardRows(row.Reward1);
        var granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);

        // Reward3 is the card part of the reward ([templateId, star, quality]) and lives in the 185
        // card bag instead of the inventory. The callback resolves every tbCardId entry through
        // DonoDuel:GetBagItemAndConfig, i.e. it wants the bag block ids we just allocated.
        var cardIds = DonoDuelBag.GrantCards(player, DonoDuelBag.ReadCardRows(row.Reward3), sync);
        var cardArray = new JsonArray();
        foreach (var cardId in cardIds)
            cardArray.Add(JsonValue.Create(cardId));

        DonoDuelTaskState.MarkReceived(player, id, sync);
        DonoDuelTaskState.Save(player);

        Logger.GetByClassName().Info(
            $"Donoduel mission {id} reward: {rows.Count} configured row(s), {granted.Count} granted, " +
            $"{cardIds.Count} card(s) for uid={player.Uid}.");

        return CallGSResult.Ok(new JsonObject
        {
            ["nId"] = id,
            ["tbRewards"] = new JsonObject
            {
                ["tbItems"] = granted,
                ["tbCardId"] = cardArray
            }
        }, sync);
    }
}
