using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("SetGroupFurIndex")]
public class SetGroupFurIndex : IHouseFuncHandler
{
    public async Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        if (root == null) return CallGSResult.NoResponse();
        var areaId = HouseJson.NumField(root, "AreaId");
        var groupId = HouseJson.NumField(root, "GroupId");
        var index = HouseJson.NumField(root, "Index");
        var sync = new NtfSyncPlayer();
        if (areaId > 0 && groupId is >= 1 and <= 10)
        {
            var sid = (uint)(areaId * 50 + 20);
            var prev = HouseAttr.Read(context.Connection.Player!, sid);
            var shift = (groupId - 1) * 3;
            var mask = ~(0b111u << shift);
            var next = (prev & mask) | (((uint)index & 0b111u) << shift);
            await HouseAttr.SetAsync(context.Connection, sid, next, sync, sendImmediate: true);
        }

        return CallGSResult.Ok(HouseRequestScript.Synthesize(root), sync);
    }
}
