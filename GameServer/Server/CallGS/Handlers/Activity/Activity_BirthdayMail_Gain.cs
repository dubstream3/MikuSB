using MikuSB.Data;
using MikuSB.Database;
using MikuSB.Proto;
using MikuSB.Util;
using System.Text.Json.Nodes;

namespace MikuSB.GameServer.Server.CallGS.Handlers.Activity;

/// <summary>
/// 生日邮件 (birthday wish mail) pickup.
///
/// Client send point: Activity/BirthdayMail.lua:137-139 RequestMail(nYear) calls
/// me:CallGS("Activity_BirthdayMail_Gain", nYear) - a BARE INTEGER, not json.encode'd, so the param
/// arrives as "2026" instead of a JSON object. The handler therefore accepts both shapes (see
/// ReadYear) instead of parsing the param as a JsonObject like the other Activity handlers do.
/// The callback is registered at :142-146 and only looks at tbParam.err
/// ("if tbParam and tbParam.err then UI.ShowTip(tbParam.err) end"), so a successful reply must not
/// contain that key.
///
/// The only state the client reads back is group 155, slot = nYear:
/// BirthdayMail.lua:8 BirthdayMail.GID = 155, read at :129 inside CheckNew (:117-134) as
/// "if me:GetAttribute(BirthdayMail.GID, nYear) > 0 then return 0 end", i.e. any non-zero value
/// means "this year's mail has already been taken". Granting is therefore writing
/// (155, nYear) = 1 through the NtfSyncAttr path (player.Attributes.SyncTo), the same way
/// Activity_Refresh.cs stamps group 50; AttrIds.Activity.BirthdayMailGroupId is the named constant
/// for 155 (Common/Data/AttrIds.cs:52).
///
/// There is no reward to hand out and no mail subsystem to hand it to: riki/wishmail.json only
/// carries Id/Time/EndTime/title/text/expiretitle/expiretext/icon/UIName/Expiration, so the mail
/// body and its icon are rendered entirely client-side from that table.
/// </summary>
[CallGSApi("Activity_BirthdayMail_Gain")]
public class Activity_BirthdayMail_Gain : CallGSHandler
{
    /// <summary>The client only tests "&gt; 0" (BirthdayMail.lua:129); 1 is the natural "taken".</summary>
    private const uint Gained = 1;

    private const uint MinYear = 2020;
    private const uint MaxYear = 2100;

    protected override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        var player = context.Connection.Player!;
        var year = ReadYear(param);

        var sync = new NtfSyncPlayer();
        var response = new JsonObject();

        if (year < MinYear || year > MaxYear)
        {
            // Refusal shape: no "err" key (BirthdayMail.lua:142 would show a tip for one) and no
            // attribute write, so the client simply keeps offering the mail for that year.
            Logger.GetByClassName().Warn(
                $"Activity_BirthdayMail_Gain without a usable year (param='{param}') for uid={player.Uid}.");
            return Task.FromResult(
                CallGSResult.Ok(response.ToJsonString(), sync, "Activity_BirthdayMail_Gain"));
        }

        var attr = player.Attributes.GetOrCreate(AttrIds.Activity.BirthdayMailGroupId, year);
        if (attr.Val != Gained)
        {
            attr.Val = Gained;
            player.Attributes.SyncTo(sync, attr);
        }

        response["nYear"] = year;

        Logger.GetByClassName().Info(
            $"Birthday mail {year} marked as gained for uid={player.Uid} " +
            $"(attr {AttrIds.Activity.BirthdayMailGroupId}/{year}={attr.Val}).");

        DatabaseHelper.SaveDatabaseType(player.Data);

        return Task.FromResult(
            CallGSResult.Ok(response.ToJsonString(), sync, "Activity_BirthdayMail_Gain"));
    }

    /// <summary>
    /// The wire value is the bare integer year (BirthdayMail.lua:138), but the JSON-object form is
    /// accepted as well so a future client build - or a GM call - cannot silently break the handler.
    /// </summary>
    private static uint ReadYear(string param)
    {
        if (string.IsNullOrWhiteSpace(param))
            return 0;

        var text = param.Trim();
        if (text.StartsWith('{'))
        {
            try
            {
                if (JsonNode.Parse(text) is JsonObject json)
                {
                    foreach (var key in new[] { "nYear", "year", "Year", "nId" })
                    {
                        var value = json[key]?.ToString();
                        if (uint.TryParse(value, out var parsed) && parsed != 0)
                            return parsed;
                    }
                }
            }
            catch (System.Text.Json.JsonException)
            {
                return 0;
            }

            return 0;
        }

        text = text.Trim('"');
        return uint.TryParse(text, out var year) ? year : 0;
    }
}
