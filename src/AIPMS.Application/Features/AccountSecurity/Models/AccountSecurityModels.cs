using AIPMS.Application.Common.Models;

namespace AIPMS.Application.Features.AccountSecurity.Models;

public sealed record AccountUser(
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

public sealed record CreateAccountData(
    long? DepartmentId,
    long? MajorId,
    string Email,
    string PasswordHash,
    string FullName,
    string? Phone,
    string? StudentCode,
    string? EmployeeCode,
    string? Title,
    IReadOnlyCollection<long> RoleIds);

public sealed record UpdateProfileData(
    string FullName,
    string? Phone,
    string? Title);

public sealed record SecurityRole(
    long Id,
    string Code,
    string Name,
    string? Description,
    bool IsSystemRole,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyCollection<SecurityPermission> Permissions);

public sealed record SecurityPermission(
    long Id,
    string Code,
    string Name,
    string? Description,
    bool IsSystemPermission,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record AuditRecord(
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

public sealed record PermissionMatrix(
    IReadOnlyCollection<SecurityRole> Roles,
    IReadOnlyCollection<SecurityPermission> Permissions);
