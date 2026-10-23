namespace MikuSB.Data.Excel;

[ResourceEntity("house/FurniturePos.json")]
public class HouseFurniturePosExcel : ExcelResource
{
    public uint AreaId { get; set; }
    public uint GroupId { get; set; }

    public override uint GetId()
    {
        return (AreaId << 48) | (GroupId << 32);
    }

    public override void Loaded()
    {
        GameData.HouseFurniturePosData.TryAdd(GetId(), this);
    }
}
