using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace AIPMS.Api.Configuration;

public static class TrustedProxyConfiguration
{
    public static IServiceCollection AddTrustedProxyHeaders(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, config) =>
        {
            if (!config.GetValue<bool>("ReverseProxy:Enabled")) return;
            var proxies = config.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
            if (proxies.Length == 0)
                throw new InvalidOperationException("ReverseProxy requires explicit trusted proxy IP addresses.");

            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var value in proxies)
            {
                if (!IPAddress.TryParse(value, out var address)
                    || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
                    throw new InvalidOperationException("ReverseProxy contains an invalid trusted proxy IP address.");
                options.KnownProxies.Add(address);
            }

            var header = config["ReverseProxy:ForwardedForHeaderName"] ?? "X-Forwarded-For";
            if (header is not ("X-Forwarded-For" or "CF-Connecting-IP"))
                throw new InvalidOperationException("ReverseProxy forwarded IP header is unsupported.");
            options.ForwardedForHeaderName = header;
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.RequireHeaderSymmetry = true;
        }).ValidateOnStart();
        return services;
    }
}
