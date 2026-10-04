using System.Net;
using AIPMS.Api.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIPMS.IntegrationTests;

public sealed class TrustedProxyHeadersTests
{
    [Theory]
    [InlineData("172.20.0.5", true)]
    [InlineData("::ffff:172.20.0.5", true)]
    [InlineData("172.20.0.6", false)]
    [InlineData("127.0.0.1", false)]
    public async Task OnlyConfiguredTunnelCanForwardClientIpAndHttps(string peer, bool trusted)
    {
        using var services = Services(true, "172.20.0.5");
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Scheme = "http";
        context.Request.Headers["CF-Connecting-IP"] = "203.0.113.20";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.99";
        context.Request.Headers["X-Forwarded-Host"] = "attacker.example";
        context.Request.Host = new HostString("api-staging.khaidz.com");

        await Middleware(services).Invoke(context);

        Assert.Equal(trusted ? "https" : "http", context.Request.Scheme);
        Assert.Equal(trusted ? "203.0.113.20" : peer, context.Connection.RemoteIpAddress!.ToString());
        Assert.Equal("api-staging.khaidz.com", context.Request.Host.Value);
    }

    [Fact]
    public async Task DisabledConfigurationDoesNotTrustForwardedHeaders()
    {
        using var services = Services(false, "172.20.0.5");
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.20.0.5");
        context.Request.Scheme = "http";
        context.Request.Headers["CF-Connecting-IP"] = "203.0.113.20";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        await Middleware(services).Invoke(context);
        Assert.Equal("http", context.Request.Scheme);
        Assert.Equal("172.20.0.5", context.Connection.RemoteIpAddress!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-ip")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void EnabledWithoutExplicitValidProxyFailsClosed(string? proxy)
    {
        using var services = Services(true, proxy);
        Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value);
    }

    private static ForwardedHeadersMiddleware Middleware(ServiceProvider services) =>
        new(_ => Task.CompletedTask, NullLoggerFactory.Instance,
            services.GetRequiredService<IOptions<ForwardedHeadersOptions>>());

    private static ServiceProvider Services(bool enabled, string? proxy)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ReverseProxy:Enabled"] = enabled.ToString(),
            ["ReverseProxy:KnownProxies:0"] = proxy,
            ["ReverseProxy:ForwardedForHeaderName"] = "CF-Connecting-IP"
        }).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(config);
        services.AddTrustedProxyHeaders();
        return services.BuildServiceProvider();
    }
}
