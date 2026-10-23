using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Data;
using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Party;

// Party (DungeonsOnlineParty / "海姆优化作战") client settings.
// Every s2c callback in DLC/Party/PartyLogic.lua is a plain acknowledgement: it closes the
// connection spinner and refreshes the local lineup, so the server only has to store the value
// in the player attributes the client reads back through me:GetAttribute.
//
//   (23, 19) Online.PartyMatchMode  0 = match players, 1 = match AI (single player)
//   (23, 17) Online.PartyMatchId    PartyLogic.SetMatchId   random matching id
//   (23, 16) Online.PartyCardId     PartyLogic.SetFightCard fighting card
//   (23, 18) Online.PartySkillId    PartyLogic.SetSkillID   two packed skill ids
//   (103, 1) PartyLogic.MapGID      string, { nId, tbMapId }
//   (147, 0) PartyLogic.TitleGID    currently equipped title
//   (148, 0) PartyLogic.TitleShowGID title display mode
internal static class PartyAttributes
{
    public static NtfSyncPlayer Sync(PlayerInstance player, PlayerAttr attr)
    {
        var sync = new NtfSyncPlayer();
        player.Attributes.SyncTo(sync, attr);
        return sync;
    }
}

// PartyLogic.lua:640 SetMatchMode -> PartyLogic_SetMatchMode { nSingle }
[CallGSApi("PartyLogic_SetMatchMode")]
public class PartyLogic_SetMatchMode : CallGSHandler<PartySetMatchModeParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, PartySetMatchModeParam request)
    {
        var player = context.Connection.Player!;
        var attr = player.Attributes.Set(AttrIds.Online.GroupId, AttrIds.Online.PartyMatchModeSid, request.Single);
        return Task.FromResult(CallGSResult.Ok("{}", PartyAttributes.Sync(player, attr)));
    }
}

// PartyLogic.lua:572 SetMatchId -> PartyLogic_SetMatchId { nId }
[CallGSApi("PartyLogic_SetMatchId")]
public class PartyLogic_SetMatchId : CallGSHandler<PartySetIdParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, PartySetIdParam request)
    {
        var player = context.Connection.Player!;
        var attr = player.Attributes.Set(AttrIds.Online.GroupId, AttrIds.Online.PartyMatchIdSid, request.Id);
        return Task.FromResult(CallGSResult.Ok("{}", PartyAttributes.Sync(player, attr)));
    }
}

// PartyLogic.lua:500 SetFightCard -> PartyLogic_SetFightCard { nId }
[CallGSApi("PartyLogic_SetFightCard")]
public class PartyLogic_SetFightCard : CallGSHandler<PartySetIdParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, PartySetIdParam request)
    {
        var player = context.Connection.Player!;
        var attr = player.Attributes.Set(AttrIds.Online.GroupId, AttrIds.Online.PartyCardIdSid, request.Id);
        return Task.FromResult(CallGSResult.Ok("{}", PartyAttributes.Sync(player, attr)));
    }
}

// PartyLogic.lua:533 SetFightSkill -> PartyLogic_SetSkillID { nId1, nId2 }
[CallGSApi("PartyLogic_SetSkillID")]
public class PartyLogic_SetSkillID : CallGSHandler<PartySetSkillParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, PartySetSkillParam request)
    {
        var player = context.Connection.Player!;
        // The client only ever reads the slot back as one id, so pack both halves into it.
        var packed = (request.Id1 & 0xFFFFu) | ((request.Id2 & 0xFFFFu) << 16);
        var attr = player.Attributes.Set(AttrIds.Online.GroupId, AttrIds.Online.PartySkillIdSid, packed);
        return Task.FromResult(CallGSResult.Ok("{}", PartyAttributes.Sync(player, attr)));
    }
}

// PartyLogic.lua:594 SetRandomMapId -> PartyLogic_SetRandomMapId { nId, nSingle?, tbMapId?, nSetData? }
// The s2c handler reads tbParam.nSetMode and, when > 0, calls Online.UpdateLineup.
[CallGSApi("PartyLogic_SetRandomMapId")]
public class PartyLogic_SetRandomMapId : CallGSHandler<PartySetRandomMapParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, PartySetRandomMapParam request)
    {
        var player = context.Connection.Player!;

        var payload = new JsonObject { ["nId"] = (int)request.Id };
        if (request.Single.HasValue)
            payload["nSingle"] = (int)request.Single.Value;
        if (request.MapId is { Count: > 0 })
        {
            var mapIds = new JsonArray();
            foreach (var id in request.MapId)
                mapIds.Add((int)id);
            payload["tbMapId"] = mapIds;
        }

        player.Attributes.SetString(PartyMapStringGid, PartyMapStringSid, payload.ToJsonString());

        var response = new JsonObject { ["nSetMode"] = 1 };
        return Task.FromResult(CallGSResult.Ok(response.ToJsonString()));
    }

    private const uint PartyMapStringGid = 103;
    private const uint PartyMapStringSid = 1;
}

// PartyLogic.lua:696 CheckFirstSign -> PartyLogic_CheckFirstSign { nId, nTeach }
[CallGSApi("PartyLogic_CheckFirstSign")]
public class PartyLogic_CheckFirstSign : CallGSHandler<PartyCheckFirstSignParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, PartyCheckFirstSignParam request)
    {
        var player = context.Connection.Player!;
        // (146, 0) == 1 means the tutorial level has already been shown, so the client can skip it.
        var attr = player.Attributes.Set(TeachGid, 0, request.Teach);
        return Task.FromResult(CallGSResult.Ok("{}", PartyAttributes.Sync(player, attr)));
    }

    private const uint TeachGid = 146;
}

// PartyLogic.lua:689 SetFirstSign -> PartyLogic_SetFirstSign { nId }
[CallGSApi("PartyLogic_SetFirstSign")]
public class PartyLogic_SetFirstSign : CallGSHandler<PartySetIdParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, PartySetIdParam request)
    {
        var player = context.Connection.Player!;
        var attr = player.Attributes.Set(146, 0, 0);
        return Task.FromResult(CallGSResult.Ok("{}", PartyAttributes.Sync(player, attr)));
    }
}

// PartyLogic.lua:716 SetTitle -> PartyLogic_SetTitle { nId }
[CallGSApi("PartyLogic_SetTitle")]
public class PartyLogic_SetTitle : CallGSHandler<PartySetIdParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, PartySetIdParam request)
    {
        var player = context.Connection.Player!;
        var attr = player.Attributes.Set(TitleGid, 0, request.Id);
        // Also mark it unlocked so PartyLogic.GetTitleUnlock keeps returning true afterwards.
        player.Attributes.Set(TitleGid, request.Id, 1);

        var response = new JsonObject { ["nId"] = (int)request.Id };
        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), PartyAttributes.Sync(player, attr)));
    }

    private const uint TitleGid = 147;
}

// PartyLogic.lua:751 SetTitleShow -> PartyLogic_SetTitleShow { nType }
[CallGSApi("PartyLogic_SetTitleShow")]
public class PartyLogic_SetTitleShow : CallGSHandler<PartySetTitleShowParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, PartySetTitleShowParam request)
    {
        var player = context.Connection.Player!;
        var attr = player.Attributes.Set(TitleShowGid, 0, request.Type);
        return Task.FromResult(CallGSResult.Ok("{}", PartyAttributes.Sync(player, attr)));
    }

    private const uint TitleShowGid = 148;
}

public sealed class PartySetMatchModeParam
{
    [JsonPropertyName("nSingle")] public uint Single { get; set; }
}

public sealed class PartySetIdParam
{
    [JsonPropertyName("nId")] public uint Id { get; set; }
}

public sealed class PartySetSkillParam
{
    [JsonPropertyName("nId1")] public uint Id1 { get; set; }
    [JsonPropertyName("nId2")] public uint Id2 { get; set; }
}

public sealed class PartySetRandomMapParam
{
    [JsonPropertyName("nId")] public uint Id { get; set; }
    [JsonPropertyName("nSingle")] public uint? Single { get; set; }
    [JsonPropertyName("tbMapId")] public List<uint>? MapId { get; set; }
    [JsonPropertyName("nSetData")] public uint? SetData { get; set; }
}

public sealed class PartyCheckFirstSignParam
{
    [JsonPropertyName("nId")] public uint Id { get; set; }
    [JsonPropertyName("nTeach")] public uint Teach { get; set; }
}

public sealed class PartySetTitleShowParam
{
    [JsonPropertyName("nType")] public uint Type { get; set; }
}
