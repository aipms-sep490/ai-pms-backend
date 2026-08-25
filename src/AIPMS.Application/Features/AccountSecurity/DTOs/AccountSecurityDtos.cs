using AIPMS.Application.Features.AccountSecurity.Models;

namespace AIPMS.Application.Features.AccountSecurity.DTOs;

public sealed record UserAccountDto(
    long Id,
    long? DepartmentId,
    long? MajorId,
    string Email,
    string FullName,
    string? Phone,
    string? StudentCode,
    string? EmployeeCode,
    string? Title,
    string Status,
    int AccessFailedCount,
    DateTime? LockoutEndAt,
    DateTime? PasswordChangedAt,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyCollection<string> Roles);

public sealed record CreateUserAccountRequest(
    long? DepartmentId,
    long? MajorId,
    string Email,
    string Password,
    string FullName,
    string? Phone,
    string? StudentCode,
    string? EmployeeCode,
    string? Title,
    IReadOnlyCollection<long> RoleIds);

public sealed record ImportUserAccountsRequest(
    IReadOnlyCollection<CreateUserAccountRequest> Accounts);

public sealed record UpdateMyProfileRequest(string FullName, string? Phone, string? Title);

public sealed record SetUserStatusRequest(string Status);

public sealed record SecurityRoleDto(
    long Id,
    string Code,
    string Name,
    string? Description,
    bool IsSystemRole,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyCollection<SecurityPermissionDto> Permissions);

public sealed record SecurityPermissionDto(
    long Id,
    string Code,
    string Name,
    string? Description,
    bool IsSystemPermission,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateSecurityCatalogRequest(
    string Code,
    string Name,
    string? Description);

public sealed record UpdateSecurityCatalogRequest(
    string Code,
    string Name,
    string? Description);

public sealed record ReplaceRolePermissionsRequest(IReadOnlyCollection<long> PermissionIds);

public sealed record PermissionMatrixDto(
    IReadOnlyCollection<SecurityRoleDto> Roles,
    IReadOnlyCollection<SecurityPermissionDto> Permissions);

public sealed record AuditRecordDto(
    long Id,
    long? ActorUserId,
    string Action,
    string EntityType,
    string? EntityId,
    string Outcome,
    Guid? CorrelationId,
    string? IpAddress,
    string? UserAgent,
    string? DetailsJson,
    DateTime OccurredAt);

internal static class AccountSecurityDtoMapper
{
    public static UserAccountDto ToDto(this AccountUser user) =>
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
            user.Roles);

    public static SecurityPermissionDto ToDto(this SecurityPermission permission) =>
        new(
            permission.Id,
            permission.Code,
            permission.Name,
            permission.Description,
            permission.IsSystemPermission,
            permission.CreatedAt,
            permission.UpdatedAt);

    public static SecurityRoleDto ToDto(this SecurityRole role) =>
        new(
            role.Id,
            role.Code,
            role.Name,
            role.Description,
            role.IsSystemRole,
            role.CreatedAt,
            role.UpdatedAt,
            role.Permissions.Select(static permission => permission.ToDto()).ToArray());

    public static AuditRecordDto ToDto(this AuditRecord record) =>
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
