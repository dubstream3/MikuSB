using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Defend;

// Client side origin: DefendLogic.GetReward(targetId) (Challenge/Defend/Defend.lua:1434-1460) sends
// {nID, nDiff, nTargetId, nIdx} (nIdx = position of the target inside the level's tbTarget list) and
// DefendLogic.GetRewardAll() (Defend.lua:1427-1431) sends {nID, nDiff, bAll = true}. The callback
// (Defend.lua:1463-1471) closes the spinner, opens the GainItem UI for a non-empty tbReward and
// refreshes the page, so the answer must carry {tbReward, bGetAll}.
//
// Reward rows come from challenge/defend/level_target.json (Award, a list of
// [genre, detail, particular, level, count] rows). The claim gate mirrors DefendLogic.GetTargetState
// (Defend.lua:1386-1412): type 1 targets unlock once the wave attribute (gid 10, sid 2) reaches their
// Param, and a claimed target is remembered in a bit of sid 3 (index <= 30) or sid 4 (index > 30).
// Request: { "nID": <levelId>, "nDiff": <uint>, "bAll": <bool>?, "nTargetId": <uint>?, "nIdx": <uint>? }
// Response: { "tbReward": [[genre, detail, particular, level, count], ...], "bGetAll": <bool> }
[CallGSApi("DefendLogic_GetReward")]
public class DefendLogic_GetReward : CallGSHandler
{
    private const uint GroupId = AttrIds.Defend.Gid;
    private const uint PassWaveSid = 2;
    private const uint AwardsGotSid = 3;
    private const uint AwardsGot2Sid = 4;
    private const int BitsPerAwardAttr = 30;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;

        JsonObject? request = null;
        try
        {
            request = JsonNode.Parse(param) as JsonObject;
        }
        catch (Exception ex)
        {
            Logger.GetByClassName().Warn($"Defend reward request could not be parsed ('{param}'): {ex.Message}");
        }

        var levelId = ReadUint(request?["nID"]);
        var diff = ReadUint(request?["nDiff"]);
        var getAll = ReadBool(request?["bAll"]);
        var targetId = ReadUint(request?["nTargetId"]);
        var requestedIndex = ReadUint(request?["nIdx"]);

        var targets = ResolveTargets(levelId, diff, targetId, requestedIndex);
        var sync = new NtfSyncPlayer();
        var rows = new List<IReadOnlyList<uint>>();

        foreach (var (id, index) in targets)
        {
            if (!GameData.DefendTargetData.TryGetValue(id, out var target))
                continue;

            if (!IsUnlocked(player, target))
                continue;

            if (IsClaimed(player, index))
                continue;

            foreach (var row in DefendLogic_GetLevelAward.ReadRewardRows(target.Award))
                rows.Add(row);

            MarkClaimed(player, index, sync);
        }

        var granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);

        if (granted.Count > 0)
            DefendLogic_GetLevelAward.Save(player);

        Logger.GetByClassName().Info(
            $"Defend reward: level {levelId} diff {diff}, all={getAll}, {rows.Count} row(s) granted for uid={player.Uid}.");

        return CallGSResult.Ok(new JsonObject
        {
            ["tbReward"] = granted,
            ["bGetAll"] = getAll
        }, sync);
    }

    /// <summary>
    /// Single target claim uses the index the client already computed; "claim everything" walks the
    /// level's Target list in display order so the claim bits line up with the client's own layout.
    /// </summary>
    private static List<(uint Id, int Index)> ResolveTargets(uint levelId, uint diff, uint targetId, uint requestedIndex)
    {
        var result = new List<(uint, int)>();
        var found = GameData.DefendLevelData.TryGetValue(levelId * 100 + diff, out var level);
        var order = found ? ReadUintList(level!.Target) : [];

        if (targetId != 0)
        {
            var index = requestedIndex != 0 ? (int)requestedIndex : order.IndexOf(targetId) + 1;
            if (index > 0)
                result.Add((targetId, index));

            return result;
        }

        for (var i = 0; i < order.Count; i++)
            result.Add((order[i], i + 1));

        return result;
    }

    private static bool IsUnlocked(PlayerInstance player, DefendTargetExcel target)
    {
        // The client only implements type 1 (wave driven) targets; anything else stays claimable so a
        // future type does not silently become unreachable.
        if (ReadUint(target.Type) != 1)
            return true;

        return player.Attributes.GetValue(GroupId, PassWaveSid) >= ReadUint(target.Param);
    }

    private static bool IsClaimed(PlayerInstance player, int index)
    {
        var (sid, position) = ResolveClaimBit(index);
        if (position < 0)
            return true;

        var value = player.Attributes.GetValue(GroupId, sid);
        return (value >> position & 1u) != 0;
    }

    private static void MarkClaimed(PlayerInstance player, int index, NtfSyncPlayer sync)
    {
        var (sid, position) = ResolveClaimBit(index);
        if (position < 0)
            return;

        var attr = player.Attributes.GetOrCreate(GroupId, sid);
        attr.Val |= 1u << position;
        player.Attributes.SyncTo(sync, attr);
    }

    private static (uint Sid, int Position) ResolveClaimBit(int index)
    {
        if (index <= 0)
            return (0, -1);

        if (index <= BitsPerAwardAttr)
            return (AwardsGotSid, index - 1);

        var shifted = index - BitsPerAwardAttr;
        return shifted <= BitsPerAwardAttr ? (AwardsGot2Sid, shifted - 1) : (0, -1);
    }

    private static List<uint> ReadUintList(JToken? node)
    {
        var result = new List<uint>();
        if (node is not JArray array)
            return result;

        foreach (var entry in array)
        {
            if (uint.TryParse(entry?.ToString(), out var value))
                result.Add(value);
        }

        return result;
    }

    private static uint ReadUint(JsonNode? node) =>
        uint.TryParse(node?.ToString(), out var value) ? value : 0;

    private static uint ReadUint(JToken? token) =>
        uint.TryParse(token?.ToString()?.Trim().Trim('[', ']', '"'), out var value) ? value : 0;

    private static bool ReadBool(JsonNode? node) =>
        bool.TryParse(node?.ToString(), out var value) && value;
}
