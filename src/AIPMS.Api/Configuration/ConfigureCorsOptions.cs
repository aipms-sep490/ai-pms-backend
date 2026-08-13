using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Configuration;

public sealed class ConfigureCorsOptions(IOptions<CorsSettings> settings)
    : IConfigureOptions<CorsOptions>
{
    public void Configure(CorsOptions options)
    {
        options.AddPolicy(CorsSettings.FrontendPolicyName, policy =>
        {
            policy.WithOrigins(settings.Value.AllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    }
}
