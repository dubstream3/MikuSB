using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MikuSB.GameServer.Server.CallGS.Handlers.House;

[CallGSApi("House_Request")]
public class House_Request : CallGSHandler<HouseRequestParam>
{
    private static readonly Dictionary<string, IHouseFuncHandler> Handlers = [];

    static House_Request()
    {
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            foreach (var attr in type.GetCustomAttributes<HouseFuncAttribute>())
                Handlers[attr.FuncName] = (IHouseFuncHandler)Activator.CreateInstance(type)!;
        }
    }

    protected override async Task<CallGSResult> HandleAsync(CallGSContext context, HouseRequestParam req)
    {

        if (req?.FuncName == null) return CallGSResult.NoResponse();
        if (Handlers.TryGetValue(req.FuncName, out var handler))
        {
            return await handler.Handle(context, context.RawParam);
        }

        var root = HouseJson.ParseObject(context.RawParam);
        if (root == null) return CallGSResult.NoResponse();
        return CallGSResult.Ok(HouseRequestScript.Synthesize(root));
    }
}

public sealed class HouseRequestParam
{
    [JsonPropertyName("FuncName")]
    public string? FuncName { get; set; }
}
