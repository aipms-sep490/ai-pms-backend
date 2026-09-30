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
    public Task<IReadOnlyList<PeriodPolicyDto>> HistoryAsync(long periodId, CancellationToken ct) => transactions.InTransactionAsync<IReadOnlyList<PeriodPolicyDto>>(async () =>
    {
        await Authorize(periodId, ct);
        await PolicyVersions.EnsureAsync(db, periodId, clock.GetUtcNow().UtcDateTime, ct);
        return (await db.Set<PeriodPolicyVersion>().AsNoTracking().Where(p => p.ProjectPeriodId == periodId)
            .OrderByDescending(p => p.Version).ToListAsync(ct)).Select(Map).ToArray();
    }, ct);
    private async Task Authorize(long periodId, CancellationToken ct)
    {
        var actor = await transactions.GetActorAsync(current.UserId ?? throw new UnauthorizedException(), ct)
            ?? throw new ForbiddenException();
        var organization = await db.ProjectPeriods.Where(p => p.Id == periodId)
            .Select(p => (long?)p.AcademicSemester.OrganizationId).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("ProjectPeriod", periodId);
        // Periods are organization-wide: staff may manage only a period whose rubric explicitly scopes it to their department.
        if (!actor.IsAdmin && !(actor.IsStaff && await db.ProjectPeriods.AnyAsync(p => p.Id == periodId
            && p.Rubric != null && p.Rubric.DepartmentId == actor.DepartmentId
            && p.Rubric.Department != null && p.Rubric.Department.OrganizationId == organization, ct))) throw new ForbiddenException();
    }
    public Task<PeriodPolicyDto> GetAsync(long periodId, DateTimeOffset? asOf, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await Authorize(periodId, ct);
        await PolicyVersions.EnsureAsync(db, periodId, clock.GetUtcNow().UtcDateTime, ct);
        var instant = asOf?.UtcDateTime ?? clock.GetUtcNow().UtcDateTime;
        var row = await db.Set<PeriodPolicyVersion>().SingleOrDefaultAsync(p => p.ProjectPeriodId == periodId
            && p.Status != "DRAFT" && p.EffectiveFrom <= instant && instant < p.EffectiveTo, ct)
            ?? throw new NotFoundException("EffectivePolicy", periodId);
        return Map(row);
    }, ct);

    public Task<PeriodPolicyDto> PutAsync(long periodId, UpdatePeriodPolicyRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await Authorize(periodId, ct);
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
            // Publication is immediate. The predecessor payload is never rewritten; only its effective interval is closed.
            if (row.EffectiveFrom > now || row.EffectiveFrom < now.AddMinutes(-5) || row.EffectiveTo <= now)
                throw new ConflictException("Publish an immediately effective policy (effectiveFrom within the last five minutes).");
            if (existing.EffectiveFrom >= row.EffectiveFrom) throw new ConflictException("The successor must start after the predecessor.");
            if (await db.Set<PeriodPolicyUsage>().AnyAsync(u => u.PolicyVersionId == existing.Id && u.CreatedAt >= row.EffectiveFrom, ct))
                throw new ConflictException("A successor cannot change the policy effective at an already recorded operation.");
            if (existing.EffectiveTo > row.EffectiveFrom) existing.EffectiveTo = row.EffectiveFrom;
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
    internal static async Task GuardLegacyEditAsync(AipmsDbContext db, long periodId, CancellationToken ct)
    {
        await db.ProjectPeriods.FromSqlInterpolated($"SELECT * FROM dbo.project_periods WITH (UPDLOCK,HOLDLOCK) WHERE id={periodId}").AsNoTracking().SingleAsync(ct);
        if (await db.Set<PeriodPolicyVersion>().AnyAsync(x => x.ProjectPeriodId == periodId, ct))
            throw new ConflictException("Use the versioned policy endpoint; this period has protected policy history.");
    }
}
