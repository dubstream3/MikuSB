using MikuSB.Database.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Player;

public sealed class PlayerAttributes
{
    private readonly PlayerGameData data;
    private readonly Dictionary<(uint Gid, uint Sid), PlayerAttr> attributes = [];
    private readonly Dictionary<(uint Gid, uint Sid), PlayerStrAttr> stringAttributes = [];

    public PlayerAttributes(PlayerGameData data)
    {
        this.data = data;
        foreach (var attr in data.Attrs)
        {
            attributes.TryAdd((attr.Gid, attr.Sid), attr);
        }

        foreach (var attr in data.StrAttrs)
        {
            stringAttributes.TryAdd((attr.Gid, attr.Sid), attr);
        }
    }

    public IReadOnlyList<PlayerAttr> All => data.Attrs;
    public IReadOnlyList<PlayerStrAttr> AllStrings => data.StrAttrs;

    public PlayerAttr? Get(uint gid, uint sid) =>
        attributes.GetValueOrDefault((gid, sid));

    public uint GetValue(uint gid, uint sid) =>
        Get(gid, sid)?.Val ?? 0;

    public bool TryGet(uint gid, uint sid, out PlayerAttr? attr) =>
        attributes.TryGetValue((gid, sid), out attr);

    public PlayerAttr GetOrCreate(uint gid, uint sid)
    {
        if (attributes.TryGetValue((gid, sid), out var attr))
            return attr;

        attr = new PlayerAttr
        {
            Gid = gid,
            Sid = sid
        };
        data.Attrs.Add(attr);
        attributes[(gid, sid)] = attr;
        return attr;
    }

    public PlayerAttr Set(uint gid, uint sid, uint value)
    {
        var attr = GetOrCreate(gid, sid);
        attr.Val = value;
        return attr;
    }

    public PlayerAttr Add(uint gid, uint sid, uint amount)
    {
        var attr = GetOrCreate(gid, sid);
        attr.Val = Math.Min(uint.MaxValue - attr.Val, amount) + attr.Val;
        return attr;
    }

    public bool Remove(uint gid, uint sid)
    {
        if (!attributes.Remove((gid, sid), out var attr))
            return false;

        data.Attrs.Remove(attr);
        return true;
    }

    public void RemoveWhere(Func<PlayerAttr, bool> predicate)
    {
        foreach (var attr in data.Attrs.Where(predicate).ToList())
            Remove(attr.Gid, attr.Sid);
    }

    public PlayerStrAttr? GetString(uint gid, uint sid) =>
        stringAttributes.GetValueOrDefault((gid, sid));

    public string? GetStringValue(uint gid, uint sid) =>
        GetString(gid, sid)?.Val;

    public PlayerStrAttr SetString(uint gid, uint sid, string value)
    {
        if (!stringAttributes.TryGetValue((gid, sid), out var attr))
        {
            attr = new PlayerStrAttr
            {
                Gid = gid,
                Sid = sid
            };
            data.StrAttrs.Add(attr);
            stringAttributes[(gid, sid)] = attr;
        }

        attr.Val = value;
        return attr;
    }

    public void SyncTo(NtfSyncPlayer sync, PlayerAttr attr)
    {
        sync.Custom[ToPackedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
        sync.Custom[ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
    }

    public void SyncTo(NtfSyncPlayer sync, uint gid, uint sid, uint value)
    {
        sync.Custom[ToPackedAttrKey(gid, sid)] = value;
        sync.Custom[ToShiftedAttrKey(gid, sid)] = value;
    }

    public void SyncTo(NtfSyncPlayer sync, PlayerStrAttr attr)
    {
        sync.CustomStr[ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
    }

    public void SyncTo(Proto.Player player)
    {
        foreach (var attr in data.Attrs)
        {
            player.Attrs[ToPackedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
            player.Attrs[ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
        }
    }

    public void SyncTo(Proto.Player player, PlayerStrAttr attr)
    {
        player.StrAttrs[ToShiftedAttrKey(attr.Gid, attr.Sid)] = attr.Val;
    }

    public uint ToPackedAttrKey(uint gid, uint sid)
    {
        if (gid == 0)
            return sid;

        return (gid * 10000) + sid;
    }

    public uint ToShiftedAttrKey(uint gid, uint sid)
    {
        if (gid == 0)
            return sid;

        return (gid << 16) | sid;
    }
}
