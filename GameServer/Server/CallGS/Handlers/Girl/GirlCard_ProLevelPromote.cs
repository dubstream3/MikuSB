using MikuSB.Database;
using MikuSB.Proto;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Girl;

[CallGSApi("GirlCard_ProLevelPromote")]
public class GirlCard_ProLevelPromote : CallGSHandler<ProLevelPromoteParam>
{
    private const uint MaxProLevel = 3;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, ProLevelPromoteParam req)
    {
        var player = context.Connection.Player!;

        if (req == null || req.CardId == 0)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        var card = player.CharacterManager.GetCharacterByGUID((uint)req.CardId);
        if (card == null)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        if (card.ProLevel >= MaxProLevel)
        {
            return Task.FromResult(CallGSResult.Error("error.BadParam"));
        }

        card.ProLevel++;

        DatabaseHelper.SaveDatabaseType(player.CharacterManager.CharacterData);

        var sync = new NtfSyncPlayer();
        sync.Items.Add(card.ToProto());

        // s2c callback takes no params — return empty arg
        return Task.FromResult(CallGSResult.Ok("{}", sync));
    }
}

public sealed class ProLevelPromoteParam
{
    [JsonPropertyName("nID")]
    public int CardId { get; set; }
}
