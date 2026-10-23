using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// Client side origin: DonoDuelTask:QuickGetReward (DLC/DonoDuel/DonoDuelTask.lua:1072-1090) collects
// every finished but unclaimed mission id and sends {tbIdList}; the callback
// (DonoDuelTask.lua:1093-1114) closes the spinner, flattens tbParam.tbRewards and feeds it straight
// into Item.Gain, so tbRewards must be a list of per mission row lists:
// [[[g,d,p,l,count], ...], [[...]], ...]. Claiming sets bit1 of attribute (186, nId).
// Request:  { "tbIdList": [ <missionId>, ... ] }
// Response: { "tbRewards": [ [[g,d,p,l,count], ...], ... ] }
[CallGSApi("DonoDuelTask_QuickGetReward")]
public class DonoDuelTask_QuickGetReward : CallGSHandler
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var ids = DonoDuelTaskState.ReadIdList(param);
        var sync = new NtfSyncPlayer();
        var perMission = new JsonArray();
        var cardCount = 0;

        foreach (var id in ids)
        {
            if (!GameData.DonoDuelStoryTaskData.TryGetValue(id, out var row))
                continue;

            if (DonoDuelTaskState.IsReceived(player, id))
                continue;

            // Missions without a Reward1 row (e.g. 104) used to be skipped outright, which silently
            // dropped their Reward3 cards: "claim all" marked them too, so the deck never received
            // the hero cards the story promises. Every finished mission is claimed now; an empty
            // Reward1 simply contributes no item rows.
            var rows = DonoDuelTaskState.ReadRewardRows(row.Reward1);
            var granted = rows.Count > 0
                ? await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync)
                : new JsonArray();

            // This callback only flattens tbRewards into item rows and never looks at tbCardId, but
            // the Reward3 cards still have to reach the 185 card bag: otherwise "claim all" marks the
            // missions as claimed while the deck stays empty.
            var cardIds = DonoDuelBag.GrantCards(player, DonoDuelBag.ReadCardRows(row.Reward3), sync);
            DonoDuelTaskState.MarkReceived(player, id, sync);
            perMission.Add(granted);
            cardCount += cardIds.Count;
        }

        if (perMission.Count > 0)
            DonoDuelTaskState.Save(player);

        Logger.GetByClassName().Info(
            $"Donoduel quick reward: {ids.Count} requested, {perMission.Count} mission(s) granted, " +
            $"{cardCount} card(s) for uid={player.Uid}.");

        return CallGSResult.Ok(new JsonObject { ["tbRewards"] = perMission }, sync);
    }
}
