using System.Reflection;
using System.Text.Json;

namespace MikuSB.GameServer.Server.CallGS;

public abstract class CallGSHandler : ICallGSHandler
{
    protected CallGSHandler()
    {
        Api = GetType().GetCustomAttribute<CallGSApiAttribute>()?.Api
            ?? throw new InvalidOperationException($"CallGSApiAttribute is missing on {GetType().Name}.");
    }

    protected string Api { get; }

    protected abstract Task<CallGSResult> HandleAsync(CallGSContext context, string param);

    public async Task Handle(Connection connection, string param, ushort seqNo)
    {
        var result = await HandleAsync(new CallGSContext
        {
            Connection = connection,
            Player = connection.Player!,
            SequenceNumber = seqNo,
            RawParam = param
        }, param);
        await SendResultAsync(connection, result);
    }

    protected static TRequest? Deserialize<TRequest>(string param)
    {
        return JsonSerializer.Deserialize<TRequest>(param, CallGSJson.Options);
    }

    protected static CallGSResult BadParam()
    {
        return CallGSResult.Error("error.BadParam");
    }

    private Task SendResultAsync(Connection connection, CallGSResult result)
    {
        if (!result.SendResponse)
            return Task.CompletedTask;

        return CallGSRouter.SendScript(connection, result.Api ?? Api, result.Argument, result.Sync!);
    }
}

public abstract class CallGSHandler<TRequest> : CallGSHandler where TRequest : class
{
    protected sealed override Task<CallGSResult> HandleAsync(CallGSContext context, string param)
    {
        // Plenty of client Lua call sites send no parameters at all, e.g.
        // me:CallGS('AlienDefense_CheckOpenAct') (Script/DLC/AlienDefense/AlienDefense.lua:793). The
        // wire payload is then an empty string and JsonSerializer throws
        // "The input does not contain any JSON tokens", which the router turns into the generic
        // ui.TxtNotOpen answer -- i.e. a working request looks like a missing feature. Treating a
        // blank payload as an empty object matches what the hand-written handlers do
        // (Handlers/Activity/Activity_Flag.cs:25, Activity_GetAward.cs:33).
        // A Lua *empty table* is encoded as "[]" (json.encode({})), which is a JSON array and cannot
        // be deserialized into a TRequest object. SeasonBoss.lua:1105-1110 is the known call site:
        // me:CallGS("SeasonBoss_CheckOpen", json.encode(tbParam)) with tbParam = {} produced
        // "The JSON value could not be converted to ...SeasonBossCheckOpenParam. Path: $ |
        // LineNumber: 0 | BytePositionInLine: 1." and the router answered ui.TxtNotOpen.
        var payload = string.IsNullOrWhiteSpace(param) ? "{}" : param.Trim();
        if (payload == "[]")
            payload = "{}";
        var request = Deserialize<TRequest>(payload);
        if (request == null)
            return Task.FromResult(BadParam());

        return HandleAsync(context, request);
    }

    protected abstract Task<CallGSResult> HandleAsync(CallGSContext context, TRequest request);
}
