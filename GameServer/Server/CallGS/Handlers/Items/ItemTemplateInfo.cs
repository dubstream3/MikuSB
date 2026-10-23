using MikuSB.Data;
using MikuSB.Data.Excel;
using MikuSB.Database.Inventory;
using MikuSB.Proto;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Items;

/// <summary>
/// Looks a bag row's template up across both useable item tables and exposes the columns the item
/// handlers need.
///
/// The client reads <c>LuaType</c> / <c>Param1</c> from its merged template lookup
/// (<c>UE4.UItem.FindTemplate(G, D, P, L)</c>, e.g. <c>Script/Item/Item.lua:871-889</c>), while the
/// server keeps the two source tables apart: genre 5 rows come from item/templates/suplies.json
/// (<see cref="GameData.SuppliesData"/>) and genre 4 rows from item/templates/others.json
/// (<see cref="GameData.OtherItemData"/>). The template id spaces do not overlap (5/24/x/x and
/// 4/2/x/x are different ids), so a bag row is found in exactly one of them and this type hides the
/// split instead of making every handler test both.
/// </summary>
public readonly struct ItemTemplateInfo
{
    private ItemTemplateInfo(ulong templateId, string luaType, uint param1, uint useMode)
    {
        TemplateId = templateId;
        LuaType = luaType;
        Param1 = param1;
        UseMode = useMode;
    }

    public ulong TemplateId { get; }

    /// <summary>Client Lua type: "itembox", "vigor_box", "token_box", "cashbox", "" ...</summary>
    public string LuaType { get; }

    /// <summary>Box index for an "itembox", vigor restored per use for a "vigor_box".</summary>
    public uint Param1 { get; }

    /// <summary>Item.UserUse (2) marks an item the client uses from the bag; see Item.lua:146.</summary>
    public uint UseMode { get; }

    /// <summary>True for the plain boxes the client opens with Item_OpenBox.</summary>
    public bool IsBox => LuaType == "itembox" && Param1 != 0;

    /// <summary>True for the stamina potions the client uses with VigorBox_Use.</summary>
    public bool IsVigorBox => LuaType == "vigor_box" && Param1 != 0;

    public static bool TryResolve(ulong templateId, out ItemTemplateInfo info)
    {
        if (GameData.OtherItemData.TryGetValue(templateId, out var other))
        {
            info = new ItemTemplateInfo(templateId, other.LuaType ?? "", other.Param1, other.UseMode);
            return true;
        }

        if (GameData.SuppliesData.TryGetValue(templateId, out var supplies))
        {
            info = new ItemTemplateInfo(templateId, supplies.LuaType ?? "", supplies.Param1, supplies.UseMode);
            return true;
        }

        info = default;
        return false;
    }

    /// <summary>
    /// The bag row as a sync entry. The count is whatever the row now holds, and 0 is how the client
    /// is told that a stack is gone (Item_Recycle.cs:79-89 uses the same idiom).
    /// </summary>
    public static Item ToSyncProto(BaseGameItemInfo item) => item.ToProto();
}
