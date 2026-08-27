namespace AIPMS.Application.Features.Auth.Models;

public sealed record RefreshTokenData(
    byte[] TokenHash,
    Guid FamilyId,
    DateTime ExpiresAtUtc,
    string? IpAddress,
    string? UserAgent);

public sealed record RefreshSession(
    long Id,
    Guid FamilyId,
    DateTime ExpiresAtUtc,
    DateTime? RevokedAtUtc,
    DateTime? ReuseDetectedAtUtc,
    AuthAccount Account);

public sealed record PasswordResetSession(
    long Id,
    long UserId,
    DateTime ExpiresAtUtc,
    DateTime? UsedAtUtc);
