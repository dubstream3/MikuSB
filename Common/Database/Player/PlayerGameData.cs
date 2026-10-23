using MikuSB.Util.Extensions;
using SqlSugar;
using MikuSB.Proto;

namespace MikuSB.Database.Player;

[SugarTable("Player")]
public class PlayerGameData : BaseDatabaseDataHelper
{
    public const string DefaultDisplayName = "Miku";

    public string? Name { get; set; } = "";
    public string? Signature { get; set; } = "MikuPS";
    public uint Level { get; set; } = 100;
    public int Exp { get; set; } = 0;
    public long RegisterTime { get; set; } = Extensions.GetUnixSec();
    public long LastActiveTime { get; set; }
    public Sex Gender { get; set; } = Sex.Female;
    public uint Vigor {  get; set; } = 240;

    /// <summary>
    /// Unix seconds of the moment the currently accruing stamina point started, mirroring
    /// Player.last_vigor_time. The client computes its recovery countdown as "360 + LastVigorTime"
    /// (Script/UMG/Common/umg_common_purchase_energy.lua:398), so it has to be kept in step with every
    /// change to <see cref="Vigor"/>; see GameServer/Game/Player/VigorService.cs.
    /// </summary>
    public uint LastVigorTime { get; set; }
    [SugarColumn(IsIgnore = true)] public bool CompleteAllQuestLevels { get; set; }

    /// <summary>
    /// False until <see cref="MikuSB.GameServer.Game.Player.CurrencyBootstrap"/> has handed this
    /// account its starting balances. Without the flag the seed would re-apply on every login and a
    /// player could never stay short of anything.
    /// </summary>
    public bool CurrencySeeded { get; set; }

    /// <summary>
    /// Per-shop refresh bookkeeping for the main shop tabs: which period window the stored goods and
    /// the purchase counters belong to, and how many manual refreshes were already paid for. Kept as
    /// one JSON column so adding a shop later does not need a schema migration; see
    /// GameServer/Game/Shop/ShopRefreshService.cs.
    /// </summary>
    [SugarColumn(IsJson = true)] public List<ShopWindowData> ShopWindows { get; set; } = [];
    [SugarColumn(IsJson = true)] public List<PlayerAttr> Attrs { get; set; } = [];
    [SugarColumn(IsJson = true)] public List<PlayerStrAttr> StrAttrs { get; set; } = [];
    [SugarColumn(IsJson = true)] public List<ulong> ShowItems { get; set; } = [];

    public static PlayerGameData? GetPlayerByUid(long uid)
    {
        var result = DatabaseHelper.GetInstance<PlayerGameData>((int)uid);
        return result;
    }

    public static string NormalizeDisplayName(string? name)
    {
        var normalized = name?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? DefaultDisplayName : normalized;
    }

    public bool EnsureDisplayName()
    {
        var normalized = NormalizeDisplayName(Name);
        if (string.Equals(Name, normalized, StringComparison.Ordinal))
            return false;

        Name = normalized;
        return true;
    }

    public PlayerProfile ToProfileProto()
    {
        var displayName = NormalizeDisplayName(Name);
        var proto = new PlayerProfile
        {
            Pid = (uint)Uid,
            Account = displayName,
            Name = displayName,
            Level = Level,
            Sex = Gender,
            Sign = Signature,
        };
        return proto;
    }
    
}

public class PlayerAttr
{
    public uint Gid { get; set; }
    public uint Sid { get; set; }
    public uint Val { get; set; }
}

public class PlayerStrAttr
{
    public uint Gid { get; set; }
    public uint Sid { get; set; }
    public string Val { get; set; } = "";
}

/// <summary>
/// One shop tab's period state. <see cref="GoodsWindow"/> is the start (unix seconds) of the period
/// the stored goods list belongs to, so a new period re-rolls the list; <see cref="LimitWindow"/> is
/// the start of the period the purchase counters belong to, so a new period resets them;
/// <see cref="RefreshNum"/> counts the manual refreshes already paid for inside this goods window.
/// The two windows differ because a shop's shelf period (RefreshRule) and its goods' purchase-limit
/// period (LimitType) are configured independently.
/// </summary>
public class ShopWindowData
{
    public uint ShopId { get; set; }
    public long GoodsWindow { get; set; }
    public long LimitWindow { get; set; }
    public uint RefreshNum { get; set; }
}