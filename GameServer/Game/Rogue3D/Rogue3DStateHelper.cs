using MikuSB.Data;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Rogue3D;

internal static class Rogue3DStateHelper
{
    private const uint GroupId = AttrIds.Rogue3D.Gid;
    private const uint DailyBuffStart = AttrIds.Rogue3D.DailyBuffStartSid;
    private const uint DailyBuffEnd = AttrIds.Rogue3D.DailyBuffEndSid;
    private const int DailyBuffBitCount = 10;
    private const int DailyBuffBitsPerValue = DailyBuffBitCount + 1;
    private const uint DailyBuffMask = (1u << DailyBuffBitCount) - 1;
    private static uint[]? ShuffledDailyBuffIds;

    public static NtfSyncPlayer EnsureUnlockState(PlayerInstance player)
    {
        var sync = new NtfSyncPlayer();

        // The per-difficulty clear counters (AttrIds.Rogue3D.LevelPassStartSid + nDiffId, i.e. sids 21..24)
        // are deliberately left alone here: Rogue3DSettlement is the only writer, and it counts a
        // difficulty once the run is actually cleared. Seeding them would both fake the "cleared" badge in
        // uw_rogue_difficulty.lua:57-62 and skip the progression server_01_difficult.json describes
        // (difficulty 3 -> clear counter of 2, 4 -> 3, season 100 -> 2).

        foreach (var scienceSid in GetUnlockTalentScienceSids())
        {
            EnsureMinAttr(player, scienceSid, 1, sync);
        }

        EnsureDailyBuffAttrs(player, sync);

        return sync;
    }

    private static IEnumerable<uint> GetUnlockTalentScienceSids()
    {
        return GameData.Rogue3DTalentData.Values
            .Select(x => x.UnlockCondition)
            .Where(x => x > 0)
            .Distinct()
            .OrderBy(x => x);
    }

    private static void EnsureDailyBuffAttrs(PlayerInstance player, NtfSyncPlayer sync)
    {
        var buffIds = GetOrCreateDailyBuffIds()
            .Take((int)(DailyBuffEnd - DailyBuffStart + 1) * 3)
            .ToArray();

        var index = 0;
        for (var sid = DailyBuffStart; sid <= DailyBuffEnd; sid++)
        {
            uint packed = 0;
            for (var slot = 0; slot < 3 && index < buffIds.Length; slot++, index++)
            {
                packed |= (buffIds[index] & DailyBuffMask) << (slot * DailyBuffBitsPerValue);
            }

            SetAttr(player, sid, packed, sync);
        }
    }

    private static uint[] GetOrCreateDailyBuffIds()
    {
        if (ShuffledDailyBuffIds != null)
        {
            return ShuffledDailyBuffIds;
        }

        var groupedBuffIds = GameData.Rogue3DDailyBuffData.Values
            .Where(x => x.ScoreBuffId > 0 && x.ScoreBuffId <= DailyBuffMask)
            .GroupBy(x => x.GroupId)
            .OrderBy(x => x.Key)
            .Select(x => x
                .OrderBy(y => y.Id)
                .Select(y => y.ScoreBuffId)
                .Distinct()
                .ToList())
            .ToList();

        var random = new Random();
        foreach (var group in groupedBuffIds)
        {
            Shuffle(group, random);
        }
        Shuffle(groupedBuffIds, random);

        var buffIds = new List<uint>();
        var indexByGroup = new int[groupedBuffIds.Count];
        var hasRemaining = true;

        while (hasRemaining)
        {
            hasRemaining = false;
            for (var i = 0; i < groupedBuffIds.Count; i++)
            {
                var group = groupedBuffIds[i];
                var index = indexByGroup[i];
                if (index >= group.Count)
                {
                    continue;
                }

                buffIds.Add(group[index]);
                indexByGroup[i] = index + 1;
                hasRemaining = true;
            }
        }

        ShuffledDailyBuffIds = buffIds.ToArray();
        return ShuffledDailyBuffIds;
    }

    private static void Shuffle<T>(IList<T> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var swapIndex = random.Next(i + 1);
            (list[i], list[swapIndex]) = (list[swapIndex], list[i]);
        }
    }

    private static void EnsureMinAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.Get(GroupId, sid);
        if (attr == null)
        {
            attr = player.Attributes.GetOrCreate(GroupId, sid);
            attr.Val = value;
            AddSync(player, sync, sid, value);
            return;
        }

        if (attr.Val >= value)
        {
            return;
        }

        attr.Val = value;
        AddSync(player, sync, sid, value);
    }

    private static void SetAttr(PlayerInstance player, uint sid, uint value, NtfSyncPlayer sync)
    {
        var attr = player.Attributes.GetOrCreate(GroupId, sid);

        if (attr.Val == value)
        {
            return;
        }

        attr.Val = value;
        AddSync(player, sync, sid, value);
    }

    private static void AddSync(PlayerInstance player, NtfSyncPlayer sync, uint sid, uint value)
    {
        player.Attributes.SyncTo(sync, GroupId, sid, value);
    }
}
