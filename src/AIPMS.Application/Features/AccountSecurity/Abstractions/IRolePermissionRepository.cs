using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AccountSecurity.Models;

namespace AIPMS.Application.Features.AccountSecurity.Abstractions;

public interface IRolePermissionRepository
{
    Task<PagedResult<SecurityRole>> GetRolesAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<SecurityRole?> GetRoleAsync(
        long roleId,
        CancellationToken cancellationToken = default);

    Task<bool> RoleCodeExistsAsync(
        string code,
        long? excludedRoleId,
        CancellationToken cancellationToken = default);

    Task<SecurityRole> CreateRoleAsync(
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<SecurityRole> UpdateRoleAsync(
        long roleId,
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task DeleteRoleAsync(long roleId, CancellationToken cancellationToken = default);

    Task<PagedResult<SecurityPermission>> GetPermissionsAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<SecurityPermission?> GetPermissionAsync(
        long permissionId,
        CancellationToken cancellationToken = default);

    Task<bool> PermissionCodeExistsAsync(
        string code,
        long? excludedPermissionId,
        CancellationToken cancellationToken = default);

    Task<SecurityPermission> CreatePermissionAsync(
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<SecurityPermission> UpdatePermissionAsync(
        long permissionId,
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task DeletePermissionAsync(
        long permissionId,
        CancellationToken cancellationToken = default);

    Task ReplaceRolePermissionsAsync(
        long roleId,
        IReadOnlyCollection<long> permissionIds,
        long actorUserId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<PermissionMatrix> GetPermissionMatrixAsync(
        CancellationToken cancellationToken = default);
}
