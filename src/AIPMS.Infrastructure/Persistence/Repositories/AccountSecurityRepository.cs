using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AccountSecurity.Abstractions;
using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.AccountSecurity.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappers;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services.Projects;
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
    public Task<PagedResult<AccountUser>> GetUsersAsync(
        string? search,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        GetUsersAsync(search, status, page, pageSize, null, null, null, cancellationToken);

    public async Task<PagedResult<AccountUser>> GetUsersAsync(
        string? search,
        string? status,
        int page,
        int pageSize,
        string? role,
        long? departmentId,
        long? majorId,
        CancellationToken cancellationToken = default)
    {
        var query = UserQuery();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(user =>
                user.Email.Contains(search)
                || user.FullName.Contains(search)
                || (user.StudentCode != null && user.StudentCode.Contains(search))
                || (user.EmployeeCode != null && user.EmployeeCode.Contains(search))
                || (user.ClassCode != null && user.ClassCode.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToUpperInvariant();
            query = query.Where(user => user.Status == normalizedStatus);
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            var normalizedRole = role.Trim().ToUpperInvariant();
            query = query.Where(user => user.UserRoleUsers.Any(ur => ur.Role.Code == normalizedRole));
        }

        if (departmentId.HasValue)
        {
            query = query.Where(user => user.DepartmentId == departmentId.Value);
        }

        if (majorId.HasValue)
        {
            query = query.Where(user => user.MajorId == majorId.Value);
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

    public Task<bool> IdentityExistsExcludingUserAsync(
        long userId,
        string? studentCode,
        string? employeeCode,
        CancellationToken cancellationToken = default) =>
        context.Users.AsNoTracking().AnyAsync(
            user => user.Id != userId
                && ((studentCode != null && user.StudentCode == studentCode)
                    || (employeeCode != null && user.EmployeeCode == employeeCode)),
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
            StudentCode = data.StudentCode, CurriculumCode = data.CurriculumCode,
            EmployeeCode = data.EmployeeCode,
            Title = data.Title,
            Status = "ACTIVE",
            AcademicProfileStatus = "PENDING",
            AccessFailedCount = 0,
            PasswordChangedAt = utcNow,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
        context.Users.Add(user);
        await SaveChangesAsync(cancellationToken);

        if (await IsStudentAccountAsync(data.RoleIds, cancellationToken)) context.AcademicProfileVerifications.Add(new() { UserId = user.Id, Status = "PENDING" });
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
        var studentRoleId = await context.Roles.Where(r => r.Code == AppRoles.Student).Select(r => (long?)r.Id).SingleOrDefaultAsync(cancellationToken);
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
                StudentCode = data.StudentCode, CurriculumCode = data.CurriculumCode,
                EmployeeCode = data.EmployeeCode,
                Title = data.Title,
                Status = "ACTIVE",
                AcademicProfileStatus = "PENDING",
                AccessFailedCount = 0,
                PasswordChangedAt = utcNow,
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            }
        }).ToArray();

        context.Users.AddRange(pairs.Select(static pair => pair.Entity));
        await SaveChangesAsync(cancellationToken);
        foreach (var pair in pairs.Where(p => studentRoleId.HasValue && p.Data.RoleIds.Contains(studentRoleId.Value)))
            context.AcademicProfileVerifications.Add(new() { UserId = pair.Entity.Id, Status = "PENDING" });
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

    private Task<bool> IsStudentAccountAsync(IReadOnlyCollection<long> roleIds, CancellationToken ct) =>
        context.Roles.AnyAsync(r => roleIds.Contains(r.Id) && r.Code == AppRoles.Student, ct);

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

    public async Task<AccountUser> UpdateAccountAsync(
        long userId,
        UpdateAccountData data,
        string? concurrencyToken,
        DateTime utcNow,
        Func<AccountUser, Task>? onUpdatedAsync = null,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        var user = await context.Users.SingleAsync(item => item.Id == userId, cancellationToken);
        if (!string.IsNullOrEmpty(concurrencyToken)
            && Convert.ToBase64String(user.RowVersion) != concurrencyToken)
        {
            throw new ConflictException("The user account was modified by another operation. Please refresh and retry.");
        }

        user.FullName = data.FullName;
        user.Phone = data.Phone;
        user.Title = data.Title;
        user.StudentCode = data.StudentCode;
        user.EmployeeCode = data.EmployeeCode;
        user.UpdatedAt = utcNow;
        await SaveChangesAsync(cancellationToken);

        var updated = (await GetUserAsync(userId, cancellationToken))!;

        if (onUpdatedAsync != null)
        {
            await onUpdatedAsync(updated);
        }

        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return updated;
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

    public async Task<UserActiveObligationsDto> GetActiveObligationsAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        var userScope = await context.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.DepartmentId,
                DepartmentName = u.Department == null ? null : u.Department.Name
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("User", userId);

        var activeTeamRows = await context.TeamMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.LeftAt == null && m.Team.Status != "DISBANDED")
            .Select(m => new
            {
                m.TeamId,
                TeamCode = m.Team.Code,
                TeamStatus = m.Team.Status,
                m.IsLeader,
                Project = context.Projects
                    .Where(p => p.TeamId == m.TeamId)
                    .OrderByDescending(p => p.Id)
                    .Select(p => new
                    {
                        p.Id,
                        p.Code,
                        p.Title,
                        p.Status,
                        LeadDeptId = p.ProjectMajors.OrderBy(pm => pm.Id)
                            .Select(pm => (long?)pm.Major.DepartmentId).FirstOrDefault(),
                        LeadDeptName = p.ProjectMajors.OrderBy(pm => pm.Id)
                            .Select(pm => pm.Major.Department.Name).FirstOrDefault()
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var teamObligations = activeTeamRows
            .Where(r => r.Project is null || r.Project.Status is not ("COMPLETED" or "ARCHIVED" or "REJECTED"))
            .Select(r => new UserObligationItemDto(
                "TEAM_MEMBER",
                r.Project?.Id ?? r.TeamId,
                r.Project?.Code ?? r.TeamCode,
                r.Project?.Title,
                r.IsLeader ? "TEAM_LEADER" : "TEAM_MEMBER",
                r.Project?.Status ?? r.TeamStatus,
                r.Project?.LeadDeptId ?? userScope.DepartmentId,
                r.Project?.LeadDeptName ?? userScope.DepartmentName))
            .ToList();

        var supervisionRows = await context.SupervisorAssignments.AsNoTracking()
            .Where(a => a.SupervisorProfile.UserId == userId
                && a.EndedAt == null
                && a.Project.Status != "COMPLETED"
                && a.Project.Status != "ARCHIVED"
                && a.Project.Status != "REJECTED")
            .Select(a => new
            {
                a.Id,
                a.ProjectId,
                ProjectCode = a.Project.Code,
                ProjectTitle = a.Project.Title,
                ProjectStatus = a.Project.Status,
                a.AssignmentType,
                LeadDeptId = a.Project.ProjectMajors.OrderBy(pm => pm.Id)
                    .Select(pm => (long?)pm.Major.DepartmentId).FirstOrDefault(),
                LeadDeptName = a.Project.ProjectMajors.OrderBy(pm => pm.Id)
                    .Select(pm => pm.Major.Department.Name).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var supervisionObligations = supervisionRows
            .Select(s => new UserObligationItemDto(
                "SUPERVISION",
                s.ProjectId,
                s.ProjectCode,
                s.ProjectTitle,
                s.AssignmentType,
                s.ProjectStatus,
                s.LeadDeptId ?? userScope.DepartmentId,
                s.LeadDeptName ?? userScope.DepartmentName))
            .ToList();

        var gateRows = await context.Set<GateReviewerAssignment>().AsNoTracking()
            .Where(a => a.ReviewerId == userId
                && a.Status == "ACTIVE"
                && a.GateInstance.Status != "PASSED"
                && a.GateInstance.Status != "WAIVED"
                && a.GateInstance.Project.Status != "COMPLETED"
                && a.GateInstance.Project.Status != "ARCHIVED")
            .Select(a => new
            {
                a.Id,
                a.GateInstance.ProjectId,
                ProjectCode = a.GateInstance.Project.Code,
                ProjectTitle = a.GateInstance.Project.Title,
                GateCode = a.GateInstance.Code,
                a.Scope,
                GateStatus = a.GateInstance.Status,
                LeadDeptId = a.GateInstance.Project.ProjectMajors.OrderBy(pm => pm.Id)
                    .Select(pm => (long?)pm.Major.DepartmentId).FirstOrDefault(),
                LeadDeptName = a.GateInstance.Project.ProjectMajors.OrderBy(pm => pm.Id)
                    .Select(pm => pm.Major.Department.Name).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var gateObligations = gateRows
            .Select(g => new UserObligationItemDto(
                "GATE_REVIEW",
                g.ProjectId,
                g.ProjectCode,
                g.ProjectTitle,
                $"{g.GateCode}:{g.Scope}",
                g.GateStatus,
                g.LeadDeptId ?? userScope.DepartmentId,
                g.LeadDeptName ?? userScope.DepartmentName))
            .ToList();

        var evalRows = await (
            from assignment in context.Set<EvaluationAssignment>().AsNoTracking()
            join project in context.Projects.AsNoTracking() on assignment.ProjectId equals project.Id
            where assignment.EvaluatorId == userId
                && assignment.Status == "ACTIVE"
                && project.Status != "COMPLETED"
                && project.Status != "ARCHIVED"
                && !context.Set<EvaluationDraftState>().Any(d =>
                    d.AssignmentId == assignment.Id
                    && context.Evaluations.Any(ev => ev.Id == d.EvaluationId && ev.Status == "FINALIZED"))
            select new
            {
                assignment.Id,
                assignment.ProjectId,
                ProjectCode = project.Code,
                ProjectTitle = project.Title,
                assignment.EvaluationType,
                assignment.Status,
                DepartmentId = assignment.DepartmentId > 0
                    ? (long?)assignment.DepartmentId
                    : project.ProjectMajors.OrderBy(pm => pm.Id)
                        .Select(pm => (long?)pm.Major.DepartmentId).FirstOrDefault(),
                DepartmentName = project.ProjectMajors.OrderBy(pm => pm.Id)
                    .Select(pm => pm.Major.Department.Name).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var evalAndAppealObligations = evalRows
            .Select(e => new UserObligationItemDto(
                "EVALUATION",
                e.ProjectId,
                e.ProjectCode,
                e.ProjectTitle,
                e.EvaluationType,
                e.Status,
                e.DepartmentId ?? userScope.DepartmentId,
                e.DepartmentName ?? userScope.DepartmentName))
            .ToList();

        var appealRows = await (
            from appeal in context.Set<ResultAppealRequest>().AsNoTracking()
            join project in context.Projects.AsNoTracking() on appeal.ProjectId equals project.Id
            where appeal.ReviewerId == userId
                && (appeal.Status == "SUBMITTED" || appeal.Status == "UNDER_REVIEW" || appeal.Status == "ESCALATED")
            select new
            {
                appeal.Id,
                appeal.ProjectId,
                ProjectCode = project.Code,
                ProjectTitle = project.Title,
                appeal.Scope,
                appeal.Status,
                DepartmentId = (long?)appeal.ResponsibleDepartmentId
            })
            .ToListAsync(cancellationToken);

        foreach (var a in appealRows)
        {
            evalAndAppealObligations.Add(new UserObligationItemDto(
                "APPEAL_REVIEW",
                a.ProjectId,
                a.ProjectCode,
                a.ProjectTitle,
                a.Scope,
                a.Status,
                a.DepartmentId ?? userScope.DepartmentId,
                null));
        }

        var impactedDepts = teamObligations
            .Concat(supervisionObligations)
            .Concat(gateObligations)
            .Concat(evalAndAppealObligations)
            .Where(o => o.DepartmentId.HasValue)
            .Select(o => o.DepartmentId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        var total = teamObligations.Count + supervisionObligations.Count + gateObligations.Count + evalAndAppealObligations.Count;
        if (total > 0 && impactedDepts.Count == 0 && userScope.DepartmentId.HasValue)
            impactedDepts.Add(userScope.DepartmentId.Value);

        return new UserActiveObligationsDto(
            userId,
            total,
            teamObligations,
            supervisionObligations,
            gateObligations,
            evalAndAppealObligations,
            impactedDepts);
    }

    public async Task NotifyDepartmentsOfAccountLockAsync(
        long actorUserId,
        long targetUserId,
        string targetFullName,
        string targetEmail,
        string newStatus,
        string? reason,
        UserActiveObligationsDto obligations,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        if (obligations.TotalCount == 0 || obligations.ImpactedDepartmentIds.Count == 0) return;

        var projectCodes = obligations.TeamMemberships
            .Concat(obligations.Supervisions)
            .Concat(obligations.GateReviews)
            .Concat(obligations.EvaluationsAndAppeals)
            .Select(o => o.ProjectCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
        var codesText = projectCodes.Length > 0 ? string.Join(", ", projectCodes) : $"User #{targetUserId}";
        var reasonText = string.IsNullOrWhiteSpace(reason) ? "Không ghi lý do." : reason;
        var title = $"Cảnh báo khẩn: Tài khoản {targetFullName} đã chuyển trạng thái {newStatus} khi còn {obligations.TotalCount} vai trò hoạt động";
        var content = $"Admin đã chuyển tài khoản {targetFullName} ({targetEmail}) sang trạng thái {newStatus}. "
            + $"Lý do: {reasonText} "
            + $"Ảnh hưởng ({obligations.TotalCount}): Nhóm={obligations.TeamMemberships.Count}, Hướng dẫn={obligations.Supervisions.Count}, Phản biện cổng={obligations.GateReviews.Count}, Chấm/Phúc khảo={obligations.EvaluationsAndAppeals.Count}. "
            + $"Mã đồ án/nhóm liên quan: {codesText}. Vui lòng kiểm tra và thực hiện bàn giao/thay thế kịp thời.";

        var staffIds = new HashSet<long>();
        foreach (var deptId in obligations.ImpactedDepartmentIds)
        {
            var ids = await V4WorkflowNotificationHelper.GetDepartmentStaffIdsAsync(context, deptId, cancellationToken);
            foreach (var id in ids) staffIds.Add(id);
        }

        if (staffIds.Count > 0)
        {
            await V4WorkflowNotificationHelper.EmitAsync(
                context,
                actorUserId,
                "ACCOUNT_LOCKED_WITH_OBLIGATIONS",
                title,
                content,
                "USER",
                targetUserId,
                staffIds,
                utcNow,
                enqueueEmail: true,
                cancellationToken);
        }
    }

    public async Task IssueTemporaryPasswordAsync(
        long userId,
        string passwordHash,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var user = await context.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User", userId);

        var normalized = new DateTime(utcNow.Ticks - (utcNow.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        var nextVersion = user.PasswordChangedAt is not null && user.PasswordChangedAt.Value >= normalized
            ? user.PasswordChangedAt.Value.AddSeconds(1)
            : normalized;

        user.PasswordHash = passwordHash;
        user.PasswordChangedAt = nextVersion;
        user.MustChangePassword = true;
        user.GoogleEnrollmentPending = false;
        user.AccessFailedCount = 0;
        user.LockoutEndAt = null;
        user.UpdatedAt = utcNow;

        await context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, utcNow),
                cancellationToken);

        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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

    public Task<bool> IsActiveAdministratorAsync(long userId, CancellationToken cancellationToken = default) =>
        context.UserRoles.AsNoTracking().AnyAsync(
            assignment => assignment.UserId == userId
                && assignment.Role.Code == AppRoles.Admin
                && assignment.User.Status == "ACTIVE",
            cancellationToken);

    public async Task<PagedResult<AccessEvent>> GetAccessEventsAsync(
        string? search,
        long? userId,
        string? eventType,
        string? outcome,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.AuditLogs.AsNoTracking()
            .Where(item => item.Action == "AUTH_LOGIN"
                || item.Action == "AUTH_GOOGLE_LOGIN"
                || item.Action == "AUTH_LOGOUT");
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(item => item.ActorUser != null
                && (item.ActorUser.FullName.Contains(search) || item.ActorUser.Email.Contains(search)));
        if (userId.HasValue) query = query.Where(item => item.ActorUserId == userId.Value);
        if (eventType == "LOGIN") query = query.Where(item => item.Action == "AUTH_LOGIN" || item.Action == "AUTH_GOOGLE_LOGIN");
        if (eventType == "LOGOUT") query = query.Where(item => item.Action == "AUTH_LOGOUT");
        if (!string.IsNullOrWhiteSpace(outcome)) query = query.Where(item => item.Outcome == outcome);
        if (fromUtc.HasValue) query = query.Where(item => item.OccurredAt >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(item => item.OccurredAt <= toUtc.Value);

        var totalCount = await query.LongCountAsync(cancellationToken);
        var records = await query.Include(static item => item.ActorUser)
            .OrderByDescending(static item => item.OccurredAt)
            .ThenByDescending(static item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AccessEvent>(
            records.Select(static item => new AccessEvent(
                item.Id,
                item.ActorUserId,
                item.ActorUser?.FullName,
                item.ActorUser?.Email,
                item.Action == "AUTH_LOGOUT" ? "LOGOUT" : "LOGIN",
                item.Action == "AUTH_GOOGLE_LOGIN" ? "GOOGLE" : item.Action == "AUTH_LOGIN" ? "PASSWORD" : "UNKNOWN",
                item.Outcome,
                item.IpAddress,
                item.OccurredAt)).ToArray(),
            page,
            pageSize,
            totalCount);
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

        var actorIds = records.Where(r => r.ActorUserId.HasValue).Select(r => r.ActorUserId!.Value).Distinct().ToArray();
        var userEntityIds = ParseEntityIds(records, "USER");
        var allUserIds = actorIds.Concat(userEntityIds).Distinct().ToArray();
        var userInfos = allUserIds.Length == 0 ? new Dictionary<long, (string FullName, string Email)>()
            : (await context.Users.AsNoTracking().Where(u => allUserIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.Email }).ToArrayAsync(cancellationToken))
                .ToDictionary(u => u.Id, u => (u.FullName, u.Email));

        var projectIds = ParseEntityIds(records, "PROJECT");
        var projectNames = projectIds.Length == 0 ? new Dictionary<long, string>()
            : (await context.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Code, p.Title }).ToArrayAsync(cancellationToken))
                .ToDictionary(p => p.Id, p => string.IsNullOrWhiteSpace(p.Title) ? p.Code : $"{p.Code} - {p.Title}");

        var teamIds = ParseEntityIds(records, "TEAM");
        var teamNames = teamIds.Length == 0 ? new Dictionary<long, string>()
            : await context.Teams.AsNoTracking().Where(t => teamIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        var roleIds = ParseEntityIds(records, "ROLE");
        var roleNames = roleIds.Length == 0 ? new Dictionary<long, string>()
            : (await context.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Name, r.Code }).ToArrayAsync(cancellationToken))
                .ToDictionary(r => r.Id, r => $"{r.Name} ({r.Code})");

        var permissionIds = ParseEntityIds(records, "PERMISSION");
        var permissionNames = permissionIds.Length == 0 ? new Dictionary<long, string>()
            : (await context.Permissions.AsNoTracking().Where(p => permissionIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name, p.Code }).ToArrayAsync(cancellationToken))
                .ToDictionary(p => p.Id, p => $"{p.Name} ({p.Code})");

        var taskIds = ParseEntityIds(records, "TASK");
        var taskNames = taskIds.Length == 0 ? new Dictionary<long, string>()
            : await context.Tasks.AsNoTracking().Where(t => taskIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Title, cancellationToken);

        var milestoneIds = ParseEntityIds(records, "MILESTONE");
        var milestoneNames = milestoneIds.Length == 0 ? new Dictionary<long, string>()
            : await context.Milestones.AsNoTracking().Where(m => milestoneIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Title, cancellationToken);

        var deliverableIds = ParseEntityIds(records, "DELIVERABLE");
        var deliverableNames = deliverableIds.Length == 0 ? new Dictionary<long, string>()
            : await context.Deliverables.AsNoTracking().Where(d => deliverableIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Title, cancellationToken);

        var meetingIds = ParseEntityIds(records, "MEETING");
        var meetingNames = meetingIds.Length == 0 ? new Dictionary<long, string>()
            : await context.Meetings.AsNoTracking().Where(m => meetingIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Title, cancellationToken);

        var departmentIds = ParseEntityIds(records, "DEPARTMENT");
        var departmentNames = departmentIds.Length == 0 ? new Dictionary<long, string>()
            : await context.Departments.AsNoTracking().Where(d => departmentIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        var majorIds = ParseEntityIds(records, "MAJOR");
        var majorNames = majorIds.Length == 0 ? new Dictionary<long, string>()
            : await context.Majors.AsNoTracking().Where(m => majorIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Name, cancellationToken);

        var semesterIds = ParseEntityIds(records, "ACADEMIC_SEMESTER");
        var semesterNames = semesterIds.Length == 0 ? new Dictionary<long, string>()
            : await context.AcademicSemesters.AsNoTracking().Where(s => semesterIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        var periodIds = ParseEntityIds(records, "PROJECT_PERIOD");
        var periodNames = periodIds.Length == 0 ? new Dictionary<long, string>()
            : await context.ProjectPeriods.AsNoTracking().Where(p => periodIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var rubricIds = ParseEntityIds(records, "RUBRIC");
        var rubricNames = rubricIds.Length == 0 ? new Dictionary<long, string>()
            : await context.Rubrics.AsNoTracking().Where(r => rubricIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        string? ResolveEntityDisplayName(string entityType, string? entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId) || !long.TryParse(entityId, out var id)) return null;
            return entityType.ToUpperInvariant() switch
            {
                "USER" => userInfos.TryGetValue(id, out var u) ? (string.IsNullOrWhiteSpace(u.FullName) ? u.Email : $"{u.FullName} ({u.Email})") : null,
                "PROJECT" => projectNames.GetValueOrDefault(id),
                "TEAM" => teamNames.GetValueOrDefault(id),
                "ROLE" => roleNames.GetValueOrDefault(id),
                "PERMISSION" => permissionNames.GetValueOrDefault(id),
                "TASK" => taskNames.GetValueOrDefault(id),
                "MILESTONE" => milestoneNames.GetValueOrDefault(id),
                "DELIVERABLE" => deliverableNames.GetValueOrDefault(id),
                "MEETING" => meetingNames.GetValueOrDefault(id),
                "DEPARTMENT" => departmentNames.GetValueOrDefault(id),
                "MAJOR" => majorNames.GetValueOrDefault(id),
                "ACADEMIC_SEMESTER" => semesterNames.GetValueOrDefault(id),
                "PROJECT_PERIOD" => periodNames.GetValueOrDefault(id),
                "RUBRIC" => rubricNames.GetValueOrDefault(id),
                _ => null
            };
        }

        return new PagedResult<AuditRecord>(
            records.Select(item => item.ToApplication() with
            {
                ActorName = item.ActorUserId.HasValue && userInfos.TryGetValue(item.ActorUserId.Value, out var actor) ? actor.FullName : null,
                EntityDisplayName = ResolveEntityDisplayName(item.EntityType, item.EntityId)
            }).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    private static long[] ParseEntityIds(IReadOnlyList<AIPMS.Infrastructure.Persistence.Generated.Models.AuditLog> records, string entityType) =>
        records.Where(r => string.Equals(r.EntityType, entityType, StringComparison.OrdinalIgnoreCase)
                && long.TryParse(r.EntityId, out _))
            .Select(r => long.Parse(r.EntityId!))
            .Distinct()
            .ToArray();

    private IQueryable<UserEntity> UserQuery() =>
        context.Users.AsNoTracking()
            .Include(static user => user.Specialization)
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
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The user account was modified by another operation. Please refresh and retry.");
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2601 or 2627 or 547 or 1205 or 1222 })
        {
            throw new ConflictException("The account security change conflicts with existing data.");
        }
    }
}
