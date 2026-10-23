using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// 多诺-巅峰对决 上阵/下阵.
//
// Client origin: DonoDuel:DoChangeFormation (DLC/DonoDuel/DonoDuel.lua:1964-2001) sends
// { tbFormation = [uniqueId, ...], nType = 1|2|3 } right after UI.ShowConnection(), and its s2c
// callback (:2058-2073) opens with UI.CloseConnection(). Before this handler existed the server
// answered with the router fallback / not at all, so the spinner never closed and a deck could never
// be assembled - the player was left with cards in the bag and "可出阵卡牌 0/5" in the arena.
//
// nType picks the attribute block that stores the deck (DonoDuel.lua:1967-1979 against :238-285):
//   1 = rogue   (LaunchType.DONO_DUEL_ROGUE)             -> (185, 10..13) = 4 slots x 2 cards
//   2 = arena   (DONO_DUEL_ARENA, nArenaType <= 2)       -> (185, 14..17)
//   3 = endless (DONO_DUEL_ARENA, nArenaType == 3)       -> (185, 18..23) = 6 slots x 2 cards
// Each slot packs two cards: DonoDuel:GetHalfAttribute returns (bits 16..31, bits 0..15) and
// DonoDuel:GetFormation appends the first half before the second, so deck[2k] goes to the high half
// of slot k and deck[2k+1] to the low half.
//
// Request:  { "tbFormation": [<uniqueId>, ...], "nType": 1|2|3, "bNotRefresh": true? }
// Response: { "nType": <type>, "tbFormation": [<uniqueId>, ...] }
// (the client only closes the connection and, unless bNotRefresh is set, hands the table to
// DonoDuelFormation:OnReceiveUpdate).
[CallGSApi("DonoDuel_ChangeFormation")]
public class DonoDuel_ChangeFormation : CallGSHandler
{
    private static readonly Logger Log = new(nameof(DonoDuel_ChangeFormation));

    private const int RogueType = 1;
    private const int ArenaType = 2;
    private const int EndlessType = 3;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var request = Parse(param);

        var (startSid, endSid) = Slots(request.Type);
        if (startSid == 0)
        {
            Log.Warn(
                $"Unknown Donoduel formation type {request.Type} from uid={player.Uid}.");
            return CallGSResult.Ok(new JsonObject { ["sError"] = "error.BadParam" });
        }

        // Mirror of DonoDuel:CheckBagItem: cards the player does not own are silently dropped instead
        // of failing the whole request, so a stale client cannot lock the deck editor.
        var capacity = (int)(endSid - startSid + 1) * 2;
        var deck = new List<uint>();
        foreach (var uniqueId in request.Deck)
        {
            if (deck.Count >= capacity)
                break;

            if (!DonoDuelBag.HasCard(player, uniqueId))
                continue;

            deck.Add(uniqueId);
        }

        var sync = new NtfSyncPlayer();
        for (var sid = startSid; sid <= endSid; sid++)
        {
            var index = (int)(sid - startSid) * 2;
            var high = index < deck.Count ? deck[index] : 0;
            var low = index + 1 < deck.Count ? deck[index + 1] : 0;
            DonoDuelBag.SetSlot(player, sid, ((uint)high << 16) | (uint)low, sync);
        }

        DonoDuelBag.Save(player);
        await player.SendPacket(CmdIds.NtfSyncAttr, sync);

        Log.Info(
            $"Donoduel formation type={request.Type} saved by uid={player.Uid}: " +
            $"{deck.Count}/{capacity} card(s), slots {startSid}-{endSid}, requested={request.Deck.Count}.");

        var array = new JsonArray();
        foreach (var uniqueId in deck)
            array.Add(JsonValue.Create(uniqueId));

        return CallGSResult.Ok(new JsonObject
        {
            ["nType"] = request.Type,
            ["tbFormation"] = array
        });
    }

    /// <summary>Attribute range of one deck type; (0, 0) marks an unknown type.</summary>
    private static (uint Start, uint End) Slots(uint type) => type switch
    {
        RogueType => (AttrIds.DonoDuel.RogueFormationStartSid, AttrIds.DonoDuel.RogueFormationEndSid),
        ArenaType => (AttrIds.DonoDuel.ArenaFormationStartSid, AttrIds.DonoDuel.ArenaFormationEndSid),
        EndlessType => (AttrIds.DonoDuel.EndlessFormationStartSid, AttrIds.DonoDuel.EndlessFormationEndSid),
        _ => (0u, 0u)
    };

    private readonly record struct FormationRequest(uint Type, List<uint> Deck);

    private static FormationRequest Parse(string param)
    {
        JsonObject? root = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(param))
                root = JsonNode.Parse(param) as JsonObject;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not parse the Donoduel formation request: {ex.Message}");
        }

        // The command is meaningless without a type; the client always sends one (rogue=1 is the only
        // value reachable from the story flow), so 1 is the safe default.
        var type = ReadUint(root?["nType"]);
        if (type == 0)
            type = RogueType;

        var deck = new List<uint>();
        if (root?["tbFormation"] is JsonArray rows)
        {
            foreach (var row in rows)
            {
                var uniqueId = ReadUint(row);
                if (uniqueId != 0)
                    deck.Add(uniqueId);
            }
        }

        return new FormationRequest(type, deck);
    }

    private static uint ReadUint(JsonNode? node)
    {
        if (node == null)
            return 0;

        try
        {
            if (node is JsonValue value)
            {
                if (value.TryGetValue<uint>(out var number))
                    return number;

                if (value.TryGetValue<string>(out var text) &&
                    uint.TryParse(text.Trim().Trim('[', ']', '"'), out var parsed))
                    return parsed;
            }
        }
        catch
        {
            // fall through: an unreadable entry is treated as "no card"
        }

        return 0;
    }
}
