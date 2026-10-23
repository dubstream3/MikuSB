using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Proto;
using MikuSB.Util;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// 多诺-巅峰对决 卡牌升星 (吃掉其它卡换星级).
//
// Client origin: DonoDuel:DoUpgradeStar (DLC/DonoDuel/DonoDuel.lua:2164-2178) sends
// { nUniqueId, tbMaterial = <card uniqueId list>, nSourStar = <target star> }; the callback
// (:2180-2204) only reacts to tbParam.sError.
//
// Material rule (client side, DonoDuel.lua:92-98 tbIntegrateMaterials / GetNeedMaterial): three
// cards of star s are worth one card of star s+1, so going from star s to star T costs
// 3^(T-s) - 1 cards of star s - and a single request has to supply exactly that equivalence sum.
// Materials must be the same hero template, must not be the main card, must not sit in a formation
// and must not be of a lower star than the main card. The result inherits the highest quality among
// main card + materials (the client previews that and the server clamps the attributes to the new
// ceilings), consumes every material and never moves (185, 2) backwards.
//
// Request:  { "nUniqueId": <1..350>, "nSourStar": <2..5>, "tbMaterial": [<uniqueId>, ...] }
// Response: { } on success, { "sError": "..." } otherwise (plus the NtfSyncAttr push).
[CallGSApi("DonoDuel_UpgradeStar")]
public class DonoDuel_UpgradeStar : CallGSHandler
{
    private static readonly Logger Log = new(nameof(DonoDuel_UpgradeStar));

    /// <summary>Guard so a nonsense request cannot overflow 3^n.</summary>
    private const int MaxEquivalenceDepth = 10;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;

        var root = DonoDuelGrowth.ParseObject(param);
        var uniqueId = DonoDuelGrowth.ReadUint(root?["nUniqueId"]);
        var targetStar = DonoDuelGrowth.ReadUint(root?["nSourStar"]);
        var materialIds = DonoDuelGrowth.ReadUintList(root?["tbMaterial"]);

        if (!DonoDuelBag.HasCard(player, uniqueId))
        {
            Log.Warn($"Donoduel star upgrade for unknown card {uniqueId} from uid={player.Uid}.");
            return DonoDuelGrowth.Bad();
        }

        var templateId = DonoDuelBag.GetTemplateId(player, uniqueId);
        var currentStar = DonoDuelBag.GetStar(player, uniqueId);
        if (!GameData.DonoDuelHeroCardData.TryGetValue(templateId, out var heroCard))
        {
            Log.Warn($"Donoduel star upgrade for card {uniqueId} with unknown hero {templateId}.");
            return DonoDuelGrowth.Bad();
        }

        var maxStar = heroCard.Stars == 0 ? 5u : heroCard.Stars;
        if (targetStar <= currentStar || targetStar > maxStar)
            return DonoDuelGrowth.Bad();

        var steps = targetStar - currentStar;
        if (steps > MaxEquivalenceDepth)
            return DonoDuelGrowth.Bad();

        var required = Power3(steps) - 1;
        var provided = 0u;
        var materials = new List<uint>();
        var inheritedQuality = DonoDuelBag.GetQuality(player, uniqueId);

        foreach (var materialId in materialIds)
        {
            if (materialId == uniqueId || materials.Contains(materialId))
                continue;

            if (!DonoDuelBag.HasCard(player, materialId))
                return DonoDuelGrowth.Bad();

            if (DonoDuelBag.GetTemplateId(player, materialId) != templateId)
                return DonoDuelGrowth.Bad();

            if (DonoDuelBag.IsInAnyFormation(player, materialId))
                return DonoDuelGrowth.Bad();

            var star = DonoDuelBag.GetStar(player, materialId);
            if (star < currentStar)
                return DonoDuelGrowth.Bad();

            var exponent = star - currentStar;
            if (exponent > MaxEquivalenceDepth)
                return DonoDuelGrowth.Bad();

            provided += Power3(exponent);
            inheritedQuality = Math.Max(inheritedQuality, DonoDuelBag.GetQuality(player, materialId));
            materials.Add(materialId);
        }

        if (materials.Count == 0 || provided < required)
        {
            Log.Info(
                $"Donoduel star upgrade of card {uniqueId} rejected for uid={player.Uid}: " +
                $"{provided} equivalents vs {required} required.");
            return DonoDuelGrowth.Bad();
        }

        var quality = Math.Min(inheritedQuality, DonoDuelConfig.QualityMax());
        var sync = new NtfSyncPlayer();

        DonoDuelBag.SetQuality(player, uniqueId, quality, sync);
        for (var position = DonoDuelGrowth.AttrAttack; position <= DonoDuelGrowth.AttrHealth; position++)
        {
            var cap = DonoDuelGrowth.AttrCap(templateId, quality, position);
            if (cap == 0)
                continue;

            var current = DonoDuelBag.GetAttr(player, uniqueId, position);
            if (current > cap)
                DonoDuelBag.SetAttr(player, uniqueId, position, cap, sync);
        }

        DonoDuelBag.SetStar(player, uniqueId, targetStar, sync);
        foreach (var materialId in materials)
            DonoDuelBag.DestroyCard(player, materialId, sync);

        DonoDuelBag.Save(player);
        await player.SendPacket(CmdIds.NtfSyncAttr, sync);

        Log.Info(
            $"Donoduel card {uniqueId} star {currentStar} -> {targetStar} (quality {quality}, " +
            $"{materials.Count} material(s), {provided}/{required} equivalents) for uid={player.Uid}.");

        return DonoDuelGrowth.Ok(new JsonObject
        {
            ["nUniqueId"] = uniqueId,
            ["nSourStar"] = targetStar
        });
    }

    private static uint Power3(uint exponent)
    {
        var value = 1u;
        for (var i = 0; i < exponent; i++)
            value *= 3;

        return value;
    }
}
