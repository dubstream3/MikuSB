using MikuSB.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MikuSB.Proxy;

public static class ProxyServiceCollectionExtensions
{
    public static IServiceCollection AddMikuSbProxy(this IServiceCollection services, ProxyOptions options)
    {
        services.AddSingleton<IOptions<ProxyOptions>>(Microsoft.Extensions.Options.Options.Create(options));
        services.AddSingleton<ProxyServer>();
        services.AddHostedService(sp => sp.GetRequiredService<ProxyServer>());
        return services;
    }
}
