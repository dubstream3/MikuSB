using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

/// <summary>
/// Donoduel deck. The mode does NOT use the normal item inventory: it keeps its own card bag in
/// attribute group 185 and the client reads every card straight out of those slots
/// (DLC/DonoDuel/DonoDuel.lua:7-53 constants, :188-205 GetDonoDuelAttribute/GetHalfAttribute,
/// :375-416 GetBagItemTableInfo, :519-533 GetBagAllCard). Layout:
///   (185, 1)   season / act id
///   (185, 2)   highest allocated card unique id (BagItemUnique), 1..350
///   (185, 3)   number of cards currently in the stockroom (CurStockroomNum, compared against the
///              InitBag config value at :501-505 and in umg_donoduel_bag.lua:229)
///   (185, 1001 + (uniqueId - 1) * 10 + n)   card block, seven slots:
///       +0 = (templateId &lt;&lt; 16) | star      -- read as (nTmpId, nStar)
///       +1 = (quality    &lt;&lt; 16) | flagBits  -- read as (nQuality, nFlag)
///       +2 = attribute 1, +3 = attribute 2, +4 = attribute 3
///       +5 = (skill1 &lt;&lt; 16) | skill2
///       +6 = (skill3 &lt;&lt; 16)
///
/// Cards are handed out by the story missions: Reward3 carries [templateId, star, quality] rows
/// (mission 101 = 入坑仪式 -&gt; cards 1/5/9, everything up to mission 114 unlocks the 20 hero card
/// templates in total) and Reward2 merely names the mission that unlocks a given card id
/// (DonoDuelTask.lua:44/151-154 builds tbDonoCardUnlockTask from it). The client has no "add card"
/// request whatsoever, so the ONLY way a deck ever receives a card is the tbCardId array the reward
/// callback feeds back into DonoDuel:GetBagItemAndConfig (DonoDuelTask.lua:610-657) - which means
/// the server has to write these slots itself when it grants a mission reward.
/// </summary>
internal static class DonoDuelBag
{
    private static readonly Logger Logger = new("DonoDuelBag");

    private const uint GroupId = AttrIds.DonoDuel.GroupId;
    private const uint UniqueIdSid = AttrIds.DonoDuel.BagItemUniqueSid;
    private const uint StockroomSid = AttrIds.DonoDuel.CurrentStockroomNumSid;
    private const uint BlockStartSid = AttrIds.DonoDuel.BagItemStartSid;
    private const uint BlockEndSid = AttrIds.DonoDuel.BagItemEndSid;
    private const uint BlockStep = AttrIds.DonoDuel.BagItemStep;
    private const uint MaxUniqueId = AttrIds.DonoDuel.MaxBagItemUnique;

    /// <summary>Slots per card block: header, flags/quality, attr1..3, skill(1,2), skill3.</summary>
    private const int BlockSlots = 7;
    private const uint DefaultStar = 1;
    private const uint DefaultQuality = 1;
    private const uint MaxStar = 5;

    /// <summary>The mission that introduces the mode; its Reward3 is the starting deck.</summary>
    private const uint StarterMissionId = 101;

    /// <summary>Reads a Reward3 column: [[templateId, star, quality], ...].</summary>
    public static List<(uint TemplateId, uint Star, uint Quality)> ReadCardRows(JToken? reward3)
    {
        var cards = new List<(uint, uint, uint)>();
        if (reward3 is not JArray rows)
            return cards;

        foreach (var row in rows)
        {
            if (row is not JArray values || values.Count < 1)
                continue;

            var templateId = ReadUint(values[0]);
            if (templateId == 0)
                continue;

            var star = values.Count > 1 ? ReadUint(values[1]) : 0;
            var quality = values.Count > 2 ? ReadUint(values[2]) : 0;
            cards.Add((templateId, star == 0 ? DefaultStar : star, quality == 0 ? DefaultQuality : quality));
        }

        return cards;
    }

    /// <summary>
    /// Writes the cards into the free blocks of the 185 bag and advances the unique id counter and
    /// the stockroom counter. Returns the new unique ids (in allocation order) - that is exactly what
    /// the client expects in tbRewards.tbCardId, because it resolves each entry with
    /// DonoDuel:GetBagItemAndConfig (a block index, not a template id).
    /// </summary>
    public static List<uint> GrantCards(
        PlayerInstance player,
        IReadOnlyList<(uint TemplateId, uint Star, uint Quality)> cards,
        NtfSyncPlayer? sync)
    {
        var granted = new List<uint>();
        if (cards.Count == 0)
            return granted;

        var allocated = player.Attributes.GetValue(GroupId, UniqueIdSid);
        if (allocated >= MaxUniqueId)
        {
            Logger.Warn($"Donoduel bag is full ({allocated}/{MaxUniqueId}) for uid={player.Uid}; " +
                        $"{cards.Count} card(s) dropped.");
            return granted;
        }

        var stockroom = player.Attributes.GetValue(GroupId, StockroomSid);

        foreach (var card in cards)
        {
            if (allocated >= MaxUniqueId)
            {
                Logger.Warn($"Donoduel bag is full ({allocated}/{MaxUniqueId}) for uid={player.Uid}; " +
                            "remaining card(s) dropped.");
                break;
            }

            allocated++;
            var block = BlockStartSid + (allocated - 1) * BlockStep;
            if (block + BlockSlots - 1 > BlockEndSid)
            {
                Logger.Warn($"Donoduel card block {allocated} would end past sid {BlockEndSid}; stopping.");
                break;
            }

            var star = Math.Min(card.Star, MaxStar);
            Set(player, block, (card.TemplateId << 16) | (star & 0xFFFFu), sync);
            Set(player, block + 1, card.Quality << 16, sync);

            // +2/+3/+4 are the card's attack/defence/health. The client feeds them straight into
            // FSpawnNpcOverrideParams (DLC/DonoDuel/DonoDuelArena.lua:838-841 SpawnPreDeployDono) with
            // no fallback, so storing zeros made every granted card fight at the model template's bare
            // stats and the story arena unwinnable. Seed them from the card's initial row.
            // +5/+6 stay 0: the card -> skill encoding is still unmapped.
            var heroCard = GameData.DonoDuelHeroCardData.GetValueOrDefault(card.TemplateId);
            Set(player, block + 2, heroCard?.AttackInitial ?? 0, sync);
            Set(player, block + 3, heroCard?.DefenseInitial ?? 0, sync);
            Set(player, block + 4, heroCard?.HealthInitial ?? 0, sync);
            Set(player, block + 5, 0, sync);
            Set(player, block + 6, 0, sync);

            granted.Add(allocated);
            stockroom++;
        }

        if (granted.Count == 0)
            return granted;

        Set(player, UniqueIdSid, allocated, sync);
        Set(player, StockroomSid, stockroom, sync);
        return granted;
    }

    /// <summary>
    /// Server side twin of DonoDuel:CheckBagItem (DonoDuel.lua:348-354): an occupied block has a
    /// template id in the high half of its header slot. Used to reject decks that reference cards the
    /// player does not own.
    /// </summary>
    public static bool HasCard(PlayerInstance player, uint uniqueId)
    {
        if (uniqueId == 0 || uniqueId > MaxUniqueId)
            return false;

        var block = BlockStartSid + (uniqueId - 1) * BlockStep;
        return (player.Attributes.GetValue(GroupId, block) >> 16) != 0;
    }

    /// <summary>Hero template id of a deck block (0 when the block is empty).</summary>
    public static uint GetTemplateId(PlayerInstance player, uint uniqueId)
    {
        if (uniqueId == 0 || uniqueId > MaxUniqueId)
            return 0;

        return player.Attributes.GetValue(GroupId, BlockStartSid + (uniqueId - 1) * BlockStep) >> 16;
    }

    /// <summary>
    /// True when the deck already holds a card of that hero template. The rogue mode reports the hero
    /// templates its in-run bag contains (DonoDuelRogue:GetFightBagTmpIdList), so this is what tells
    /// the server which of them still have to be allocated instead of granting the whole bag again on
    /// every room.
    /// </summary>
    public static bool HasTemplate(PlayerInstance player, uint templateId)
    {
        if (templateId == 0)
            return false;

        var allocated = Math.Min(player.Attributes.GetValue(GroupId, UniqueIdSid), MaxUniqueId);
        for (var id = 1u; id <= allocated; id++)
        {
            if (GetTemplateId(player, id) == templateId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// How many cards of that hero template at that star the deck holds. Star-up material must
    /// match template and star (GetBagStarMaterial), so the guide top-up uses this to complete the
    /// guide set without becoming farmable.
    /// </summary>
    public static uint CountTemplateStar(PlayerInstance player, uint templateId, uint star)
    {
        if (templateId == 0)
            return 0;

        var count = 0u;
        var allocated = Math.Min(player.Attributes.GetValue(GroupId, UniqueIdSid), MaxUniqueId);
        for (var id = 1u; id <= allocated; id++)
        {
            var block = BlockStartSid + (id - 1) * BlockStep;
            var header = player.Attributes.GetValue(GroupId, block);
            if ((header >> 16) == templateId && (header & 0xFFFFu) == star)
                count++;
        }

        return count;
    }

    /// <summary>Reads the flag half of a card block (the low 16 bits of header slot +1).</summary>
    public static uint GetFlags(PlayerInstance player, uint uniqueId)
    {
        if (uniqueId == 0 || uniqueId > MaxUniqueId)
            return 0;

        var block = BlockStartSid + (uniqueId - 1) * BlockStep;
        return player.Attributes.GetValue(GroupId, block + 1) & 0xFFFFu;
    }

    /// <summary>Rewrites the flag half of a card block, keeping the quality half untouched.</summary>
    public static void SetFlags(PlayerInstance player, uint uniqueId, uint flags, NtfSyncPlayer? sync)
    {
        if (uniqueId == 0 || uniqueId > MaxUniqueId)
            return;

        var block = BlockStartSid + (uniqueId - 1) * BlockStep;
        var quality = player.Attributes.GetValue(GroupId, block + 1) >> 16;
        Set(player, block + 1, (quality << 16) | (flags & 0xFFFFu), sync);
    }

    /// <summary>Writes one slot of the deck storage (used by the formation handlers).</summary>
    public static void SetSlot(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
        => Set(player, sid, value, sync);

    /// <summary>Persists the attribute changes a deck edit produced.</summary>
    public static void Save(PlayerInstance player) => DatabaseHelper.SaveDatabaseType(player.Data);

    /// <summary>
    /// Repairs an account that claimed the story missions while their cards were still being dropped
    /// on the floor (tbCardId used to be an empty array, so every deck was empty and no battle could
    /// be started). Only fires while the bag counter is still zero, which makes it idempotent and
    /// also means a player who legitimately recycled everything keeps their state.
    /// </summary>
    public static bool EnsureStarterDeck(PlayerInstance player, NtfSyncPlayer? sync = null)
    {
        if (player.Attributes.GetValue(GroupId, UniqueIdSid) != 0)
            return false;

        if (!GameData.DonoDuelStoryTaskData.TryGetValue(StarterMissionId, out var row))
            return false;

        if (!DonoDuelTaskState.IsReceived(player, StarterMissionId))
            return false;

        var cards = ReadCardRows(row.Reward3);
        var granted = GrantCards(player, cards, sync);
        if (granted.Count == 0)
            return false;

        DatabaseHelper.SaveDatabaseType(player.Data);
        Logger.Info($"Seeded the Donoduel starter deck ({granted.Count} card(s)) for uid={player.Uid}.");
        return true;
    }

    /// <summary>Slot index of a card block: BagItemStartSid + (uniqueId - 1) * BagItemStep.</summary>
    public static uint BlockOf(uint uniqueId) => BlockStartSid + (uniqueId - 1) * BlockStep;

    // ---- card growth helpers (DonoDuel_UpgradeStar / UpgradeQuality / UpgradeAttr / UpgradeSkill) ----

    /// <summary>Star of a card (low 16 bits of header slot +0).</summary>
    public static uint GetStar(PlayerInstance player, uint uniqueId)
    {
        if (uniqueId == 0 || uniqueId > MaxUniqueId)
            return 0;

        return player.Attributes.GetValue(GroupId, BlockOf(uniqueId)) & 0xFFFFu;
    }

    /// <summary>Quality of a card (high 16 bits of slot +1).</summary>
    public static uint GetQuality(PlayerInstance player, uint uniqueId)
    {
        if (uniqueId == 0 || uniqueId > MaxUniqueId)
            return 0;

        return player.Attributes.GetValue(GroupId, BlockOf(uniqueId) + 1) >> 16;
    }

    /// <summary>One of the three card attributes: position 1 = attack, 2 = defence, 3 = health.</summary>
    public static uint GetAttr(PlayerInstance player, uint uniqueId, uint position)
    {
        if (uniqueId == 0 || uniqueId > MaxUniqueId || position is < 1 or > 3)
            return 0;

        return player.Attributes.GetValue(GroupId, BlockOf(uniqueId) + 1 + position);
    }

    public static void SetAttr(PlayerInstance player, uint uniqueId, uint position, uint value, NtfSyncPlayer? sync)
    {
        if (uniqueId == 0 || uniqueId > MaxUniqueId || position is < 1 or > 3)
            return;

        Set(player, BlockOf(uniqueId) + 1 + position, value, sync);
    }

    public static void SetStar(PlayerInstance player, uint uniqueId, uint star, NtfSyncPlayer? sync)
    {
        if (uniqueId == 0 || uniqueId > MaxUniqueId)
            return;

        var block = BlockOf(uniqueId);
        var templateId = player.Attributes.GetValue(GroupId, block) >> 16;
        Set(player, block, (templateId << 16) | (star & 0xFFFFu), sync);
    }

    public static void SetQuality(PlayerInstance player, uint uniqueId, uint quality, NtfSyncPlayer? sync)
    {
        if (uniqueId == 0 || uniqueId > MaxUniqueId)
            return;

        var block = BlockOf(uniqueId);
        var flags = player.Attributes.GetValue(GroupId, block + 1) & 0xFFFFu;
        Set(player, block + 1, (quality << 16) | flags, sync);
    }

    /// <summary>Card skill slot read (1..3): +5 holds skill1/skill2 and +6 holds skill3 in its high half.</summary>
    public static uint GetSkill(PlayerInstance player, uint uniqueId, int slot)
    {
        if (uniqueId == 0 || uniqueId > MaxUniqueId || slot is < 1 or > 3)
            return 0;

        var block = BlockOf(uniqueId);
        var value5 = player.Attributes.GetValue(GroupId, block + 5);
        var value6 = player.Attributes.GetValue(GroupId, block + 6);

        return slot switch
        {
            1 => value5 >> 16,
            2 => value5 & 0xFFFFu,
            _ => value6 >> 16
        };
    }

    /// <summary>Writes all three skill slots back (0 clears a slot).</summary>
    public static void SetSkills(PlayerInstance player, uint uniqueId, uint skill1, uint skill2, uint skill3, NtfSyncPlayer? sync)
    {
        if (uniqueId == 0 || uniqueId > MaxUniqueId)
            return;

        var block = BlockOf(uniqueId);
        Set(player, block + 5, (skill1 << 16) | (skill2 & 0xFFFFu), sync);
        Set(player, block + 6, skill3 << 16, sync);
    }

    /// <summary>DonoDuel.Enum_Flag.Lock -- bit0 of the flag half means the whole card is locked.</summary>
    public static bool IsLocked(PlayerInstance player, uint uniqueId) => (GetFlags(player, uniqueId) & 1u) != 0;

    /// <summary>
    /// True when the card occupies one of the three formation decks. Every formation slot packs two
    /// card ids as (high &lt;&lt; 16) | low (DonoDuel.lua:188-205), and the decks are the rogue slots
    /// 10..13, the arena slots 14..17 and the endless arena slots 18..23.
    /// </summary>
    public static bool IsInAnyFormation(PlayerInstance player, uint uniqueId)
    {
        if (uniqueId == 0)
            return false;

        for (var sid = AttrIds.DonoDuel.RogueFormationStartSid; sid <= AttrIds.DonoDuel.EndlessFormationEndSid; sid++)
        {
            var value = player.Attributes.GetValue(GroupId, sid);
            if ((value >> 16) == uniqueId || (value & 0xFFFFu) == uniqueId)
                return true;
        }

        return false;
    }

    /// <summary>Number of cards in the stockroom (the counter the bag UI compares against InitBag).</summary>
    public static uint GetStockroom(PlayerInstance player) => player.Attributes.GetValue(GroupId, StockroomSid);

    public static void SetStockroom(PlayerInstance player, uint value, NtfSyncPlayer? sync)
        => Set(player, StockroomSid, value, sync);

    /// <summary>
    /// Removes a card: all seven block slots go back to zero and the stockroom counter drops by one.
    /// (185, 2) is a monotonically increasing allocator and must never move backwards, otherwise
    /// HasTemplate would start matching the wrong blocks.
    /// </summary>
    public static void DestroyCard(PlayerInstance player, uint uniqueId, NtfSyncPlayer? sync)
    {
        if (uniqueId == 0 || uniqueId > MaxUniqueId)
            return;

        var block = BlockOf(uniqueId);
        for (var slot = 0; slot < BlockSlots; slot++)
            Set(player, block + (uint)slot, 0, sync);

        var stockroom = GetStockroom(player);
        if (stockroom > 0)
            SetStockroom(player, stockroom - 1, sync);
    }

    private static void Set(PlayerInstance player, uint sid, uint value, NtfSyncPlayer? sync)
    {
        var attr = player.Attributes.Set(GroupId, sid, value);
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);
    }

    /// <summary>
    /// Tolerant number read: the shipped tables mix numbers and strings ("1", "[1]", "").
    /// </summary>
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
