using AIPMS.Application.Features.AccountSecurity.Models;
using AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Mappers;

internal static class AccountSecurityMapper
{
    public static AccountUser ToApplication(this User user) =>
        new(
            user.Id,
            user.DepartmentId,
            user.MajorId,
            user.Email,
            user.FullName,
            user.Phone,
            user.StudentCode,
            user.EmployeeCode,
            user.Title,
            user.Status,
            user.AccessFailedCount,
            user.LockoutEndAt,
            user.PasswordChangedAt,
            user.LastLoginAt,
            user.CreatedAt,
            user.UpdatedAt,
            user.UserRoleUsers
                .Select(static assignment => assignment.Role.Code)
                .OrderBy(static code => code, StringComparer.Ordinal)
                .ToArray());

    public static SecurityPermission ToApplication(this Permission permission) =>
        new(
            permission.Id,
            permission.Code,
            permission.Name,
            permission.Description,
            permission.IsSystemPermission,
            permission.CreatedAt,
            permission.UpdatedAt);

    public static SecurityRole ToApplication(this Role role) =>
        new(
            role.Id,
            role.Code,
            role.Name,
            role.Description,
            role.IsSystemRole,
            role.CreatedAt,
            role.UpdatedAt,
            role.RolePermissions
                .Select(static assignment => assignment.Permission.ToApplication())
                .OrderBy(static permission => permission.Code, StringComparer.Ordinal)
                .ToArray());

    public static AuditRecord ToApplication(this AuditLog record) =>
        new(
            record.Id,
            record.ActorUserId,
            record.Action,
            record.EntityType,
            record.EntityId,
            record.Outcome,
            record.CorrelationId,
            record.IpAddress,
            record.UserAgent,
            record.DetailsJson,
            record.OccurredAt);
}
