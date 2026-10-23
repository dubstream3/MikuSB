using MikuSB.Proto;
using MikuSB.GameServer.Game.Player;
using System.Text.Json.Serialization;

using MikuSB.Data;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Misc;

[CallGSApi("SettingChange")]
public class SettingChange : CallGSHandler<List<SettingChangeParam>>
{
    private const uint PlayerSettingGid = AttrIds.Settings.Gid;

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, List<SettingChangeParam> changes)
    {
        var player = context.Player;
        var sync = new NtfSyncPlayer();

        foreach (var change in changes)
        {
            var value = player.Attributes.GetStringValue(PlayerSettingGid, change.Id);

            if (value == null)
                continue;

            player.Attributes.SyncTo(sync, player.Attributes.GetString(PlayerSettingGid, change.Id)!);
        }

        if (sync.CustomStr.Count > 0)
            await context.Connection.SendPacket(CmdIds.NtfSyncAttr, sync);

        return CallGSResult.NoResponse();
    }
}

public sealed class SettingChangeParam
{
    [JsonPropertyName("id")]
    public uint Id { get; set; }
}
