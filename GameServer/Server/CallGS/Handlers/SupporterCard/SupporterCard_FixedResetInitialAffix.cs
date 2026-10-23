namespace MikuSB.GameServer.Server.CallGS.Handlers.SupporterCard;

[CallGSApi("SupporterCard_FixedResetInitialAffix")]
public class SupporterCard_FixedResetInitialAffix : CallGSHandler<SupporterCardResetInitialParam>
{
    protected override Task<CallGSResult> HandleAsync(CallGSContext context, SupporterCardResetInitialParam req)
    {
        return SupporterCard_ResetInitialAffix.Reset(context.Connection, req, fixedMode: true);
    }
}
