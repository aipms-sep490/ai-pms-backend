using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed record DefenseCommitteeBinding(long CommitteeId, int Version);

internal static class DefenseCommitteeAuthority
{
    internal static async Task<DefenseCommitteeBinding> RequireAsync(AipmsDbContext db, long projectId,
        long evaluatorId, string scope, long? majorId, CancellationToken ct)
    {
        var committee = await db.Set<DefenseCommittee>().AsNoTracking().SingleOrDefaultAsync(
            c => c.ProjectId == projectId && c.Status == "CONFIRMED", ct);
        if (committee is null) throw new ConflictException("A confirmed defense committee is required.", "DEFENSE_COMMITTEE_REQUIRED");
        if (!await db.Set<DefenseCommitteeMember>().AsNoTracking().AnyAsync(m => m.CommitteeId == committee.Id
            && m.UserId == evaluatorId && (scope == "COMMON" || majorId.HasValue && m.MajorId == majorId), ct))
            throw new ForbiddenException("Only a confirmed committee member in the assigned scope can evaluate this project.");
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == evaluatorId && u.Status == "ACTIVE"
            && u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER")
            && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive, ct))
            throw new ForbiddenException("The committee member is no longer an active lecturer.");
        if (await AcademicAssignmentGuard.HasSupervisorOrMentorConflictAsync(db, projectId, evaluatorId, ct))
            throw new ConflictException("A current or former primary supervisor, or an active mentor, cannot evaluate the same project.", WorkflowErrorCodes.EvaluatorRoleConflict);
        return new(committee.Id, committee.Version);
    }

    internal static async Task RequirePinnedAsync(AipmsDbContext db, EvaluationAssignment assignment, CancellationToken ct)
    {
        DefenseCommitteeBinding? pinned;
        try
        {
            using var json = JsonDocument.Parse(assignment.ScopeSnapshotJson ?? "{}");
            pinned = json.RootElement.TryGetProperty("Committee", out var node)
                ? node.Deserialize<DefenseCommitteeBinding>() : null;
        }
        catch (JsonException) { pinned = null; }
        if (pinned is null) throw new ConflictException("Committee binding is missing from this evaluation assignment.", "DEFENSE_COMMITTEE_BINDING_REQUIRED");
        var current = await RequireAsync(db, assignment.ProjectId, assignment.EvaluatorId, assignment.Scope, assignment.MajorId, ct);
        if (current != pinned) throw new ConflictException("The effective committee changed; reassign the evaluation.", "DEFENSE_COMMITTEE_CHANGED");
    }
}
