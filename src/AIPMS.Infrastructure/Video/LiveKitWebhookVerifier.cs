using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using AIPMS.Application.Features.Meetings.Video;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AIPMS.Infrastructure.Video;

internal sealed class LiveKitWebhookVerifier(IOptions<VideoMeetingOptions> options, TimeProvider clock)
{
    public bool Verify(string? authorization, string rawBody)
    {
        var settings = options.Value;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ApiKey)
            || string.IsNullOrWhiteSpace(settings.ApiSecret) || string.IsNullOrWhiteSpace(authorization)) return false;

        // LiveKit sends the compact JWT directly. Retain Bearer support for existing callers.
        var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization[7..] : authorization;
        try
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 16384 };
            handler.ValidateToken(token, new TokenValidationParameters
            {
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.ApiSecret)),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidateIssuer = true,
                ValidIssuer = settings.ApiKey,
                ValidateAudience = false,
                RequireExpirationTime = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(10),
                LifetimeValidator = (notBefore, expires, _, _) => expires.HasValue
                    && expires.Value > now.AddSeconds(-10)
                    && (!notBefore.HasValue || (notBefore.Value <= now.AddSeconds(10) && notBefore.Value <= expires.Value))
            }, out var validated);

            var jwt = (JwtSecurityToken)validated;
            if (!jwt.Payload.TryGetValue("sha256", out var claim) || claim is not string checksum) return false;
            // Hash the original body without parsing/re-serializing; the claim uses standard Base64.
            var expected = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody)));
            return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.UTF8.GetBytes(checksum));
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException or FormatException)
        {
            return false;
        }
    }
}
