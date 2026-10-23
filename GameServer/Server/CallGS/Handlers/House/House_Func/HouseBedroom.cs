using MikuSB.Proto;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[HouseFunc("GirlRegister")]
public class GirlRegister : IHouseFuncHandler
{
    public async Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        if (root == null) return CallGSResult.NoResponse();
        var girlId = HouseJson.NumField(root, "GirlId");
        var floorId = HouseJson.NumField(root, "FloorId");
        var sync = new NtfSyncPlayer();
        if (girlId > 0)
        {
            var bedroomSid = HouseAttr.GetNextBedroomSid(context.Connection.Player!, (uint)floorId);
            await HouseAttr.SetAsync(context.Connection, HouseAttr.GirlRoomNumSid(girlId), HouseAttr.BedroomRegisteredNoRoom, sync);
            if (bedroomSid != 0)  await HouseAttr.SetAsync(context.Connection, bedroomSid, (uint)girlId, sync);
        }
            

        return CallGSResult.Ok(HouseRequestScript.Synthesize(root), sync);
    }
}

[HouseFunc("SetBedroomGirlId")]
public class SetBedroomGirlId : IHouseFuncHandler
{
    public async Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        if (root == null) return CallGSResult.NoResponse();
        var bedroomId = HouseJson.NumField(root, "BedroomId");
        var girlId = HouseJson.NumField(root, "GirlId");
        var sync = new NtfSyncPlayer();
        if (bedroomId > 0 && girlId > 0)
            await HouseAttr.MoveGirlIntoRoomAsync(context.Connection, girlId, bedroomId, sync);

        return CallGSResult.Ok(HouseRequestScript.Synthesize(root), sync);
    }
}

[HouseFunc("GirlRoomChange")]
public class GirlRoomChange : IHouseFuncHandler
{
    public async Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        if (root == null) return CallGSResult.NoResponse();
        var girlId = HouseJson.NumField(root, "GirlId");
        var roomId = HouseJson.NumField(root, "RoomId");
        var oldRoomId = girlId > 0 ? (int)HouseAttr.Read(context.Connection.Player!, HouseAttr.GirlRoomNumSid(girlId)) : 0;
        var sync = new NtfSyncPlayer();
        if (girlId > 0 && roomId > 0)
            await HouseAttr.MoveGirlIntoRoomAsync(context.Connection, girlId, roomId, sync);

        var rsp = new JsonObject
        {
            ["FuncName"] = "GirlRoomChangeSuccess",
            ["GirlId"] = girlId,
            ["OldRoomId"] = oldRoomId,
            ["NewRoomId"] = roomId
        };
        return CallGSResult.Ok(HouseRequestScript.Success(rsp), sync);
    }
}

[HouseFunc("GirlLeaveRoom")]
public class GirlLeaveRoom : IHouseFuncHandler
{
    public async Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        if (root == null) return CallGSResult.NoResponse();
        var bedroomId = HouseJson.NumField(root, "BedroomId");
        var girlId = HouseJson.NumField(root, "GirlId");
        var sync = new NtfSyncPlayer();
        if (bedroomId > 0 && girlId > 0)
        {
            await HouseAttr.SetAsync(context.Connection, HouseAttr.BedroomSlotSid(bedroomId), 0, sync);
            await HouseAttr.SetAsync(context.Connection, HouseAttr.GirlRoomNumSid(girlId), HouseAttr.BedroomRegisteredNoRoom, sync);
        }

        return CallGSResult.Ok(HouseRequestScript.Synthesize(root), sync);
    }
}

[HouseFunc("ExchangeRoomGirl")]
public class ExchangeRoomGirl : IHouseFuncHandler
{
    public async Task<CallGSResult> Handle(CallGSContext context, string param)
    {
        var root = HouseJson.ParseObject(param);
        if (root == null) return CallGSResult.NoResponse();
        var roomId1 = HouseJson.NumField(root, "RoomId1");
        var roomId2 = HouseJson.NumField(root, "RoomId2");
        var sync = new NtfSyncPlayer();
        if (roomId1 > 0 && roomId2 > 0 && roomId1 != roomId2)
        {
            var slot1 = HouseAttr.BedroomSlotSid(roomId1);
            var slot2 = HouseAttr.BedroomSlotSid(roomId2);
            var girl1 = HouseAttr.Read(context.Connection.Player!, slot1);
            var girl2 = HouseAttr.Read(context.Connection.Player!, slot2);
            await HouseAttr.SetAsync(context.Connection, slot1, girl2, sync);
            await HouseAttr.SetAsync(context.Connection, slot2, girl1, sync);
            if (girl1 > 0) await HouseAttr.SetAsync(context.Connection, HouseAttr.GirlRoomNumSid((int)girl1), (uint)roomId2, sync);
            if (girl2 > 0) await HouseAttr.SetAsync(context.Connection, HouseAttr.GirlRoomNumSid((int)girl2), (uint)roomId1, sync);
        }

        return CallGSResult.Ok(HouseRequestScript.Synthesize(root), sync);
    }
}
