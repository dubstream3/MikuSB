using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Database;
using MikuSB.GameServer.Game.GirlRush;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.GirlRush;

// 异绪消解 (DLC27) entry flow. GirlRush/UMG/GirlRush/umg_girlrush_role.lua:51-63 saves the loadout
// first and only launches the fight from the save callback (:489-499 OnSaveData -> Launch.Start), while
// Launch/Classes/GirlRushLevelLogic.lua:33-34 asks for a seed and opens the map once it arrives. Both
// requests open a connection overlay, so a missing handler leaves the player stuck on a spinner.
[CallGSApi("GirlRush_SaveData")]
public class GirlRush_SaveData : CallGSHandler<GirlRushSaveDataParam>
{
    private static readonly Logger Log = new(nameof(GirlRush_SaveData));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, GirlRushSaveDataParam request)
    {
        var player = context.Connection.Player!;
        if (!GirlRushService.IsSeasonLevel(request.LevelId))
        {
            Log.Warn($"GirlRush_SaveData: stage {request.LevelId} is not in the season.");
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var sync = new NtfSyncPlayer();
        GirlRushService.SaveSelection(
            player, request.LevelId, request.RoleId, request.WeaponIndex, request.SkillIndex, request.FireModel, sync);
        GirlRushService.Persist(player);

        // OnSaveData takes no arguments; the client only needs the reply to advance to Launch.Start().
        return Task.FromResult(CallGSResult.Ok(new JsonObject(), sync));
    }
}

[CallGSApi("GirlRush_EnterLevel")]
public class GirlRush_EnterLevel : CallGSHandler<GirlRushEnterLevelParam>
{
    private static readonly Logger Log = new(nameof(GirlRush_EnterLevel));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, GirlRushEnterLevelParam request)
    {
        var player = context.Connection.Player!;

        // The callback only reads sErr and nSeed (GirlRush/GirlRush.lua:463-472); the stage config and
        // the map id are already on the client, the seed just has to be stable for the settlement call.
        var error = GirlRushService.EntryError(player, request.LevelId);
        if (error != null)
        {
            Log.Warn($"GirlRush_EnterLevel: stage {request.LevelId} rejected with {error}.");
            return Task.FromResult(CallGSResult.Error(error));
        }

        return Task.FromResult(CallGSResult.Ok(new JsonObject
        {
            ["nSeed"] = GirlRushService.NewSeed()
        }));
    }
}

// umg_girlrush_reward.lua:15 sends nIndex = -1 for the quick claim button and a single index for the
// per mission button; the reply is fed straight into Item.Gain (umg_girlrush_reward.lua:117-121), so the
// payload has to be one flat array of [G, D, P, L, Count] rows.
[CallGSApi("GirlRush_GetAward")]
public class GirlRush_GetAward : CallGSHandler<GirlRushGetAwardParam>
{
    private static readonly Logger Log = new(nameof(GirlRush_GetAward));

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, GirlRushGetAwardParam request)
    {
        var player = context.Connection.Player!;
        var seasonId = GirlRushService.SeasonId;
        var sync = new NtfSyncPlayer();
        var granted = new JsonArray();

        if (seasonId == 0)
            return CallGSResult.Ok(new JsonObject { ["tbAward"] = granted });

        // The quick claim button sends -1, so the parameter stays signed: binding a negative JSON
        // number to a uint throws inside the dispatcher and the caller ends up with the router's
        // ui.TxtNotOpen fallback instead of a payload (that is exactly how the first smoke run failed).
        var claimAll = request.Index < 0;

        foreach (var award in GirlRushService.Awards(seasonId))
        {
            if (!claimAll && award.Index != (uint)request.Index)
                continue;

            if (GirlRushService.IsAwardClaimed(player, award.Index))
                continue;

            if (!GirlRushService.Satisfies(player, award))
            {
                if (!claimAll)
                    return CallGSResult.Error("ui.TxtNotOpen");
                continue;
            }

            var paid = await player.RewardManager.GrantConfiguredRewardsAsync(award.AwardRows, sync);
            GirlRushService.MarkAwardClaimed(player, award.Index, sync);
            GirlRushService.Persist(player);

            if (paid is JsonArray rows)
            {
                foreach (var row in rows)
                {
                    if (row is not null)
                        granted.Add(row.DeepClone());
                }
            }

            if (!claimAll)
                break;
        }

        Log.Info($"GirlRush_GetAward: index={request.Index} -> {granted.Count} row(s) for uid={player.Uid}.");
        return CallGSResult.Ok(new JsonObject { ["tbAward"] = granted }, sync);
    }
}

// GirlRush.Req_LevelFail (GirlRush/GirlRush.lua:577-589) never registers an s2c callback - the client
// only reports the loss and closes the panel itself, so an answer would hit Misc/S2C.lua:29-36 and
// raise a Lua error.
[CallGSApi("GirlRush_LevelFail")]
public class GirlRush_LevelFail : CallGSHandler<GirlRushEnterLevelParam>
{
    private static readonly Logger Log = new(nameof(GirlRush_LevelFail));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, GirlRushEnterLevelParam request)
    {
        Log.Info($"GirlRush_LevelFail: stage {request.LevelId} for uid={context.Connection.Player?.Uid}.");
        return Task.FromResult(CallGSResult.NoResponse());
    }
}

public sealed class GirlRushSaveDataParam
{
    [JsonPropertyName("nLeveId")] public uint LevelId { get; set; }
    [JsonPropertyName("nRoleId")] public uint RoleId { get; set; }
    [JsonPropertyName("nWeaponIdx")] public uint WeaponIndex { get; set; }
    [JsonPropertyName("nSkillIdx")] public uint SkillIndex { get; set; }
    [JsonPropertyName("nFireModel")] public uint FireModel { get; set; }
}

public sealed class GirlRushEnterLevelParam
{
    [JsonPropertyName("nID")] public uint LevelId { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}

public sealed class GirlRushGetAwardParam
{
    /// <summary>Board mission index; -1 means "claim every finished mission" (umg_girlrush_reward.lua:15).</summary>
    [JsonPropertyName("nIndex")] public int Index { get; set; }
}

/// <summary>
/// <c>GirlRush_LevelSettlement</c> travels through Reconnect.Send_SettleInfo
/// (GirlRush/GirlRush.lua:539-574), so it never reaches CallGS directly - Chapter_DealLevelSettlement
/// dispatches it here. The callback ignores the payload entirely (Launch/Classes/GirlRushLevelLogic.lua
/// :48-53 just opens the Success panel), but the client blocks on the reply for up to 10 seconds before
/// it drops the connection, so an answer must always be produced.
///
/// What has to be recorded: the stage clear counter (Launch.GPASSID) and the best rank of the run
/// (GirlRush.GID bits 26..30, fed by the client's nScoreLevel). Mission progress on the reward board is
/// derived from those two every time the panel opens, so no extra bookkeeping is needed.
/// </summary>
public static class GirlRushSettlement
{
    private static readonly Logger Log = new(nameof(GirlRushSettlement));

    public static Task<(JsonNode Payload, NtfSyncPlayer? Sync)> HandleSettlementAsync(
        PlayerInstance player,
        JsonNode? tbParam)
    {
        var levelId = ReadUint(tbParam, "nID");
        var scoreLevel = ReadUint(tbParam, "nScoreLevel");

        if (!GirlRushService.IsSeasonLevel(levelId))
        {
            Log.Warn($"GirlRush settlement: stage {levelId} is not in the season.");
            return Task.FromResult<(JsonNode, NtfSyncPlayer?)>((new JsonObject { ["sErr"] = "error.BadParam" }, null));
        }

        var sync = new NtfSyncPlayer();
        var pass = GirlRushService.AddPass(player, levelId, sync);
        var improved = GirlRushService.RecordRank(player, levelId, scoreLevel, sync);
        GirlRushService.Persist(player);

        Log.Info($"GirlRush settlement: stage {levelId} cleared {pass} time(s), rank {scoreLevel}" +
                 (improved ? " (new best)" : string.Empty) + $" for uid={player.Uid}.");

        return Task.FromResult<(JsonNode, NtfSyncPlayer?)>((new JsonObject(), sync));
    }

    private static uint ReadUint(JsonNode? node, string key) =>
        node is JsonObject obj && obj[key] is JsonValue value && value.TryGetValue<uint>(out var id) ? id : 0;
}
