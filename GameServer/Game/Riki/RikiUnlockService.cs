using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;

namespace MikuSB.GameServer.Game.Riki;

/// <summary>
/// 图鉴 (riki) unlock bookkeeping for the monster page.
///
/// The client owns the page: riki.lua:77-82 reads an entry's unlock bit from attribute
/// (103, nTaskId) and its "new" dot from (103, nTaskId+500), with the id mapping in riki.lua:552-560.
/// The client never writes the unlock bit (only CleanDot touches the +500 slot), so every unlock has to
/// come from the server - and until now nothing did: AttrIds.Misc.RikiTaskGroupId (103) had no
/// reference anywhere in the server.
///
/// The report side already exists in the protocol: every settlement payload carries tbMonster, filled
/// from RikiLogic:GetMonsterData(TaskSubActor) (riki.lua:539-550) at 30 call sites across chapter,
/// daily, role, boss, defend, tower, online and rogue3d. This bridge takes those reports, maps each
/// monster template id to its riki entry through the Type=4 rows of riki/Riki.json
/// (GameData.RikiMonsterData) and sets the bit.
///
/// The 异星守护 (alien defense) branch does not report monsters at all (AlienDefense.lua:942-979 sends
/// no tbMonster) and its monster ids (47012xxxx) have no Riki.json entry, so AD clears cannot unlock a
/// 图鉴 entry even with this bridge - that needs data the shipped tables do not contain.
/// </summary>
public static class RikiUnlockService
{
    /// <summary>riki.lua:10 RikiLogic.nTaskGroup.</summary>
    private const uint GroupId = AttrIds.Misc.RikiTaskGroupId;

    /// <summary>Slot distance between the unlock bits and the "unseen" dot bits (riki.lua:80).</summary>
    private const uint DotSlotOffset = 500;

    /// <summary>
    /// Unlocks every monster reported in a settlement payload. Returns how many entries flipped, and
    /// fills <paramref name="sync"/> so the caller can hand the new bits to the client.
    /// </summary>
    public static int UnlockMonsters(PlayerInstance player, JsonNode? tbParam, NtfSyncPlayer? sync)
    {
        if (tbParam is not JsonObject obj ||
            !obj.TryGetPropertyValue("tbMonster", out var node) ||
            node is not JsonArray monsters)
        {
            return 0;
        }

        var unlocked = 0;
        foreach (var monster in monsters)
        {
            if (monster is not JsonValue value ||
                !value.TryGetValue<uint>(out var monsterId) ||
                monsterId == 0)
            {
                continue;
            }

            unlocked += UnlockMonster(player, monsterId, sync);
        }

        if (unlocked > 0)
            DatabaseHelper.SaveDatabaseType(player.Data);

        return unlocked;
    }

    private static int UnlockMonster(PlayerInstance player, uint monsterId, NtfSyncPlayer? sync)
    {
        if (!GameData.RikiMonsterData.TryGetValue(monsterId, out var rikiId) || rikiId == 0)
            return 0;

        var (taskId, bit) = GetTaskSlot(rikiId);
        if (taskId == 0 || bit is < 0 or > 31)
            return 0;

        var attr = player.Attributes.GetOrCreate(GroupId, taskId);
        var mask = 1u << bit;
        if ((attr.Val & mask) != 0)
            return 0;

        attr.Val |= mask;
        if (sync != null)
            player.Attributes.SyncTo(sync, attr);

        return 1;
    }

    /// <summary>
    /// Mirrors riki.lua:552-560 GetTask: 4001 -&gt; task 91, bit 1. The entry id encodes the group in its
    /// thousands and the offset inside the group in the last three digits, 30 bits per packed slot.
    /// </summary>
    private static (uint TaskId, int Bit) GetTaskSlot(uint rikiId)
    {
        var index = rikiId / 1000u;
        var value = rikiId % 1000u;
        if (index == 0)
            return (0, -1);

        var taskId = (index - 1) * 30u + (uint)Math.Ceiling(value / 30.0);
        return (taskId, (int)(value % 30u));
    }
}
