using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Application.Features.Semesters.Abstractions;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class PeriodPolicyService(AipmsDbContext db, ICurrentUser current, IEvaluationDraftRepository transactions,
    IAuditTrail audit, TimeProvider clock) : IPeriodPolicyService
{
    private static PeriodPolicyDto BuildVirtualPolicy(AIPMS.Infrastructure.Persistence.Generated.Models.ProjectPeriod period) =>
        new(
            0,
            period.Id,
            period.PolicyVersion,
            "PUBLISHED",
            DateTime.SpecifyKind(period.StartAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(period.EndAt, DateTimeKind.Utc),
            string.Empty,
            new PeriodPolicyFields(
                period.AllowedProjectModes,
                period.AllowedProposalSources,
                period.MinTeamSize ?? 3,
                period.MaxTeamSize ?? 5,
                period.MinDistinctMajors ?? 1,
                period.MaxProjectsPerSupervisor ?? 5));

    public async Task<IReadOnlyList<PeriodPolicyDto>> HistoryAsync(long periodId, CancellationToken ct)
    {
        await Authorize(periodId, false, ct);
        var rows = await db.Set<PeriodPolicyVersion>().AsNoTracking()
            .Where(p => p.ProjectPeriodId == periodId)
            .OrderByDescending(p => p.Version)
            .ToListAsync(ct);
        if (rows.Count > 0)
            return rows.Select(Map).ToArray();

        var period = await db.ProjectPeriods.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == periodId, ct)
            ?? throw new NotFoundException("ProjectPeriod", periodId);
        return [BuildVirtualPolicy(period)];
    }

    private async Task Authorize(long periodId, bool write, CancellationToken ct)
    {
        var actor = await transactions.GetActorAsync(current.UserId ?? throw new UnauthorizedException(), ct)
            ?? throw new ForbiddenException();
        var periodScope = await db.ProjectPeriods.AsNoTracking()
            .Where(p => p.Id == periodId)
            .Select(p => new
            {
                OrganizationId = p.AcademicSemester.OrganizationId,
                RubricDepartmentId = p.Rubric == null ? (long?)null : p.Rubric.DepartmentId
            })
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("ProjectPeriod", periodId);

        if (!write && actor.IsAdmin) return;
        if (!actor.IsStaff || !actor.DepartmentId.HasValue)
        {
            throw new ForbiddenException(
                write && actor.IsAdmin
                    ? "Platform administrators without DEPARTMENT_STAFF role cannot modify academic period policies."
                    : "Active department staff authority is required.",
                write && actor.IsAdmin ? WorkflowErrorCodes.AdminNotAcademicAuthority : null);
        }

        var staffInSameOrg = await db.Departments.AsNoTracking()
            .AnyAsync(d => d.Id == actor.DepartmentId.Value && d.IsActive && d.OrganizationId == periodScope.OrganizationId && d.Organization.IsActive, ct);
        if (!staffInSameOrg)
            throw new ForbiddenException();

        if (periodScope.RubricDepartmentId.HasValue && periodScope.RubricDepartmentId.Value != actor.DepartmentId.Value)
            throw new ForbiddenException();
    }

    public async Task<PeriodPolicyDto> GetAsync(long periodId, DateTimeOffset? asOf, CancellationToken ct)
    {
        await Authorize(periodId, false, ct);
        var instant = asOf?.UtcDateTime ?? clock.GetUtcNow().UtcDateTime;
        var hasVersions = await db.Set<PeriodPolicyVersion>().AsNoTracking()
            .AnyAsync(p => p.ProjectPeriodId == periodId, ct);
        if (hasVersions)
        {
            var row = await db.Set<PeriodPolicyVersion>().AsNoTracking()
                .SingleOrDefaultAsync(p => p.ProjectPeriodId == periodId
                    && p.Status != "DRAFT" && p.EffectiveFrom <= instant && instant < p.EffectiveTo, ct)
                ?? throw new NotFoundException("EffectivePolicy", periodId);
            return Map(row);
        }

        var period = await db.ProjectPeriods.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == periodId, ct)
            ?? throw new NotFoundException("ProjectPeriod", periodId);
        if (instant < period.StartAt || instant >= period.EndAt)
            throw new NotFoundException("EffectivePolicy", periodId);
        return BuildVirtualPolicy(period);
    }

    public Task<PeriodPolicyDto> PutAsync(long periodId, UpdatePeriodPolicyRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await Authorize(periodId, true, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await PolicyVersions.EnsureAsync(db, periodId, now, ct);
        var period = await db.ProjectPeriods.SingleAsync(p => p.Id == periodId, ct);
        var latest = await db.Set<PeriodPolicyVersion>().Where(p => p.ProjectPeriodId == periodId).OrderByDescending(p => p.Version).FirstAsync(ct);
        if (input.ExpectedVersion != latest.Version) throw new ConflictException("Policy version changed. Reload before saving.");
        var fields = input.Policy;
        if (fields is null || fields.AllowedProjectModes is null || fields.AllowedProposalSources is null
            || !ProjectPeriodGovernancePolicy.IsValid(fields.AllowedProjectModes, true)
            || !ProjectPeriodGovernancePolicy.IsValid(fields.AllowedProposalSources, false)
            || fields.MinTeamSize < 1 || fields.MaxTeamSize < fields.MinTeamSize || fields.MinDistinctMajors < 1
            || fields.MinDistinctMajors > fields.MaxTeamSize || fields.MaxProjectsPerSupervisor < 1
            || fields.SupervisorInvitationExpiryDays is < 1 or > 30
            || fields.MaxPendingSupervisorRequestsPerSlot is < 1 or > 5
            || input.EffectiveFrom >= input.EffectiveTo || input.EffectiveTo.UtcDateTime > period.EndAt
            || input.EffectiveFrom.UtcDateTime < period.StartAt)
            throw new ValidationException(new Dictionary<string, string[]> { ["policy"] = ["Invalid policy fields or effective interval."] });
        if (period.Status is "CLOSED" or "ARCHIVED") throw new ConflictException("The period is closed.");
        PeriodPolicyVersion row;
        if (input.Operation == "SUCCESSOR")
        {
            if (latest.Status == "DRAFT") throw new ConflictException("Edit or publish the existing draft first.");
            row = new() { ProjectPeriodId = periodId, Version = latest.Version + 1, CreatedBy = current.UserId, CreatedAt = now };
            db.Add(row);
        }
        else if (input.Operation is "UPDATE_DRAFT" or "PUBLISH")
        {
            if (latest.Status != "DRAFT") throw new ConflictException("Published or referenced policy is immutable; explicitly create a SUCCESSOR.");
            if (!Guid.TryParse(input.ConcurrencyToken, out var token) || token != latest.ConcurrencyToken)
                throw new ConflictException("Policy draft changed. Reload before saving.");
            row = latest;
        }
        else throw new ValidationException(new Dictionary<string, string[]> { ["operation"] = ["Use SUCCESSOR, UPDATE_DRAFT or PUBLISH."] });
        row.SnapshotJson = JsonSerializer.Serialize(fields); row.EffectiveFrom = input.EffectiveFrom.UtcDateTime;
        row.EffectiveTo = input.EffectiveTo.UtcDateTime; row.ConcurrencyToken = Guid.NewGuid();
        if (input.Operation == "PUBLISH")
        {
            var isUpcomingPublish = period.Status == "UPCOMING"
                && row.EffectiveFrom >= period.StartAt
                && row.EffectiveTo <= period.EndAt;
            // Publication is immediate for active periods, or scheduled at period.StartAt for UPCOMING periods.
            if (!isUpcomingPublish && (row.EffectiveFrom > now || row.EffectiveFrom < now.AddMinutes(-5) || row.EffectiveTo <= now))
                throw new ConflictException("Publish an immediately effective policy (effectiveFrom within the last five minutes).");
            var existingHasUsage = await db.Set<PeriodPolicyUsage>().AnyAsync(u => u.PolicyVersionId == existing.Id, ct);
            if (isUpcomingPublish && !existingHasUsage && existing.EffectiveFrom == row.EffectiveFrom)
            {
                existing.EffectiveTo = existing.EffectiveFrom;
            }
            else
            {
                if (existing.EffectiveFrom >= row.EffectiveFrom) throw new ConflictException("The successor must start after the predecessor.");
                if (await db.Set<PeriodPolicyUsage>().AnyAsync(u => u.PolicyVersionId == existing.Id && u.CreatedAt >= row.EffectiveFrom, ct))
                    throw new ConflictException("A successor cannot change the policy effective at an already recorded operation.");
                if (existing.EffectiveTo > row.EffectiveFrom) existing.EffectiveTo = row.EffectiveFrom;
            }
            row.Status = "PUBLISHED";
            period.AllowedProjectModes = ProjectPeriodGovernancePolicy.Normalize(fields.AllowedProjectModes);
            period.AllowedProposalSources = ProjectPeriodGovernancePolicy.Normalize(fields.AllowedProposalSources);
            period.MinTeamSize = fields.MinTeamSize; period.MaxTeamSize = fields.MaxTeamSize;
            period.MinDistinctMajors = fields.MinDistinctMajors; period.MaxProjectsPerSupervisor = fields.MaxProjectsPerSupervisor;
            period.PolicyVersion = row.Version; period.UpdatedAt = now;
            // Eligibility snapshots are immutable. The freshness evaluator compares the new policy version/hash.
        }
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(current.UserId!.Value, "PERIOD_POLICY_" + input.Operation, "PROJECT_PERIOD", periodId,
            new Dictionary<string, object?> { ["policy"] = Map(row) }), ct);
        return Map(row);
    }, ct);
    internal static PeriodPolicyDto Map(PeriodPolicyVersion row) => new(row.Id, row.ProjectPeriodId, row.Version, row.Status,
        DateTime.SpecifyKind(row.EffectiveFrom, DateTimeKind.Utc), DateTime.SpecifyKind(row.EffectiveTo, DateTimeKind.Utc),
        row.ConcurrencyToken.ToString("N"), JsonSerializer.Deserialize<PeriodPolicyFields>(row.SnapshotJson)!);
}

internal static class PolicyVersions
{
    internal static async Task InheritSupervisorAsync(AipmsDbContext db, long projectId, long assignmentId, DateTime now, CancellationToken ct)
    {
        var snapshotId = await db.Set<ProjectRegistrationSnapshot>().Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.Id).Select(s => (long?)s.Id).FirstOrDefaultAsync(ct);
        var policyId = await db.Set<PeriodPolicyUsage>().Where(u => u.EntityType == "PROJECT_SUBMISSION" && u.EntityId == snapshotId)
            .Select(u => (long?)u.PolicyVersionId).SingleOrDefaultAsync(ct);
        // No retroactive policy guessing for legacy submissions.
        if (policyId.HasValue)
        {
            db.Add(new PeriodPolicyUsage { PolicyVersionId = policyId.Value, EntityType = "SUPERVISOR_ASSIGNMENT", EntityId = assignmentId, CreatedAt = now });
            await db.SaveChangesAsync(ct);
        }
        // Capacity comes from the selection period, independently of the submitted registration policy.
        var assignment = await db.SupervisorAssignments.SingleAsync(a => a.Id == assignmentId, ct);
        var semesterId = await db.Projects.Where(p => p.Id == projectId).Select(p => p.Team.AcademicSemesterId).SingleAsync(ct);
        var execution = !assignment.IsPrimary || assignment.ReplacesAssignmentId.HasValue;
        var selection = await db.ProjectPeriods.Where(p => p.AcademicSemesterId == semesterId && p.PeriodType == "SUPERVISOR_SELECTION"
            && (execution ? (p.Status == "ACTIVE" || p.Status == "CLOSED") && p.StartAt <= now
                : p.Status == "ACTIVE" && p.StartAt <= now && now < p.EndAt))
            .OrderByDescending(p => p.StartAt).ThenByDescending(p => p.Id).FirstOrDefaultAsync(ct);
        if (selection is null && !await db.ProjectPeriods.AnyAsync(p => p.AcademicSemesterId == semesterId && p.PeriodType == "SUPERVISOR_SELECTION", ct))
        {
            selection = await db.ProjectPeriods.Where(p => p.AcademicSemesterId == semesterId && p.PeriodType == "REGISTRATION"
                && p.Status == "ACTIVE" && p.StartAt <= now && now < p.EndAt && p.MaxProjectsPerSupervisor != null)
                .OrderByDescending(p => p.StartAt).ThenByDescending(p => p.Id).FirstOrDefaultAsync(ct);
        }
        if (selection is null) throw new ConflictException("A supervisor capacity policy is required.");
        var capacity = await EnsureAsync(db, selection.Id, now, ct);
        if (!execution && (now < capacity.EffectiveFrom || now >= capacity.EffectiveTo))
            throw new ConflictException("No effective supervisor selection policy.");
        capacity.Status = "LOCKED";
        db.Add(new PeriodPolicyUsage { PolicyVersionId = capacity.Id, EntityType = "SUPERVISOR_CAPACITY", EntityId = assignmentId, CreatedAt = now });
        await db.SaveChangesAsync(ct);
    }
    internal static async Task<PeriodPolicyVersion> EnsureAsync(AipmsDbContext db, long periodId, DateTime now, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Policy capture requires a transaction.");
        var period = await db.ProjectPeriods.FromSqlInterpolated($"SELECT * FROM dbo.project_periods WITH (UPDLOCK,HOLDLOCK) WHERE id={periodId}").SingleAsync(ct);
        var row = await db.Set<PeriodPolicyVersion>().Where(x => x.ProjectPeriodId == periodId && x.Status != "DRAFT")
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        if (row is not null) return row;
        row = new() { ProjectPeriodId = periodId, Version = period.PolicyVersion, Status = "PUBLISHED",
            EffectiveFrom = period.StartAt, EffectiveTo = period.EndAt, CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
            SnapshotJson = JsonSerializer.Serialize(new PeriodPolicyFields(period.AllowedProjectModes, period.AllowedProposalSources,
                period.MinTeamSize ?? 3, period.MaxTeamSize ?? 5, period.MinDistinctMajors ?? 1, period.MaxProjectsPerSupervisor ?? 5)) };
        db.Add(row); await db.SaveChangesAsync(ct); return row;
    }
    internal static async Task<long> CaptureAsync(AipmsDbContext db, long periodId, string type, long entityId, DateTime now, CancellationToken ct)
    {
        var row = await EnsureAsync(db, periodId, now, ct);
        if (now < row.EffectiveFrom || now >= row.EffectiveTo) throw new ConflictException("No effective period policy at this instant.");
        var usage = await db.Set<PeriodPolicyUsage>().SingleOrDefaultAsync(x => x.EntityType == type && x.EntityId == entityId, ct);
        if (usage is not null) return usage.PolicyVersionId;
        row.Status = "LOCKED";
        db.Add(new PeriodPolicyUsage { PolicyVersionId = row.Id, EntityType = type, EntityId = entityId, CreatedAt = now });
        await db.SaveChangesAsync(ct); return row.Id;
    }
    internal static Task<bool> HasPolicyUsageAsync(AipmsDbContext db, long periodId, CancellationToken ct) =>
        (from u in db.Set<PeriodPolicyUsage>().AsNoTracking()
         join v in db.Set<PeriodPolicyVersion>().AsNoTracking() on u.PolicyVersionId equals v.Id
         where v.ProjectPeriodId == periodId
         select u.Id).AnyAsync(ct);

    internal static async Task GuardLegacyEditAsync(AipmsDbContext db, long periodId, CancellationToken ct)
    {
        await db.ProjectPeriods.FromSqlInterpolated($"SELECT * FROM dbo.project_periods WITH (UPDLOCK,HOLDLOCK) WHERE id={periodId}").AsNoTracking().SingleAsync(ct);
        var hasUsage = await HasPolicyUsageAsync(db, periodId, ct);
        var versionCount = await db.Set<PeriodPolicyVersion>().AsNoTracking().CountAsync(x => x.ProjectPeriodId == periodId, ct);
        if (hasUsage || versionCount > 1)
            throw new ConflictException("Use the versioned policy endpoint; this period has protected policy history or recorded usage.");
    }

    internal static async Task SyncPolicyWindowOnDateUpdateAsync(
        AipmsDbContext db,
        long periodId,
        DateTime oldStartAt,
        DateTime oldEndAt,
        DateTime newStartAt,
        DateTime newEndAt,
        CancellationToken ct)
    {
        if (newEndAt < oldEndAt)
        {
            var usageAfterEnd = await (from u in db.Set<PeriodPolicyUsage>().AsNoTracking()
                                       join v in db.Set<PeriodPolicyVersion>().AsNoTracking() on u.PolicyVersionId equals v.Id
                                       where v.ProjectPeriodId == periodId && u.CreatedAt >= newEndAt
                                       select u.Id).AnyAsync(ct);
            if (usageAfterEnd)
            {
                throw new ConflictException(
                    "Cannot shrink the period end date before already recorded policy usage.",
                    WorkflowErrorCodes.PeriodShrinkConflictsWithUsage);
            }
        }

        if (newStartAt > oldStartAt)
        {
            var usageBeforeStart = await (from u in db.Set<PeriodPolicyUsage>().AsNoTracking()
                                          join v in db.Set<PeriodPolicyVersion>().AsNoTracking() on u.PolicyVersionId equals v.Id
                                          where v.ProjectPeriodId == periodId && u.CreatedAt < newStartAt
                                          select u.Id).AnyAsync(ct);
            if (usageBeforeStart)
            {
                throw new ConflictException(
                    "Cannot move the period start date after already recorded policy usage.",
                    WorkflowErrorCodes.PeriodShrinkConflictsWithUsage);
            }
        }

        var versions = await db.Set<PeriodPolicyVersion>()
            .Where(v => v.ProjectPeriodId == periodId && v.Status != "DRAFT")
            .OrderByDescending(v => v.Version)
            .ToListAsync(ct);
        if (versions.Count == 0) return;

        var latest = versions[0];
        if (newEndAt != oldEndAt && (latest.EffectiveTo == oldEndAt || latest.EffectiveTo < newEndAt || newEndAt < latest.EffectiveTo))
        {
            if (newEndAt <= latest.EffectiveFrom)
            {
                throw new ConflictException(
                    "The new period end date must be after the latest published policy effective start.",
                    WorkflowErrorCodes.PeriodShrinkConflictsWithUsage);
            }
            latest.EffectiveTo = newEndAt;
        }

        var earliest = versions[^1];
        if (newStartAt != oldStartAt && earliest.EffectiveFrom == oldStartAt && newStartAt < earliest.EffectiveTo)
        {
            earliest.EffectiveFrom = newStartAt;
        }
    }
}
