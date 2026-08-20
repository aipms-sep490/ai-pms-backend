namespace AIPMS.Application.Abstractions.Security;

public interface IAccessTokenService
{
    AccessTokenResult Create(AccessTokenDescriptor descriptor);
}

public sealed record AccessTokenDescriptor(
    long UserId,
    string Email,
    string FullName,
    IReadOnlyCollection<string> Roles);

public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);
