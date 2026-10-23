using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Achievement;
using MikuSB.GameServer.Game.StarWish;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

// 星祈心愿 repeat (auto) quests -- the daily task column of the panel.
//
//     request : me:CallGS("StarWish_GetRepeatQuestAward",      { tbTaskID = [id, ...] })
//     reply   : { tbAward = [[G,D,P,L,N], ...], tbID = [id, ...] }
//
//     request : me:CallGS("StarWish_GetRepeatQuestExtraAward", { nPos })
//     reply   : { tbAward = [[G,D,P,L,N], ...] }
//
// tbID is not optional: umg_starwish_autotask.lua:175-181 walks it to flip the claimed rows, and the
// panel reads the quest state from the achievement table (group 7) instead of from the reply, so a
// claim without the echo leaves the button lit.
//
// The three extra awards unlock at "1, 2, 3 repeat quests *received*" (umg_starwish_autotask.lua:
// 95-112 counts Achievement.IsReceive over tbRepeatQuest and caps at 3, then UpdateTaskIcon compares
// nGotNum >= nPos). Claimed bit == received, so the gate is the claimed count, not the finished one.
[CallGSApi("StarWish_GetRepeatQuestAward")]
public class StarWish_GetRepeatQuestAward : CallGSHandler<StarWishIdListParam>
{
    private static readonly Logger Log = new(nameof(StarWish_GetRepeatQuestAward));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, StarWishIdListParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return CallGSResult.Error("error.BadParam");

        var act = StarWishService.SelectAct(DateTime.Now);
        if (act == null || !StarWishService.IsOpen(player))
            return CallGSResult.Ok(new JsonObject { ["tbAward"] = new JsonArray(), ["tbID"] = new JsonArray() });

        var sync = new NtfSyncPlayer();
        var granted = new JsonArray();
        var claimed = new JsonArray();

        foreach (var id in request.Ids)
        {
            if (!act.RepeatQuests.Contains(id))
            {
                Log.Warn($"uid={player.Uid} claimed repeat quest {id}, which is not on period {act.Id}.");
                continue;
            }

            if (!GameData.ActivityQuestData.TryGetValue(id, out var quest))
                continue;

            var rows = AchievementClaimService.ReadRewardRows(quest.Rewards);
            var result = await AchievementClaimService.TryClaimAsync(
                player, AttrIds.Achievement.QuestGroupId, id, quest.CheckValue, rows, sync);

            if (result == null)
                continue;

            foreach (var row in result)
            {
                if (row != null)
                    granted.Add(row.DeepClone());
            }
            claimed.Add(id);
        }

        return CallGSResult.Ok(new JsonObject
        {
            ["tbAward"] = granted,
            ["tbID"] = claimed
        }, sync);
    }
}

[CallGSApi("StarWish_GetRepeatQuestExtraAward")]
public class StarWish_GetRepeatQuestExtraAward : CallGSHandler<StarWishExtraAwardParam>
{
    private static readonly Logger Log = new(nameof(StarWish_GetRepeatQuestExtraAward));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, StarWishExtraAwardParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return CallGSResult.Error("error.BadParam");

        var act = StarWishService.SelectAct(DateTime.Now);
        if (act == null || !StarWishService.IsOpen(player))
            return CallGSResult.Ok(new JsonObject { ["tbAward"] = new JsonArray() });

        if (request.Pos == 0 || request.Pos > 3)
            return CallGSResult.Ok(new JsonObject { ["tbAward"] = new JsonArray() });

        if (StarWishService.IsExtraAwardClaimed(player, request.Pos))
            return CallGSResult.Ok(new JsonObject { ["tbAward"] = new JsonArray() });

        if (StarWishService.ReceivedRepeatQuestCount(player, act) < request.Pos)
        {
            Log.Info($"uid={player.Uid} asked for extra award {request.Pos} before receiving that many quests.");
            return CallGSResult.Ok(new JsonObject { ["tbAward"] = new JsonArray() });
        }

        var index = (int)request.Pos - 1;
        IReadOnlyList<IReadOnlyList<uint>> rows = index < act.RepeatQuestAwards.Count
            ? [act.RepeatQuestAwards[index]]
            : [];

        var sync = new NtfSyncPlayer();
        var granted = rows.Count > 0
            ? await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync)
            : new JsonArray();

        StarWishService.MarkExtraAwardClaimed(player, request.Pos, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return CallGSResult.Ok(new JsonObject { ["tbAward"] = granted }, sync);
    }
}

public sealed class StarWishExtraAwardParam
{
    [JsonPropertyName("nPos")] public uint Pos { get; set; }
}
