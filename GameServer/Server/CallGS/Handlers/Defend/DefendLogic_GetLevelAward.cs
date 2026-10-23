using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Defend;

// Client side origin: DefendLogic.Req_GetLevelAward (Challenge/Defend/Defend.lua:2087-2092) shows the
// spinner and sends {tbIdList}; the callback (Defend.lua:2094-2102) closes it again and forwards the
// answer to the battle pass UI via UI.Call2('DefenseBP', 'OnRspGetLevelAward', ret), which feeds
// ret.tbAwardList straight into Item.Gain (umg_defense_bp.lua:132-144). An sErr would be shown as a
// tip instead, so the answer must stay free of it.
//
// tbIdList entries are the Index column of challenge/defend/levelaward_list.json for the current act
// (attribute gid 10, sid 0 - the client never writes it). A claim is recorded in the same bit layout
// the client reads back in DefendLogic.GetAwardFlag (Defend.lua:2076-2085): sid = Index / 30 + 20
// (AwardFlag_Start, capped at AwardFlag_End = 30) and bit = Index % 30.
// Request: { "tbIdList": [ <index>, ... ] }
// Response: { "tbAwardList": [[genre, detail, particular, level, count], ...] }
[CallGSApi("DefendLogic_GetLevelAward")]
public class DefendLogic_GetLevelAward : CallGSHandler
{
    private const uint GroupId = AttrIds.Defend.Gid;
    private const uint ActIdSid = 0;
    private const uint AwardFlagStart = 20;
    private const uint AwardFlagEnd = 30;
    private const int BitsPerFlagAttr = 30;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var indices = ParseIndexList(param);
        if (indices.Count == 0)
            return CallGSResult.Ok("{}");

        var actId = player.Attributes.GetValue(GroupId, ActIdSid);
        var sync = new NtfSyncPlayer();
        var rows = new List<IReadOnlyList<uint>>();

        foreach (var index in indices)
        {
            if (!GameData.DefendLevelAwardData.TryGetValue(actId * 100 + index, out var award))
                continue;

            if (IsClaimed(player, index))
                continue;

            foreach (var row in ReadRewardRows(award.AwardList))
                rows.Add(row);

            MarkClaimed(player, index, sync);
        }

        var granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);

        if (granted.Count > 0)
            Save(player);

        Logger.GetByClassName().Info(
            $"Defend level award: act {actId}, {indices.Count} requested, {rows.Count} row(s) granted for uid={player.Uid}.");

        return CallGSResult.Ok(new JsonObject { ["tbAwardList"] = granted }, sync);
    }

    private static List<uint> ParseIndexList(string param)
    {
        var result = new List<uint>();

        try
        {
            if (JsonNode.Parse(param) is not JsonObject request)
                return result;

            if (request["tbIdList"] is not JsonArray list)
                return result;

            foreach (var entry in list)
            {
                var raw = entry?.ToString();
                if (uint.TryParse(raw, out var index) && index > 0)
                    result.Add(index);
            }
        }
        catch (Exception ex)
        {
            Logger.GetByClassName().Warn($"Defend level award request could not be parsed ('{param}'): {ex.Message}");
        }

        return result;
    }

    private static bool IsClaimed(PlayerInstance player, uint index)
    {
        var offset = index / BitsPerFlagAttr + AwardFlagStart;
        if (offset > AwardFlagEnd)
            return true;

        var position = (int)(index % BitsPerFlagAttr);
        var value = player.Attributes.GetValue(GroupId, offset);
        return (value >> position & 1u) != 0;
    }

    private static void MarkClaimed(PlayerInstance player, uint index, NtfSyncPlayer sync)
    {
        var offset = index / BitsPerFlagAttr + AwardFlagStart;
        if (offset > AwardFlagEnd)
            return;

        var position = (int)(index % BitsPerFlagAttr);
        var attr = player.Attributes.GetOrCreate(GroupId, offset);
        attr.Val |= 1u << position;
        player.Attributes.SyncTo(sync, attr);
    }

    /// <summary>Reads a reward column that is either one flat row or a list of rows.</summary>
    internal static IEnumerable<IReadOnlyList<uint>> ReadRewardRows(JToken? node)
    {
        var result = new List<IReadOnlyList<uint>>();

        if (node is not JArray array || array.Count == 0)
            return result;

        if (array[0] is JArray)
        {
            foreach (var entry in array)
            {
                if (entry is JArray row && TryReadRow(row, out var values))
                    result.Add(values);
            }

            return result;
        }

        if (TryReadRow(array, out var single))
            result.Add(single);

        return result;
    }

    private static bool TryReadRow(JArray row, out uint[] values)
    {
        values = new uint[row.Count];

        for (var i = 0; i < row.Count; i++)
        {
            if (!uint.TryParse(row[i]?.ToString(), out var value))
                return false;

            values[i] = value;
        }

        return values.Length >= 5 && values[4] != 0;
    }

    internal static void Save(PlayerInstance player)
    {
        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
    }
}
