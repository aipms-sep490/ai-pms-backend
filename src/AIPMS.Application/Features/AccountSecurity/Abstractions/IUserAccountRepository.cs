using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AccountSecurity.Models;

namespace AIPMS.Application.Features.AccountSecurity.Abstractions;

public interface IUserAccountRepository
{
    Task<PagedResult<AccountUser>> GetUsersAsync(
        string? search,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<AccountUser?> GetUserAsync(
        long userId,
        CancellationToken cancellationToken = default);

    Task<bool> IdentityExistsAsync(
        string email,
        string? studentCode,
        string? employeeCode,
        CancellationToken cancellationToken = default);

    Task<AccountUser> CreateUserAsync(
        CreateAccountData data,
        long actorUserId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountUser>> CreateUsersAsync(
        IReadOnlyCollection<CreateAccountData> accounts,
        long actorUserId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<AccountUser> UpdateProfileAsync(
        long userId,
        UpdateProfileData data,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<AccountUser> SetStatusAsync(
        long userId,
        string status,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<bool> UserHasRoleAsync(
        long userId,
        string roleCode,
        CancellationToken cancellationToken = default);

    Task<int> CountActiveUsersInRoleAsync(
        string roleCode,
        CancellationToken cancellationToken = default);

    Task AssignRoleAsync(
        long userId,
        long roleId,
        long actorUserId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task RemoveRoleAsync(
        long userId,
        long roleId,
        CancellationToken cancellationToken = default);
}
