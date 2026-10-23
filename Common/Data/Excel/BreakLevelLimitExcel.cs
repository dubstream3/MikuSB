namespace MikuSB.Data.Excel;

[ResourceEntity("item/break_level_limit.json")]
public class BreakLevelLimitExcel : ExcelResource
{
    public int ID { get; set; }
    public uint Break0 { get; set; }
    public uint Break1 { get; set; }
    public uint Break2 { get; set; }
    public uint Break3 { get; set; }
    public uint Break4 { get; set; }
    public uint Break5 { get; set; }
    public uint Break6 { get; set; }

    public override uint GetId()
    {
        return (uint)ID;
    }

    public override void Loaded()
    {
        GameData.BreakLevelLimitData[ID] = this;
    }
}
