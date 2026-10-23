using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// Client side origin: DLC_Chapter.Req_Moppingup (Launch/Dlc/DLC_Chapter.lua:449-472), reached from
// UMG/Common/umg_common_clean_start.lua:281 when the player sweeps an already cleared DLC story level.
// The request is {nID, nChapterID, nTeamID, tbLog, nMultiple}; the callback
// (DLC_Chapter.lua:475-479) closes the "联网中" overlay and opens the MopupResult widget with the whole
// payload, so an unanswered request leaves the overlay on screen forever - the sweep button is on the
// level info panel (UMG/DlcCommon/Widgets/uw_dlc_common_levelinfo.lua:458-472), not in the level.
//
// Payload contract (UMG/Common/umg_common_clean_result.lua:25-29):
//   nTeamID   -> formation rendered by the result panel
//   nMultiple -> number of sweeps shown
//   nLevelID  -> the swept level
//   tbAward   -> a LIST OF PER-SWEEP ROW LISTS. The widget walks it in ShowAward
//                (umg_common_clean_result.lua:165-176) and hands each entry to
//                umg_common_clean_resultitem.lua:22-31, which iterates it with ipairs and unpacks
//                every element as a flat [G,D,P,L,Count] row. RewardManager.GrantLevelRewardsAsync
//                answers with three nested category arrays (first / base / random), so they must be
//                flattened here - unlike the settlement payload, which the Success/Settlement widgets
//                walk as Launch.tbAward[category][row] (umg_success.lua:158-159,
//                Launch/Classes/DlcChapterLogic.lua:50-56).
//
// Vigor: the client pre-checks (tbCfg.tbConsumeVigor[1] + tbCfg.tbConsumeVigor[2]) * nCount and
// refuses locally with ui.TxtEnergyTips (DLC_Chapter.lua:452-456). The charge has to happen here as
// well, otherwise sweeping is free; the same numbers are read back out of the level row's ConsumeVigor
// column.
//
// A sweep never grants the first-clear drop (the level is already cleared, so isFirstClear is false)
// and never touches the star bits in (21, levelId); it only advances the clear counter (22, levelId),
// matching the repeat clear in DlcChapterSettlement.
[CallGSApi("DLC_Chapter_Mopup")]
public class DLC_Chapter_Mopup : CallGSHandler<DlcChapterMopupParam>
{
    private static readonly Logger Logger = new("DLC_Chapter");

    /// <summary>
    /// player/moppingup.json gives Multiple = 9 for LevelType 12 (DLC1_CHAPTER, the type
    /// DLC_Chapter.lua:459 logs) and the client's selector cannot go above it
    /// (umg_common_clean_start.lua:44), so a larger nMultiple is a forged request.
    /// </summary>
    private const uint MaxMultiple = 9;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, DlcChapterMopupParam request)
    {
        var player = context.Player;
        var levelId = request.LevelId;
        var multiple = request.Multiple;

        if (multiple == 0 || multiple > MaxMultiple)
        {
            Logger.Warn($"Rejected DLC chapter mopup x{multiple} for level {levelId} by uid={player.Uid}.");
            return BadParam();
        }

        if (levelId == 0 || !GameData.DlcChapterLevelData.TryGetValue(levelId, out var levelConfig))
        {
            Logger.Warn($"Rejected DLC chapter mopup for unknown level {levelId} by uid={player.Uid}.");
            return BadParam();
        }

        // Launch.CheckLevelMutipleOpen(true) (Launch/Launch.lua:692-700) only shows the sweep button
        // when the level's IsCompleted() is true, so sweeping an uncleared level is forged.
        var passAttr = player.Attributes.GetOrCreate(AttrIds.Launch.LevelPassGid, levelId);
        if (passAttr.Val == 0)
        {
            Logger.Warn($"Rejected DLC chapter mopup for uncleared level {levelId} by uid={player.Uid}.");
            return BadParam();
        }

        var sync = new NtfSyncPlayer();
        var vigorCost = ReadVigorCost(levelConfig) * multiple;
        if (!CurrencyService.TrySpend(player, AttrIds.Currency.Vigor, vigorCost, sync, out var error))
        {
            Logger.Warn($"Rejected DLC chapter mopup x{multiple} of level {levelId} for uid={player.Uid}: {error}.");
            return CallGSResult.Error(error);
        }

        var awards = new JsonArray();
        for (var i = 0u; i < multiple; i++)
        {
            // A fresh seed per sweep so RandomDropID does not repeat inside one request.
            var seed = (uint)Random.Shared.Next(1, int.MaxValue);
            var categories = await player.RewardManager.GrantLevelRewardsAsync(
                levelConfig, isFirstClear: false, seed, sync);
            awards.Add(Flatten(categories));
        }

        passAttr.Val += multiple;
        player.Attributes.SyncTo(sync, passAttr);

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        var payload = new JsonObject
        {
            ["nLevelID"] = levelId,
            ["nTeamID"] = request.TeamId,
            ["nMultiple"] = multiple,
            ["tbAward"] = awards
        };

        Logger.Info(
            $"DLC chapter level {levelId} swept x{multiple} (vigor={vigorCost}) by uid={player.Uid}, " +
            $"clear counter -> {passAttr.Val}.");

        return CallGSResult.Ok(payload, sync);
    }

    /// <summary>
    /// Flattens GrantLevelRewardsAsync's three category arrays into the single flat row list the sweep
    /// result item expects (umg_common_clean_resultitem.lua:22-31).
    /// </summary>
    private static JsonArray Flatten(JsonArray categories)
    {
        var rows = new JsonArray();
        foreach (var category in categories)
        {
            if (category is not JsonArray list)
                continue;

            foreach (var row in list)
                rows.Add(row?.DeepClone());
        }

        return rows;
    }

    /// <summary>
    /// (ConsumeVigor[1] + ConsumeVigor[2]) of the level row - the exact expression the client checks in
    /// DLC_Chapter.lua:453. The column is a JSON array that the generic level loader does not model, so
    /// the DLC chapter Excel keeps it in ExtraData.
    /// </summary>
    private static uint ReadVigorCost(DlcChapterLevelExcel levelConfig)
    {
        if (!levelConfig.ExtraData.TryGetValue("ConsumeVigor", out var raw) || raw is not JArray rows)
            return 0;

        uint total = 0;
        foreach (var row in rows)
        {
            try
            {
                total += row.Value<uint>();
            }
            catch (Exception)
            {
                // A blank or non numeric entry contributes nothing, mirroring tonumber() returning nil.
            }
        }

        return total;
    }
}

public sealed class DlcChapterMopupParam
{
    [JsonPropertyName("nID")] public uint LevelId { get; set; }
    [JsonPropertyName("nChapterID")] public uint ChapterId { get; set; }
    [JsonPropertyName("nTeamID")] public uint TeamId { get; set; }
    [JsonPropertyName("nMultiple")] public uint Multiple { get; set; }
    [JsonPropertyName("tbLog")] public JsonNode? Log { get; set; }
}
