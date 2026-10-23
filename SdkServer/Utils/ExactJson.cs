using Microsoft.AspNetCore.Mvc;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MikuSB.SdkServer.Utils;

/// <summary>
/// Serializes emulated SDK replies with their original property casing.
///
/// MVC is configured with <c>JsonNamingPolicy.SnakeCaseLower</c>, which rewrites
/// <c>responseSuccess</c> into <c>response_success</c> and <c>agreementUpdateTime</c> into
/// <c>agreement_update_time</c>. The real XGSDK/js2sdk services use camelCase, so the CN
/// emulation returns strings produced with the default naming policy instead.
/// </summary>
public static class ExactJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static ContentResult Serialize(object payload) => new()
    {
        Content = JsonSerializer.Serialize(payload, Options),
        ContentType = "application/json; charset=utf-8"
    };
}
