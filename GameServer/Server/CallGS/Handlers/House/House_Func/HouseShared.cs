using MikuSB.Database.Player;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Text.Json.Nodes;

using MikuSB.Data;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

internal static class HouseRequestScript
{
    internal static readonly Dictionary<string, string> FuncNameMap = new()
    {
        ["ChangeGirlBeachSuitId"] = "ChangeGirlBeachSuitIdSuccess",
        ["ChangeNpcSuitByAreaId"] = "ChangeNpcSuitByAreaIdRsp",
        ["SetGroupFurIndex"] = "SetGroupFurSuccess",
        ["RouletteEnd"] = "RouletteEndRsp",
        ["UnLockWaterGunGameItem"] = "UnLockWaterGunGameItemRsp",
        ["ChangeNpcSuit"] = "ChangeNpcSuitSuccess",
        ["GiveGiftToArea"] = "GiveGiftToAreaSuccess",
        ["BuyRefreshMapTimes"] = "BuyRefreshMapTimesSuccess",
        ["CompleteHousePuzzleTask"] = "CompleteHousePuzzleTaskSuccess",
        ["CompleteStarWishTask"] = "CompleteStarWishTaskSuccess",
        ["SetPlayerRingInfo"] = "SetPlayerRingInfoSuccess",
        ["ReadGirlLoveStory"] = "ReadGirlLoveStorySuccess",
        ["GirlRoomChange"] = "GirlRoomChangeSuccess",
        ["GirlRegister"] = "GirlRegisterSuccess"
    };

    internal static string Synthesize(JsonObject request)
    {
        var funcName = request["FuncName"]?.GetValue<string>();
        var response = CreateSuccessObject();
        foreach (var (key, value) in request)
        {
            if (key is "FuncName" or "tblog")
                continue;

            response[key] = value?.DeepClone();
        }

        if (!string.IsNullOrEmpty(funcName))
            response["FuncName"] = FuncNameMap.TryGetValue(funcName, out var renamed) ? renamed : funcName;

        return response.ToJsonString();
    }

    internal static string Success(JsonObject? extra = null) => CreateSuccessObject(extra).ToJsonString();

    internal static JsonObject CreateSuccessObject(JsonObject? extra = null)
    {
        var response = new JsonObject
        {
            ["bSuccess"] = true,
            ["nResult"] = 0
        };

        if (extra != null)
        {
            foreach (var (key, value) in extra)
                response[key] = value?.DeepClone();
        }

        return response;
    }
}

internal static class HouseJson
{
    internal static JsonObject? ParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    internal static int NumField(JsonObject? obj, string name) => ToInt(obj?[name]);

    internal static int ToInt(JsonNode? node)
    {
        if (node == null) return 0;
        try
        {
            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out var i)) return i;
                if (value.TryGetValue<uint>(out var u)) return unchecked((int)u);
                if (value.TryGetValue<long>(out var l)) return unchecked((int)l);
                if (value.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed)) return parsed;
            }
        }
        catch
        {
        }
        return 0;
    }

    /// <summary>Tolerant boolean read: the client sends Lua true/false, but "1" and 1 also occur.</summary>
    internal static bool BoolField(JsonObject? obj, string name)
    {
        if (obj?[name] is not JsonValue value)
            return false;
        if (value.TryGetValue<bool>(out var flag))
            return flag;
        if (value.TryGetValue<string>(out var text))
            return text is "true" or "1";
        return ToInt(value) != 0;
    }

    /// <summary>
    /// Copies a request back (the client reads its own fields out of the reply) and renames FuncName to a
    /// name the client actually dispatches. Equivalent to <see cref="HouseRequestScript.Synthesize"/>
    /// (HouseShared.cs:30-46) but with an explicit reply name instead of the rename table — needed by the
    /// house mini-games, whose request name and reply name differ without being in FuncNameMap.
    /// </summary>
    internal static JsonObject EchoReply(JsonObject request, string replyFuncName)
    {
        var rsp = new JsonObject();
        foreach (var pair in request)
        {
            if (pair.Key is "FuncName" or "tblog")
                continue;
            rsp[pair.Key] = pair.Value?.DeepClone();
        }

        rsp["FuncName"] = replyFuncName;
        return rsp;
    }
}

internal static class HouseAttr
{
    internal const uint Gid = AttrIds.House.Gid;
    internal const uint BedroomStartSid = AttrIds.House.BedroomStartSid;
    internal const uint BedroomRegisteredNoRoom = 100;
    internal const uint PlayerRingInfoSidBase = AttrIds.House.PlayerRingInfoSidBase;
    internal const uint BedroomPerFloor = 8;

    internal static uint Read(PlayerInstance player, uint sid)
    {
        return player.Attributes.GetValue(Gid, sid);
    }

    internal static async Task SetAsync(
        Connection connection,
        uint sid,
        uint value,
        NtfSyncPlayer sync,
        bool deleteIfZero = false,
        bool sendImmediate = false)
    {
        var player = connection.Player!;
        if (value == 0 && deleteIfZero)
        {
            player.Attributes.Remove(Gid, sid);
        }
        else
        {
            player.Attributes.Set(Gid, sid, value);
        }

        player.Attributes.SyncTo(sync, Gid, sid, value);
        if (sendImmediate)
            await player.SendPacket(CmdIds.NtfSetAttr, new NtfSetAttr { Gid = Gid, Sid = sid, Val = value });
    }

    internal static uint BedroomSlotSid(int roomId) => BedroomStartSid + (uint)roomId;

    internal static uint GirlRoomNumSid(int girlId) => (uint)(girlId * 50 + 1);

    internal static async Task MoveGirlIntoRoomAsync(Connection connection, int girlId, int roomId, NtfSyncPlayer sync)
    {
        var player = connection.Player!;
        var oldRoom = (int)Read(player, GirlRoomNumSid(girlId));
        var targetSlotSid = BedroomSlotSid(roomId);
        var oldSlotSid = oldRoom is >= 1 and < 100 ? BedroomSlotSid(oldRoom) : 0;
        var occupant = Read(player, targetSlotSid);

        await SetAsync(connection, targetSlotSid, (uint)girlId, sync);
        await SetAsync(connection, GirlRoomNumSid(girlId), (uint)roomId, sync);

        if (oldRoom is >= 1 and < 100 && oldRoom != roomId)
            await SetAsync(connection, oldSlotSid, 0, sync);

        if (occupant > 0 && occupant != (uint)girlId)
        {
            if (oldRoom is >= 1 and < 100 && oldRoom != roomId)
            {
                await SetAsync(connection, oldSlotSid, occupant, sync);
                await SetAsync(connection, GirlRoomNumSid((int)occupant), (uint)oldRoom, sync);
            }
            else
            {
                await SetAsync(connection, GirlRoomNumSid((int)occupant), BedroomRegisteredNoRoom, sync);
            }
        }
    }

    internal static uint GetNextBedroomSid(PlayerInstance player, uint floorId)
    {
        var floorStartSid = BedroomStartSid + ((floorId - 2) * BedroomPerFloor) + 1;

        for (uint i = 0; i < BedroomPerFloor; i++)
        {
            var sid = floorStartSid + i;

            if (player.Attributes.Get(Gid, sid) == null)
                return sid;
        }

        return 0;
    }

    internal static uint PackArcadePropUse(int type, int id, ushort count) =>
        (((uint)count & 0xffffu) << 16) | (((uint)id & 0xffu) << 8) | ((uint)type & 0xffu);
}
