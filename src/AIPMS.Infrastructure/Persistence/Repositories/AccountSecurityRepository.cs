using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AccountSecurity.Abstractions;
using AIPMS.Application.Features.AccountSecurity.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PermissionEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Permission;
using RoleEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Role;
using RolePermission = AIPMS.Infrastructure.Persistence.Generated.Models.RolePermission;
using UserEntity = AIPMS.Infrastructure.Persistence.Generated.Models.User;
using UserRole = AIPMS.Infrastructure.Persistence.Generated.Models.UserRole;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class AccountSecurityRepository(AipmsDbContext context)
    : IUserAccountRepository, IRolePermissionRepository, IAuditLogRepository
{
    public async Task<PagedResult<AccountUser>> GetUsersAsync(
        string? search,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = UserQuery();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(user =>
                user.Email.Contains(search)
                || user.FullName.Contains(search)
                || (user.StudentCode != null && user.StudentCode.Contains(search))
                || (user.EmployeeCode != null && user.EmployeeCode.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(user => user.Status == status);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var users = await query
            .OrderBy(static user => user.Email)
            .ThenBy(static user => user.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<AccountUser>(
            users.Select(static user => user.ToApplication()).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<AccountUser?> GetUserAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        var user = await UserQuery().SingleOrDefaultAsync(
            item => item.Id == userId,
            cancellationToken);
        return user?.ToApplication();
    }

    public Task<bool> IdentityExistsAsync(
        string email,
        string? studentCode,
        string? employeeCode,
        CancellationToken cancellationToken = default) =>
        context.Users.AsNoTracking().AnyAsync(
            user => user.Email == email
                || (studentCode != null && user.StudentCode == studentCode)
                || (employeeCode != null && user.EmployeeCode == employeeCode),
            cancellationToken);

    public async Task<AccountUser> CreateUserAsync(
        CreateAccountData data,
        long actorUserId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var user = new UserEntity
        {
            DepartmentId = data.DepartmentId,
            MajorId = data.MajorId,
            Email = data.Email,
            PasswordHash = data.PasswordHash,
            FullName = data.FullName,
            Phone = data.Phone,
            StudentCode = data.StudentCode,
            EmployeeCode = data.EmployeeCode,
            Title = data.Title,
            Status = "ACTIVE",
            AccessFailedCount = 0,
            PasswordChangedAt = utcNow,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
        context.Users.Add(user);
        await SaveChangesAsync(cancellationToken);

        context.UserRoles.AddRange(data.RoleIds.Select(roleId => new UserRole
        {
            UserId = user.Id,
            RoleId = roleId,
            AssignedBy = actorUserId,
            AssignedAt = utcNow
        }));
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetUserAsync(user.Id, cancellationToken))!;
    }

    public async Task<IReadOnlyList<AccountUser>> CreateUsersAsync(
        IReadOnlyCollection<CreateAccountData> accounts,
        long actorUserId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var pairs = accounts.Select(data => new
        {
            Data = data,
            Entity = new UserEntity
            {
                DepartmentId = data.DepartmentId,
                MajorId = data.MajorId,
                Email = data.Email,
                PasswordHash = data.PasswordHash,
                FullName = data.FullName,
                Phone = data.Phone,
                StudentCode = data.StudentCode,
                EmployeeCode = data.EmployeeCode,
                Title = data.Title,
                Status = "ACTIVE",
                AccessFailedCount = 0,
                PasswordChangedAt = utcNow,
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            }
        }).ToArray();

        context.Users.AddRange(pairs.Select(static pair => pair.Entity));
        await SaveChangesAsync(cancellationToken);
        context.UserRoles.AddRange(pairs.SelectMany(pair =>
            pair.Data.RoleIds.Select(roleId => new UserRole
            {
                UserId = pair.Entity.Id,
                RoleId = roleId,
                AssignedBy = actorUserId,
                AssignedAt = utcNow
            })));
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var userIds = pairs.Select(static pair => pair.Entity.Id).ToArray();
        var users = await UserQuery()
            .Where(user => userIds.Contains(user.Id))
            .ToListAsync(cancellationToken);
        var byId = users.ToDictionary(static user => user.Id);
        return userIds.Select(userId => byId[userId].ToApplication()).ToArray();
    }

    public async Task<AccountUser> UpdateProfileAsync(
        long userId,
        UpdateProfileData data,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var user = await context.Users.SingleAsync(item => item.Id == userId, cancellationToken);
        user.FullName = data.FullName;
        user.Phone = data.Phone;
        user.Title = data.Title;
        user.UpdatedAt = utcNow;
        await SaveChangesAsync(cancellationToken);
        return (await GetUserAsync(userId, cancellationToken))!;
    }

    public async Task<AccountUser> SetStatusAsync(
        long userId,
        string status,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var user = await context.Users
            .Include(static item => item.UserRoleUsers)
            .ThenInclude(static assignment => assignment.Role)
            .SingleAsync(item => item.Id == userId, cancellationToken);
        if (!string.Equals(status, "ACTIVE", StringComparison.Ordinal)
            && string.Equals(user.Status, "ACTIVE", StringComparison.Ordinal)
            && user.UserRoleUsers.Any(static assignment => assignment.Role.Code == AppRoles.Admin))
        {
            var activeAdminCount = await context.UserRoles
                .Where(static assignment =>
                    assignment.Role.Code == AppRoles.Admin
                    && assignment.User.Status == "ACTIVE")
                .Select(static assignment => assignment.UserId)
                .Distinct()
                .CountAsync(cancellationToken);
            if (activeAdminCount <= 1)
            {
                throw new ConflictException(
                    "The last active System Administrator cannot be deactivated or suspended.");
            }
        }
        user.Status = status;
        user.AccessFailedCount = 0;
        user.LockoutEndAt = null;
        user.UpdatedAt = utcNow;
        if (!string.Equals(status, "ACTIVE", StringComparison.Ordinal))
        {
            await context.RefreshTokens
                .Where(token => token.UserId == userId && token.RevokedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(token => token.RevokedAt, utcNow),
                    cancellationToken);
        }

        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetUserAsync(userId, cancellationToken))!;
    }

    public Task<bool> UserHasRoleAsync(
        long userId,
        string roleCode,
        CancellationToken cancellationToken = default) =>
        context.UserRoles.AsNoTracking().AnyAsync(
            assignment => assignment.UserId == userId && assignment.Role.Code == roleCode,
            cancellationToken);

    public Task<int> CountActiveUsersInRoleAsync(
        string roleCode,
        CancellationToken cancellationToken = default) =>
        context.UserRoles.AsNoTracking()
            .Where(assignment => assignment.Role.Code == roleCode && assignment.User.Status == "ACTIVE")
            .Select(static assignment => assignment.UserId)
            .Distinct()
            .CountAsync(cancellationToken);

    public async Task AssignRoleAsync(
        long userId,
        long roleId,
        long actorUserId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        context.UserRoles.Add(new UserRole
        {
            UserId = userId,
            RoleId = roleId,
            AssignedBy = actorUserId,
            AssignedAt = utcNow
        });
        await SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveRoleAsync(
        long userId,
        long roleId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var assignment = await context.UserRoles
            .Include(static item => item.Role)
            .Include(static item => item.User)
            .SingleAsync(
                item => item.UserId == userId && item.RoleId == roleId,
                cancellationToken);
        if (string.Equals(assignment.Role.Code, AppRoles.Admin, StringComparison.Ordinal)
            && string.Equals(assignment.User.Status, "ACTIVE", StringComparison.Ordinal))
        {
            var activeAdminCount = await context.UserRoles
                .Where(static item =>
                    item.Role.Code == AppRoles.Admin
                    && item.User.Status == "ACTIVE")
                .Select(static item => item.UserId)
                .Distinct()
                .CountAsync(cancellationToken);
            if (activeAdminCount <= 1)
            {
                throw new ConflictException(
                    "The ADMIN role cannot be removed from the last active System Administrator.");
            }
        }

        context.UserRoles.Remove(assignment);
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PagedResult<SecurityRole>> GetRolesAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = RoleQuery();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(role => role.Code.Contains(search) || role.Name.Contains(search));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var roles = await query.OrderBy(static role => role.Code).ThenBy(static role => role.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<SecurityRole>(
            roles.Select(static role => role.ToApplication()).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<SecurityRole?> GetRoleAsync(
        long roleId,
        CancellationToken cancellationToken = default)
    {
        var role = await RoleQuery().SingleOrDefaultAsync(item => item.Id == roleId, cancellationToken);
        return role?.ToApplication();
    }

    public Task<bool> RoleCodeExistsAsync(
        string code,
        long? excludedRoleId,
        CancellationToken cancellationToken = default) =>
        context.Roles.AsNoTracking().AnyAsync(
            role => role.Code == code && (!excludedRoleId.HasValue || role.Id != excludedRoleId.Value),
            cancellationToken);

    public async Task<SecurityRole> CreateRoleAsync(
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var role = new RoleEntity
        {
            Code = code,
            Name = name,
            Description = description,
            IsSystemRole = false,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
        context.Roles.Add(role);
        await SaveChangesAsync(cancellationToken);
        return (await GetRoleAsync(role.Id, cancellationToken))!;
    }

    public async Task<SecurityRole> UpdateRoleAsync(
        long roleId,
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var role = await context.Roles.SingleAsync(item => item.Id == roleId, cancellationToken);
        role.Code = code;
        role.Name = name;
        role.Description = description;
        role.UpdatedAt = utcNow;
        await SaveChangesAsync(cancellationToken);
        return (await GetRoleAsync(roleId, cancellationToken))!;
    }

    public async Task DeleteRoleAsync(long roleId, CancellationToken cancellationToken = default)
    {
        var role = await context.Roles.SingleAsync(item => item.Id == roleId, cancellationToken);
        context.Roles.Remove(role);
        await SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<SecurityPermission>> GetPermissionsAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.Permissions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(permission =>
                permission.Code.Contains(search) || permission.Name.Contains(search));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var permissions = await query
            .OrderBy(static permission => permission.Code)
            .ThenBy(static permission => permission.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<SecurityPermission>(
            permissions.Select(static permission => permission.ToApplication()).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<SecurityPermission?> GetPermissionAsync(
        long permissionId,
        CancellationToken cancellationToken = default)
    {
        var permission = await context.Permissions.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == permissionId,
            cancellationToken);
        return permission?.ToApplication();
    }

    public Task<bool> PermissionCodeExistsAsync(
        string code,
        long? excludedPermissionId,
        CancellationToken cancellationToken = default) =>
        context.Permissions.AsNoTracking().AnyAsync(
            permission => permission.Code == code
                && (!excludedPermissionId.HasValue || permission.Id != excludedPermissionId.Value),
            cancellationToken);

    public async Task<SecurityPermission> CreatePermissionAsync(
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var permission = new PermissionEntity
        {
            Code = code,
            Name = name,
            Description = description,
            IsSystemPermission = false,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
        context.Permissions.Add(permission);
        await SaveChangesAsync(cancellationToken);
        return permission.ToApplication();
    }

    public async Task<SecurityPermission> UpdatePermissionAsync(
        long permissionId,
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var permission = await context.Permissions.SingleAsync(
            item => item.Id == permissionId,
            cancellationToken);
        permission.Code = code;
        permission.Name = name;
        permission.Description = description;
        permission.UpdatedAt = utcNow;
        await SaveChangesAsync(cancellationToken);
        return permission.ToApplication();
    }

    public async Task DeletePermissionAsync(
        long permissionId,
        CancellationToken cancellationToken = default)
    {
        var permission = await context.Permissions.SingleAsync(
            item => item.Id == permissionId,
            cancellationToken);
        context.Permissions.Remove(permission);
        await SaveChangesAsync(cancellationToken);
    }

    public async Task ReplaceRolePermissionsAsync(
        long roleId,
        IReadOnlyCollection<long> permissionIds,
        long actorUserId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.RolePermissions.Where(item => item.RoleId == roleId)
            .ExecuteDeleteAsync(cancellationToken);
        context.RolePermissions.AddRange(permissionIds.Select(permissionId => new RolePermission
        {
            RoleId = roleId,
            PermissionId = permissionId,
            AssignedBy = actorUserId,
            AssignedAt = utcNow
        }));
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PermissionMatrix> GetPermissionMatrixAsync(
        CancellationToken cancellationToken = default)
    {
        var roles = await RoleQuery().OrderBy(static role => role.Code).ToListAsync(cancellationToken);
        var permissions = await context.Permissions.AsNoTracking()
            .OrderBy(static permission => permission.Code)
            .ToListAsync(cancellationToken);
        return new PermissionMatrix(
            roles.Select(static role => role.ToApplication()).ToArray(),
            permissions.Select(static permission => permission.ToApplication()).ToArray());
    }

    public async Task<PagedResult<AuditRecord>> GetAuditLogsAsync(
        long? actorUserId,
        string? action,
        string? entityType,
        string? outcome,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.AuditLogs.AsNoTracking();
        if (actorUserId.HasValue) query = query.Where(item => item.ActorUserId == actorUserId.Value);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(item => item.Action == action);
        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(item => item.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(outcome)) query = query.Where(item => item.Outcome == outcome);
        if (fromUtc.HasValue) query = query.Where(item => item.OccurredAt >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(item => item.OccurredAt <= toUtc.Value);

        var totalCount = await query.LongCountAsync(cancellationToken);
        var records = await query.OrderByDescending(static item => item.OccurredAt)
            .ThenByDescending(static item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<AuditRecord>(
            records.Select(static item => item.ToApplication()).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    private IQueryable<UserEntity> UserQuery() =>
        context.Users.AsNoTracking()
            .Include(static user => user.UserRoleUsers)
            .ThenInclude(static assignment => assignment.Role);

    private IQueryable<RoleEntity> RoleQuery() =>
        context.Roles.AsNoTracking()
            .Include(static role => role.RolePermissions)
            .ThenInclude(static assignment => assignment.Permission);

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2601 or 2627 or 547 })
        {
            throw new ConflictException("The account security change conflicts with existing data.");
        }
    }
}
