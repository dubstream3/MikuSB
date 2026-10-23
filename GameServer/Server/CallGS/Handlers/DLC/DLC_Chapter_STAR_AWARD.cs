using System.Text.Json.Nodes;
using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;
using MikuSB.Util;
using Newtonsoft.Json.Linq;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Dlc;

// Client side origin: DLC_Chapter.GetAllStarAward (Launch/Dlc/DLC_Chapter.lua:481-499) collects the
// unclaimed star award indices whose threshold is already reached and sends them as
// [[chapterId, starAwardIndex], ...] through Req_GetStarAward (DLC_Chapter.lua:502). The claim flag
// lives in bit <index> of attribute (Launch.ChapterMaskGid=20, chapterId), which is exactly what
// DLC_Chapter.DidGotStarAward (DLC_Chapter.lua:254) reads back.
// Chapters without a star award table (for example 28 and 35, the chapters the DLC35 activity uses)
// simply have nothing to claim, and the handler answers with an empty list.
// Request:  [ [chapterId, index], ... ]
// Response: { "tbAward": [ [[g,d,p,l,count], ...], ... ] }
[CallGSApi("DLC_Chapter_STAR_AWARD")]
public class DLC_Chapter_STAR_AWARD : CallGSHandler
{
    private static readonly Logger Logger = new("DLC_Chapter");

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var sync = new NtfSyncPlayer();
        var granted = new JsonArray();

        foreach (var (chapterId, index) in ReadRequests(param))
        {
            if (!GameData.DlcChapterData.TryGetValue(chapterId, out var chapter))
                continue;

            var maskAttr = player.Attributes.GetOrCreate(AttrIds.Launch.ChapterMaskGid, chapterId);
            if (((maskAttr.Val >> (int)index) & 1u) != 0)
                continue;

            var rows = ReadStarAwardRows(chapter.StarAwardRaw, index);
            if (rows.Count == 0)
                continue;

            var grantedRows = await player.RewardManager.GrantConfiguredRewardsAsync(rows, sync);
            maskAttr.Val |= 1u << (int)index;
            player.Attributes.SyncTo(sync, maskAttr);
            granted.Add(grantedRows);

            Logger.Info($"DLC chapter {chapterId} star award {index}: {grantedRows.Count} row(s) granted for uid={player.Uid}.");
        }

        if (granted.Count > 0)
        {
            DatabaseHelper.SaveDatabaseType(player.Data);
            DatabaseHelper.SaveDatabaseType(player.InventoryManager.InventoryData);
            DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);
        }

        return CallGSResult.Ok(new JsonObject { ["tbAward"] = granted }, sync);
    }

    private static IEnumerable<(uint ChapterId, uint Index)> ReadRequests(string param)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(param);
        }
        catch
        {
            yield break;
        }

        if (node is not JsonArray list)
            yield break;

        foreach (var entry in list.OfType<JsonArray>())
        {
            if (entry.Count < 2)
                continue;

            if (!uint.TryParse(entry[0]?.ToString(), out var chapterId) || chapterId == 0)
                continue;

            if (!uint.TryParse(entry[1]?.ToString(), out var index) || index == 0 || index > 31)
                continue;

            yield return (chapterId, index);
        }
    }

    /// <summary>
    /// The star award column is a per index reward table. The client config (and therefore the
    /// generated resource json) stores it keyed by the required star count, so index 1 is the entry
    /// with the smallest threshold. A plain list of entries is accepted too.
    /// </summary>
    private static List<IReadOnlyList<uint>> ReadStarAwardRows(JToken? raw, uint index)
    {
        if (raw is not JArray and not JObject)
            return [];

        JArray? entry = raw switch
        {
            JArray array => array.Count >= index ? array[(int)index - 1] as JArray : null,
            JObject obj => obj.Properties()
                .OrderBy(p => uint.TryParse(p.Name, out var key) ? key : uint.MaxValue)
                .ElementAtOrDefault((int)index - 1)?.Value as JArray,
            _ => null
        };

        if (entry == null)
            return [];

        var rows = new List<IReadOnlyList<uint>>();
        foreach (var row in entry.OfType<JArray>())
        {
            var values = new uint[row.Count];
            var ok = true;
            for (var i = 0; i < row.Count; i++)
            {
                if (!uint.TryParse(row[i]?.ToString(), out values[i]))
                {
                    ok = false;
                    break;
                }
            }

            if (ok && values.Length >= 5 && values[4] != 0)
                rows.Add(values);
        }

        return rows;
    }
}
