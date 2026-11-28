using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ReviewGateService
{
    private async Task RequireActiveActor(CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == Actor && x.Status == "ACTIVE", ct))
            throw new ForbiddenException("An active account is required.");
    }

    private bool IsAssignmentWithinValidity(GateReviewerAssignment row) =>
        row.Status != "REVOKED"
        && (!row.ValidFrom.HasValue || row.ValidFrom.Value <= Now)
        && (!row.ValidUntil.HasValue || Now <= row.ValidUntil.Value);

    private async Task<long?> FrozenMemberMajor(long projectId, long userId, CancellationToken ct)
    {
        var json = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking()
            .Where(x => x.ProjectId == projectId).OrderByDescending(x => x.Id)
            .Select(x => x.SnapshotJson).FirstOrDefaultAsync(ct);
        return json is null ? null : ProjectAcademicScopeReader.ParseEvidence(json)?.Members?
            .SingleOrDefault(x => x.UserId == userId)?.MajorId;
    }

    private static bool CoversRequirement(GateReviewerAssignment assignment, ProjectGateRequirement requirement) =>
        (assignment.Scope == "COMMON" && requirement.Scope is "COMMON" or "INTEGRATION")
        || (assignment.Scope == "INTEGRATION" && requirement.Scope == "INTEGRATION")
        || (assignment.Scope == "MAJOR_SPECIFIC" && (requirement.Scope is "COMMON" or "INTEGRATION"
            || (requirement.Scope == "MAJOR_SPECIFIC" && assignment.MajorId == requirement.MajorId)))
        || (assignment.Scope == "INDIVIDUAL" && requirement.Scope == "INDIVIDUAL" && assignment.StudentId == requirement.TargetStudentId);

    private async Task<bool> CanReadEvidence(
        long projectId,
        ProjectAcademicScope scope,
        ProjectGateRequirement requirement,
        CancellationToken ct,
        bool forTimeline = false)
    {
        if (requirement.Scope is not ("COMMON" or "INTEGRATION" or "MAJOR_SPECIFIC" or "INDIVIDUAL")) return false;
        if (requirement.Scope is "MAJOR_SPECIFIC" or "INDIVIDUAL" && (!requirement.MajorId.HasValue || !scope.MajorIds.Contains(requirement.MajorId.Value)))
            return false;
        if (await IsAdmin(ct)) return true;

        var membership = await db.TeamMembers.AsNoTracking()
            .Where(x => x.Team.Project != null && x.Team.Project.Id == projectId && x.UserId == Actor && x.LeftAt == null)
            .Select(x => new { x.IsLeader })
            .FirstOrDefaultAsync(ct);
        if (membership is not null)
        {
            return requirement.Scope switch
            {
                "COMMON" or "INTEGRATION" => true,
                "INDIVIDUAL" => requirement.TargetStudentId == Actor,
                _ => (forTimeline && membership.IsLeader) || await FrozenMemberMajor(projectId, Actor, ct) == requirement.MajorId
            };
        }

        var assignments = await db.SupervisorAssignments.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.EndedAt == null && x.SupervisorProfile.UserId == Actor)
            .Select(x => new { x.AssignmentType, x.IsPrimary, x.MajorId }).ToArrayAsync(ct);
        if (assignments.Length > 0)
            return requirement.Scope is "COMMON" or "INTEGRATION"
                || assignments.Any(x => x.AssignmentType == "PRIMARY" && x.IsPrimary)
                || assignments.Any(x => x.AssignmentType == "DISCIPLINE_MENTOR" && x.MajorId.HasValue && x.MajorId == requirement.MajorId);

        var now = Now;
        var activeReviewAssignments = await db.GateReviewerAssignments.AsNoTracking()
            .Where(x => x.GateInstanceId == requirement.GateInstanceId && x.ReviewerId == Actor
                && x.Status != "REVOKED"
                && (x.ValidFrom == null || x.ValidFrom <= now)
                && (x.ValidUntil == null || now <= x.ValidUntil))
            .ToArrayAsync(ct);
        if (activeReviewAssignments.Any(a => CoversRequirement(a, requirement)))
            return true;

        // Project visibility alone never grants access to another department's evidence.
        if (!await IsStaff(ct)) return false;
        var department = await db.Users.Where(x => x.Id == Actor).Select(x => x.DepartmentId).SingleAsync(ct);
        return department.HasValue && (department == scope.LeadDepartmentId
            || (requirement.Scope is "COMMON" or "INTEGRATION" && scope.DepartmentIds.Contains(department.Value))
            || (requirement.MajorId.HasValue && scope.MajorDepartmentIds?.GetValueOrDefault(requirement.MajorId.Value) == department));
    }
}
