using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Application.Features.Supervisors.Queries;

namespace AIPMS.Application.Features.Supervisors.Services;

public sealed record SupervisorCandidateContext(SupervisorCandidateProject Project, SupervisorSelectionPolicy Policy);

public sealed class SupervisorCandidatePolicy(ISupervisorCandidateRepository repository,
    SupervisorAccessService access, IProjectAccessService projectAccess, TimeProvider clock)
{
    public async Task<SupervisorCandidateProject> Read(long projectId, CancellationToken ct)
    {
        var actor = await access.EnsureCanReadAsync(ct);
        if (actor.Roles.Contains(AppRoles.Admin) && await repository.GetProjectAsync(projectId, clock.GetUtcNow().UtcDateTime, ct) is null)
            throw new NotFoundException("Project", projectId);
        if ((!actor.Roles.Contains(AppRoles.Admin) && !actor.HasActiveAcademicScope)
            || !await projectAccess.CanAccessAsync(actor.UserId, projectId, ct))
            throw new ForbiddenException("You cannot view supervisor candidates for this project.");

        var now = clock.GetUtcNow().UtcDateTime;
        var project = await repository.GetProjectAsync(projectId, now, ct)
            ?? throw new NotFoundException("Project", projectId);
        return project;
    }
    public async Task<SupervisorCandidateContext> Select(GetSupervisorCandidatesQuery request, CancellationToken ct)
    {
        var project = await Read(request.ProjectId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (request.AssignmentType == "DISCIPLINE_MENTOR")
        {
            if (project.Status is not ("DRAFT" or "REVISION_REQUIRED" or "ACTIVE")
                || (project.Status == "ACTIVE" && !project.HasActiveAssignment)
                || project.RequiredMajorIds?.Contains(request.MajorId ?? 0) != true
                || project.OccupiedMentorMajors?.Contains(request.MajorId ?? 0) == true)
                throw new ConflictException("Discipline mentor selection requires an ACTIVE, DRAFT, or REVISION_REQUIRED project.");
        }
        else if (project.Status is not ("DRAFT" or "REVISION_REQUIRED" or "APPROVED") || project.HasActiveAssignment)
            throw new ConflictException("Supervisor selection requires a draft, revision-required, or approved project without an active assignment.");
        if (!project.HasActiveSemester || project.DepartmentIds.Count == 0)
            throw new ConflictException("The project must have an active semester and active majors in its organization.");

        var policies = await repository.GetSelectionPoliciesAsync(project.AcademicSemesterId, now, ct, request.AssignmentType == "DISCIPLINE_MENTOR");
        if (policies.Count != 1 || policies[0].MaxProjectsPerSupervisor is not > 0)
            throw new ConflictException("One active supervisor-selection period with a configured quota is required.");
        var policy = policies[0];
        return new(project, policy);
    }
}
