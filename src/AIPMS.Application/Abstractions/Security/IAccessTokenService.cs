namespace AIPMS.Application.Abstractions.Security;

public interface IAccessTokenService
{
    AccessTokenResult Create(AccessTokenDescriptor descriptor);
}

public sealed record AccessTokenDescriptor(
    long UserId,
    string Email,
    string FullName,
    IReadOnlyCollection<string> Roles,
    DateTime? PasswordChangedAtUtc = null);

public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);
