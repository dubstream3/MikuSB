using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;
using MikuSB.GameServer.Game.Player;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Gacha;

[CallGSApi("Gacha_UpSelect")]
public class Gacha_UpSelect : CallGSHandler<GachaUpSelectParam>
{
    private const uint GachaStrGid = AttrIds.Gacha.StringGid;
    private const int UpSelectIndex = 0;
    private const int UpSelectGetFlagIndex = 1;
    private const int UpPickPoolIndex = 2;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, GachaUpSelectParam req)
    {

        var player = context.Connection.Player!;
        if (req == null || req.NId == 0 || req.Gdpl == null || req.Gdpl.Count < 4)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        if (!GameData.GachaData.TryGetValue((uint)req.NId, out var gachaCfg) || gachaCfg.UpSelect != 1)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var valid = (gachaCfg.Pool ?? [])
            .Where(GameData.GachaPoolData.ContainsKey)
            .SelectMany(name => GameData.GachaPoolData[name])
            .Any(item =>
                item.UPSelectTag == 1 &&
                item.GDPL.Count >= 4 &&
                item.GDPL[0] == req.Gdpl[0] &&
                item.GDPL[1] == req.Gdpl[1] &&
                item.GDPL[2] == req.Gdpl[2] &&
                item.GDPL[3] == req.Gdpl[3]);

        if (!valid)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var existing = player.Attributes.GetStringValue(GachaStrGid, (uint)req.NId);
        var state = string.IsNullOrWhiteSpace(existing) ? new JArray() : JArray.Parse(existing);

        EnsureArraySize(state, 3);
        state[UpSelectIndex] = new JArray(req.Gdpl);
        state[UpSelectGetFlagIndex] = 0;
        if (state[UpPickPoolIndex] == null)
            state[UpPickPoolIndex] = 0;

        var attr = player.Attributes.SetString(
            GachaStrGid,
            (uint)req.NId,
            state.ToString(Newtonsoft.Json.Formatting.None));
        DatabaseHelper.SaveDatabaseType(player.Data);

        var sync = new NtfSyncPlayer();
        player.Attributes.SyncTo(sync, attr);
        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }

    private static void EnsureArraySize(JArray state, int size)
    {
        while (state.Count < size)
            state.Add(JValue.CreateNull());
    }
}

public sealed class GachaUpSelectParam
{
    [JsonPropertyName("nId")]
    public int NId { get; set; }

    [JsonPropertyName("gdpl")]
    public List<uint>? Gdpl { get; set; }
}
