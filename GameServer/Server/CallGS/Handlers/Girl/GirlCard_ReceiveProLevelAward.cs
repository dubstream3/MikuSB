using MikuSB.Data;
using MikuSB.Database;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

// Pro-level (职级认定) reward claim. Client origin: RoleCard.ReceiveProLevelAward
// (UMG/Role/RoleCard.lua:1327-1332) sends { nID = <card guid>, nProLevel = <stage 1..3 or nil> }
// after the panel already checked "completed && not yet received" (:427-429), and the callback
// (:1334-1350) closes the spinner, re-reads the spine string attribute, refreshes the nerve panel
// and feeds tbParam.tbAward into Item.Gain.
//
// The claimed flags live in GetStrAttribute(Gid=30, Sid=card Detail) — the same JSON blob the spine
// nodes use: { "<Particular>": { ns, tbn, tbr } } — and the client tests tbr[stage] > 0
// (Item/Spine.lua:160-173 GetReceiveData + uw_nerve_main.lua:415-423). The award rows themselves come
// from item/card/prolevel.json Award1..Award3 and are a flat [G,D,P,L,Count] list (DefendLogic
// GetLevelAward pattern). A response is mandatory: the client opened a spinner and only closes it in
// the callback.
//
// Request:  { "nID": <card guid>, "nProLevel": <1..3> | absent = claim every completed stage }
// Response: { "tbAward": [[g, d, p, l, count], ...] }
[CallGSApi("GirlCard_ReceiveProLevelAward")]
public class GirlCard_ReceiveProLevelAward : CallGSHandler<ReceiveProLevelAwardParam>
{
    private const uint SpineStrAttrGid = AttrIds.Girl.SpineStringGid;
    private const int MaxProLevel = 3;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, ReceiveProLevelAwardParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.CardId == 0 || req.ProLevel < 0 || req.ProLevel > MaxProLevel)
        {
            return CallGSResult.Error("error.BadParam");
        }

        var card = player.CharacterManager.GetCharacterByGUID((uint)req.CardId);
        if (card == null)
        {
            return CallGSResult.Error("error.BadParam");
        }

        if (!GameData.ProLevelData.TryGetValue(card.TemplateId, out var proLevel))
        {
            return CallGSResult.Error("error.BadParam");
        }

        var cardDetail = (uint)((card.TemplateId >> 16) & 0xFFFF);
        var cardParticular = (uint)((card.TemplateId >> 32) & 0xFFFF);

        var root = ParseRoot(player, cardDetail);
        var entry = GetOrCreateEntry(root, cardParticular);

        var rows = new List<IReadOnlyList<uint>>();
        for (var stage = 1; stage <= MaxProLevel; stage++)
        {
            // nProLevel == 0 means "claim everything the player already earned" (the panel's
            // quick-claim button passes nil, uw_nerve_main.lua:445).
            if (req.ProLevel != 0 && stage != req.ProLevel) continue;

            // The stage is only claimable once the card actually reached it (uw_nerve_main.lua:424).
            if (card.ProLevel < stage) continue;

            if (IsClaimed(entry, stage)) continue;

            foreach (var row in proLevel.GetAward(stage))
            {
                if (row == null || row.Count < 5) continue;
                if (row[4] <= 0) continue;
                rows.Add([
                    (uint)Math.Max(0, row[0]), (uint)Math.Max(0, row[1]),
                    (uint)Math.Max(0, row[2]), (uint)Math.Max(0, row[3]), (uint)row[4]]);
            }

            SetClaimed(entry, stage);
        }

        var sync = new NtfSyncPlayer();
        var granted = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);

        var strAttr = player.Attributes.SetString(SpineStrAttrGid, cardDetail, JsonSerializer.Serialize(root));

        DatabaseHelper.SaveDatabaseType(player.Data);
        DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        // The claim flags live only in this string attribute, and the client caches it: the panel
        // copies GetStrAttribute(30, Detail) into Spine.tbCardData and re-reads it only when
        // Spine.UpdateData runs (Item/Spine.lua:103-107 and :160-173), which the s2c callback does
        // (UMG/Role/RoleCard.lua:1337). The store therefore has to be fresh *before* that read, or
        // the row keeps saying 可领取 and a second click answers with an empty tbAward.
        //
        // Which packet actually refreshes the client store is not documented anywhere in the client
        // scripts, so push on all three channels - they are idempotent and carry the same value:
        //   1. the reply's own sync (CustomStr), applied together with the CallGS answer;
        //   2. CmdIds.NtfSyncAttr, the channel SettingChange (:26-30) and VigorService.Regen use;
        //   3. CmdIds.NtfSetStrAttr, the same-type notification GirlSpine_ChildUnLock sends.
        player.Attributes.SyncTo(sync, strAttr);

        var strSync = new NtfSyncPlayer();
        player.Attributes.SyncTo(strSync, strAttr);
        await player.SendPacket(CmdIds.NtfSyncAttr, strSync);
        await player.SendPacket(CmdIds.NtfSetStrAttr, new NtfSetStrAttr
        {
            Gid = SpineStrAttrGid,
            Sid = cardDetail,
            Val = strAttr.Val
        });

        return CallGSResult.Ok(new JsonObject { ["tbAward"] = granted }, sync);
    }

    private static Dictionary<string, SpineStrData> ParseRoot(PlayerInstance player, uint detail)
    {
        var raw = player.Attributes.GetStringValue(SpineStrAttrGid, detail);
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, SpineStrData>>(raw) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static SpineStrData GetOrCreateEntry(Dictionary<string, SpineStrData> root, uint particular)
    {
        var key = particular.ToString();
        if (!root.TryGetValue(key, out var entry))
        {
            entry = new SpineStrData();
            root[key] = entry;
        }

        // The client indexes tbr with the 1-based award stage; keep three slots so a claim of stage 3
        // does not leave holes the client would read as "already received".
        while (entry.Tbr.Count < MaxProLevel)
            entry.Tbr.Add(0);

        return entry;
    }

    private static bool IsClaimed(SpineStrData entry, int stage) =>
        entry.Tbr.Count >= stage && entry.Tbr[stage - 1] > 0;

    private static void SetClaimed(SpineStrData entry, int stage)
    {
        while (entry.Tbr.Count < MaxProLevel)
            entry.Tbr.Add(0);

        entry.Tbr[stage - 1] = 1;
    }
}

public sealed class ReceiveProLevelAwardParam
{
    [JsonPropertyName("nID")]
    public int CardId { get; set; }

    // 0 = the request carried no stage (claim every completed one).
    [JsonPropertyName("nProLevel")]
    public int ProLevel { get; set; }
}
