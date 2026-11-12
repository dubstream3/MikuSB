using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 爬塔章节星级奖励（VirCaptureTower.lua:700-720 / umg_vircapture_tower.lua:391-430）.
///
/// Stars are never stored as a counter: a level's star history is the low 8 bits of
/// `Tower.LevelStateGid[sid=levelId]`, so the chapter total is the popcount over its levels. The
/// claimed tiers live in `(128, 141 + chapterId - 1)`, one bit per tier index (0-based), which is
/// the same flag the client reads through GetTowerNormalStarAwardFlag.
/// nIndex == -1 (or 0) claims every tier that is reachable and still unclaimed, like the client's
/// 一键领取. The client does not read sErr on this api, so a refusal must not report success.
/// </summary>
[CallGSApi("VirCaptureTower_GetStarAward")]
public class VirCaptureTower_GetStarAward : CallGSHandler
{
    private const uint StarGroupId = AttrIds.VirCapture.Gid;
    private const uint ClaimFlagStartSid = AttrIds.VirCapture.TowerStarNormalFlagStartSid;
    private const uint ClaimFlagEndSid = AttrIds.VirCapture.TowerStarNormalFlagEndSid;
    private const uint LevelStateGroupId = AttrIds.Tower.LevelStateGid;
    private const int StarBits = 8;

    private static readonly Logger Logger = new("VirCapture.Tower.StarAward");

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var req = JsonSerializer.Deserialize<VirCaptureTowerStarAwardParam>(param, CallGSJson.Options);

        if (req == null || req.ChapterId <= 0)
            return CallGSResult.Error("error.BadParam");

        var chapterId = (uint)req.ChapterId;
        if (!GameData.VirCaptureTowerChapterData.TryGetValue((uint)Math.Max(0, req.Difficult), out var chapters) ||
            !chapters.TryGetValue(chapterId, out var chapter))
        {
            Logger.Warn($"unknown tower chapter {chapterId} (difficult {req.Difficult}) uid={player.Uid}");
            return CallGSResult.Error("error.BadParam");
        }

        var stars = CountChapterStars(player, chapter);
        var flagSid = ClaimFlagStartSid + chapterId - 1;
        if (flagSid < ClaimFlagStartSid || flagSid > ClaimFlagEndSid)
            return CallGSResult.Error("error.BadParam");

        var claimFlags = player.Attributes.GetOrCreate(StarGroupId, flagSid);
        var sync = new NtfSyncPlayer();
        var awardList = new JsonArray();
        var claimed = 0;

        for (var i = 0; i < chapter.StarAwards.Count; i++)
        {
            var tier = chapter.StarAwards[i];
            var bit = 1u << i;
            var tierIndex = i + 1;

            if (req.Index > 0 && req.Index != tierIndex)
                continue;

            if (stars < tier.Need)
            {
                if (req.Index > 0)
                {
                    Logger.Info($"chapter {chapterId} tier {tierIndex} needs {tier.Need} stars, has {stars} uid={player.Uid}");
                    return CallGSResult.Error("tip.star_not_enough");
                }

                continue;
            }

            if ((claimFlags.Val & bit) != 0)
            {
                if (req.Index > 0)
                {
                    Logger.Info($"chapter {chapterId} tier {tierIndex} already claimed uid={player.Uid}");
                    return CallGSResult.Error("tip.star_award_geted");
                }

                continue;
            }

            var rows = new JsonArray();
            foreach (var row in tier.Rows)
            {
                var json = new JsonArray();
                foreach (var cell in row)
                    json.Add(cell);
                rows.Add(json);

                await VirCaptureRewardHelper.GrantItemRewardAsync(player, sync, row);
            }

            claimFlags.Val |= bit;
            claimed++;
            awardList.Add(rows);
        }

        if (claimed == 0)
        {
            Logger.Info($"chapter {chapterId} no claimable tier (stars={stars}) uid={player.Uid}");
            return CallGSResult.Ok("{\"tbAwardList\":[]}", sync);
        }

        player.Attributes.SyncTo(sync, claimFlags);
        DatabaseHelper.SaveDatabaseType(player.Data);

        Logger.Info($"chapter {chapterId} claimed {claimed} tier(s) stars={stars} flags={claimFlags.Val} uid={player.Uid}");
        var response = new JsonObject { ["tbAwardList"] = awardList };
        return CallGSResult.Ok(response.ToJsonString(), sync);
    }

    /// <summary>Star total of a chapter = popcount over the low 8 bits of every level's state.</summary>
    public static uint CountChapterStars(PlayerInstance player, VirCaptureTowerChapterExcel chapter)
    {
        uint total = 0;
        foreach (var levelId in chapter.Levels)
        {
            var value = player.Attributes.GetValue(LevelStateGroupId, levelId);
            for (var bit = 0; bit < StarBits; bit++)
                total += (value >> bit) & 1u;
        }

        return total;
    }
}

internal sealed class VirCaptureTowerStarAwardParam
{
    [JsonPropertyName("nChapterId")] public int ChapterId { get; set; }

    [JsonPropertyName("nDifficult")] public int Difficult { get; set; }

    [JsonPropertyName("nIndex")] public int Index { get; set; }
}
