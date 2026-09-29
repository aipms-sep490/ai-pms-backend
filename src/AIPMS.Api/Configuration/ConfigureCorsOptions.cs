using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Configuration;

public sealed class ConfigureCorsOptions(IOptions<CorsSettings> settings,
    IOptions<AIPMS.Infrastructure.Identity.Configuration.GoogleAuthSettings> google)
    : IConfigureOptions<CorsOptions>
{
    public void Configure(CorsOptions options)
    {
        options.AddPolicy("google-auth", policy =>
        {
            if (google.Value.AllowedOrigins.Length > 0)
                policy.WithOrigins(google.Value.AllowedOrigins).WithMethods("GET", "POST")
                    .WithHeaders("Content-Type", "Authorization").AllowCredentials();
        });
        options.AddPolicy(CorsSettings.FrontendPolicyName, policy =>
        {
            policy.WithOrigins(settings.Value.AllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    }
}
