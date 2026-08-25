using System.Globalization;
using System.Security.Claims;
using System.Text;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Infrastructure.Identity.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AIPMS.Api.Security;

internal sealed class ConfigureJwtBearerOptions(IOptions<JwtSettings> settings)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(JwtBearerOptions options) =>
        Configure(JwtBearerDefaults.AuthenticationScheme, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (!string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            return;
        }

        var jwt = settings.Value;
        options.MapInboundClaims = false;
        options.SaveToken = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var passwordVersionValue = context.Principal?.FindFirstValue("pwd");
                if (!long.TryParse(userIdValue, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
                    || !long.TryParse(passwordVersionValue, NumberStyles.None, CultureInfo.InvariantCulture, out var passwordVersionTicks))
                {
                    context.Fail("The access token does not contain valid account claims.");
                    return;
                }

                var validator = context.HttpContext.RequestServices
                    .GetRequiredService<IAccessTokenAccountValidator>();
                var isValid = await validator.IsValidAsync(
                    userId,
                    passwordVersionTicks == 0
                        ? null
                        : new DateTime(passwordVersionTicks, DateTimeKind.Utc),
                    context.HttpContext.RequestAborted);
                if (!isValid)
                {
                    context.Fail("The account is inactive or its credentials have changed.");
                }
            }
        };
    }
}
