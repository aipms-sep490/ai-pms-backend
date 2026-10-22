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

    private async Task<long?> FrozenMemberMajor(long projectId, long userId, CancellationToken ct)
    {
        var json = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking()
            .Where(x => x.ProjectId == projectId).OrderByDescending(x => x.Id)
            .Select(x => x.SnapshotJson).FirstOrDefaultAsync(ct);
        return json is null ? null : ProjectAcademicScopeReader.ParseEvidence(json)?.Members?
            .SingleOrDefault(x => x.UserId == userId)?.MajorId;
    }

    private async Task<bool> CanReadEvidence(long projectId, ProjectAcademicScope scope, ProjectGateRequirement requirement, CancellationToken ct)
    {
        if (requirement.Scope is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL")) return false;
        if (requirement.Scope != "COMMON" && (!requirement.MajorId.HasValue || !scope.MajorIds.Contains(requirement.MajorId.Value))) return false;
        var member = await db.TeamMembers.AnyAsync(x => x.Team.Project != null && x.Team.Project.Id == projectId
            && x.UserId == Actor && x.LeftAt == null, ct);
        if (member) return requirement.Scope switch
        {
            "COMMON" => true,
            "INDIVIDUAL" => requirement.TargetStudentId == Actor,
            _ => await FrozenMemberMajor(projectId, Actor, ct) == requirement.MajorId
        };
        var assignments = await db.SupervisorAssignments.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.EndedAt == null && x.SupervisorProfile.UserId == Actor)
            .Select(x => new { x.AssignmentType, x.IsPrimary, x.MajorId }).ToArrayAsync(ct);
        if (assignments.Length > 0)
            return requirement.Scope == "COMMON" || assignments.Any(x => x.AssignmentType == "PRIMARY" && x.IsPrimary)
                || assignments.Any(x => x.AssignmentType == "DISCIPLINE_MENTOR" && x.MajorId.HasValue && x.MajorId == requirement.MajorId);

        // Project visibility alone never grants access to another department's evidence.
        if (!await IsStaff(ct)) return false;
        var department = await db.Users.Where(x => x.Id == Actor).Select(x => x.DepartmentId).SingleAsync(ct);
        return department.HasValue && (department == scope.LeadDepartmentId
            || (requirement.Scope == "COMMON" && scope.DepartmentIds.Contains(department.Value))
            || (requirement.MajorId.HasValue && scope.MajorDepartmentIds?.GetValueOrDefault(requirement.MajorId.Value) == department));
    }
}
