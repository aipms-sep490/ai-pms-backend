namespace AIPMS.Application.Features.Auth.Models;

public sealed record AuthAccount(
    long Id,
    string Email,
    string PasswordHash,
    string FullName,
    string Status,
    IReadOnlyCollection<string> Roles,
    int AccessFailedCount,
    DateTime? LockoutEndAt,
    DateTime? PasswordChangedAt);
