using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.GameServer.Game.Dlc;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Rogue;

// DLC1 2D rogue (Script/DLC/Rogue/Rogue.lua). Unlike BagRogue the node graph is a client static table
// (dlc/dlc1/rogue/map1.json, 98 nodes per map) and the client decides which node is clickable from its
// own Next edges + (25,2) states, so the server only persists state: base info, path info, role info,
// formation, shop goods.
//
// The client never writes gid 25 (no SetAttribute anywhere), and (25,1) is the one slot with no
// "or {}" fallback -- GetGoodsPriceBuff() indexes it directly, so a missing/empty object crashes the
// shop widget (Rogue.lua:681-682 -> uw_dlcrogue_goods.lua:53).
public static class RogueService
{
    // (25,9999) carries a timestamp that the DLC1 main panel passes to IsInTime as "now"
    // (umg_dlc1_main.lua:381-384), which is how the 2023 window is kept open.
    private static readonly Random Rng = new();

    public static uint DefaultActionTimes => 3;
    public static uint DefaultGoodsPriceBuff => 100;
    public static uint CurrencyId => 9;

    public static void SeedPinnedOpenStory(PlayerInstance player, NtfSyncPlayer? sync)
    {
        if (DlcRuntimeOverrides.EffectiveDlcId != 1)
            return;

        var act = SelectActivity(DateTime.Now);
        if (act != null)
            SeedOpenStory(player, act, sync);
    }
    public static IEnumerable<RogueActivityExcel> Activities =>
        GameData.RogueActivityData.Values.OrderBy(row => row.Id);

    public static RogueActivityExcel? SelectActivity(DateTime now)
    {
        var rows = Activities.ToList();
        if (rows.Count == 0)
            return null;

        var nowUnix = DlcRuntimeOverrides.ToUnixSeconds(now);
        foreach (var row in rows)
        {
            var start = RogueJson.ToUnix(row.Start);
            var end = RogueJson.ToUnix(row.End);
            if ((start == 0 || nowUnix >= start) && (end == 0 || nowUnix < end))
                return row;
        }

        var started = rows.Where(row => RogueJson.ToUnix(row.Start) is var start && start != 0 && nowUnix >= start).ToList();
        return started.Count > 0 ? started.MaxBy(row => RogueJson.ToUnix(row.Start)) : rows[^1];
    }

    public static RogueActivityExcel? FindActivity(uint id) =>
        GameData.RogueActivityData.TryGetValue(id, out var row) ? row : null;

    // Node ids repeat across maps, so every lookup is (mapId, nodeId).
    public static RogueMapExcel? FindNode(uint mapId, uint nodeId) =>
        GameData.RogueMapData.TryGetValue(mapId * 100000 + nodeId, out var row) ? row : null;

    public static uint CurrentMap(PlayerInstance player)
    {
        var baseInfo = ReadObject(player, AttrIds.Rogue.BaseInfoSid);
        var mapId = MapId(baseInfo);
        if (mapId != 0)
            return mapId;

        var act = SelectActivity(DateTime.Now);
        return act != null ? SelectMapId(act) : 0;
    }

    public static RogueLevelExcel? FindLevel(uint levelId) =>
        GameData.RogueLevelData.TryGetValue(levelId, out var row) ? row : null;

    // The first page of every map starts with a pure entry node (Node=0, Name empty) that points at
    // the first real node; the client hardcodes 10001 as already complete, so nCurNode must be the
    // real one or the map widget indexes a node the table does not have.
    public static uint EntryNode(uint mapId)
    {
        var nodes = GameData.RogueMapData.Values.Where(row => row.MapId == mapId).ToList();
        if (nodes.Count == 0)
            return 0;

        var entry = nodes.Where(row => row.Node == 0).OrderBy(row => row.Id).FirstOrDefault();
        if (entry != null && entry.NextIds.Count > 0)
            return entry.NextIds[0];

        return nodes.Where(row => row.Node != 0).OrderBy(row => row.Id).Select(row => row.Id).FirstOrDefault();
    }

    public static uint SelectMapId(RogueActivityExcel act)
    {
        var nodes = GameData.RogueMapData.Values.Where(row => row.MapId == act.MapId).ToList();
        return nodes.Count > 0 ? act.MapId : (GameData.RogueMapData.Keys.Count > 0 ? GameData.RogueMapData.Values.Min(row => row.MapId) : act.MapId);
    }

    public static string? ReadString(PlayerInstance player, uint sid) =>
        player.Attributes.GetStringValue(AttrIds.Rogue.Gid, sid);

    public static JsonObject ReadObject(PlayerInstance player, uint sid)
    {
        var raw = ReadString(player, sid);
        if (string.IsNullOrWhiteSpace(raw))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(raw) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    public static JsonArray ReadArray(PlayerInstance player, uint sid)
    {
        var raw = ReadString(player, sid);
        if (string.IsNullOrWhiteSpace(raw))
            return new JsonArray();

        try
        {
            return JsonNode.Parse(raw) as JsonArray ?? new JsonArray();
        }
        catch
        {
            return new JsonArray();
        }
    }

    public static void Write(PlayerInstance player, uint sid, JsonNode value, NtfSyncPlayer? sync)
    {
        var attr = player.Attributes.SetString(AttrIds.Rogue.Gid, sid, value.ToJsonString());
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }

    public static JsonObject BuildBaseInfo(RogueActivityExcel act, uint mapId, uint curNode)
    {
        return new JsonObject
        {
            ["nID"] = act.Id,
            ["nMapID"] = mapId,
            ["nCurNode"] = curNode,
            ["nAvaActTimes"] = DefaultActionTimes,
            ["nUpperActTimes"] = DefaultActionTimes,
            ["nUsedActTimes"] = 0,
            ["nRefreshTimes"] = 0,
            ["nReviveTimes"] = 0,
            ["nGoodsPriceBuff"] = DefaultGoodsPriceBuff
        };
    }

    // (25,9999) is a plain decimal timestamp string; tonumber() is all the client does with it.
    public static void SeedOpenStory(PlayerInstance player, RogueActivityExcel act, NtfSyncPlayer? sync)
    {
        if (!string.IsNullOrWhiteSpace(ReadString(player, AttrIds.Rogue.OpenStorySid)))
            return;

        var start = RogueJson.ToUnix(act.Start);
        var end = RogueJson.ToUnix(act.End);
        var fake = start > 0 && end > start ? start + (end - start) / 2 : DlcRuntimeOverrides.ToUnixSeconds(DateTime.Now);
        var attr = player.Attributes.SetString(AttrIds.Rogue.Gid, AttrIds.Rogue.OpenStorySid,
            fake.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }

    public static (RogueActivityExcel Act, JsonObject BaseInfo)? EnsureStarted(PlayerInstance player, NtfSyncPlayer? sync)
    {
        var act = SelectActivity(DateTime.Now);
        if (act == null)
            return null;

        var baseInfo = ReadObject(player, AttrIds.Rogue.BaseInfoSid);
        if (baseInfo.Count == 0)
        {
            var mapId = SelectMapId(act);
            baseInfo = BuildBaseInfo(act, mapId, EntryNode(mapId));
            Write(player, AttrIds.Rogue.BaseInfoSid, baseInfo, sync);
        }

        SeedOpenStory(player, act, sync);
        return (act, baseInfo);
    }

    public static uint CurrentNode(JsonObject baseInfo) =>
        (uint)(baseInfo["nCurNode"]?.GetValue<uint>() ?? 0);

    public static uint MapId(JsonObject baseInfo) =>
        (uint)(baseInfo["nMapID"]?.GetValue<uint>() ?? 0);

    public static uint Field(JsonObject baseInfo, string key) =>
        (uint)(baseInfo[key]?.GetValue<uint>() ?? 0);

    public static void SetField(JsonObject baseInfo, string key, long value) => baseInfo[key] = value;

    public static JsonObject NodeState(JsonObject pathInfo, uint nodeId)
    {
        var key = nodeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (pathInfo[key] is JsonObject existing)
            return existing;

        var created = new JsonObject { ["nState"] = 0 };
        pathInfo[key] = created;
        return created;
    }

    public static void SetNodeState(PlayerInstance player, uint nodeId, uint state, NtfSyncPlayer? sync)
    {
        var pathInfo = ReadObject(player, AttrIds.Rogue.PathInfoSid);
        NodeState(pathInfo, nodeId)["nState"] = state;
        Write(player, AttrIds.Rogue.PathInfoSid, pathInfo, sync);
    }

    public static void MoveNext(PlayerInstance player, uint nodeId, NtfSyncPlayer? sync)
    {
        var act = SelectActivity(DateTime.Now);
        if (act == null)
            return;

        var baseInfo = ReadObject(player, AttrIds.Rogue.BaseInfoSid);
        if (baseInfo.Count == 0)
            baseInfo = BuildBaseInfo(act, SelectMapId(act), nodeId);

        SetField(baseInfo, "nCurNode", nodeId);
        Write(player, AttrIds.Rogue.BaseInfoSid, baseInfo, sync);
        SetNodeState(player, nodeId, 1, sync);
    }

    public static uint NewSeed() =>
        (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);

    // Shop goods live in (25,10) as { "<nodeId>": [ {nGoodsID,nBuyState} ] }. The pools come from
    // shop.json keyed by Periods(= activities.ShopID); one pick per non-empty group, six slots.
    public static JsonArray RollGoods(RogueActivityExcel act, uint nodeId)
    {
        var goods = new List<uint>();
        if (GameData.RogueShopData.TryGetValue(act.ShopId, out var shop))
        {
            foreach (var pool in shop.GoodsPools)
            {
                var pick = PickWeighted(pool);
                if (pick != 0 && !goods.Contains(pick))
                    goods.Add(pick);
            }
        }

        var array = new JsonArray();
        foreach (var id in goods)
            array.Add(new JsonObject { ["nGoodsID"] = id, ["nBuyState"] = 0 });

        return array;
    }

    private static uint PickWeighted(List<(uint Id, uint Weight)> pool)
    {
        if (pool.Count == 0)
            return 0;

        var total = pool.Sum(entry => (long)entry.Weight);
        if (total <= 0)
            return pool[0].Id;

        var roll = Rng.NextInt64(total);
        foreach (var (id, weight) in pool)
        {
            roll -= weight;
            if (roll < 0)
                return id;
        }

        return pool[^1].Id;
    }

    public static void StoreShopGoods(PlayerInstance player, uint nodeId, JsonArray goods, NtfSyncPlayer? sync)
    {
        var list = ReadObject(player, AttrIds.Rogue.ShopGoodsListSid);
        list[nodeId.ToString(System.Globalization.CultureInfo.InvariantCulture)] = goods;
        Write(player, AttrIds.Rogue.ShopGoodsListSid, list, sync);
    }

    public static void EnsureShopGoods(PlayerInstance player, RogueActivityExcel act, uint nodeId, NtfSyncPlayer? sync)
    {
        var list = ReadObject(player, AttrIds.Rogue.ShopGoodsListSid);
        var key = nodeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (list[key] is JsonArray { Count: > 0 })
            return;

        list[key] = RollGoods(act, nodeId);
        Write(player, AttrIds.Rogue.ShopGoodsListSid, list, sync);
    }

    // Formation travels up as {tbFormation = [[roleId, isTrial], ...]} (empty slot = [0]) but comes
    // back down as [{RoleID, nGot}] -- the key names and types are not symmetrical (Rogue.lua:908-920
    // vs :847-860), so the conversion has to happen here.
    public static JsonArray ConvertFormation(JsonArray? incoming)
    {
        var result = new JsonArray();
        for (var slot = 0; slot < 3; slot++)
        {
            var entry = incoming != null && slot < incoming.Count ? incoming[slot] as JsonArray : null;
            var roleId = entry is { Count: > 0 } ? AsUInt(entry[0]) : 0;
            var isTrial = entry is { Count: > 1 } && AsBool(entry[1]);
            if (roleId == 0)
            {
                result.Add(new JsonObject { ["RoleID"] = 0, ["nGot"] = 0 });
                continue;
            }

            result.Add(new JsonObject { ["RoleID"] = roleId, ["nGot"] = isTrial ? 0 : 1 });
        }

        return result;
    }

    private static uint AsUInt(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<uint>(out var number))
                return number;
            if (value.TryGetValue<int>(out var signed) && signed > 0)
                return (uint)signed;
            if (value.TryGetValue<string>(out var text) && uint.TryParse(text, out var parsed))
                return parsed;
        }

        return 0;
    }

    private static bool AsBool(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var flag))
                return flag;
            if (value.TryGetValue<uint>(out var number))
                return number != 0;
        }

        return false;
    }

    // (25,3) is { "<roleId>_P|_T": {RoleID, nIsDead, nCurHP, nGot} }; only HP/death move at
    // settlement, and the key suffix decides item vs trial card on the client side.
    public static void ApplyHealth(PlayerInstance player, JsonNode? tbHp, NtfSyncPlayer? sync)
    {
        if (tbHp is not JsonObject hp || hp.Count == 0)
            return;

        var roles = ReadObject(player, AttrIds.Rogue.RoleInfoSid);
        foreach (var (key, value) in hp)
        {
            if (!uint.TryParse(key, out _))
                continue;

            var entry = roles[key] as JsonObject ?? new JsonObject { ["RoleID"] = 0, ["nGot"] = 0 };
            var percent = AsUInt(value);
            entry["nCurHP"] = percent;
            entry["nIsDead"] = percent == 0 ? 1 : 0;
            roles[key] = entry;
        }

        Write(player, AttrIds.Rogue.RoleInfoSid, roles, sync);
    }

    public static void ReviveFirstDead(PlayerInstance player, NtfSyncPlayer? sync)
    {
        var roles = ReadObject(player, AttrIds.Rogue.RoleInfoSid);
        foreach (var (key, value) in roles)
        {
            if (value is not JsonObject entry)
                continue;

            if (AsUInt(entry["nIsDead"]) == 0)
                continue;

            entry["nIsDead"] = 0;
            entry["nCurHP"] = 100;
            roles[key] = entry;
            break;
        }

        Write(player, AttrIds.Rogue.RoleInfoSid, roles, sync);
    }

    public static void Reset(PlayerInstance player, NtfSyncPlayer? sync)
    {
        var act = SelectActivity(DateTime.Now);
        if (act == null)
            return;

        Write(player, AttrIds.Rogue.PathInfoSid, new JsonObject(), sync);
        Write(player, AttrIds.Rogue.RoleInfoSid, new JsonObject(), sync);
        Write(player, AttrIds.Rogue.BuffInfoSid, new JsonArray(), sync);
        Write(player, AttrIds.Rogue.ShopGoodsListSid, new JsonObject(), sync);
        Write(player, AttrIds.Rogue.BuyListSid, new JsonArray(), sync);
        Write(player, AttrIds.Rogue.FormationInfoSid, new JsonArray(), sync);

        var mapId = SelectMapId(act);
        var baseInfo = BuildBaseInfo(act, mapId, EntryNode(mapId));
        Write(player, AttrIds.Rogue.BaseInfoSid, baseInfo, sync);
    }
}
