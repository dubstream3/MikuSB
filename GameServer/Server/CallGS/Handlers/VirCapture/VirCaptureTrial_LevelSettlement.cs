using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// 试炼结算（VirCaptureTrial.lua:328-376, settle channel）.
///
/// Response contract: `{nScore, tbAward}`. The client stores `nScore` (VirCapture:SetCurFinishScore)
/// and hands `tbAward` straight to `Launch.Response`, so `tbAward` must be the nested
/// "categories of [g,d,p,l,n] rows" shape the success page walks; trial levels ship no drop columns,
/// so the array is empty and the honest answer is an empty list rather than a fabricated reward.
///
/// `trial.json`'s Score column is [[metric, [threshold, score] ...]] and the client only knows the
/// maximum (GetMaxScore). The metric is not visible in client source, so the server reads the run's
/// total damage and applies the tier table with a divisor that can be pinned through
/// MIKU_VIRCAPTURE_TRIAL_SCORE_DIVISOR (default 10000 => thresholds are 万 damage). `(128,7)` keeps
/// the best score ever reached, which is what the main screen shows.
/// </summary>
[CallGSApi("VirCaptureTrial_LevelSettlement")]
public class VirCaptureTrial_LevelSettlement : CallGSHandler
{
    private const uint GroupId = AttrIds.VirCapture.Gid;
    private const uint TrialPointsSid = AttrIds.VirCapture.TrialPointsSid;
    private const uint DefaultScoreDivisor = 10000;

    private static readonly Logger Logger = new("VirCapture.Trial.Settlement");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var (response, sync) = HandleSettlement(player, JsonNode.Parse(param));
        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), sync));
    }

    public static (JsonNode Response, NtfSyncPlayer Sync) HandleSettlement(PlayerInstance player, JsonNode? tbParam)
    {
        var req = tbParam?.Deserialize<VirCaptureTrialSettlementParam>(CallGSJson.Options);
        var sync = new NtfSyncPlayer();

        if (req == null || req.LevelId == 0)
        {
            Logger.Error($"Invalid vircapture trial settlement payload: {tbParam?.ToJsonString() ?? "null"}");
            return (new JsonObject { ["sErr"] = "error.BadParam" }, sync);
        }

        var levelId = (uint)req.LevelId;
        uint score = 0;

        if (GameData.VirCaptureTrialData.TryGetValue(levelId, out var trial))
        {
            var divisor = ResolveDivisor();
            var metric = req.TotalDamage / divisor;
            score = trial.ResolveScore(metric);

            Logger.Info(
                $"trial settlement level={levelId} damage={req.TotalDamage} divisor={divisor} metric={metric} " +
                $"score={score} max={trial.MaxScore} metricId={trial.ScoreMetric} uid={player.Uid}");
        }
        else
        {
            Logger.Warn($"trial settlement for unknown level {levelId} uid={player.Uid}");
        }

        var pointsAttr = player.Attributes.GetOrCreate(GroupId, TrialPointsSid);
        if (score > pointsAttr.Val)
        {
            pointsAttr.Val = score;
            player.Attributes.SyncTo(sync, pointsAttr);
        }

        DatabaseHelper.SaveDatabaseType(player.Data);

        var response = new JsonObject
        {
            ["nScore"] = score,
            ["tbAward"] = new JsonArray()
        };

        return (response, sync);
    }

    private static uint ResolveDivisor()
    {
        var raw = Environment.GetEnvironmentVariable("MIKU_VIRCAPTURE_TRIAL_SCORE_DIVISOR");
        if (uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var divisor) && divisor > 0)
            return divisor;

        return DefaultScoreDivisor;
    }
}

internal sealed class VirCaptureTrialSettlementParam
{
    [JsonPropertyName("nID")] public int LevelId { get; set; }

    [JsonPropertyName("nTotalDamage")] public uint TotalDamage { get; set; }
}
