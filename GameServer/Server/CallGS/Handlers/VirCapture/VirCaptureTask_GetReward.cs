using MikuSB.Proto;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Claims a finished blueprint island mission. The 163 attribute bit0 marks the claim, so a
/// repeated call neither re-grants the item rewards nor advances any progress (S3 once).
/// </summary>
[CallGSApi("VirCaptureTask_GetReward")]
public class VirCaptureTask_GetReward : CallGSHandler<VirCaptureTaskGetRewardParam>
{
    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureTaskGetRewardParam req)
    {
        if (req == null || req.Id <= 0)
        {
            return CallGSResult.Error("error.BadParam");
        }

        var player = context.Connection.Player!;
        var cfg = VirCaptureTaskState.GetTask((uint)req.Id);
        if (cfg == null)
        {
            return CallGSResult.Error("error.BadParam");
        }

        if (VirCaptureTaskState.IsReceived(player, cfg.Id))
        {
            // S3: the claim is already recorded, report success without granting again.
            return CallGSResult.Ok(new JsonObject { ["nId"] = cfg.Id }.ToJsonString());
        }

        if (!VirCaptureTaskState.IsFinished(player, cfg))
        {
            return CallGSResult.Error("error.condition_limit");
        }

        if (!VirCaptureConditionHelper.Check(player, cfg.Condition, $"Task.GetReward:{cfg.Id}"))
            return CallGSResult.Error("error.condition_limit");

        if (!VirCaptureTaskState.PreReceived(player, cfg))
        {
            return CallGSResult.Error("error.condition_limit");
        }

        var sync = new NtfSyncPlayer();
        VirCaptureTaskState.MarkReceived(player, cfg.Id, sync);

        var (awardList, deferred) = await VirCaptureRewardHelper.GrantMissionRewardsAsync(player, sync, cfg.Rewards);
        VirCaptureRewardHelper.Save(player);

        var rsp = new JsonObject
        {
            ["nId"] = cfg.Id,
            ["tbAwardList"] = awardList
        };
        if (deferred.Count > 0)
        {
            var entries = new JsonArray();
            foreach (var entry in deferred)
                entries.Add(new JsonArray((int)entry.Kind, (int)entry.Param, (int)entry.Value));
            rsp["tbStateRewards"] = entries;
        }

        return CallGSResult.Ok(rsp.ToJsonString(), sync);
    }
}

public sealed class VirCaptureTaskGetRewardParam
{
    [JsonPropertyName("nId")] public int Id { get; set; }
}
