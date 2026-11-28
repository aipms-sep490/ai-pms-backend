using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

// Both assignment directions serialize on the project before checking D17.
internal static class AcademicAssignmentGuard
{
    internal const string ConflictCode = WorkflowErrorCodes.EvaluatorRoleConflict;
    internal const string LegacyTypeCode = "SUPERVISOR_EVALUATION_TYPE_RETIRED";

    internal static async Task LockProjectAsync(AipmsDbContext db, long projectId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Academic assignments require a transaction.");
        var rows = await db.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.projects WITH (XLOCK, HOLDLOCK) WHERE id = {projectId}").ToListAsync(ct);
        if (rows.Count == 0) throw new NotFoundException("Project", projectId);
    }

    internal static Task<bool> HasBeenPrimarySupervisorAsync(AipmsDbContext db, long projectId, long userId, CancellationToken ct) =>
        db.SupervisorAssignments.AsNoTracking().AnyAsync(a => a.ProjectId == projectId
            && a.IsPrimary && a.AssignmentType == "PRIMARY"
            && a.SupervisorProfile.UserId == userId, ct);

    internal static Task<bool> HasSupervisorOrMentorConflictAsync(AipmsDbContext db, long projectId, long userId, CancellationToken ct) =>
        db.SupervisorAssignments.AsNoTracking().AnyAsync(a => a.ProjectId == projectId
            && a.SupervisorProfile.UserId == userId, ct);

    internal static async Task<HashSet<long>> GetConflictingSupervisorOrMentorUserIdsAsync(
        AipmsDbContext db, long projectId, IEnumerable<long>? userIds, CancellationToken ct)
    {
        var query = db.SupervisorAssignments.AsNoTracking()
            .Where(a => a.ProjectId == projectId);
        if (userIds is not null)
        {
            var ids = userIds.Distinct().ToArray();
            if (ids.Length == 0) return [];
            query = query.Where(a => ids.Contains(a.SupervisorProfile.UserId));
        }
        var rows = await query.Select(a => a.SupervisorProfile.UserId).Distinct().ToListAsync(ct);
        return rows.ToHashSet();
    }

    internal static async Task EnsureEvaluatorAsync(AipmsDbContext db, long projectId, long userId, string type, CancellationToken ct)
    {
        await LockProjectAsync(db, projectId, ct);
        if (type == "SUPERVISOR")
            throw new ConflictException("SUPERVISOR evaluation assignments are legacy read/complete only.", LegacyTypeCode);
        if (await HasSupervisorOrMentorConflictAsync(db, projectId, userId, ct))
            throw new ConflictException("A current or former primary supervisor or discipline mentor cannot be assigned as evaluator on this project (D17).", ConflictCode);
    }

    internal static Task EnsurePrimaryAsync(AipmsDbContext db, long projectId, long profileId, CancellationToken ct) =>
        EnsureSupervisorOrMentorAsync(db, projectId, profileId, ct);

    internal static async Task EnsureSupervisorOrMentorAsync(AipmsDbContext db, long projectId, long profileId, CancellationToken ct)
    {
        await LockProjectAsync(db, projectId, ct);
        var userId = await db.SupervisorProfiles.Where(p => p.Id == profileId).Select(p => p.UserId).SingleAsync(ct);
        var hasEvaluator = await db.Set<EvaluationAssignment>()
            .AnyAsync(a => a.ProjectId == projectId && a.EvaluatorId == userId && a.Status == "ACTIVE", ct);
        var hasGateReviewer = !hasEvaluator && await db.GateReviewerAssignments.AnyAsync(
            a => a.GateInstance.ProjectId == projectId && a.ReviewerId == userId && a.Status != "REVOKED", ct);
        var hasCommitteeRole = !hasEvaluator && !hasGateReviewer && await db.Set<DefenseCommitteeMember>().AnyAsync(
            m => m.UserId == userId && db.Set<DefenseCommittee>().Any(
                c => c.Id == m.CommitteeId && c.ProjectId == projectId && (c.Status == "CONFIRMED" || c.Status == "DRAFT")), ct);
        var hasAppealReviewer = !hasEvaluator && !hasGateReviewer && !hasCommitteeRole && await db.Set<ResultAppealRequest>().AnyAsync(
            r => r.ProjectId == projectId && r.ReviewerId == userId
                && (r.Status == "PENDING" || r.Status == "UNDER_REVIEW" || r.Status == "ACCEPTED" || r.Status == "REJECTED"), ct);
        if (hasEvaluator || hasGateReviewer || hasCommitteeRole || hasAppealReviewer)
            throw new ConflictException(
                "An active evaluator, gate reviewer, defense committee member, or appeal reviewer cannot become primary supervisor or discipline mentor on this project (D17).",
                ConflictCode);
    }
}
