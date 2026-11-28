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
    IReadOnlyCollection<long> RoleIds, string? CurriculumCode = null) : IRequest<UserAccountDto>;

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
            var role = await roleRepository.GetRoleAsync(roleId, cancellationToken)
                ?? throw new NotFoundException("Role", roleId);
            GlobalAccountRoleGuard.RequireAssignable(role.Code);
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
                roleIds, NormalizeOptional(request.CurriculumCode)),
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
            account.RoleIds.Distinct().ToArray(), NormalizeOptional(account.CurriculumCode))).ToArray();

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
                    var role = await roleRepository.GetRoleAsync(roleId, cancellationToken)
                        ?? throw new NotFoundException("Role", roleId);
                    GlobalAccountRoleGuard.RequireAssignable(role.Code);
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
        var existing = await repository.GetUserAsync(userId, cancellationToken)
            ?? throw new UnauthorizedException();

        var isPureStudent = existing.Roles.Contains(AppRoles.Student, StringComparer.Ordinal)
            && existing.Roles.All(r => string.Equals(r, AppRoles.Student, StringComparison.Ordinal));
        var trimmedFullName = request.FullName.Trim();
        var normalizedTitle = NormalizeOptional(request.Title);
        if (isPureStudent
            && (!string.Equals(trimmedFullName, existing.FullName, StringComparison.Ordinal)
                || !string.IsNullOrWhiteSpace(normalizedTitle)))
        {
            throw new ConflictException(
                "Students cannot change their full name or assign a title. Contact an administrator to correct your legal name.",
                WorkflowErrorCodes.StudentIdentityLocked);
        }

        var user = await repository.UpdateProfileAsync(
            userId,
            new UpdateProfileData(
                isPureStudent ? existing.FullName : trimmedFullName,
                NormalizeOptional(request.Phone),
                isPureStudent ? existing.Title : normalizedTitle),
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

public sealed record UpdateUserAccountCommand(
    long UserId,
    string? FullName,
    string? Phone,
    string? Title,
    string? StudentCode,
    string? EmployeeCode,
    string? ConcurrencyToken) : IRequest<UserAccountDto>;

public sealed class UpdateUserAccountCommandHandler(
    IUserAccountRepository userRepository,
    AccountSecurityAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider,
    Microsoft.Extensions.Options.IOptions<AIPMS.Application.Common.Options.ExecutionConcurrencySettings>? concurrencyOptions = null) : IRequestHandler<UpdateUserAccountCommand, UserAccountDto>
{
    public async Task<UserAccountDto> Handle(
        UpdateUserAccountCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        if (concurrencyOptions?.Value.RequireTokens == true && string.IsNullOrWhiteSpace(request.ConcurrencyToken))
        {
            throw new ConflictException(
                "A current concurrencyToken is required. Reload before saving.",
                WorkflowErrorCodes.StaleConcurrencyToken);
        }
        var user = await userRepository.GetUserAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        if (!string.IsNullOrEmpty(request.ConcurrencyToken)
            && !string.Equals(user.ConcurrencyToken, request.ConcurrencyToken, StringComparison.Ordinal))
        {
            throw new ConflictException(
                "The user account was modified by another administrator.",
                WorkflowErrorCodes.StaleConcurrencyToken);
        }

        var nextFullName = string.IsNullOrWhiteSpace(request.FullName) ? user.FullName : request.FullName.Trim();
        var nextPhone = request.Phone is null ? user.Phone : NormalizeOptional(request.Phone);
        var nextTitle = request.Title is null ? user.Title : NormalizeOptional(request.Title);
        var nextStudentCode = request.StudentCode is null ? user.StudentCode : NormalizeCode(request.StudentCode);
        var nextEmployeeCode = request.EmployeeCode is null ? user.EmployeeCode : NormalizeCode(request.EmployeeCode);

        var studentCodeChanged = !string.Equals(user.StudentCode, nextStudentCode, StringComparison.OrdinalIgnoreCase);
        var employeeCodeChanged = !string.Equals(user.EmployeeCode, nextEmployeeCode, StringComparison.OrdinalIgnoreCase);
        if ((studentCodeChanged && nextStudentCode is not null) || (employeeCodeChanged && nextEmployeeCode is not null))
        {
            if (await userRepository.IdentityExistsExcludingUserAsync(
                request.UserId,
                studentCodeChanged ? nextStudentCode : null,
                employeeCodeChanged ? nextEmployeeCode : null,
                cancellationToken))
            {
                throw new ConflictException("Student code or employee code already exists for another account.");
            }
        }

        var updated = await userRepository.UpdateAccountAsync(
            request.UserId,
            new UpdateAccountData(nextFullName, nextPhone, nextTitle, nextStudentCode, nextEmployeeCode),
            request.ConcurrencyToken,
            timeProvider.GetUtcNow().UtcDateTime,
            async updatedUser =>
            {
                await auditTrail.RecordAsync(
                    new AuditEntry(
                        accessService.ActorUserId,
                        "ACCOUNT_UPDATED",
                        "USER",
                        updatedUser.Id,
                        new Dictionary<string, object?>
                        {
                            ["previous"] = new Dictionary<string, object?>
                            {
                                ["fullName"] = user.FullName,
                                ["phone"] = user.Phone,
                                ["title"] = user.Title,
                                ["studentCode"] = user.StudentCode,
                                ["employeeCode"] = user.EmployeeCode
                            },
                            ["current"] = new Dictionary<string, object?>
                            {
                                ["fullName"] = updatedUser.FullName,
                                ["phone"] = updatedUser.Phone,
                                ["title"] = updatedUser.Title,
                                ["studentCode"] = updatedUser.StudentCode,
                                ["employeeCode"] = updatedUser.EmployeeCode
                            }
                        }),
                    cancellationToken);
            },
            cancellationToken);

        return updated.ToDto();
    }

    private static string? NormalizeCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SetUserStatusCommand(
    long UserId,
    string Status,
    string? Reason = null,
    bool? AcknowledgeObligations = null)
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
        var isLocking = !string.Equals(status, "ACTIVE", StringComparison.Ordinal);

        if (isLocking
            && string.Equals(user.Status, "ACTIVE", StringComparison.Ordinal)
            && user.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal)
            && await repository.CountActiveUsersInRoleAsync(
                AppRoles.Admin, cancellationToken) <= 1)
        {
            throw new ConflictException(
                "The last active System Administrator cannot be deactivated or suspended.");
        }

        UserActiveObligationsDto? obligations = null;
        if (isLocking)
        {
            obligations = await repository.GetActiveObligationsAsync(request.UserId, cancellationToken);
            if (obligations.TotalCount > 0 && request.AcknowledgeObligations == false)
            {
                throw new ConflictException(
                    "This account has active academic obligations. Explicit acknowledgement is required before deactivating or suspending.",
                    WorkflowErrorCodes.ActiveObligationsAcknowledgementRequired);
            }
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var updated = await repository.SetStatusAsync(
            request.UserId,
            status,
            utcNow,
            cancellationToken);

        var trimmedReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var details = new Dictionary<string, object?>
        {
            ["previousStatus"] = user.Status,
            ["status"] = updated.Status,
            ["reason"] = trimmedReason
        };
        if (obligations is not null && obligations.TotalCount > 0)
        {
            details["obligationsSummary"] = new Dictionary<string, object?>
            {
                ["totalCount"] = obligations.TotalCount,
                ["teamMemberships"] = obligations.TeamMemberships.Count,
                ["supervisions"] = obligations.Supervisions.Count,
                ["gateReviews"] = obligations.GateReviews.Count,
                ["evaluationsAndAppeals"] = obligations.EvaluationsAndAppeals.Count,
                ["impactedDepartmentIds"] = obligations.ImpactedDepartmentIds
            };
            await repository.NotifyDepartmentsOfAccountLockAsync(
                accessService.ActorUserId,
                updated.Id,
                updated.FullName,
                updated.Email,
                updated.Status,
                trimmedReason,
                obligations,
                utcNow,
                cancellationToken);
        }

        await auditTrail.RecordAsync(
            new AuditEntry(
                accessService.ActorUserId,
                "ACCOUNT_STATUS_CHANGED",
                "USER",
                updated.Id,
                details),
            cancellationToken);

        return updated.ToDto();
    }
}

public sealed record IssueTemporaryPasswordCommand(long UserId, string? Reason = null)
    : IRequest<TemporaryPasswordResultDto>;

public sealed class IssueTemporaryPasswordCommandHandler(
    IUserAccountRepository repository,
    AccountSecurityAccessService accessService,
    IPasswordHashingService passwordHashingService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : IRequestHandler<IssueTemporaryPasswordCommand, TemporaryPasswordResultDto>
{
    public async Task<TemporaryPasswordResultDto> Handle(
        IssueTemporaryPasswordCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var user = await repository.GetUserAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        var tempPassword = GenerateStrongPassword(12);
        var hash = passwordHashingService.Hash(tempPassword);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        await repository.IssueTemporaryPasswordAsync(user.Id, hash, utcNow, cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                accessService.ActorUserId,
                "ACCOUNT_TEMPORARY_PASSWORD_ISSUED",
                "USER",
                user.Id,
                new Dictionary<string, object?>
                {
                    ["reason"] = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
                    ["mustChangePassword"] = true
                }),
            cancellationToken);

        return new TemporaryPasswordResultDto(user.Id, tempPassword, true, null);
    }

    public static string GenerateStrongPassword(int length)
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@#$%^&*";
        const string all = upper + lower + digits + special;

        var chars = new char[Math.Max(length, 12)];
        chars[0] = upper[System.Security.Cryptography.RandomNumberGenerator.GetInt32(upper.Length)];
        chars[1] = lower[System.Security.Cryptography.RandomNumberGenerator.GetInt32(lower.Length)];
        chars[2] = digits[System.Security.Cryptography.RandomNumberGenerator.GetInt32(digits.Length)];
        chars[3] = special[System.Security.Cryptography.RandomNumberGenerator.GetInt32(special.Length)];
        for (var i = 4; i < chars.Length; i++)
            chars[i] = all[System.Security.Cryptography.RandomNumberGenerator.GetInt32(all.Length)];
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = System.Security.Cryptography.RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }
}

public sealed record AssignUserRoleCommand(long UserId, long RoleId) : IRequest;

internal static class GlobalAccountRoleGuard
{
    internal static void RequireAssignable(string code)
    {
        if (code is not (AppRoles.Admin or AppRoles.DepartmentStaff or AppRoles.Lecturer or AppRoles.Student))
            throw new ConflictException("Resource roles must be assigned through their project or evaluation workflow.");
    }
}

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

        GlobalAccountRoleGuard.RequireAssignable(role.Code);

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
