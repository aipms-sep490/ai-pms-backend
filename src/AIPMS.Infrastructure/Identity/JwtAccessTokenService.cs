using System.Security.Claims;
using System.Text;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Infrastructure.Identity.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AIPMS.Infrastructure.Identity;

internal sealed class JwtAccessTokenService(
    IOptions<JwtSettings> settings,
    TimeProvider timeProvider)
    : IAccessTokenService
{
    private readonly JwtSettings _settings = settings.Value;

    public AccessTokenResult Create(AccessTokenDescriptor descriptor)
    {
        var issuedAt = timeProvider.GetUtcNow();
        var expiresAt = issuedAt.AddMinutes(_settings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new("sub", descriptor.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.NameIdentifier, descriptor.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Email, descriptor.Email),
            new(ClaimTypes.Name, descriptor.FullName),
            new("jti", Guid.NewGuid().ToString("N")),
            new(
                "pwd",
                (descriptor.PasswordChangedAtUtc?.Ticks ?? 0)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        claims.AddRange(descriptor.Roles.Select(static role => new Claim(ClaimTypes.Role, role)));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        var token = new JsonWebTokenHandler().CreateToken(tokenDescriptor);
        return new AccessTokenResult(token, expiresAt.UtcDateTime);
    }
}
