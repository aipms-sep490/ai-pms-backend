using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AccountSecurity.Abstractions;
using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.AccountSecurity.Models;
using AIPMS.Application.Features.AccountSecurity.Services;
using MediatR;

namespace AIPMS.Application.Features.AccountSecurity.Commands;

public sealed record CreateUserAccountCommand(
    long? DepartmentId,
    long? MajorId,
    string Email,
    string Password,
    string FullName,
    string? Phone,
    string? StudentCode,
    string? EmployeeCode,
    string? Title,
    IReadOnlyCollection<long> RoleIds) : IRequest<UserAccountDto>;

public sealed class CreateUserAccountCommandHandler(
    IUserAccountRepository userRepository,
    IRolePermissionRepository roleRepository,
    IPasswordHashingService passwordHashingService,
    AccountSecurityAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<CreateUserAccountCommand, UserAccountDto>
{
    public async Task<UserAccountDto> Handle(
        CreateUserAccountCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var email = request.Email.Trim().ToLowerInvariant();
        var studentCode = NormalizeCode(request.StudentCode);
        var employeeCode = NormalizeCode(request.EmployeeCode);

        if (await userRepository.IdentityExistsAsync(
            email, studentCode, employeeCode, cancellationToken))
        {
            throw new ConflictException("Email, student code or employee code already exists.");
        }

        var roleIds = request.RoleIds.Distinct().ToArray();
        foreach (var roleId in roleIds)
        {
            _ = await roleRepository.GetRoleAsync(roleId, cancellationToken)
                ?? throw new NotFoundException("Role", roleId);
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var user = await userRepository.CreateUserAsync(
            new CreateAccountData(
                request.DepartmentId,
                request.MajorId,
                email,
                passwordHashingService.Hash(request.Password),
                request.FullName.Trim(),
                NormalizeOptional(request.Phone),
                studentCode,
                employeeCode,
                NormalizeOptional(request.Title),
                roleIds),
            accessService.ActorUserId,
            utcNow,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                accessService.ActorUserId,
                "ACCOUNT_CREATED",
                "USER",
                user.Id,
                new Dictionary<string, object?>
                {
                    ["status"] = user.Status,
                    ["roles"] = user.Roles
                }),
            cancellationToken);

        return user.ToDto();
    }

    private static string? NormalizeCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ImportUserAccountsCommand(
    IReadOnlyCollection<CreateUserAccountRequest> Accounts)
    : IRequest<IReadOnlyList<UserAccountDto>>;

public sealed class ImportUserAccountsCommandHandler(
    IUserAccountRepository userRepository,
    IRolePermissionRepository roleRepository,
    IPasswordHashingService passwordHashingService,
    AccountSecurityAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<ImportUserAccountsCommand, IReadOnlyList<UserAccountDto>>
{
    public async Task<IReadOnlyList<UserAccountDto>> Handle(
        ImportUserAccountsCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var normalized = request.Accounts.Select(account => new CreateAccountData(
            account.DepartmentId,
            account.MajorId,
            account.Email.Trim().ToLowerInvariant(),
            passwordHashingService.Hash(account.Password),
            account.FullName.Trim(),
            NormalizeOptional(account.Phone),
            NormalizeCode(account.StudentCode),
            NormalizeCode(account.EmployeeCode),
            NormalizeOptional(account.Title),
            account.RoleIds.Distinct().ToArray())).ToArray();

        EnsureBatchIdentifiersAreUnique(normalized);

        var verifiedRoleIds = new HashSet<long>();
        foreach (var account in normalized)
        {
            if (await userRepository.IdentityExistsAsync(
                account.Email,
                account.StudentCode,
                account.EmployeeCode,
                cancellationToken))
            {
                throw new ConflictException(
                    $"An account identifier in the import batch already exists for {account.Email}.");
            }

            foreach (var roleId in account.RoleIds)
            {
                if (verifiedRoleIds.Add(roleId))
                {
                    _ = await roleRepository.GetRoleAsync(roleId, cancellationToken)
                        ?? throw new NotFoundException("Role", roleId);
                }
            }
        }

        var accounts = await userRepository.CreateUsersAsync(
            normalized,
            accessService.ActorUserId,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                accessService.ActorUserId,
                "ACCOUNTS_IMPORTED",
                "USER",
                null,
                new Dictionary<string, object?>
                {
                    ["count"] = accounts.Count,
                    ["userIds"] = accounts.Select(static account => account.Id).ToArray()
                }),
            cancellationToken);

        return accounts.Select(static account => account.ToDto()).ToArray();
    }

    private static void EnsureBatchIdentifiersAreUnique(
        IReadOnlyCollection<CreateAccountData> accounts)
    {
        if (accounts.Select(static account => account.Email).Distinct(StringComparer.Ordinal).Count()
                != accounts.Count
            || HasDuplicateNonNull(accounts.Select(static account => account.StudentCode))
            || HasDuplicateNonNull(accounts.Select(static account => account.EmployeeCode)))
        {
            throw new ConflictException(
                "The import batch contains duplicate email, student code or employee code values.");
        }
    }

    private static bool HasDuplicateNonNull(IEnumerable<string?> values)
    {
        var present = values.Where(static value => value is not null).Cast<string>().ToArray();
        return present.Distinct(StringComparer.Ordinal).Count() != present.Length;
    }

    private static string? NormalizeCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record UpdateMyProfileCommand(
    string FullName,
    string? Phone,
    string? Title) : IRequest<UserAccountDto>;

public sealed class UpdateMyProfileCommandHandler(
    IUserAccountRepository repository,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : IRequestHandler<UpdateMyProfileCommand, UserAccountDto>
{
    public async Task<UserAccountDto> Handle(
        UpdateMyProfileCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        _ = await repository.GetUserAsync(userId, cancellationToken)
            ?? throw new UnauthorizedException();

        var user = await repository.UpdateProfileAsync(
            userId,
            new UpdateProfileData(
                request.FullName.Trim(),
                NormalizeOptional(request.Phone),
                NormalizeOptional(request.Title)),
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                userId,
                "ACCOUNT_PROFILE_UPDATED",
                "USER",
                userId,
                new Dictionary<string, object?>()),
            cancellationToken);

        return user.ToDto();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SetUserStatusCommand(long UserId, string Status)
    : IRequest<UserAccountDto>;

public sealed class SetUserStatusCommandHandler(
    IUserAccountRepository repository,
    AccountSecurityAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : IRequestHandler<SetUserStatusCommand, UserAccountDto>
{
    public async Task<UserAccountDto> Handle(
        SetUserStatusCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var user = await repository.GetUserAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);
        var status = request.Status.Trim().ToUpperInvariant();

        if (!string.Equals(status, "ACTIVE", StringComparison.Ordinal)
            && string.Equals(user.Status, "ACTIVE", StringComparison.Ordinal)
            && user.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal)
            && await repository.CountActiveUsersInRoleAsync(
                AppRoles.Admin, cancellationToken) <= 1)
        {
            throw new ConflictException(
                "The last active System Administrator cannot be deactivated or suspended.");
        }

        var updated = await repository.SetStatusAsync(
            request.UserId,
            status,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                accessService.ActorUserId,
                "ACCOUNT_STATUS_CHANGED",
                "USER",
                updated.Id,
                new Dictionary<string, object?>
                {
                    ["previousStatus"] = user.Status,
                    ["status"] = updated.Status
                }),
            cancellationToken);

        return updated.ToDto();
    }
}

public sealed record AssignUserRoleCommand(long UserId, long RoleId) : IRequest;

public sealed class AssignUserRoleCommandHandler(
    IUserAccountRepository userRepository,
    IRolePermissionRepository roleRepository,
    AccountSecurityAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : IRequestHandler<AssignUserRoleCommand>
{
    public async Task Handle(
        AssignUserRoleCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        _ = await userRepository.GetUserAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);
        var role = await roleRepository.GetRoleAsync(request.RoleId, cancellationToken)
            ?? throw new NotFoundException("Role", request.RoleId);

        if (await userRepository.UserHasRoleAsync(
            request.UserId, role.Code, cancellationToken))
        {
            throw new ConflictException("The user already has this role.");
        }

        await userRepository.AssignRoleAsync(
            request.UserId,
            request.RoleId,
            accessService.ActorUserId,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                accessService.ActorUserId,
                "ACCOUNT_ROLE_ASSIGNED",
                "USER",
                request.UserId,
                new Dictionary<string, object?> { ["roleCode"] = role.Code }),
            cancellationToken);
    }
}

public sealed record RemoveUserRoleCommand(long UserId, long RoleId) : IRequest;

public sealed class RemoveUserRoleCommandHandler(
    IUserAccountRepository userRepository,
    IRolePermissionRepository roleRepository,
    AccountSecurityAccessService accessService,
    IAuditTrail auditTrail) : IRequestHandler<RemoveUserRoleCommand>
{
    public async Task Handle(
        RemoveUserRoleCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var user = await userRepository.GetUserAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);
        var role = await roleRepository.GetRoleAsync(request.RoleId, cancellationToken)
            ?? throw new NotFoundException("Role", request.RoleId);

        if (!await userRepository.UserHasRoleAsync(
            request.UserId, role.Code, cancellationToken))
        {
            throw new NotFoundException("User role", $"{request.UserId}:{request.RoleId}");
        }

        if (string.Equals(role.Code, AppRoles.Admin, StringComparison.Ordinal)
            && string.Equals(user.Status, "ACTIVE", StringComparison.Ordinal)
            && await userRepository.CountActiveUsersInRoleAsync(
                AppRoles.Admin, cancellationToken) <= 1)
        {
            throw new ConflictException(
                "The ADMIN role cannot be removed from the last active System Administrator.");
        }

        await userRepository.RemoveRoleAsync(
            request.UserId, request.RoleId, cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                accessService.ActorUserId,
                "ACCOUNT_ROLE_REMOVED",
                "USER",
                request.UserId,
                new Dictionary<string, object?> { ["roleCode"] = role.Code }),
            cancellationToken);
    }
}
