using MikuSB.Data;
using MikuSB.Proto;
using MikuSB.Util.Extensions;

namespace MikuSB.GameServer.Game.Player;

/// <summary>
/// Stamina ("vigor") bookkeeping. Vigor is not a group 1 attribute on the wire: it is
/// <see cref="PlayerGameData.Vigor"/> plus the <see cref="PlayerCoreAttribute.Vigor"/> core entry,
/// and the client derives its recovery countdown from <c>me:LastVigorTime()</c>:
///   Script/UMG/Common/umg_common_purchase_energy.lua:398  TimeDiff(360 + me:LastVigorTime(), GetTime())
///   Script/LocalNotification/LocalNotification.lua:153   me:LastVigorTime() + (360 * (nMax - nHave))
/// so the interval is a fixed 360 seconds per point and the stored timestamp is the moment the
/// current partial point started accruing. The cap comes from the level table
/// (Script/Player/player.lua:57-58 GetMaxVigor -> tbLevelCfg[nLevel].nMaxVigor).
/// </summary>
public static class VigorService
{
    /// <summary>Seconds of real time per recovered point (client hardcodes 360 in two places).</summary>
    public const uint RecoverIntervalSeconds = 360;

    /// <summary>Fallback cap when the level table has no row for the player's level.</summary>
    private const uint DefaultMaxVigor = 240;

    public static uint GetMaxVigor(PlayerInstance player) =>
        GameData.PlayerLevelData.TryGetValue(player.Data.Level, out var level) && level.MaxVigor > 0
            ? level.MaxVigor
            : DefaultMaxVigor;

    /// <summary>
    /// Applies the points earned since <see cref="PlayerGameData.LastVigorTime"/> and advances the
    /// timestamp. Call it once per login: the value is persisted, so repeated logins keep accruing.
    /// Returns the number of points recovered.
    /// </summary>
    public static uint Regen(PlayerInstance player)
    {
        var now = (uint)Extensions.GetUnixSec();
        var data = player.Data;

        // First login for this account: start the clock instead of handing out a windfall.
        if (data.LastVigorTime == 0)
        {
            data.LastVigorTime = now;
            return 0;
        }

        if (now <= data.LastVigorTime)
            return 0;

        var max = GetMaxVigor(player);
        if (data.Vigor >= max)
        {
            // Keep the clock at "now" while full so the first point after spending starts fresh.
            data.LastVigorTime = now;
            return 0;
        }

        var elapsed = now - data.LastVigorTime;
        var recovered = elapsed / RecoverIntervalSeconds;
        if (recovered == 0)
            return 0;

        var vigor = data.Vigor + Math.Min(recovered, max - data.Vigor);
        data.Vigor = vigor;
        // Carry the remainder of the last partial interval forward so the countdown stays honest.
        data.LastVigorTime = vigor >= max ? now : data.LastVigorTime + recovered * RecoverIntervalSeconds;

        var sync = new NtfSyncPlayer();
        sync.Core[(uint)PlayerCoreAttribute.Vigor] = data.Vigor;
        sync.Core[(uint)PlayerCoreAttribute.VigorTime] = data.LastVigorTime;
        _ = player.SendPacket(CmdIds.NtfSyncAttr, sync);

        return recovered;
    }

    /// <summary>
    /// Grants stamina from a reward or a potion, clamped to the level cap, and resets the recovery
    /// clock when the player was already full (otherwise the countdown would keep running while
    /// there is nothing to recover).
    /// </summary>
    /// <param name="allowOverflow">
    /// Bought stamina (数据金 exchange) is allowed past the level cap, which is what the retail
    /// server does: the client renders the overflow as a full bar and <see cref="Regen"/> keeps
    /// treating "at or above the cap" as full, so nothing extra regenerates. Rewards and potions
    /// keep the clamp.
    /// </param>
    public static void Add(PlayerInstance player, uint amount, NtfSyncPlayer? sync = null, bool allowOverflow = false)
    {
        if (amount == 0)
            return;

        var data = player.Data;
        var max = GetMaxVigor(player);
        var wasFull = data.Vigor >= max;

        data.Vigor = allowOverflow
            ? (uint)Math.Min(uint.MaxValue, (ulong)data.Vigor + amount)
            : Math.Min(max, data.Vigor + Math.Min(amount, uint.MaxValue - data.Vigor));
        if (wasFull)
            data.LastVigorTime = (uint)Extensions.GetUnixSec();

        if (sync == null)
            return;

        sync.Core[(uint)PlayerCoreAttribute.Vigor] = data.Vigor;
        sync.Core[(uint)PlayerCoreAttribute.VigorTime] = data.LastVigorTime;
    }

    /// <summary>Sets an absolute value (GM path), clamped to the level cap.</summary>
    public static void Set(PlayerInstance player, uint value, NtfSyncPlayer? sync = null)
    {
        var data = player.Data;
        data.Vigor = Math.Min(GetMaxVigor(player), value);
        data.LastVigorTime = (uint)Extensions.GetUnixSec();

        if (sync == null)
            return;

        sync.Core[(uint)PlayerCoreAttribute.Vigor] = data.Vigor;
        sync.Core[(uint)PlayerCoreAttribute.VigorTime] = data.LastVigorTime;
    }

    /// <summary>
    /// Spends stamina. Returns false (and changes nothing) when the player cannot afford it; on
    /// success the recovery clock starts if it was not already running.
    /// </summary>
    public static bool TrySpend(PlayerInstance player, uint amount, NtfSyncPlayer? sync = null)
    {
        if (amount == 0)
            return true;

        var data = player.Data;
        if (data.Vigor < amount)
            return false;

        var wasFull = data.Vigor >= GetMaxVigor(player);
        data.Vigor -= amount;
        if (wasFull || data.LastVigorTime == 0)
            data.LastVigorTime = (uint)Extensions.GetUnixSec();

        if (sync != null)
        {
            sync.Core[(uint)PlayerCoreAttribute.Vigor] = data.Vigor;
            sync.Core[(uint)PlayerCoreAttribute.VigorTime] = data.LastVigorTime;
        }

        return true;
    }
}
