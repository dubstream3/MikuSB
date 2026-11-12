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
/// 试炼进场（VirCaptureTrial.lua:213-260）.
///
/// The period comes from `trial_timelist` and its window is evaluated with the shared era rule
/// (<see cref="VirCaptureEraTime"/>), so a period that closed inside the served DLC era stays
/// playable exactly like the other era-pinned windows. `Condition` is checked on the server even
/// though the client also checks it: the client only knows its local table.
/// </summary>
[CallGSApi("VirCaptureTrial_EnterLevel")]
public class VirCaptureTrial_EnterLevel : CallGSHandler<VirCaptureTrialEnterParam>
{
    private const uint GroupId = AttrIds.VirCapture.Gid;
    private const uint TrialActIdSid = AttrIds.VirCapture.TrialActIdSid;

    private static readonly Logger Logger = new("VirCapture.Trial.Enter");

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureTrialEnterParam req)
    {
        var player = context.Connection.Player!;
        if (req == null || req.LevelId == 0 || req.TeamId <= 0)
            return Task.FromResult(CallGSResult.Error("error.BadParam"));

        var levelId = (uint)req.LevelId;
        if (!GameData.VirCaptureTrialData.ContainsKey(levelId))
        {
            Logger.Warn($"unknown trial level {levelId} uid={player.Uid}");
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
        }

        var period = VirCaptureEraTime.ResolveCurrent(
            GameData.VirCaptureTrialTimeData.Values,
            x => x.StartTime,
            x => x.EndTime);

        if (period == null)
        {
            Logger.Info($"no open trial period for level {levelId} uid={player.Uid} (eraPinned={VirCaptureEraTime.IsEraPinned})");
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
        }

        if (!period.TrialIds.Contains(levelId))
        {
            Logger.Warn(
                $"trial period {period.Id} runs trial {period.TrialId}, not {levelId} uid={player.Uid}");
            return Task.FromResult(CallGSResult.Error("ui.TxtNotOpen"));
        }

        if (!VirCaptureConditionHelper.Check(player, period.ConditionRaw, $"Trial.EnterLevel:{period.Id}"))
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));

        var trial = GameData.VirCaptureTrialData[levelId];
        if (!VirCaptureConditionHelper.Check(player, trial.ConditionRaw, $"Trial.EnterLevel.level:{levelId}"))
            return Task.FromResult(CallGSResult.Error("error.condition_limit"));

        var sync = new NtfSyncPlayer();
        var actAttr = player.Attributes.GetOrCreate(GroupId, TrialActIdSid);
        if (actAttr.Val != period.Id)
        {
            actAttr.Val = period.Id;
            player.Attributes.SyncTo(sync, actAttr);
            DatabaseHelper.SaveDatabaseType(player.Data);
        }

        Logger.Info($"trial enter level={levelId} period={period.Id} uid={player.Uid}");
        var response = new JsonObject { ["nSeed"] = Random.Shared.Next(1, 1_000_000_000) };
        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), sync));
    }
}

public sealed class VirCaptureTrialEnterParam
{
    [JsonPropertyName("nID")] public int LevelId { get; set; }

    [JsonPropertyName("nTeamID")] public int TeamId { get; set; }
}
