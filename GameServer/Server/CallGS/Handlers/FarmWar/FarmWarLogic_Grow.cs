using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.GameServer.Game.FarmWar;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.FarmWar;

// 培育树 / 极限培育 (FarmWarActivity.lua:946-1077).
//
// All seven routes sit behind a connection overlay and every callback echoes the ids straight back
// into the widget (umg_farmwar_grow.lua:517-579 reads tbData.nId / tbData.nSkillId), so the reply has
// to repeat the request ids or the node will not light up until the panel is reopened.
//
// Persistence is the (165, nodeId) slot the client already reads through
// FarmWarActivityCfg.lua:157 (GetAttribute(SKILL_GID, nId) == 1). Limit-grow nodes go to the same
// slot: the client never distinguishes the two families when reading state back, it only branches on
// the tab it is showing.
internal static class FarmWarGrowOps
{
    public static JsonObject Apply(PlayerInstance player, FarmWarGrowParam request, bool active)
    {
        var sync = new NtfSyncPlayer();
        if (request.Id != 0)
            FarmWarActivityService.SetSkill(player, request.Id, active, sync);

        var payload = new JsonObject { ["nId"] = request.Id };
        if (request.SkillId != 0)
            payload["nSkillId"] = request.SkillId;

        return payload;
    }
}

[CallGSApi("FarmWarLogic_ActiveGrowNode")]
public class FarmWarLogic_ActiveGrowNode : CallGSHandler<FarmWarGrowParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, FarmWarGrowParam request) =>
        Task.FromResult(CallGSResult.Ok(
            FarmWarGrowOps.Apply(context.Connection.Player!, request, true).ToJsonString(),
            null, "FarmWarLogic_ActiveGrowNode"));
}

[CallGSApi("FarmWarLogic_ResetGrowNode")]
public class FarmWarLogic_ResetGrowNode : CallGSHandler<FarmWarGrowParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, FarmWarGrowParam request) =>
        Task.FromResult(CallGSResult.Ok(
            FarmWarGrowOps.Apply(context.Connection.Player!, request, false).ToJsonString(),
            null, "FarmWarLogic_ResetGrowNode"));
}

[CallGSApi("FarmWarLogic_ResetGrow")]
public class FarmWarLogic_ResetGrow : CallGSHandler<FarmWarGrowParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, FarmWarGrowParam request)
    {
        var sync = new NtfSyncPlayer();
        FarmWarActivityService.ResetAllSkills(context.Connection.Player!, sync);
        return Task.FromResult(CallGSResult.Ok("{}", sync, "FarmWarLogic_ResetGrow"));
    }
}

[CallGSApi("FarmWarLogic_ActiveLimitGrowNode")]
public class FarmWarLogic_ActiveLimitGrowNode : CallGSHandler<FarmWarGrowParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, FarmWarGrowParam request) =>
        Task.FromResult(CallGSResult.Ok(
            FarmWarGrowOps.Apply(context.Connection.Player!, request, true).ToJsonString(),
            null, "FarmWarLogic_ActiveLimitGrowNode"));
}

[CallGSApi("FarmWarLogic_ResetLimitGrowNode")]
public class FarmWarLogic_ResetLimitGrowNode : CallGSHandler<FarmWarGrowParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, FarmWarGrowParam request) =>
        Task.FromResult(CallGSResult.Ok(
            FarmWarGrowOps.Apply(context.Connection.Player!, request, false).ToJsonString(),
            null, "FarmWarLogic_ResetLimitGrowNode"));
}

[CallGSApi("FarmWarLogic_ResetLimitGrow")]
public class FarmWarLogic_ResetLimitGrow : CallGSHandler<FarmWarGrowParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, FarmWarGrowParam request)
    {
        var sync = new NtfSyncPlayer();
        FarmWarActivityService.ResetAllSkills(context.Connection.Player!, sync);
        return Task.FromResult(CallGSResult.Ok("{}", sync, "FarmWarLogic_ResetLimitGrow"));
    }
}

[CallGSApi("FarmWarLogic_SwitchLimitGrowNode")]
public class FarmWarLogic_SwitchLimitGrowNode : CallGSHandler<FarmWarGrowParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, FarmWarGrowParam request)
    {
        var payload = new JsonObject { ["nId"] = request.Id };
        return Task.FromResult(CallGSResult.Ok(payload.ToJsonString(), null, "FarmWarLogic_SwitchLimitGrowNode"));
    }
}

public sealed class FarmWarGrowParam
{
    [JsonPropertyName("nId")] public uint Id { get; set; }
    [JsonPropertyName("nSkillId")] public uint SkillId { get; set; }
    [JsonPropertyName("nType")] public uint Type { get; set; }
}
