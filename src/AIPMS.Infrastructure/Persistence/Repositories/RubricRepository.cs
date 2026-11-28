using System;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services.Projects;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class RubricRepository(AipmsDbContext context) : IRubricRepository
{
    public async Task<RubricActor?> GetActorAsync(long userId, CancellationToken ct)
    {
        var user = await context.Users.AsNoTracking().Where(u => u.Id == userId && u.Status == "ACTIVE")
            .Select(u => new
            {
                u.Id, u.DepartmentId,
                Admin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"),
                Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"),
                OrganizationId = u.Department != null ? (long?)u.Department.OrganizationId : null,
                ActiveScope = u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
            }).SingleOrDefaultAsync(ct);
        if (user is null || (!user.Admin && (!user.Staff || !user.ActiveScope))) return null;
        return new(user.Id, user.Admin, user.DepartmentId, user.OrganizationId, user.Staff && user.ActiveScope);
    }

    public async Task<RubricScope?> GetScopeAsync(long departmentId, long semesterId, CancellationToken ct) =>
        await (from department in context.Departments.AsNoTracking()
               from semester in context.AcademicSemesters.AsNoTracking()
               where department.Id == departmentId && semester.Id == semesterId
                   && department.IsActive && department.Organization.IsActive
                   && department.OrganizationId == semester.OrganizationId
               select new RubricScope(department.Id, department.OrganizationId, semester.Status)).SingleOrDefaultAsync(ct);

    private IQueryable<M.Rubric> Visible(RubricActor actor)
    {
        if (actor.IsAdmin) return context.Rubrics.AsNoTracking();
        if (!actor.DepartmentId.HasValue) return context.Rubrics.AsNoTracking().Where(r => false);
        var deptId = actor.DepartmentId.Value;
        return context.Rubrics.AsNoTracking().Where(r =>
            r.Department != null && r.Department.IsActive && r.Department.Organization.IsActive
            && r.AcademicSemester != null && r.AcademicSemester.OrganizationId == r.Department.OrganizationId
            && (
                r.DepartmentId == deptId
                || (r.ProjectMode == "INTERDISCIPLINARY" && r.Department.Organization.Departments.Any(d => d.Id == deptId && d.IsActive))
            ));
    }

    public Task<PagedResult<RubricRecord>> ListAsync(RubricActor actor, RubricFilter filter, CancellationToken ct) => Transaction(async () =>
    {
        // Keep content and its edit token from the same committed version across the reads.
        var query = Visible(actor);
        if (filter.DepartmentId.HasValue) query = query.Where(r => r.DepartmentId == filter.DepartmentId);
        if (filter.AcademicSemesterId.HasValue) query = query.Where(r => r.AcademicSemesterId == filter.AcademicSemesterId);
        if (filter.MajorId.HasValue) query = query.Where(r => r.MajorId == filter.MajorId);
        if (!string.IsNullOrWhiteSpace(filter.ProjectMode)) query = query.Where(r => r.ProjectMode == filter.ProjectMode);
        if (!string.IsNullOrWhiteSpace(filter.EvaluationStage)) query = query.Where(r => r.EvaluationStage == filter.EvaluationStage);
        if (!string.IsNullOrWhiteSpace(filter.EvaluationScope)) query = query.Where(r => r.EvaluationScope == filter.EvaluationScope);
        if (!string.IsNullOrWhiteSpace(filter.Search)) query = query.Where(r => r.Code.Contains(filter.Search) || r.Name.Contains(filter.Search));
        if (filter.Status is not null)
            query = query.Where(r => context.Set<RubricVersion>().Any(v => v.RubricId == r.Id && v.Status == filter.Status));
        var count = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(r => r.Id).Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Include(r => r.RubricCriteria).ThenInclude(c => c.Criterion)
            .Include(r => r.RubricCriteria).ThenInclude(c => c.Levels).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToArray();
        var versions = await context.Set<RubricVersion>().AsNoTracking().Where(v => ids.Contains(v.RubricId)).ToDictionaryAsync(v => v.RubricId, ct);
        var referenced = await context.Rubrics.Where(r => ids.Contains(r.Id) &&
                (r.Evaluations.Any() || r.ProjectPeriods.Any() || r.RubricCriteria.Any(c => c.EvaluationDetails.Any())))
            .Select(r => r.Id).ToListAsync(ct);
        return new PagedResult<RubricRecord>(rows.Select(r => Map(r, versions.GetValueOrDefault(r.Id), referenced.Contains(r.Id))).ToArray(), filter.Page, filter.PageSize, count);
    }, System.Data.IsolationLevel.RepeatableRead, ct);

    public Task<RubricRecord?> GetAsync(long id, RubricActor actor, bool forUpdate, CancellationToken ct) => forUpdate
        ? GetCore(id, actor, true, ct)
        : Transaction(() => GetCore(id, actor, false, ct), System.Data.IsolationLevel.RepeatableRead, ct);

    private async Task<RubricRecord?> GetCore(long id, RubricActor actor, bool forUpdate, CancellationToken ct)
    {
        if (!await Visible(actor).AnyAsync(r => r.Id == id, ct)) return null;
        if (forUpdate)
        {
            // All family mutations take the root lock first, avoiding sibling-version lock inversion.
            var root = await context.Set<RubricVersion>().Where(v => v.RubricId == id).Select(v => (long?)v.RootRubricId).SingleOrDefaultAsync(ct);
            if (root is null) throw new ConflictException("Rubric version metadata is missing. Apply the rubric migration before managing legacy rows.");
            await Lock(root.Value, ct);
            if (root != id) await Lock(id, ct);
        }
        return await Read(id, ct);
    }

    private async Task Lock(long id, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null) throw new InvalidOperationException("Rubric mutation requires a transaction.");
        var row = await context.Rubrics.FromSqlInterpolated($"SELECT * FROM dbo.rubrics WITH (XLOCK, HOLDLOCK) WHERE id = {id}")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (row is null) throw new NotFoundException("Rubric", id);
    }

    private async Task<RubricRecord> Read(long id, CancellationToken ct)
    {
        var row = await context.Rubrics.AsNoTracking()
            .Include(r => r.RubricCriteria).ThenInclude(c => c.Criterion)
            .Include(r => r.RubricCriteria).ThenInclude(c => c.Levels)
            .SingleAsync(r => r.Id == id, ct);
        var version = await context.Set<RubricVersion>().AsNoTracking().SingleOrDefaultAsync(v => v.RubricId == id, ct);
        var referenced = await context.Rubrics.Where(r => r.Id == id).AnyAsync(r => r.Evaluations.Any()
            || r.ProjectPeriods.Any() || r.RubricCriteria.Any(c => c.EvaluationDetails.Any()), ct);
        return Map(row, version, referenced);
    }

    private static RubricRecord Map(M.Rubric r, RubricVersion? v, bool referenced) => new(r.Id, r.DepartmentId,
        r.AcademicSemesterId, r.Code, r.Name, r.Description,
        v?.Status ?? (r.IsActive ? RubricStatuses.Published : RubricStatuses.Retired),
        v?.RootRubricId ?? r.Id, v?.VersionNumber ?? 1, v?.ConcurrencyToken.ToString("N") ?? "", referenced,
        r.CreatedAt, r.UpdatedAt, r.RubricCriteria.OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new RubricCriterionRecord(c.Id, c.CriterionId, c.Criterion.Name, c.Criterion.Description,
                c.WeightPercent, c.MaxScore, c.SortOrder, c.IsRequired)
            {
                ParentId = c.ParentId,
                CriterionCode = c.Criterion.Code,
                LearningOutcome = c.LearningOutcome,
                EvidenceGuidance = c.EvidenceGuidance,
                TargetScope = c.TargetScope,
                IsIntegration = c.IsIntegration,
                Levels = c.Levels.OrderBy(l => l.LevelOrder)
                    .Select(l => new RubricCriterionLevelDto(l.LevelOrder, l.Label, l.Descriptor, l.ScoreFrom, l.ScoreTo))
                    .ToArray()
            }).ToArray())
    {
        MajorId = r.MajorId,
        ProjectMode = r.ProjectMode,
        EvaluationStage = r.EvaluationStage,
        EvaluationScope = r.EvaluationScope,
        EffectiveFrom = r.EffectiveFrom,
        EffectiveTo = r.EffectiveTo,
        ApprovalReference = r.ApprovalReference,
        ParticipatingDepartmentId = r.ParticipatingDepartmentId
    };

    public async Task<RubricRecord> CreateAsync(long departmentId, long semesterId, string code, string name,
        string? description, IReadOnlyList<RubricCriterionInput> criteria, long actorId, DateTime now,
        long? sourceId, long? majorId, string? projectMode, string? evaluationStage, string? evaluationScope,
        DateOnly? effectiveFrom, DateOnly? effectiveTo, CancellationToken ct, long? participatingDepartmentId = null)
    {
        var source = sourceId.HasValue
            ? await context.Set<RubricVersion>().SingleAsync(v => v.RubricId == sourceId.Value, ct) : null;
        var versionNumber = source is null ? 1 : 1 + await context.Set<RubricVersion>()
            .Where(v => v.RootRubricId == source.RootRubricId).MaxAsync(v => v.VersionNumber, ct);
        var row = new M.Rubric
        {
            DepartmentId = departmentId,
            ParticipatingDepartmentId = participatingDepartmentId,
            AcademicSemesterId = semesterId,
            Code = code,
            Name = name,
            Description = description,
            MajorId = majorId,
            ProjectMode = projectMode,
            EvaluationStage = evaluationStage,
            EvaluationScope = evaluationScope,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            ApprovalReference = null,
            IsActive = false,
            CreatedBy = actorId,
            CreatedAt = now,
            UpdatedAt = now,
            RubricCriteria = Criteria(criteria, now)
        };
        context.Rubrics.Add(row);
        await context.SaveChangesAsync(ct);
        context.Set<RubricVersion>().Add(new RubricVersion
        {
            RubricId = row.Id,
            RootRubricId = source?.RootRubricId ?? row.Id,
            VersionNumber = versionNumber,
            Status = RubricStatuses.Draft,
            ConcurrencyToken = Guid.NewGuid()
        });
        await context.SaveChangesAsync(ct);
        return await Read(row.Id, ct);
    }

    private static List<M.RubricCriterion> Criteria(IReadOnlyList<RubricCriterionInput> input, DateTime now)
    {
        var result = new List<M.RubricCriterion>();
        var pending = new Queue<(RubricCriterionInput Input, M.RubricCriterion? Parent)>();
        foreach (var root in input) pending.Enqueue((root, null));
        while (pending.TryDequeue(out var item))
        {
            var c = item.Input;
            var row = new M.RubricCriterion
            {
                Parent = item.Parent,
                WeightPercent = c.WeightPercent,
                MaxScore = c.MaxScore,
                SortOrder = c.SortOrder,
                IsRequired = c.IsRequired,
                LearningOutcome = c.LearningOutcome?.Trim(),
                EvidenceGuidance = c.EvidenceGuidance?.Trim(),
                TargetScope = c.TargetScope?.Trim(),
                IsIntegration = c.IsIntegration,
                CreatedAt = now,
                UpdatedAt = now,
                // Own definitions for every version, including every descendant.
                Criterion = new M.EvaluationCriterion
                {
                    Code = "RC_" + Guid.NewGuid().ToString("N"),
                    Name = c.Name.Trim(),
                    Description = c.Description?.Trim(),
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                Levels = c.Levels.Select(l => new RubricCriterionLevel
                {
                    LevelOrder = l.LevelOrder,
                    Label = l.Label.Trim(),
                    Descriptor = l.Descriptor.Trim(),
                    ScoreFrom = l.ScoreFrom,
                    ScoreTo = l.ScoreTo,
                    CreatedAt = now,
                    UpdatedAt = now
                }).ToList()
            };
            result.Add(row);
            foreach (var child in c.Children) pending.Enqueue((child, row));
        }
        return result;
    }

    private async Task RemoveCriteria(long id, CancellationToken ct)
    {
        var old = await context.RubricCriteria.AsNoTracking().Where(c => c.RubricId == id).ToListAsync(ct);
        var ids = old.Select(c => c.CriterionId).ToArray();
        // A single set-based delete removes descendants and roots together while satisfying
        // the self-reference; deleting tracked rows individually can violate the parent FK.
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.rubric_criteria WHERE rubric_id = {id}", ct);
        var unused = await context.EvaluationCriteria.Where(c => ids.Contains(c.Id) && !c.RubricCriteria.Any()).ToListAsync(ct);
        context.EvaluationCriteria.RemoveRange(unused);
        await context.SaveChangesAsync(ct);
    }

    public async Task<RubricRecord> UpdateAsync(long id, string name, string? description,
        IReadOnlyList<RubricCriterionInput> criteria, DateTime now,
        long? majorId, string? projectMode, string? evaluationStage, string? evaluationScope,
        DateOnly? effectiveFrom, DateOnly? effectiveTo, CancellationToken ct, long? participatingDepartmentId = null)
    {
        await RemoveCriteria(id, ct);
        var row = await context.Rubrics.SingleAsync(r => r.Id == id, ct);
        row.Name = name;
        row.Description = description;
        row.MajorId = majorId;
        row.ParticipatingDepartmentId = participatingDepartmentId;
        row.ProjectMode = projectMode;
        row.EvaluationStage = evaluationStage;
        row.EvaluationScope = evaluationScope;
        row.EffectiveFrom = effectiveFrom;
        row.EffectiveTo = effectiveTo;
        row.UpdatedAt = now;
        row.RubricCriteria = Criteria(criteria, now);
        var version = await context.Set<RubricVersion>().SingleAsync(v => v.RubricId == id, ct);
        version.ConcurrencyToken = Guid.NewGuid();
        await context.SaveChangesAsync(ct);
        return await Read(id, ct);
    }

    public async Task<RubricRecord> SetStatusAsync(long id, string status, DateTime now, CancellationToken ct)
    {
        var row = await context.Rubrics.SingleAsync(r => r.Id == id, ct);
        var version = await context.Set<RubricVersion>().SingleAsync(v => v.RubricId == id, ct);
        row.IsActive = status == RubricStatuses.Published;
        row.UpdatedAt = now;
        version.Status = status;
        version.ConcurrencyToken = Guid.NewGuid();
        await context.SaveChangesAsync(ct);
        return await Read(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        if (await context.Set<RubricVersion>().AnyAsync(v => v.RootRubricId == id && v.RubricId != id, ct))
            throw new ConflictException("A rubric that anchors other versions cannot be deleted.");
        var approvals = await context.RubricApprovals.Where(a => a.RubricId == id).ToListAsync(ct);
        context.RubricApprovals.RemoveRange(approvals);
        await RemoveCriteria(id, ct);
        context.Set<RubricVersion>().Remove(await context.Set<RubricVersion>().SingleAsync(v => v.RubricId == id, ct));
        await context.SaveChangesAsync(ct);
        context.Rubrics.Remove(await context.Rubrics.SingleAsync(r => r.Id == id, ct));
        await context.SaveChangesAsync(ct);
    }

    public async Task<long?> GetMajorDepartmentIdAsync(long majorId, CancellationToken ct) =>
        await context.Majors.AsNoTracking().Where(m => m.Id == majorId).Select(m => (long?)m.DepartmentId).SingleOrDefaultAsync(ct);

    public async Task<RubricApprovalRecord?> GetLatestApprovalAsync(long rubricId, CancellationToken ct)
    {
        var row = await context.RubricApprovals.AsNoTracking()
            .Where(a => a.RubricId == rubricId)
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;
        return new RubricApprovalRecord(row.Id, row.RubricId, row.SubmittedBy, row.SubmittedAt,
            row.DecidedBy, row.DecidedAt, row.Decision, row.Reason,
            row.DepartmentId, row.ScopeRole, row.IsSingleStaffSelfApproval);
    }

    public async Task<IReadOnlyList<RubricApprovalRecord>> GetCurrentRoundApprovalsAsync(long rubricId, CancellationToken ct)
    {
        var latestSubmittedAt = await context.RubricApprovals.AsNoTracking()
            .Where(a => a.RubricId == rubricId)
            .OrderByDescending(a => a.SubmittedAt).ThenByDescending(a => a.Id)
            .Select(a => (DateTime?)a.SubmittedAt)
            .FirstOrDefaultAsync(ct);
        if (!latestSubmittedAt.HasValue) return [];

        var rows = await context.RubricApprovals.AsNoTracking()
            .Where(a => a.RubricId == rubricId && a.SubmittedAt == latestSubmittedAt.Value)
            .OrderBy(a => a.ScopeRole).ThenBy(a => a.Id)
            .ToListAsync(ct);
        return rows.Select(row => new RubricApprovalRecord(
            row.Id, row.RubricId, row.SubmittedBy, row.SubmittedAt,
            row.DecidedBy, row.DecidedAt, row.Decision, row.Reason,
            row.DepartmentId, row.ScopeRole, row.IsSingleStaffSelfApproval)).ToArray();
    }

    public Task<int> CountActiveDepartmentStaffAsync(long departmentId, CancellationToken ct) =>
        context.Users.AsNoTracking()
            .CountAsync(u => u.Status == "ACTIVE"
                && u.DepartmentId == departmentId
                && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"), ct);

    public async Task<bool> DepartmentInSameOrganizationAsync(long departmentIdA, long departmentIdB, CancellationToken ct)
    {
        var orgs = await context.Departments.AsNoTracking()
            .Where(d => (d.Id == departmentIdA || d.Id == departmentIdB) && d.IsActive && d.Organization.IsActive)
            .Select(d => new { d.Id, d.OrganizationId })
            .ToListAsync(ct);
        return orgs.Count == 2 && orgs[0].OrganizationId == orgs[1].OrganizationId;
    }

    public async Task SubmitForApprovalAsync(long rubricId, long actorId, DateTime now, CancellationToken ct)
    {
        var rubric = await context.Rubrics.AsNoTracking().SingleAsync(r => r.Id == rubricId, ct);
        var version = await context.Set<RubricVersion>().SingleAsync(v => v.RubricId == rubricId, ct);
        version.Status = RubricStatuses.PendingApproval;
        version.ConcurrencyToken = Guid.NewGuid();

        var normalizedNow = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        var isDualDepartment = rubric.EvaluationScope == "COMMON"
            && rubric.ProjectMode == "INTERDISCIPLINARY"
            && rubric.ParticipatingDepartmentId.HasValue
            && rubric.ParticipatingDepartmentId != rubric.DepartmentId;

        context.RubricApprovals.Add(new RubricApproval
        {
            RubricId = rubricId,
            DepartmentId = rubric.DepartmentId,
            ScopeRole = "LEAD",
            SubmittedBy = actorId,
            SubmittedAt = normalizedNow
        });

        if (isDualDepartment)
        {
            context.RubricApprovals.Add(new RubricApproval
            {
                RubricId = rubricId,
                DepartmentId = rubric.ParticipatingDepartmentId,
                ScopeRole = "PARTICIPATING",
                SubmittedBy = actorId,
                SubmittedAt = normalizedNow
            });
        }

        await context.SaveChangesAsync(ct);
    }

    public Task ApproveAsync(long rubricId, long approvalId, long actorId, string? approvalReference, DateTime now, CancellationToken ct) =>
        ApproveAsync(rubricId, approvalId, actorId, approvalReference, isSingleStaffSelfApproval: false, now, ct);

    public async Task ApproveAsync(long rubricId, long approvalId, long actorId, string? approvalReference, bool isSingleStaffSelfApproval, DateTime now, CancellationToken ct)
    {
        var approval = await context.RubricApprovals.SingleAsync(a => a.Id == approvalId && a.RubricId == rubricId, ct);
        approval.DecidedBy = actorId;
        approval.DecidedAt = now;
        approval.Decision = "APPROVED";
        approval.IsSingleStaffSelfApproval = isSingleStaffSelfApproval;

        var roundApprovals = await context.RubricApprovals
            .Where(a => a.RubricId == rubricId && a.SubmittedAt == approval.SubmittedAt)
            .ToListAsync(ct);
        var allApproved = roundApprovals.All(a => a.Id == approval.Id || a.Decision == "APPROVED");

        var rubric = await context.Rubrics.SingleAsync(r => r.Id == rubricId, ct);
        rubric.UpdatedAt = now;
        if (!string.IsNullOrWhiteSpace(approvalReference))
        {
            var trimmed = approvalReference.Trim();
            rubric.ApprovalReference = string.IsNullOrWhiteSpace(rubric.ApprovalReference)
                ? trimmed
                : $"{rubric.ApprovalReference}; {trimmed}";
        }

        var version = await context.Set<RubricVersion>().SingleAsync(v => v.RubricId == rubricId, ct);
        if (allApproved)
        {
            rubric.IsActive = true;
            version.Status = RubricStatuses.Published;
        }
        version.ConcurrencyToken = Guid.NewGuid();

        await context.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(long rubricId, long approvalId, long actorId, string reason, DateTime now, CancellationToken ct)
    {
        var approval = await context.RubricApprovals.SingleAsync(a => a.Id == approvalId && a.RubricId == rubricId, ct);
        approval.DecidedBy = actorId;
        approval.DecidedAt = now;
        approval.Decision = "REJECTED";
        approval.Reason = reason;

        // Close any undecided sibling approval row in the same round so the rubric returns cleanly to DRAFT.
        var pendingSiblings = await context.RubricApprovals
            .Where(a => a.RubricId == rubricId && a.SubmittedAt == approval.SubmittedAt && a.Id != approval.Id && a.DecidedAt == null)
            .ToListAsync(ct);
        foreach (var sibling in pendingSiblings)
        {
            sibling.DecidedBy = actorId;
            sibling.DecidedAt = now;
            sibling.Decision = "REJECTED";
            sibling.Reason = $"Closed because partner department rejected: {reason}";
            sibling.IsSingleStaffSelfApproval = sibling.SubmittedBy == actorId;
        }

        var rubric = await context.Rubrics.SingleAsync(r => r.Id == rubricId, ct);
        rubric.IsActive = false;
        rubric.UpdatedAt = now;

        var version = await context.Set<RubricVersion>().SingleAsync(v => v.RubricId == rubricId, ct);
        version.Status = RubricStatuses.Draft;
        version.ConcurrencyToken = Guid.NewGuid();

        await context.SaveChangesAsync(ct);
    }

    public async Task NotifyApprovalRequestedAsync(long rubricId, long actorId, string rubricCode, string rubricName, IReadOnlyCollection<long> departmentIds, DateTime now, CancellationToken ct)
    {
        var staffIds = new HashSet<long>();
        foreach (var deptId in departmentIds.Distinct())
        {
            var ids = await V4WorkflowNotificationHelper.GetDepartmentStaffIdsAsync(context, deptId, ct);
            foreach (var id in ids) staffIds.Add(id);
        }

        if (staffIds.Count > 0)
        {
            await V4WorkflowNotificationHelper.EmitAsync(
                context,
                actorId,
                "RUBRIC_APPROVAL_REQUESTED",
                $"Bộ tiêu chí {rubricCode} đang chờ phê duyệt",
                $"Bộ tiêu chí \"{rubricName}\" ({rubricCode}) đã được trình duyệt. Vui lòng kiểm tra và phê duyệt hoặc yêu cầu chỉnh sửa.",
                "RUBRIC",
                rubricId,
                staffIds,
                now,
                enqueueEmail: true,
                ct);
        }
    }

    public Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct) =>
        Transaction(action, System.Data.IsolationLevel.ReadCommitted, ct);

    private async Task<T> Transaction<T>(Func<Task<T>> action, System.Data.IsolationLevel isolation, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is not null)
        {
            return await action();
        }

        await using var transaction = await context.Database.BeginTransactionAsync(isolation, ct);
        try
        {
            var result = await action();
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The rubric has changed. Reload it before retrying.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException("The rubric code or version already exists.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
        {
            throw new ConflictException("The rubric is referenced or its academic scope has changed.");
        }
        catch (Exception ex) when (IsLockConflict(ex))
        {
            throw new ConflictException("The rubric is being changed by another request. Reload it before retrying.");
        }
    }

    private static bool IsLockConflict(Exception exception)
    {
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
            if (cause is SqlException { Number: 1205 or 1222 }) return true;
        return false;
    }
}
