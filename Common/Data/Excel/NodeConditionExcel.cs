using Newtonsoft.Json;

namespace MikuSB.Data.Excel;

// nodecondition.json: NodeConditionId → NodeXCost per sub-node (1-9)
[ResourceEntity("item/skill/nodecondition.json")]
public class NodeConditionExcel : ExcelResource
{
    [JsonProperty("ID")] public uint Id { get; set; }

    [JsonProperty("Node1Cost")] public List<List<int>> Node1Cost { get; set; } = [];
    [JsonProperty("Node2Cost")] public List<List<int>> Node2Cost { get; set; } = [];
    [JsonProperty("Node3Cost")] public List<List<int>> Node3Cost { get; set; } = [];
    [JsonProperty("Node4Cost")] public List<List<int>> Node4Cost { get; set; } = [];
    [JsonProperty("Node5Cost")] public List<List<int>> Node5Cost { get; set; } = [];
    [JsonProperty("Node6Cost")] public List<List<int>> Node6Cost { get; set; } = [];
    [JsonProperty("Node7Cost")] public List<List<int>> Node7Cost { get; set; } = [];
    [JsonProperty("Node8Cost")] public List<List<int>> Node8Cost { get; set; } = [];
    [JsonProperty("Node9Cost")] public List<List<int>> Node9Cost { get; set; } = [];

    // Refund columns: what the node gives back when the spine tree is reset
    // (Item/Spine.lua:658 GetChildNodeReset reads the same table client side).
    [JsonProperty("Node1Refund")] public List<List<int>> Node1Refund { get; set; } = [];
    [JsonProperty("Node2Refund")] public List<List<int>> Node2Refund { get; set; } = [];
    [JsonProperty("Node3Refund")] public List<List<int>> Node3Refund { get; set; } = [];
    [JsonProperty("Node4Refund")] public List<List<int>> Node4Refund { get; set; } = [];
    [JsonProperty("Node5Refund")] public List<List<int>> Node5Refund { get; set; } = [];
    [JsonProperty("Node6Refund")] public List<List<int>> Node6Refund { get; set; } = [];
    [JsonProperty("Node7Refund")] public List<List<int>> Node7Refund { get; set; } = [];
    [JsonProperty("Node8Refund")] public List<List<int>> Node8Refund { get; set; } = [];
    [JsonProperty("Node9Refund")] public List<List<int>> Node9Refund { get; set; } = [];

    public List<List<int>> GetNodeCost(int subIdx) => subIdx switch
    {
        1 => Node1Cost,
        2 => Node2Cost,
        3 => Node3Cost,
        4 => Node4Cost,
        5 => Node5Cost,
        6 => Node6Cost,
        7 => Node7Cost,
        8 => Node8Cost,
        9 => Node9Cost,
        _ => []
    };

    public List<List<int>> GetNodeRefund(int subIdx) => subIdx switch
    {
        1 => Node1Refund,
        2 => Node2Refund,
        3 => Node3Refund,
        4 => Node4Refund,
        5 => Node5Refund,
        6 => Node6Refund,
        7 => Node7Refund,
        8 => Node8Refund,
        9 => Node9Refund,
        _ => []
    };

    public override uint GetId() => Id;

    public override void Loaded()
    {
        GameData.NodeConditionData[Id] = this;
    }
}
