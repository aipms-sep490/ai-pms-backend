using System.Net.Http;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Infrastructure.Identity.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AIPMS.Infrastructure.Identity;

internal interface IGoogleSigningKeys
{
    Task<IReadOnlyCollection<SecurityKey>> GetAsync(bool refresh, CancellationToken ct);
}

internal sealed class GoogleSigningKeys(HttpClient http, TimeProvider clock) : IGoogleSigningKeys, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyCollection<SecurityKey> keys = [];
    private DateTimeOffset fetchedAt = DateTimeOffset.MinValue;

    public async Task<IReadOnlyCollection<SecurityKey>> GetAsync(bool refresh, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var age = clock.GetUtcNow() - fetchedAt;
            if (keys.Count > 0 && age < TimeSpan.FromHours(1) && (!refresh || age < TimeSpan.FromMinutes(1))) return keys;
            try
            {
                var json = await http.GetStringAsync("https://www.googleapis.com/oauth2/v3/certs", ct);
                keys = new JsonWebKeySet(json).GetSigningKeys().ToArray();
                if (keys.Count == 0) throw new ServiceUnavailableException("Google authentication is temporarily unavailable.");
                fetchedAt = clock.GetUtcNow();
                return keys;
            }
            catch (Exception ex) when (ex is HttpRequestException or ArgumentException || ex is OperationCanceledException && !ct.IsCancellationRequested)
            {
                // Provider response bodies and tokens must never reach logs or ProblemDetails.
                throw new ServiceUnavailableException("Google authentication is temporarily unavailable.");
            }
        }
        finally { gate.Release(); }
    }

    public void Dispose() { gate.Dispose(); http.Dispose(); }
}

internal sealed class GoogleIdentityVerifier(IGoogleSigningKeys keys, IOptions<GoogleAuthSettings> settings,
    TimeProvider clock) : IGoogleIdentityVerifier
{
    public async Task<GoogleIdentity> VerifyAsync(string idToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idToken) || idToken.Length > 16384) throw Invalid();
        var handler = new JsonWebTokenHandler { MaximumTokenSizeInBytes = 16384 };
        if (!handler.CanReadToken(idToken)) throw Invalid();
        var parameters = new TokenValidationParameters
        {
            ValidIssuers = ["https://accounts.google.com", "accounts.google.com"],
            ValidAudience = settings.Value.ClientId,
            ValidateIssuer = true, ValidateAudience = true, ValidateIssuerSigningKey = true,
            RequireSignedTokens = true, RequireExpirationTime = true, ValidateLifetime = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            IssuerSigningKeys = await keys.GetAsync(false, ct),
            LifetimeValidator = (notBefore, expires, _, _) => expires is not null
                && expires > clock.GetUtcNow().UtcDateTime.AddSeconds(-30)
                && (notBefore is null || notBefore <= clock.GetUtcNow().UtcDateTime.AddSeconds(30))
        };
        var result = await handler.ValidateTokenAsync(idToken, parameters);
        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            parameters.IssuerSigningKeys = await keys.GetAsync(true, ct);
            result = await handler.ValidateTokenAsync(idToken, parameters);
        }
        if (!result.IsValid || result.SecurityToken is not JsonWebToken token) throw Invalid();
        if (!token.TryGetPayloadValue<string>("sub", out var subject) || string.IsNullOrWhiteSpace(subject) || subject.Length > 255
            || !token.TryGetPayloadValue<string>("email", out var email) || string.IsNullOrWhiteSpace(email) || email.Length > 255
            || !token.TryGetPayloadValue<bool>("email_verified", out var verified) || !verified
            || !token.TryGetPayloadValue<string>("nonce", out var nonce) || string.IsNullOrWhiteSpace(nonce) || nonce.Length > 256
            || !token.TryGetPayloadValue<long>("iat", out var issued) || issued > clock.GetUtcNow().AddSeconds(30).ToUnixTimeSeconds()) throw Invalid();
        if (token.TryGetPayloadValue<string>("azp", out var presenter) && presenter != settings.Value.ClientId) throw Invalid();
        return new GoogleIdentity(subject, email.Trim(), nonce);
    }

    private static UnauthorizedException Invalid() => new("Google authentication failed.");
}
