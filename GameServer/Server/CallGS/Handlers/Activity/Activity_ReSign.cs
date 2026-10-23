using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Activity;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;
using System.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

/// <summary>
/// 补签 / make-up sign-in (pay a currency to advance the sign-in chain by one day).
///
/// Client send point: Activity/Sign.lua:573-587 Sign.DoResign(nActivityId) builds
/// cmd = { nId = nActivityId, nType = Sign.AWARD_TYPE_AMOUNT (=2, Sign.lua:17) } and calls
/// me:CallGS("Activity_ReSign", json.encode(cmd)) at :585; the callback is registered at :606-639
/// and reads the same FLAT tbRewards list the quick-quest handler uses (dedup on
/// "%d-%d-%d-%d" over info[1..4], sum info[5], then Activity:OnReceiveUpdate).
///
/// The PRICE IS NOT ON THE WIRE. The client derives it from its own copy of
/// activity/activities.txt (Activity/Activity.lua:855 tbCashId = Eval(tbLine.CashID),
/// :856 tbCustomData = Eval(tbLine.CustomData)) and refuses to send at all when it cannot pay:
/// Sign.GetResignNum (Sign.lua:559-571) returns nCashId = tonumber(tbCashId[1]), nValue =
/// tonumber(tbCustomData[1]) or 1 (so an empty CustomData means "1 unit") and Sign.CheckResign
/// (:589-603) demands nDoNum > 0 and CheckEnd(tbConfig, true). Because the charge must therefore be
/// taken server-side, this handler re-reads the CashID/CustomData columns itself - ActivityExcel
/// (Common/Data/Excel/ActivityExcel.cs) exposes CustomData but NOT CashID, so GameData.ActivityData
/// cannot answer the currency question. See LoadResignCosts for that workaround and its follow-up.
///
/// Grant semantics: the make-up sign advances the same counter the daily sign uses -
/// Sign.CheckEnd (Sign.lua:230-254) computes the next day as GetSginTag(nId) + 1 when it is called
/// with bForce (which CheckResign does), and GetSginTag is Activity.GetDiyData(nId, 1) =
/// (group 50, sid = nId * 10 + 3) = ActivitySignService.SignedDaySid. So this grants
/// SignDayData[(nId, SignedDay + 1)] and increments SignedDay, exactly like the AWARD_TYPE_DAY path
/// in Activity_GetAward.cs - but it deliberately leaves TodayStatusSid and LastSignDateSid alone,
/// so paying for a missed day never consumes today's free sign-in.
///
/// The reward table is the one the client walks in Sign.GetRewardConfig (Sign.lua:141):
/// Sign.GetSignConfig(nId) (= sign/signday.txt, loaded server-side as GameData.SignDayData) first,
/// then Sign.GetShortSignConfig(nId) (= sign/signshort.txt, keyed [nActivityId][nDayId] at
/// Sign.lua:52-73). No Excel resource class loads signshort.json, so that table is read from disk.
/// </summary>
[CallGSApi("Activity_ReSign")]
public class Activity_ReSign : CallGSHandler
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var request = JsonNode.Parse(string.IsNullOrWhiteSpace(param) ? "{}" : param) as JsonObject
                      ?? new JsonObject();

        var activityId = ReadUint(request["nId"]);
        var awardType = ReadUint(request["nType"]);

        var response = new JsonObject();
        foreach (var pair in request)
            response[pair.Key] = pair.Value?.DeepClone();

        var sync = new NtfSyncPlayer();
        var rewarded = new JsonArray();

        // The client only ever sends AWARD_TYPE_AMOUNT (Sign.lua:580). A missing nType is tolerated so
        // a GM call with just {"nId":N} still works, anything else is refused without charging.
        if (activityId == 0 ||
            (awardType != 0 && awardType != ActivitySignService.AwardTypeAmount))
        {
            Logger.GetByClassName().Warn(
                $"Activity_ReSign bad request (param='{param}') for uid={player.Uid}; answering without a reward.");
            response["tbRewards"] = rewarded;
            return CallGSResult.Ok(response.ToJsonString(), sync, "Activity_ReSign");
        }

        var day = player.Attributes.GetValue(
            ActivitySignService.GroupId, ActivitySignService.SignedDaySid(activityId)) + 1;
        var rows = ResignRewardRows(activityId, day);

        if (rows == null || rows.Count == 0)
        {
            // No configured reward for the next day: do not charge and do not move the counter, so the
            // state stays consistent and the client can retry once the reward table is fixed.
            Logger.GetByClassName().Warn(
                $"Activity_ReSign activity {activityId} day {day}: no sign reward row in " +
                $"sign/signday.json or sign/signshort.json; answering without a reward or a charge " +
                $"for uid={player.Uid}.");
        }
        else
        {
            var (currencyId, amount) = ResignCost(activityId);

            if (currencyId == 0)
            {
                // No price on file: refusing is the only honest answer (never grant a paid reward for free).
                Logger.GetByClassName().Warn(
                    $"Activity_ReSign activity {activityId}: activity/activities.json has no CashID for " +
                    $"this row, so the make-up sign price is unknown; refusing the claim for uid={player.Uid}.");
            }
            else if (!CurrencyService.TrySpend(player, currencyId, amount, sync, out var error))
            {
                Logger.GetByClassName().Info(
                    $"Activity_ReSign activity {activityId}: uid={player.Uid} cannot pay {amount} x " +
                    $"currency {currencyId} ({error}); refusing the claim.");
            }
            else
            {
                rewarded = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);

                ActivitySignService.SetAttr(
                    player, ActivitySignService.SignedDaySid(activityId), day, sync);

                Logger.GetByClassName().Info(
                    $"Activity {activityId} make-up sign-in for uid={player.Uid}: paid {amount} x currency " +
                    $"{currencyId}, day -> {day}, granted {rewarded.Count} reward row(s).");

                DatabaseHelper.SaveDatabaseType(player.Data);
                DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
                DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
            }
        }

        response["tbRewards"] = rewarded;
        return CallGSResult.Ok(response.ToJsonString(), sync, "Activity_ReSign");
    }

    /// <summary>Reward rows for one sign-in day, in the order Sign.GetRewardConfig walks them.</summary>
    private static List<IReadOnlyList<uint>>? ResignRewardRows(uint activityId, uint day)
    {
        if (GameData.SignDayData.TryGetValue((activityId, day), out var signDay))
        {
            // DlcRuntimeOverrides.ReadRewardRows returns List<uint[]>; project it explicitly so the
            // argument matches RewardManager.GrantConfiguredRewardsAsync's
            // IEnumerable<IReadOnlyList<uint>> without relying on array variance.
            var rows = DlcRuntimeOverrides.ReadRewardRows(signDay.SignReward);
            if (rows.Count > 0)
                return rows.Select(row => (IReadOnlyList<uint>)row).ToList();
        }

        if (SignShortCache.Value is { } shortTable && shortTable.TryGetValue((activityId, day), out var shortRows))
            return shortRows.Select(row => (IReadOnlyList<uint>)row).ToList();

        return null;
    }

    /// <summary>
    /// The make-up sign price of one activity: (currency id, amount).
    ///
    /// WORKAROUND: ActivityExcel has no CashID property (Common/Data/Excel/ActivityExcel.cs), so the
    /// column the client uses at Activity.lua:855 is not reachable through GameData.ActivityData.
    /// The raw activity/activities.json is read once and cached instead - the same approach
    /// DreamCard/ DreamCard_EnterLevel.cs:56-81 uses for its own tables. The real fix is to add
    /// "[JsonProperty("CashID")] public JToken? CashId { get; set; }" to ActivityExcel.cs (a Common/
    /// file, outside this handler's change scope) and drop this loader.
    /// </summary>
    private static (uint CurrencyId, uint Amount) ResignCost(uint activityId)
    {
        return CostCache.Value.TryGetValue(activityId, out var cost) ? cost : (0u, 1u);
    }

    private static readonly Lazy<Dictionary<uint, (uint CurrencyId, uint Amount)>> CostCache =
        new(LoadResignCosts);

    private static Dictionary<uint, (uint CurrencyId, uint Amount)> LoadResignCosts()
    {
        var costs = new Dictionary<uint, (uint, uint)>();

        try
        {
            var path = Path.Combine(
                ConfigManager.Config.Path.ResourcePath, "activity", "activities.json");
            if (!File.Exists(path))
                return costs;

            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonArray table)
                return costs;

            foreach (var entry in table)
            {
                if (entry is not JsonObject row)
                    continue;

                var id = ReadUint(row["Id"]);
                var currencyId = ReadFirstUint(row["CashID"]);
                if (id == 0 || currencyId == 0)
                    continue; // 106 of the 107 rows that carry CashID carry an empty one.

                // Sign.lua:561 "tonumber(tbCustomData[1]) or 1": an empty CustomData means one unit.
                var amount = ReadFirstUint(row["CustomData"]);
                costs[id] = (currencyId, amount == 0 ? 1u : amount);
            }

            Logger.GetByClassName().Info(
                $"Loaded {costs.Count} make-up sign price(s) from {path}.");
        }
        catch (Exception ex)
        {
            Logger.GetByClassName().Warn(
                $"activity/activities.json could not be read for make-up sign prices: {ex.Message}");
        }

        return costs;
    }

    private static readonly Lazy<Dictionary<(uint ActivityId, uint DayId), List<List<uint>>>?> SignShortCache =
        new(LoadSignShortRows);

    /// <summary>
    /// sign/signshort.json -> [activityId][dayId] -> reward rows, mirroring the client's
    /// Sign.LoadShortSignReward (Sign.lua:52-73: tbSignShortReward[nActivityId][tbReward.nDayId]).
    /// There is no Excel resource class for this table, hence the direct read.
    /// </summary>
    private static Dictionary<(uint ActivityId, uint DayId), List<List<uint>>>? LoadSignShortRows()
    {
        try
        {
            var path = Path.Combine(
                ConfigManager.Config.Path.ResourcePath, "activity", "sign", "signshort.json");
            if (!File.Exists(path))
                return null;

            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonArray table)
                return null;

            var map = new Dictionary<(uint, uint), List<List<uint>>>();
            foreach (var entry in table)
            {
                if (entry is not JsonObject row)
                    continue;

                var activityId = ReadUint(row["ActivityId"]);
                var dayId = ReadUint(row["DayId"]);
                if (activityId == 0 || dayId == 0)
                    continue;

                var rows = ReadRewardRows(row["SignReward"]);
                if (rows.Count > 0)
                    map[(activityId, dayId)] = rows;
            }

            Logger.GetByClassName().Info($"Loaded {map.Count} signshort reward row(s) from {path}.");
            return map;
        }
        catch (Exception ex)
        {
            Logger.GetByClassName().Warn($"sign/signshort.json could not be read: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Same row contract as AchievementClaimService.ReadRewardRows, but over System.Text.Json nodes:
    /// [G, D, P, L, Count] with Count &gt; 0, which is what RewardManager.GrantConfiguredRewardsAsync
    /// and the client's GDPL handling expect.
    /// </summary>
    private static List<List<uint>> ReadRewardRows(JsonNode? node)
    {
        var rows = new List<List<uint>>();
        if (node is not JsonArray array)
            return rows;

        foreach (var entry in array)
        {
            if (entry is not JsonArray row || row.Count < 5)
                continue;

            var parsed = new List<uint>(5);
            var ok = true;
            for (var i = 0; i < 5; i++)
            {
                if (!TryReadUint(row[i], out var value))
                {
                    ok = false;
                    break;
                }

                parsed.Add(value);
            }

            if (ok && parsed[4] > 0)
                rows.Add(parsed);
        }

        return rows;
    }

    /// <summary>
    /// Activity.lua:855-856 evaluates CashID/CustomData as arrays and Sign.lua:559-571 reads slot 1,
    /// so accept both the client CSV form ("{5}" -> [5]) and a bare scalar. Empty strings (the usual
    /// value of both columns) parse to 0, which callers treat as "not configured".
    /// </summary>
    private static uint ReadFirstUint(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                var value = ReadUint(item);
                if (value != 0)
                    return value;
            }

            return 0;
        }

        var direct = ReadUint(node);
        if (direct != 0)
            return direct;

        var text = node?.ToString()?.Trim(' ', '"', '{', '}', '[', ']');
        return uint.TryParse(text, out var parsed) ? parsed : 0;
    }

    private static uint ReadUint(JsonNode? node)
        => TryReadUint(node, out var value) ? value : 0;

    private static bool TryReadUint(JsonNode? node, out uint value)
    {
        value = 0;
        if (node is not JsonValue json)
            return false;

        if (json.TryGetValue<uint>(out value))
            return true;

        return uint.TryParse(node.ToString(), out value);
    }
}
