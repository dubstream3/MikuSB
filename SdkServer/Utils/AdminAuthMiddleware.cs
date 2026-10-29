using Microsoft.AspNetCore.Http;

namespace MikuSB.SdkServer.Utils;

/// <summary>
/// Guards every <c>/api/*</c> call that arrives on the admin port. Static panel files stay public on that
/// port (they carry no data), the API does not: without a valid bearer token the response is a 401 the
/// front-end turns into its login gate.
/// </summary>
public class AdminAuthMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!AdminApi.IsAdminRequest(context) || !context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;

        if (!AdminToken.IsValid(token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync("{\"code\":\"unauthorized\",\"message\":\"missing or invalid admin token\"}");
            return;
        }

        await next(context);
    }
}
