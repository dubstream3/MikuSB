using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.StarWish;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.StarWish;

// 星祈心愿 story quest track (DLC/StarWish/StarWishLogic.lua:1047-1180).
//
// Three different progress directions live here and the client mixes them freely:
//   * SetSubIDCompleted            -- one sub step of a quest finished (bitmap at 102+3*id)
//   * SetStoryQuestCompleted       -- the whole quest finished; the client also fires the quest's
//                                     end-of-story side effects (dorm mask, StartPV/EndPV)
//   * AutoCompleteStoryQuest       -- "skip" button: same as above plus bAuto so the widget suppresses
//                                     the fight-entry animation
// Value keys are case sensitive on the wire: SetSubIDCompleted takes a lowercase `v`, while
// SetStoryQuestCompleted takes an uppercase `Value`. Do not "normalize" them.
//
// Reply fields are echo-only (the widgets compare against what they already hold), except for
// GetStoryQuestAward where tbAward is gained and tbID decides which rows flip to "claimed".
//
// bAuto is the one field that is not merely echoed: SetStoryQuestCompleted's callback reuses the
// AutoCompleteStoryQuest handler (StarWishLogic.lua:1082) and only refreshes the quest list when
// bAuto is true, so both apis answer bAuto = true.
[CallGSApi("StarWish_GetStoryQuestAward")]
public class StarWish_GetStoryQuestAward : CallGSHandler<StarWishIdListParam>
{
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
            if (!GameData.StarWishStoryQuestData.TryGetValue(id, out var quest))
            {
                Logger.GetByClassName().Warn($"uid={player.Uid} asked for story quest {id}, which is not in the table.");
                continue;
            }

            if (StarWishService.IsStoryQuestClaimed(player, id))
                continue;

            // The quest has to be finished locally (sub steps done) before its reward can be claimed.
            if (!StarWishService.IsStoryQuestCompleted(player, quest))
                continue;

            if (quest.Awards.Count > 0)
            {
                var rows = await player.RewardManager.GrantConfiguredRewardsAsync(quest.Awards, sync);
                foreach (var row in rows)
                {
                    if (row != null)
                        granted.Add(row.DeepClone());
                }
            }

            StarWishService.Set(player, StarWishService.StoryProgressSid(id) + 1, 1, sync);
            claimed.Add(id);
        }

        DatabaseHelper.SaveDatabaseType(player.Data);

        return CallGSResult.Ok(new JsonObject
        {
            ["tbAward"] = granted,
            ["tbID"] = claimed
        }, sync);
    }
}

[CallGSApi("StarWish_SetStoryQuestCompleted")]
public class StarWish_SetStoryQuestCompleted : CallGSHandler<StarWishSetStoryQuestCompletedParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, StarWishSetStoryQuestCompletedParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        if (!GameData.StarWishStoryQuestData.TryGetValue(request.Id, out var quest))
            return Task.FromResult(CallGSResult.Ok(new JsonObject
            {
                ["nID"] = request.Id,
                ["bShowPop"] = request.ShowPop,
                ["bAuto"] = true
            }));

        // Cap the reported progress at the table value: the client normally sends CheckValue, and a
        // forged larger number would only make the row claimable earlier than intended.
        var value = request.Value == 0 ? quest.CheckValue : Math.Min(request.Value, quest.CheckValue);

        var sync = new NtfSyncPlayer();
        StarWishService.SetStoryProgress(player, quest.Id, value, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nID"] = quest.Id,
            ["bShowPop"] = request.ShowPop,
            ["bAuto"] = true
        }, sync));
    }
}

[CallGSApi("StarWish_AutoCompleteStoryQuest")]
public class StarWish_AutoCompleteStoryQuest : CallGSHandler<StarWishIdParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, StarWishIdParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        if (!GameData.StarWishStoryQuestData.TryGetValue(request.Id, out var quest))
            return Task.FromResult(CallGSResult.Ok(new JsonObject
            {
                ["nID"] = request.Id,
                ["bAuto"] = true
            }));

        var sync = new NtfSyncPlayer();
        StarWishService.SetStoryProgress(player, quest.Id, quest.CheckValue, sync);

        // The sub-step bitmap drives the "fight" rows; skipping the quest means all of them are done.
        for (var pos = 0u; pos < quest.CheckValue; pos++)
            StarWishService.SetSubStep(player, quest.Id, pos, true, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nID"] = quest.Id,
            ["bAuto"] = true
        }, sync));
    }
}

[CallGSApi("StarWish_SetSubIDCompleted")]
public class StarWish_SetSubIDCompleted : CallGSHandler<StarWishSetSubParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, StarWishSetSubParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        if (!GameData.StarWishStoryQuestData.TryGetValue(request.Id, out var quest) ||
            request.Pos >= quest.CheckValue)
            return Task.FromResult(CallGSResult.Ok(new JsonObject
            {
                ["nID"] = request.Id,
                ["nPos"] = request.Pos,
                ["v"] = request.Value,
                ["bShowPop"] = request.ShowPop
            }));

        var sync = new NtfSyncPlayer();
        StarWishService.SetSubStep(player, quest.Id, request.Pos, request.Value != 0, sync);

        // Sub steps are the progress of the quest: the widget greys the claim button from the quest
        // progress slot, so keep it in step with the bitmap instead of waiting for the explicit
        // SetStoryQuestCompleted call.
        var done = 0u;
        for (var pos = 0u; pos < quest.CheckValue; pos++)
        {
            if (StarWishService.IsSubStepDone(player, quest.Id, pos))
                done++;
        }

        if (done > 0)
            StarWishService.SetStoryProgress(player, quest.Id, done, sync);

        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nID"] = request.Id,
            ["nPos"] = request.Pos,
            ["v"] = request.Value,
            ["bShowPop"] = request.ShowPop
        }, sync));
    }
}

[CallGSApi("StarWish_CleanSubIDCompleted")]
public class StarWish_CleanSubIDCompleted : CallGSHandler<StarWishIdParam>
{
    // No client code ever sends this (StarWishLogic declares the sender but nothing calls it), so the
    // handler just clears the bitmap in case a future build starts using it.
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, StarWishIdParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var sync = new NtfSyncPlayer();
        StarWishService.Set(player, StarWishService.StoryProgressSid(request.Id) + 2, 0, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

[CallGSApi("StarWish_GetStoryPageAward")]
public class StarWish_GetStoryPageAward : CallGSHandler<StarWishIdParam>
{
    // One page (storyquest_page.json) groups four story quests and unlocks when all four are done.
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, StarWishIdParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return CallGSResult.Error("error.BadParam");

        if (!GameData.StarWishStoryPageData.TryGetValue(request.Id, out var page))
        {
            Logger.GetByClassName().Warn($"uid={player.Uid} asked for story page {request.Id}, which is not in the table.");
            return CallGSResult.Ok(new JsonObject { ["tbAward"] = new JsonArray() });
        }

        if (StarWishService.IsStoryPageClaimed(player, page.Id))
            return CallGSResult.Ok(new JsonObject { ["tbAward"] = new JsonArray() });

        foreach (var questId in page.Quests)
        {
            if (!GameData.StarWishStoryQuestData.TryGetValue(questId, out var quest))
                continue;

            if (!StarWishService.IsStoryQuestCompleted(player, quest))
                return CallGSResult.Ok(new JsonObject { ["tbAward"] = new JsonArray() });
        }

        var sync = new NtfSyncPlayer();
        var granted = page.Awards.Count > 0
            ? await player.RewardManager.GrantConfiguredRewardsAsync(page.Awards, sync)
            : new JsonArray();

        StarWishService.MarkStoryPageClaimed(player, page.Id, sync);
        DatabaseHelper.SaveDatabaseType(player.Data);

        return CallGSResult.Ok(new JsonObject { ["tbAward"] = granted }, sync);
    }
}

public sealed class StarWishIdParam
{
    [JsonPropertyName("nID")] public uint Id { get; set; }
}

public sealed class StarWishIdListParam
{
    [JsonPropertyName("tbID")] public List<uint> Ids { get; set; } = [];
}

public sealed class StarWishSetStoryQuestCompletedParam
{
    [JsonPropertyName("nID")] public uint Id { get; set; }

    /// <summary>Uppercase on purpose -- the wire key is `Value` for this api only.</summary>
    [JsonPropertyName("Value")] public uint Value { get; set; }

    [JsonPropertyName("bShowPop")] public bool ShowPop { get; set; }
}

public sealed class StarWishSetSubParam
{
    [JsonPropertyName("nID")] public uint Id { get; set; }
    [JsonPropertyName("nPos")] public uint Pos { get; set; }

    /// <summary>Lowercase on purpose -- the wire key is `v` for this api.</summary>
    [JsonPropertyName("v")] public uint Value { get; set; }

    [JsonPropertyName("bShowPop")] public bool ShowPop { get; set; }
}
