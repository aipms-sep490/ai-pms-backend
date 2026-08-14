namespace AIPMS.Application.Features.Auth.DTOs;

public sealed record AuthUserDto(
    long Id,
    string Email,
    string FullName,
    IReadOnlyCollection<string> Roles);
