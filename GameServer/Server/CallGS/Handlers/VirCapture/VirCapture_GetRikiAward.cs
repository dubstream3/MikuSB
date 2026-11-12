using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;
using Newtonsoft.Json.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

/// <summary>
/// Claims capture-book milestones. The client sends the ids it considers claimable
/// (VirCapture.lua:600-615 GetAwardRickList) but the server decides: the milestone must be inside
/// the current activity and the number of distinct unlocked monsters (gid 135 entries above zero)
/// must reach its Point.
///
/// Claim flags use the packed layout the client reads back in GetRikiAwardFlag
/// (VirCapture.lua:289-297): sid = 121 + Id/30, bit = Id%30. The reply feeds tbAwardList into
/// VirCapture:MergeItems (umg_vircapture_book.lua:421-427), which walks one nested list per row
/// group, so each milestone's rows are sent as their own array.
/// </summary>
[CallGSApi("VirCapture_GetRikiAward")]
public class VirCapture_GetRikiAward : CallGSHandler<VirCaptureGetRikiAwardParam>
{
    private const uint FlagStartSid = AttrIds.VirCapture.RikiAwardFlagStartSid;
    private const uint FlagEndSid = AttrIds.VirCapture.RikiAwardFlagEndSid;
    private const uint BitsPerSid = 30;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureGetRikiAwardParam req)
    {
        var player = context.Connection.Player!;
        if (VirCaptureStateHelper.ResolveCurrentAct(player) == null)
        {
            return CallGSResult.Error("ui.TxtNotOpen");
        }

        if (req?.IdList == null || req.IdList.Count == 0)
        {
            return CallGSResult.Error("error.BadParam");
        }

        var unlocked = VirCaptureStateHelper.CountUnlockedRiki(player);
        var sync = new NtfSyncPlayer();
        var awardList = new JsonArray();
        var granted = 0;

        foreach (var rawId in req.IdList.Distinct())
        {
            if (!GameData.VirCaptureRikiAwardData.TryGetValue(rawId, out var milestone))
                continue;

            if (unlocked < milestone.Point)
                continue;

            var sid = FlagStartSid + milestone.Id / BitsPerSid;
            if (sid > FlagEndSid)
                continue;

            var bit = milestone.Id % BitsPerSid;
            var attr = player.Attributes.GetOrCreate(AttrIds.VirCapture.Gid, sid);
            var mask = 1u << (int)bit;
            if ((attr.Val & mask) != 0)
                continue;

            var rows = ParseRewards(milestone.Rewards);
            if (rows.Count == 0)
                continue;

            var group = new JsonArray();
            foreach (var row in rows)
            {
                if (!await VirCaptureRewardHelper.GrantItemRewardAsync(player, sync, row))
                    continue;
                group.Add(new JsonArray(
                    (int)row[0], (int)row[1], (int)row[2], (int)row[3], (int)row[4]));
            }

            if (group.Count == 0)
                continue;

            attr.Val |= mask;
            player.Attributes.SyncTo(sync, attr);
            awardList.Add(group);
            granted++;
        }

        if (granted == 0)
        {
            // Everything asked for was already claimed or not eligible; report success (S3) so the
            // client closes its spinner and redraws from the synced flags.
            return CallGSResult.Ok(new JsonObject { ["tbAwardList"] = new JsonArray() }.ToJsonString());
        }

        VirCaptureRewardHelper.Save(player);
        return CallGSResult.Ok(new JsonObject { ["tbAwardList"] = awardList }.ToJsonString(), sync);
    }

    private static List<uint[]> ParseRewards(JToken? token)
    {
        var rows = new List<uint[]>();
        if (token is not JArray array)
            return rows;

        foreach (var row in array)
        {
            if (row is not JArray cells || cells.Count < 5)
                continue;
            rows.Add([
                cells[0].Value<uint>(), cells[1].Value<uint>(), cells[2].Value<uint>(),
                cells[3].Value<uint>(), Math.Max(1u, cells[4].Value<uint>())
            ]);
        }

        return rows;
    }
}

public sealed class VirCaptureGetRikiAwardParam
{
    [JsonPropertyName("nId")]
    public int Id { get; set; }

    [JsonPropertyName("tbIdList")]
    public List<uint> IdList { get; set; } = [];
}
