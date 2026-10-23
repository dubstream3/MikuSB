using MikuSB.Data;
using MikuSB.Database.Inventory;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.GameServer.Server.Packet.Send.Misc;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Shop;

// Character piece conversion (角色碎片兑换), the overflow window of a character whose 天启 is already
// maxed. The window sends a BARE JSON ARRAY, one element per selected character card
// (Script/UMG/Shop/Widgets/uw_shop_pieceexchange.lua:25-32, sent at :41):
//   [ { "sGirlGDPL": "G-D-P-L", "sPieceGDPLN": "G-D-P-L-N" }, ... ]
// sGirlGDPL is the card's own GDPL and sPieceGDPLN is the piece stack to convert together with how
// many of it to convert - the window always offers the whole owned stack (:167). A bare array cannot
// be bound to a request type by CallGSHandler<TRequest>, so this handler overrides the raw string
// overload exactly like ShopLogic_GetOpenTime does and parses the array by hand.
//
// What the conversion pays out is fixed by the client's own table item/pieces_convert.txt, which
// Item.LoadGirlPiecesConvert reads into Item.tbPiecesConvert (Script/Item/Item.lua:1413-1424) and
// which the window uses to draw its "you will get" preview (uw_shop_pieceexchange.lua:104-119). The
// shipped table has three rows:
//   Color 3 -> {{5,19,1,1,10}}
//   Color 4 -> {{5,19,1,1,10}}
//   Color 5 -> {{5,19,1,2,10}}
// so every single piece yields CutPerPiece (10) units of a 5-19-1-x supply, picked by the COLOR of
// the piece stack: 4★ pieces become 5-19-1-1 (supplies.cut1, 原初思念片段) and 5★ pieces become
// 5-19-1-2 (supplies.cut2, 新选思念片段). Color 3 never occurs - the 5-4-* piece rows of
// item/templates/suplies.json are Color 4 or 5 only - so the 3 and 4 rows describe the same target.
//
// The request name has no s2c callback in the client (the window does not read an answer), so echoing
// it back would make Misc/S2C.lua:32 raise "s2c.Dispatch error. No command named: ..." and leave the
// window spinning on an answer that never arrives. The award travels under the name the client does
// register, Script/Item/Item.lua:1166-1173:
//   s2c.Register("Item_ConvertGirlPieces", function(tbParam) Item.Gain(tbParam.tbAward) end)
// hence the Item_ConvertGirlPieces answer below carrying { tbAward: [[G, D, P, L, Count], ...] }.
[CallGSApi("ShopLogic_ConvertGirlPieces")]
public class ShopLogic_ConvertGirlPieces : CallGSHandler
{
    private static readonly Logger Log = new(nameof(ShopLogic_ConvertGirlPieces));

    // The only callback the client registered for this feature (Script/Item/Item.lua:1166).
    private const string AwardApi = "Item_ConvertGirlPieces";

    // Genre of every row this feature touches: ItemTypeEnum.TYPE_SUPPLIES holds both the character
    // pieces (5-4-*) and the shards they convert into (5-19-1-*).
    private const uint SuppliesGenre = (uint)ItemTypeEnum.TYPE_SUPPLIES;

    // Character pieces are 5-4-<card Detail>-<card Particular> (item/templates/suplies.json, e.g.
    // supplies.pieces001 .. pieces050).
    private const uint PieceDetail = 4;

    // Target of the conversion: 5-19-1-<Particular>, Particular 1 for 4★ pieces and 2 for 5★ ones
    // (item/pieces_convert.txt, item/templates/suplies.txt:111-112 supplies.cut1/cut2).
    private const uint CutDetail = 19;
    private const uint CutLevel = 1;
    private const uint CutParticularLow = 1;
    private const uint CutParticularHigh = 2;

    // The whole conversion rate: one piece -> 10 shards, the 5th number of the tbItem rows in
    // item/pieces_convert.txt. Change it here and the preview and the payout stay in step only if
    // the client table is changed the same way, so treat it as a mirror of that file, not a knob.
    private const uint CutPerPiece = 10;

    // Item.lua:142 caps an owned 思念片段 stack at 999999, so a genuine request cannot ask for more;
    // the bound keeps a hand written count from turning the multiplication below into an overflow.
    private const uint MaxPieceCount = 999999;

    // Same idea for the body itself: the client can only list the cards it owns (54 shipped cards),
    // so a longer array is not the window talking.
    private const int MaxEntries = 200;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;

        JsonArray? body;
        try
        {
            body = string.IsNullOrWhiteSpace(param) ? null : JsonNode.Parse(param) as JsonArray;
        }
        catch (JsonException)
        {
            // A Lua empty table arrives as "[]" and an unparsable body is just as unusable.
            body = null;
        }

        if (body == null || body.Count == 0 || body.Count > MaxEntries)
            return Reject(player, $"body is not a usable array of {MaxEntries} entries or fewer " +
                                  $"({param.Length} chars: {Truncate(param)})");

        // Pass 1: validate every element and total the request. Nothing is spent yet, so a body that
        // mixes one good element with one bad one leaves the account untouched.
        var requests = new List<PieceRequest>(body.Count);
        var wantedPieces = new Dictionary<ulong, uint>();
        var awardTotals = new Dictionary<uint, uint>();
        var awardOrder = new List<uint>();

        foreach (var node in body)
        {
            if (node is not JsonObject entry)
                return Reject(player, "element is not an object");

            // The window writes both fields with string.format (uw_shop_pieceexchange.lua:25,:71), so
            // anything that is not "G-D-P-L" shaped text is not this request.
            var pieceGdpl = ReadGdpl(entry["sPieceGDPLN"], 5);
            var girlGdpl = ReadGdpl(entry["sGirlGDPL"], 4);

            if (pieceGdpl == null || girlGdpl == null)
                return Reject(player, "element has no usable sGirlGDPL/sPieceGDPLN");

            if (pieceGdpl[0] != SuppliesGenre || pieceGdpl[1] != PieceDetail)
                return Reject(player, $"piece {Describe(pieceGdpl)} is not a character piece");

            // The window only ever offers a card's own piece (uw_shop_pieceexchange.lua:71 derives
            // sGirlGDPL from the same card object it took the piece from, and column "Pieces" of
            // item/templates/card.txt is {{5,4,<card Detail>,<card Particular>,n}} for all 54 shipped
            // cards), so a pair that disagrees was hand written.
            if (pieceGdpl[1] != girlGdpl[1] || pieceGdpl[2] != girlGdpl[2])
                return Reject(player, $"piece {Describe(pieceGdpl)} does not belong to card {Describe(girlGdpl)}");

            var count = pieceGdpl[4];
            if (count == 0 || count > MaxPieceCount)
                return Reject(player, $"piece count {count} is out of range");

            var pieceTemplate = GameResourceTemplateId.FromGdpl(pieceGdpl[0], pieceGdpl[1], pieceGdpl[2], pieceGdpl[3]);

            if (!GameData.SuppliesData.TryGetValue(pieceTemplate, out var piece))
                return Reject(player, $"piece {Describe(pieceGdpl)} has no supply template");

            // The colour decides the payout (item/pieces_convert.txt). Reading it from the piece's own
            // row means a piece added by a later client version is priced without touching this file.
            var cutParticular = piece.Color switch
            {
                >= 3 and <= 4 => CutParticularLow,
                5 => CutParticularHigh,
                _ => 0u
            };

            if (cutParticular == 0)
                return Reject(player, $"piece {Describe(pieceGdpl)} has unconvertible color {piece.Color}");

            // Refuse before charging when the target supply has no template: RewardManager would skip
            // the row silently and the pieces would be gone for nothing.
            if (!GameData.SuppliesData.ContainsKey(GameResourceTemplateId.FromGdpl(SuppliesGenre, CutDetail, cutParticular, CutLevel)))
                return Reject(player, $"converted supply {SuppliesGenre}-{CutDetail}-{cutParticular}-{CutLevel} has no template");

            requests.Add(new PieceRequest(pieceGdpl, count));
            wantedPieces[pieceTemplate] = wantedPieces.GetValueOrDefault(pieceTemplate) + count;

            if (!awardTotals.ContainsKey(cutParticular))
                awardOrder.Add(cutParticular);

            awardTotals[cutParticular] = awardTotals.GetValueOrDefault(cutParticular) + count * CutPerPiece;
        }

        // Pass 2: the whole basket has to be affordable before the first stack is charged. Totalling
        // by template also covers a body that lists the same piece twice with the full count.
        var wallets = new List<BaseGameItemInfo>(wantedPieces.Count);

        foreach (var (pieceTemplate, wanted) in wantedPieces)
        {
            var wallet = player.InventoryManager.InventoryData.Items.Values
                .FirstOrDefault(x => x.TemplateId == pieceTemplate);

            if (wallet == null || wallet.ItemCount < wanted)
            {
                Log.Info($"Conversion refused, uid={player.Uid}: wants {wanted}x template {pieceTemplate}, " +
                         $"owns {wallet?.ItemCount ?? 0}");
                return CallGSResult.Error("tip.NotEnoughItem");
            }

            wallets.Add(wallet);
        }

        // Pass 3: charge. TrySpendItem re-reads the stack it spends from, so this loop can never take
        // more than pass 2 proved the player owns.
        foreach (var request in requests)
        {
            if (!CurrencyService.TrySpendItem(player, request.PieceGdpl, request.Count, out var spendError))
            {
                Log.Warn($"Charge failed after the affordability pass, uid={player.Uid} " +
                         $"{request.Count}x {Describe(request.PieceGdpl)}: {spendError}");
                return CallGSResult.Error(spendError);
            }
        }

        // The bag keeps showing the old piece counts until the client is told; ShopLogic_BuyGoods.cs:82
        // mirrors its spent price back the same way.
        await player.SendPacket(new PacketNtfCallScript(wallets));

        var rewardRows = new List<uint[]>(awardOrder.Count);
        foreach (var cutParticular in awardOrder)
            rewardRows.Add([SuppliesGenre, CutDetail, cutParticular, CutLevel, awardTotals[cutParticular]]);

        var sync = new NtfSyncPlayer();
        var award = await player.RewardManager.GrantConfiguredRewardsAsync(rewardRows, sync);
        CurrencyService.Persist(player);

        var spentPieces = wantedPieces.Values.Aggregate(0u, (total, value) => total + value);
        Log.Info($"Conversion, uid={player.Uid}: {spentPieces} piece(s) into " +
                 $"{string.Join(" + ", rewardRows.Select(Describe))}");

        // Item.Gain reads tbParam.tbAward and nothing else, so the granted rows are sent back verbatim
        // - what the gain screen lists is exactly what RewardManager wrote into the bag.
        return CallGSResult.Ok(new JsonObject { ["tbAward"] = award }, sync, AwardApi);
    }

    private static CallGSResult Reject(PlayerInstance player, string reason)
    {
        Log.Warn($"Rejected ShopLogic_ConvertGirlPieces request, uid={player.Uid}: {reason}");
        return CallGSResult.Error("error.BadParam");
    }

    /// <summary>
    /// Reads one "G-D-P-L[-N]" field of the request. The client builds both fields with string.format
    /// so they are always text, but json.encode may still hand back a number for a single element
    /// array; anything that is not exactly <paramref name="parts"/> digits separated by '-' counts as
    /// malformed instead of being guessed at.
    /// </summary>
    private static uint[]? ReadGdpl(JsonNode? node, int parts)
    {
        try
        {
            if (node is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
                return null;

            var chunks = text.Split('-');

            if (chunks.Length != parts)
                return null;

            var gdpl = new uint[parts];

            for (var i = 0; i < parts; i++)
            {
                if (!uint.TryParse(chunks[i].Trim(), out gdpl[i]))
                    return null;
            }

            return gdpl;
        }
        catch
        {
            // An unreadable node is malformed too; the caller answers error.BadParam.
            return null;
        }
    }

    private static string Describe(uint[] gdpl) => string.Join('-', gdpl);

    // A rejected body can be arbitrarily large, so only its head is logged.
    private static string Truncate(string text) => text.Length <= 200 ? text : text[..200] + "...";

    /// <summary>
    /// One element of the request body: the piece stack to spend and how many of it the client asked
    /// for. The payout is totalled separately while parsing, because it is keyed by the piece colour
    /// rather than by the stack.
    /// </summary>
    private readonly record struct PieceRequest(uint[] PieceGdpl, uint Count);
}
