using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// Shared plumbing for the Donoduel card growth requests (star / quality / attribute / skill /
/// leader skill / skill tree / recycle / lock). Every one of them answers an error as
/// <c>{"sError": ...}</c> (NOT <c>sErr</c> - see DonoDuel.lua:2086-2110), refreshes nothing in the
/// reply body (the client rebuilds its lists and re-reads group 185, umg_donoduel_bag.lua:882-889)
/// and therefore has to push every changed slot through <see cref="NtfSyncPlayer"/>.
/// </summary>
internal static class DonoDuelGrowth
{
    /// <summary>Season currency. donoduel_season.json declares "Currency": 63 for every act.</summary>
    public const uint FallbackSeasonCurrency = 63;

    public const uint AttrAttack = 1;
    public const uint AttrDefence = 2;
    public const uint AttrHealth = 3;

    /// <summary>Card skill slots (DonoDuel.lua:56-67 SKILL1/SKILL2/SKILL3).</summary>
    public const int SkillSlots = 3;

    /// <summary>Condition kinds used by donoduel_config ConfigID 3 / 25 and leaderskill Condition.</summary>
    public const uint ConditionStoryMission = 36;
    public const uint ConditionArenaLevel = 37;

    private static readonly Logger Log = new("DonoDuelGrowth");

    /// <summary>Season currency id of the player's current act, falling back to 63.</summary>
    public static uint SeasonCurrency(PlayerInstance player)
    {
        var actId = player.Attributes.GetValue(AttrIds.DonoDuel.GroupId, AttrIds.DonoDuel.ActivitySid);
        if (actId != 0 && GameData.DonoDuelSeasonData.TryGetValue(actId, out var season))
        {
            var raw = season.Currency?.ToString().Trim().Trim('[', ']', '"') ?? string.Empty;
            if (uint.TryParse(raw, out var currency) && currency != 0)
                return currency;
        }

        return FallbackSeasonCurrency;
    }

    /// <summary>
    /// Evaluates one donoduel condition pair: {36, missionId} = that Donoduel story mission was
    /// claimed, {37, levelId} = that arena level was cleared.
    /// </summary>
    public static bool ConditionMet(PlayerInstance player, IReadOnlyList<uint> condition)
    {
        if (condition.Count < 2)
            return true;

        return condition[0] switch
        {
            ConditionStoryMission => DonoDuelTaskState.IsReceived(player, condition[1]),
            ConditionArenaLevel => player.Attributes.GetValue(AttrIds.Launch.LevelPassGid, condition[1]) > 0,
            _ => false
        };
    }

    /// <summary>The card's ceiling for one attribute position at its current quality.</summary>
    public static uint AttrCap(uint templateId, uint quality, uint position)
    {
        if (!GameData.DonoDuelHeroCardData.TryGetValue(templateId, out var heroCard))
            return 0;

        return position switch
        {
            AttrAttack => heroCard.AttackCap(quality),
            AttrDefence => heroCard.DefenseCap(quality),
            _ => heroCard.HealthCap(quality)
        };
    }

    /// <summary>Error reply with the client's expected key ({"sError": ...}).</summary>
    public static CallGSResult Bad(string key = "error.BadParam")
        => CallGSResult.Ok(new JsonObject { ["sError"] = key });

    /// <summary>Success reply - an empty body is enough, the sync carries the new state.</summary>
    public static CallGSResult Ok(JsonObject? body = null) => CallGSResult.Ok(body ?? new JsonObject());

    public static JsonObject? ParseObject(string param)
    {
        try
        {
            return string.IsNullOrWhiteSpace(param) ? null : JsonNode.Parse(param) as JsonObject;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not parse a Donoduel growth request: {ex.Message}");
            return null;
        }
    }

    public static uint ReadUint(JsonNode? node)
    {
        if (node is not JsonValue value)
            return 0;

        try
        {
            if (value.TryGetValue<uint>(out var number))
                return number;

            if (value.TryGetValue<int>(out var signed))
                return signed < 0 ? 0 : (uint)signed;

            if (value.TryGetValue<string>(out var text))
                return uint.TryParse(text.Trim(), out var parsed) ? parsed : 0;
        }
        catch
        {
            // unreadable -> 0
        }

        return 0;
    }

    public static bool ReadBool(JsonNode? node)
    {
        if (node is not JsonValue value)
            return false;

        try
        {
            if (value.TryGetValue<bool>(out var flag))
                return flag;

            if (value.TryGetValue<uint>(out var number))
                return number != 0;

            if (value.TryGetValue<string>(out var text))
                return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1";
        }
        catch
        {
            // unreadable -> false
        }

        return false;
    }

    public static List<uint> ReadUintList(JsonNode? node)
    {
        var values = new List<uint>();
        if (node is not JsonArray array)
            return values;

        foreach (var item in array)
        {
            var value = ReadUint(item);
            if (value != 0)
                values.Add(value);
        }

        return values;
    }
}
