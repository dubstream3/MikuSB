using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Enums.Item;
using MikuSB.GameServer.Game.Player;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.VirCapture;

[CallGSApi("VirCapture_ChangeFormation")]
public class VirCapture_ChangeFormation : CallGSHandler<VirCaptureChangeFormationParam>
{
    private const uint StrGroupId = AttrIds.VirCapture.FormationStringGid;
    private const uint FormationSid = AttrIds.VirCapture.FormationSid;
    private const uint VirCaptureGroupId = AttrIds.VirCapture.Gid;
    private const uint CurLevelSid = AttrIds.VirCapture.CurrentLevelSid;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, VirCaptureChangeFormationParam req)
    {

        if (req == null)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var player = context.Connection.Player!;
        var formation = ReadFormation(player);
        var addId = (uint)Math.Max(0, req.Id);
        var unloadId = (uint)Math.Max(0, req.UnloadId);

        var unloadIndex = unloadId == 0 ? -1 : formation.FindIndex(x => x == unloadId);
        if (unloadId > 0 && unloadIndex < 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        if (addId > 0)
        {
            if (formation.Contains(addId))
            {
                return Task.FromResult(CallGSResult.Error("error.BadParam"));
            }

            var addItem = player.InventoryManager.GetNormalItem(addId);
            if (addItem == null || addItem.ItemType != ItemTypeEnum.TYPE_MONSTER_CARD)
            {
                return Task.FromResult(CallGSResult.Error("error.BadParam"));
            }
        }

        if (unloadIndex >= 0)
            formation.RemoveAt(unloadIndex);

        if (addId > 0)
        {
            if (unloadIndex >= 0 && unloadIndex <= formation.Count)
                formation.Insert(unloadIndex, addId);
            else
                formation.Add(addId);
        }

        if (!ValidateFormation(player, formation))
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var json = JsonSerializer.Serialize(formation);
        var formationAttr = player.Attributes.SetString(StrGroupId, FormationSid, json);

        DatabaseHelper.SaveDatabaseType(player.Data);

        var sync = new NtfSyncPlayer();
        player.Attributes.SyncTo(sync, formationAttr);

        var response = new JsonObject
        {
            ["nId"] = req.Id,
            ["nUnloadId"] = req.UnloadId,
            ["bAdd"] = addId > 0
        };

        return Task.FromResult(CallGSResult.Ok(response.ToJsonString(), sync));
    }

    private static List<uint> ReadFormation(MikuSB.GameServer.Game.Player.PlayerInstance player)
    {
        var raw = player.Attributes.GetStringValue(StrGroupId, FormationSid);
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<uint>>(raw) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static bool ValidateFormation(MikuSB.GameServer.Game.Player.PlayerInstance player, List<uint> formation)
    {
        var curLevel = player.Attributes.GetValue(VirCaptureGroupId, CurLevelSid);
        if (curLevel == 0)
            curLevel = 1;
        if (!GameData.VirCaptureLevelListData.TryGetValue(curLevel, out var levelCfg))
            return formation.Count == 0;

        if (formation.Count > levelCfg.Num)
            return false;

        uint totalCost = 0;
        foreach (var itemId in formation)
        {
            var item = player.InventoryManager.GetNormalItem(itemId);
            if (item == null || item.ItemType != ItemTypeEnum.TYPE_MONSTER_CARD)
                return false;

            if (!GameData.MonsterCardData.TryGetValue(item.TemplateId, out var monsterCfg))
                return false;

            totalCost += monsterCfg.CostValue;
        }

        return totalCost <= levelCfg.MaxCost;
    }
}

public sealed class VirCaptureChangeFormationParam
{
    [JsonPropertyName("nId")]
    public int Id { get; set; }

    [JsonPropertyName("nUnloadId")]
    public int UnloadId { get; set; }
}
