using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// 多诺-巅峰对决 卡片锁定 (锁定 = 回收/升级时不会被吃掉).
//
// Client origin: DonoDuel:DoSetLock (DLC/DonoDuel/DonoDuel.lua:1932-1940) sends
// { nUniqueId, bLock = not DonoDuel:IsLocked(tbHeroInfo) } after UI.ShowConnection(); the callback
// (:1942-1961) closes the connection and either calls the pending fReqUnlockFunc or refreshes
// DonoDuelFormation. The flag lives in the low 16 bits of the card's second block slot
// (DonoDuel.lua:364-371 GetBagItemTableInfo reads it as nFlag) and bit0 is Enum_Flag.Lock
// (DonoDuel.lua:107-112, read back by DonoDuel:IsLocked :448-452).
//
// Request:  { "nUniqueId": <1..350>, "bLock": true|false }
// Response: { "nUniqueId": <id>, "bLock": <bool> }
[CallGSApi("DonoDuel_SetLock")]
public class DonoDuel_SetLock : CallGSHandler
{
    private static readonly Logger Log = new(nameof(DonoDuel_SetLock));

    /// <summary>DonoDuel.Enum_Flag.Lock (DonoDuel.lua:107-112).</summary>
    private const uint LockBit = 0;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;

        var root = ParseObject(param);
        var uniqueId = ReadUint(root?["nUniqueId"]);
        var locked = ReadBool(root?["bLock"]);

        if (!DonoDuelBag.HasCard(player, uniqueId))
        {
            Log.Warn(
                $"Donoduel lock request for unknown card {uniqueId} from uid={player.Uid}.");
            return CallGSResult.Ok(new JsonObject { ["sError"] = "error.BadParam" });
        }

        var flags = DonoDuelBag.GetFlags(player, uniqueId);
        flags = locked ? flags | (1u << (int)LockBit) : flags & ~(1u << (int)LockBit);

        var sync = new NtfSyncPlayer();
        DonoDuelBag.SetFlags(player, uniqueId, flags, sync);
        DonoDuelBag.Save(player);
        await player.SendPacket(CmdIds.NtfSyncAttr, sync);

        Log.Info(
            $"Donoduel card {uniqueId} {(locked ? "locked" : "unlocked")} by uid={player.Uid} (flags={flags}).");

        return CallGSResult.Ok(new JsonObject
        {
            ["nUniqueId"] = uniqueId,
            ["bLock"] = locked
        });
    }

    private static JsonObject? ParseObject(string param)
    {
        try
        {
            return string.IsNullOrWhiteSpace(param) ? null : JsonNode.Parse(param) as JsonObject;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not parse the Donoduel lock request: {ex.Message}");
            return null;
        }
    }

    private static uint ReadUint(JsonNode? node)
    {
        if (node is not JsonValue value)
            return 0;

        try
        {
            if (value.TryGetValue<uint>(out var number))
                return number;

            if (value.TryGetValue<string>(out var text))
                return uint.TryParse(text.Trim(), out var parsed) ? parsed : 0;
        }
        catch
        {
            // unreadable -> 0
        }

        return 0;
    }

    private static bool ReadBool(JsonNode? node)
    {
        if (node is not JsonValue value)
            return false;

        try
        {
            if (value.TryGetValue<bool>(out var flag))
                return flag;

            if (value.TryGetValue<uint>(out var number))
                return number != 0;

            if (value.TryGetValue<string>(out var text))
                return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1";
        }
        catch
        {
            // unreadable -> false
        }

        return false;
    }
}
