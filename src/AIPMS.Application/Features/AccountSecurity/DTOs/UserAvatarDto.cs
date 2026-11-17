namespace AIPMS.Application.Features.AccountSecurity.DTOs;

public sealed record UserAvatarDto(bool HasAvatar, string? ConcurrencyToken, string? ContentType,
    long? SizeBytes, DateTime? UpdatedAt);
