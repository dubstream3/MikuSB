using System.Globalization;
using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

// Client side contract: Activity/Activityface.lua (group 60 = AttrIds.Activity.FaceGroupId).
//
// Report (Activityface.SetPopNum / SetPopFlag, lines 200-238):
//   nType = 1 -> pop count incremented (the face is being shown)
//   nType = 2 -> pop count reset to 0
//   nType = 3 -> the 7 day "do not show again" flag is set (bFlag = true) or cleared (false)
//
// Reply (s2c.Register('ActivityFace_Update', ...) lines 366-400): when the answer carries nId the
// client opens the face UI (nType == 1 -> UI.Open(sPopName or "ActiviyFace", {nActivityId = nId}),
// nType == 3 -> sUI:ShowCheckFlag()). Without nId the client advances to the face after nFaceId.
//
// Attribute value layout for sid = face id (Activityface.lua:151-238):
//   bit 0      = 7 day pop flag            (GetBits(value, 0, 0))
//   bits 1-5   = pop count, capped at 30   (GetBits(value, 1, 5))
//   bits 6-30  = pop time, decimal yyyyMMdd; the client parses GetBits(value, 6, 30) * 10000 + 400
//                as "yyyyMMddHHmm" so the value must be the day without the 04:00 hour suffix
// sid 0 holds the data version (Activityface.nVersion == 1). CheckVersion() returns false unless it
// matches, in which case every getter falls back to raw values and the popup logic misbehaves.
[CallGSApi("ActivityFace_Update")]
public class ActivityFace_Update : CallGSHandler
{
    private const uint GroupId = AttrIds.Activity.FaceGroupId;
    private const uint VersionSid = 0;
    private const uint VersionValue = 1;
    private const int MaxPopNum = 30;
    private const uint PopFlagMask = 0x1;
    private const uint PopNumMask = 0x1F;
    private const int PopNumShift = 1;
    private const uint PopTimeMask = 0x1FFFFFF;
    private const int PopTimeShift = 6;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player;
        if (player == null)
            return Task.FromResult(CallGSResult.NoResponse());

        var request = ParseRequest(param);
        if (request == null)
            return Task.FromResult(CallGSResult.NoResponse());

        var sync = new NtfSyncPlayer();

        // Make sure the packed interpretation is enabled on the client.
        SetAttr(player, VersionSid, VersionValue, sync);

        var value = player.Attributes.GetOrCreate(GroupId, request.Value.Id).Val;
        switch (request.Value.Type)
        {
            case 1:
            {
                var count = (int)((value >> PopNumShift) & PopNumMask);
                if (count < MaxPopNum)
                    count++;

                var now = DateTime.Now;
                var day = (uint)(now.Year * 10000 + now.Month * 100 + now.Day);
                value = (value & PopFlagMask)
                    | ((uint)count << PopNumShift)
                    | ((day & PopTimeMask) << PopTimeShift);
                break;
            }
            case 2:
                value &= PopFlagMask;
                break;
            case 3:
                value = request.Value.Flag ? value | PopFlagMask : value & ~PopFlagMask;
                break;
            default:
                Logger.GetByClassName().Warn($"ActivityFace_Update: unknown nType {request.Value.Type}.");
                return Task.FromResult(CallGSResult.NoResponse());
        }

        SetAttr(player, request.Value.Id, value, sync);

        var response = new JsonObject
        {
            ["nFaceId"] = request.Value.Id,
            ["nId"] = request.Value.Id,
            ["nType"] = request.Value.Type,
            ["bFlag"] = request.Value.Flag
        };

        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), sync));
    }

    private static (int Type, uint Id, bool Flag)? ParseRequest(string param)
    {
        try
        {
            if (JsonNode.Parse(param) is not JsonObject json)
                return null;

            var type = json["nType"]?.GetValue<int>() ?? 0;
            var id = ParseUInt(json["nId"]) ?? 0;
            var flag = json["bFlag"]?.GetValue<bool>() ?? false;
            if (type == 0 || id == 0)
                return null;

            return (type, id, flag);
        }
        catch (Exception ex)
        {
            Logger.GetByClassName().Warn($"ActivityFace_Update: bad param '{param}': {ex.Message}");
            return null;
        }
    }

    private static uint? ParseUInt(JsonNode? node)
    {
        if (node == null)
            return null;

        var raw = node.ToString();
        if (uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        return null;
    }

    private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, sid);
        if (attr.Val == value)
            return;

        attr.Val = value;
        player.Attributes.SyncTo(sync, attr);
    }
}
