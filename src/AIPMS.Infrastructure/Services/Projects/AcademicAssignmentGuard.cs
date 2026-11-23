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
            && ((a.IsPrimary && a.AssignmentType == "PRIMARY") || a.EndedAt == null)
            && a.SupervisorProfile.UserId == userId, ct);

    internal static async Task EnsureEvaluatorAsync(AipmsDbContext db, long projectId, long userId, string type, CancellationToken ct)
    {
        await LockProjectAsync(db, projectId, ct);
        if (type == "SUPERVISOR")
            throw new ConflictException("SUPERVISOR evaluation assignments are legacy read/complete only.", LegacyTypeCode);
        if (await HasBeenPrimarySupervisorAsync(db, projectId, userId, ct))
            throw new ConflictException("A current or former primary supervisor cannot be assigned as evaluator on this project.", ConflictCode);
    }

    internal static async Task EnsurePrimaryAsync(AipmsDbContext db, long projectId, long profileId, CancellationToken ct)
    {
        await LockProjectAsync(db, projectId, ct);
        var userId = await db.SupervisorProfiles.Where(p => p.Id == profileId).Select(p => p.UserId).SingleAsync(ct);
        var hasEvaluator = await db.Set<EvaluationAssignment>().AnyAsync(a => a.ProjectId == projectId && a.EvaluatorId == userId && a.Status == "ACTIVE", ct);
        var hasGateReviewer = !hasEvaluator && await db.GateReviewerAssignments.AnyAsync(
            a => a.GateInstance.ProjectId == projectId && a.ReviewerId == userId && a.Status != "REVOKED", ct);
        var hasCommitteeRole = !hasEvaluator && !hasGateReviewer && await db.Set<DefenseCommitteeMember>().AnyAsync(
            m => m.UserId == userId && db.Set<DefenseCommittee>().Any(c => c.Id == m.CommitteeId && c.ProjectId == projectId && c.Status == "CONFIRMED"), ct);
        if (hasEvaluator || hasGateReviewer || hasCommitteeRole)
            throw new ConflictException("An active evaluator, gate reviewer, or confirmed committee member cannot become primary supervisor on this project.", ConflictCode);
    }
}
