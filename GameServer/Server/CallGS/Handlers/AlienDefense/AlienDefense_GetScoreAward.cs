using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.AlienDefense;

// 异星守护 (alien defense / Mow) score award claim. AlienDefense.GetScoreAward
// (DLC/AlienDefense/AlienDefense.lua:1019-1049) sends a single index, or a non-positive nIdx for
// the quick-claim button (the UI itself calls GetScoreAward(-1), umg_mow_score.lua:12):
//
//     { nIdx }
//
// and the callback (:1051-1059) tips sErr, otherwise feeds the response into
// MowScore.OnRspGetScoreAward, which needs ret.tbAward as a FLAT list of [G,D,P,L,Count] rows
// (umg_mow_score.lua:55-66: Item.Gain(ret.tbAward) only when #ret.tbAward > 0).
//
// Eligibility mirrors the client: the cumulative score (143,2) must reach the row's NeedScore and
// the row's bit must be unset ((143,3) for Index 1..30, (143,4) for Index 31..50, bit nIdx resp.
// nIdx-30 -- GetBits is 0-based, so award #k owns bit k and bit 0 stays unused).
//
// Request:  { nIdx }
// Response: { tbAward: [[g, d, p, l, count], ...] } or { sErr }
[CallGSApi("AlienDefense_GetScoreAward")]
public class AlienDefense_GetScoreAward : CallGSHandler<AlienDefenseGetScoreAwardParam>
{
    private static readonly Logger Log = new(nameof(AlienDefense_GetScoreAward));

    protected override async Task<CallGSResult> HandleAsync(
        CallGSContext context,
        AlienDefenseGetScoreAwardParam request)
    {
        var player = context.Connection.Player;
        if (player == null)
            return CallGSResult.Error("error.BadParam");

        var uid = player.Uid;
        var actId = player.Attributes.GetValue(AttrIds.AlienDefense.Gid, AttrIds.AlienDefense.ActivitySid);
        if (!GameData.AlienDefenseAwardData.TryGetValue(actId, out var awards) || awards.Count == 0)
        {
            Log.Warn($"Alien defense score award: no award rows for act {actId} (uid={uid}).");
            return CallGSResult.Ok(new JsonObject { ["sErr"] = "tip.congif_err" });
        }

        var indices = request.Index > 0
            ? new List<uint> { (uint)request.Index }
            : awards.Keys.OrderBy(i => i).ToList();

        var score = player.Attributes.GetValue(AttrIds.AlienDefense.Gid, AttrIds.AlienDefense.ScoreSid);
        var sync = new NtfSyncPlayer();
        var granted = new JsonArray();
        var claimedAny = false;

        foreach (var idx in indices)
        {
            if (!awards.TryGetValue(idx, out var award))
            {
                if (request.Index > 0)
                {
                    Log.Warn($"Alien defense score award: unknown index {idx} for act {actId} (uid={uid}).");
                    return CallGSResult.Ok(new JsonObject { ["sErr"] = "tip.congif_err" });
                }

                continue;
            }

            if (IsClaimed(player, idx))
            {
                Log.Warn($"Alien defense score award: index {idx} already claimed for uid={uid}.");
                if (request.Index > 0)
                    return CallGSResult.Ok(new JsonObject { ["sErr"] = "tip.star_award_geted" });

                continue;
            }

            if (score < award.NeedScore)
            {
                if (request.Index > 0)
                {
                    Log.Warn($"Alien defense score award: index {idx} not reached " +
                             $"({score}/{award.NeedScore}) for uid={uid}.");
                    return CallGSResult.Ok(new JsonObject { ["sErr"] = "error.BadParam" });
                }

                continue;
            }

            var rows = DlcRuntimeOverrides.ReadRewardRows(award.ScoreAward);
            var rewards = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);

            // JsonNode cannot have two parents, so the rows have to be cloned out of the manager's
            // array before they can be collected into the response.
            foreach (var row in rewards)
                granted.Add(row?.DeepClone());

            SetClaimed(player, idx, sync);
            claimedAny = true;

            Log.Info($"Alien defense score award: index {idx} claimed, {rewards.Count} row(s) granted for uid={uid}.");
        }

        if (claimedAny)
        {
            DatabaseHelper.SaveDatabaseType(player.Data);
            DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
        }

        return CallGSResult.Ok(new JsonObject { ["tbAward"] = granted }, sync);
    }

    private static bool IsClaimed(Game.Player.PlayerInstance player, uint idx)
    {
        var (sid, bit) = AwardBit(idx);
        var val = player.Attributes.GetValue(AttrIds.AlienDefense.Gid, sid);
        return (val & (1u << (int)bit)) != 0;
    }

    private static void SetClaimed(Game.Player.PlayerInstance player, uint idx, NtfSyncPlayer sync)
    {
        var (sid, bit) = AwardBit(idx);
        var attr = player.Attributes.GetOrCreate(AttrIds.AlienDefense.Gid, sid);
        attr.Val |= 1u << (int)bit;
        player.Attributes.SyncTo(sync, attr);
    }

    /// <summary>
    /// (sid, bit) owning award <paramref name="idx"/>, mirroring CheckScoreGot. Bit 0 of each word
    /// stays unused because GetBits is 0-based while award indices start at 1.
    /// </summary>
    private static (uint Sid, uint Bit) AwardBit(uint idx) =>
        idx <= 30
            ? (AttrIds.AlienDefense.AwardGotSid, idx)
            : (AttrIds.AlienDefense.AwardGotAdvancedSid, idx - 30);
}

public sealed class AlienDefenseGetScoreAwardParam
{
    [JsonPropertyName("nIdx")] public int Index { get; set; }
}
