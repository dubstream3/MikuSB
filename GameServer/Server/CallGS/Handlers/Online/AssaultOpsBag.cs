using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Online;

/// <summary>
/// 进击行动 (assault ops, DLC35 "Assaultops" = online id 106) bag. The mode keeps its whole state in
/// attribute group 183 and the client reads the bag straight out of those slots
/// (Launch/Online/Classes/OnlineAssaultOps.lua:7-43 constants, :174-290 readers):
///   (183, 2)   formation the player last entered with (GetEquipFormation, :206-208)
///   (183, 3)   season id; the mission list stays hidden while it does not resolve in the shipped
///              online/AssaultOps/assaultops_season.json (GetMaxSeasonId, :211-213 + :783-805)
///   (183, 4)   season contribution (GetSeasonContribution, :216-218)
///   (183, 5)   system-equip flag, one byte per formation (GetFormationSysFlag, :221-233)
///   (183, 11 + (formation-1)*10 + (position-1)/2)   two chip ids per slot: the odd position sits in
///              bits 0..15 and the even one in bits 16..31 (GetFormationValue, :236-255)
///   (183, 101 + (chipId-1)/3)                       three chip levels per slot, 10 bits each
///              (GetBagChipLevel, :258-275); a level of 0 means "not owned", which is also the filter
///              of the chip list (GetAllShowChip, :820-847)
///   (183, 1001 + (season-1)*30 + (position-1))      per-season mission progress (GetHistoryValue)
///
/// The client never writes these slots itself - every change goes through one of the four CallGS
/// requests (Equip / ChangeFormation / SystemEquip / QuestAward) - so the server owns the bag.
/// </summary>
internal static class AssaultOpsBag
{
    private static readonly Logger Logger = new("AssaultOpsBag");

    private const uint GroupId = AttrIds.AssaultOps.GroupId;
    private const uint TakeFormationSid = AttrIds.AssaultOps.TakeFormationSid;
    private const uint MaxSeasonSid = AttrIds.AssaultOps.MaxSeasonSid;
    private const uint SysModeFlagSid = AttrIds.AssaultOps.SysModeFlagSid;
    private const uint FormationStartSid = AttrIds.AssaultOps.FormationStartSid;
    private const uint FormationStep = AttrIds.AssaultOps.FormationStep;
    private const uint FormationCount = AttrIds.AssaultOps.FormationCount;
    private const uint FormationPositions = AttrIds.AssaultOps.FormationPositions;
    private const uint ChipStartSid = AttrIds.AssaultOps.ChipStartSid;
    private const uint ChipsPerSlot = AttrIds.AssaultOps.ChipsPerSlot;
    private const uint ChipLevelBits = AttrIds.AssaultOps.ChipLevelBits;
    private const uint MaxChipId = AttrIds.AssaultOps.MaxChipId;
    private const uint DefaultSeasonId = AttrIds.AssaultOps.DefaultSeasonId;

    /// <summary>Level handed out when a chip row does not carry MaxQuality.</summary>
    private const uint DefaultChipLevel = 1;

    private const uint ChipLevelMask = (1u << (int)ChipLevelBits) - 1;
    private const uint HalfMask = 0xFFFFu;

    /// <summary>Formation 1..3, position 1..10: the slots the formation panel can equip.</summary>
    public static bool IsValidSlot(uint formationId, uint position)
        => formationId >= 1 && formationId <= FormationCount
           && position >= 1 && position <= FormationPositions;

    public static bool IsValidChip(uint chipId) => chipId >= 1 && chipId <= MaxChipId;

    /// <summary>Attribute slot that stores two chip ids of one formation.</summary>
    public static uint FormationSlotSid(uint formationId, uint position)
        => FormationStartSid + (formationId - 1) * FormationStep + (position - 1) / 2;

    /// <summary>Bit offset of a formation position inside its slot (odd positions are the low half).</summary>
    public static int FormationShift(uint position) => (int)((position - 1) % 2) * 16;

    /// <summary>Attribute slot that stores three chip levels.</summary>
    public static uint ChipSlotSid(uint chipId) => ChipStartSid + (chipId - 1) / ChipsPerSlot;

    /// <summary>Bit offset of a chip level inside its slot.</summary>
    public static int ChipShift(uint chipId) => (int)((chipId - 1) % ChipsPerSlot) * (int)ChipLevelBits;

    /// <summary>Level the client sees for a chip id; 0 means "not owned", so it stays hidden.</summary>
    public static uint GetChipLevel(PlayerInstance player, uint chipId)
    {
        if (!IsValidChip(chipId))
            return 0;

        var value = player.Attributes.GetValue(GroupId, ChipSlotSid(chipId));
        return (value >> ChipShift(chipId)) & ChipLevelMask;
    }

    /// <summary>Chip id equipped in one formation position (0 = empty).</summary>
    public static uint GetFormationChip(PlayerInstance player, uint formationId, uint position)
    {
        if (!IsValidSlot(formationId, position))
            return 0;

        var value = player.Attributes.GetValue(GroupId, FormationSlotSid(formationId, position));
        return (value >> FormationShift(position)) & HalfMask;
    }

    /// <summary>Writes one formation position, keeping the chip stored in the other half intact.</summary>
    public static void SetFormationChip(
        PlayerInstance player, uint formationId, uint position, uint chipId, NtfSyncPlayer? sync)
    {
        if (!IsValidSlot(formationId, position))
            return;

        var sid = FormationSlotSid(formationId, position);
        var shift = FormationShift(position);
        var value = player.Attributes.GetValue(GroupId, sid);
        var mask = HalfMask << shift;
        Set(player, sid, (value & ~mask) | ((chipId & HalfMask) << shift), sync);
    }

    /// <summary>Byte that remembers which recommended scheme a formation follows.</summary>
    public static uint GetFormationSysFlag(PlayerInstance player, uint formationId)
    {
        if (formationId < 1 || formationId > FormationCount)
            return 0;

        var value = player.Attributes.GetValue(GroupId, SysModeFlagSid);
        return (value >> (int)(8 * (formationId - 1))) & 0xFFu;
    }

    public static void SetFormationSysFlag(
        PlayerInstance player, uint formationId, uint flag, NtfSyncPlayer? sync)
    {
        if (formationId < 1 || formationId > FormationCount)
            return;

        var shift = (int)(8 * (formationId - 1));
        var value = player.Attributes.GetValue(GroupId, SysModeFlagSid);
        var mask = 0xFFu << shift;
        Set(player, SysModeFlagSid, (value & ~mask) | ((flag & 0xFFu) << shift), sync);
    }

    /// <summary>Formation the next 出击 enters with.</summary>
    public static void SetTakeFormation(PlayerInstance player, uint formationId, NtfSyncPlayer? sync)
        => Set(player, TakeFormationSid, formationId, sync);

    /// <summary>Writes one chip level, keeping the other two chips of the slot intact.</summary>
    public static void SetChipLevel(PlayerInstance player, uint chipId, uint level, NtfSyncPlayer? sync)
    {
        if (!IsValidChip(chipId))
            return;

        var sid = ChipSlotSid(chipId);
        var shift = ChipShift(chipId);
        var value = player.Attributes.GetValue(GroupId, sid);
        var mask = ChipLevelMask << shift;
        Set(player, sid, (value & ~mask) | ((level & ChipLevelMask) << shift), sync);
    }

    /// <summary>Persists the attribute changes a bag edit produced.</summary>
    public static void Save(PlayerInstance player) => DatabaseHelper.SaveDatabaseType(player.Data);

    /// <summary>
    /// Gives an account the assault ops content it can never obtain from this server: the season id
    /// (without it every mission stays hidden), the formation it enters with and each chip of the
    /// shipped table at its MaxQuality. With an empty chip bag no formation can be assembled at all,
    /// because CheckFormationEmpty (:1376-1395) blocks 出击 while every unlocked slot is empty.
    /// Idempotent: it only fires while the season slot is still zero, so a player who owns nothing
    /// else keeps their state.
    /// </summary>
    public static bool EnsureSeeded(PlayerInstance player, NtfSyncPlayer? sync = null)
    {
        if (player.Attributes.GetValue(GroupId, MaxSeasonSid) != 0)
            return false;

        Set(player, MaxSeasonSid, DefaultSeasonId, sync);
        if (player.Attributes.GetValue(GroupId, TakeFormationSid) == 0)
            Set(player, TakeFormationSid, 1, sync);

        var chips = 0;
        foreach (var (chipId, level) in ReadChipTable())
        {
            SetChipLevel(player, chipId, level, sync);
            chips++;
        }

        Save(player);
        Logger.Info(
            $"Seeded the assault ops bag for uid={player.Uid}: season={DefaultSeasonId}, {chips} chip(s).");
        return true;
    }

    /// <summary>Chip rows of online/AssaultOps/assaultops_chips.json as (id, level to seed).</summary>
    private static List<(uint ChipId, uint Level)> ReadChipTable()
    {
        var chips = new List<(uint, uint)>();
        var path = Path.Combine(
            ConfigManager.Config.Path.ResourcePath, "online", "AssaultOps", "assaultops_chips.json");

        if (!File.Exists(path))
        {
            Logger.Warn($"Assault ops chip table not found at {path}; the bag stays empty.");
            return chips;
        }

        try
        {
            var rows = JsonConvert.DeserializeObject<List<JObject>>(File.ReadAllText(path)) ?? [];
            foreach (var row in rows)
            {
                var chipId = ReadUint(row["Id"]);
                if (!IsValidChip(chipId))
                    continue;

                var level = ReadUint(row["MaxQuality"]);
                chips.Add((chipId, level == 0 ? DefaultChipLevel : level));
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Could not read the assault ops chip table: {ex.Message}", ex);
        }

        return chips;
    }

    private static void Set(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
    {
        var attr = player.Attributes.Set(GroupId, sid, value);
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }

    /// <summary>Tolerant number read: the shipped tables mix numbers and strings ("1", "[1]", "").</summary>
    private static uint ReadUint(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return 0;

        try
        {
            return token.Type switch
            {
                JTokenType.Integer => token.Value<uint>(),
                JTokenType.Float => (uint)token.Value<double>(),
                _ => uint.TryParse(token.ToString().Trim().Trim('[', ']', '"'), out var parsed) ? parsed : 0
            };
        }
        catch
        {
            return 0;
        }
    }
}
