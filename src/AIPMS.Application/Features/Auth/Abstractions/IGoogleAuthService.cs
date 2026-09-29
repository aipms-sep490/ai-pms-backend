using AIPMS.Application.Features.Auth.DTOs;

namespace AIPMS.Application.Features.Auth.Abstractions;

public sealed record GoogleChallenge(Guid ChallengeId, string ClientId, string Nonce, DateTime ExpiresAtUtc);
public sealed record ExternalLoginDto(string Provider, string Email, DateTime LinkedAtUtc);
public sealed record GoogleIdentity(string Subject, string Email, string Nonce);

public interface IGoogleIdentityVerifier
{
    Task<GoogleIdentity> VerifyAsync(string idToken, CancellationToken ct);
}

public interface IGoogleAuthService
{
    Task<GoogleChallenge> ChallengeAsync(string purpose, string browserBinding, CancellationToken ct);
    Task<LoginResponse> LoginAsync(Guid challengeId, string idToken, string browserBinding, CancellationToken ct);
    Task LinkAsync(Guid challengeId, string idToken, string browserBinding, string currentPassword, CancellationToken ct);
    Task UnlinkAsync(string currentPassword, CancellationToken ct);
    Task<IReadOnlyList<ExternalLoginDto>> ListAsync(CancellationToken ct);
    Task CleanupAsync(CancellationToken ct);
}
