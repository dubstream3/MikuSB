using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// 多诺-巅峰对决 抓捕玩法: 标签(bond tag)选择.
//
// DLC/DonoDuel/DonoDuelRogue.lua:824-833 DonoDuelRogue:Req_SelectBondTag sends { nBondTag } after
// showing the connection overlay; the callback (:835-842) only closes it again (and shows a tip when
// sErr is set), so the answer carries no data - the picked tag is kept client side
// (DonoDuelRogue.nSelectedBondTag) and rides along in the next enter-level log.
//
// Request:  { nBondTag }
// Response: {}
[CallGSApi("DonoDuelRogue_SelectBondTag")]
public class DonoDuelRogue_SelectBondTag : CallGSHandler<DonoDuelRogueSelectBondTagParam>
{
    private static readonly Logger Log = new(nameof(DonoDuelRogue_SelectBondTag));

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, DonoDuelRogueSelectBondTagParam request)
    {
        Log.Info($"Donoduel rogue bond tag {request.BondTag} selected by uid={context.Connection.Player?.Uid ?? 0}.");
        return Task.FromResult(CallGSResult.Ok(new JsonObject()));
    }
}

public sealed class DonoDuelRogueSelectBondTagParam
{
    [JsonPropertyName("nBondTag")]
    public uint BondTag { get; set; }
}
