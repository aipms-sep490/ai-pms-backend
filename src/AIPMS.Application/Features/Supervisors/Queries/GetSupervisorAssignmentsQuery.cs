using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Services;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Domain.Supervisors;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Queries;

public sealed record GetSupervisorAssignmentsQuery(long? ProjectId, string? Status = null,
    int Page = 1, int PageSize = 20) : IRequest<PagedResult<SupervisorAssignmentDto>>;

public sealed record GetSupervisorAssignmentQuery(long AssignmentId) : IRequest<SupervisorAssignmentDto>;
public sealed record GetSupervisorReplacementCandidatesQuery(long AssignmentId, string? Search = null,
    int Page = 1, int PageSize = 20) : IRequest<PagedResult<SupervisorReplacementCandidateDto>>;

public sealed class GetSupervisorAssignmentsQueryHandler(SupervisorAssignmentWorkflow workflow)
    : IRequestHandler<GetSupervisorAssignmentsQuery, PagedResult<SupervisorAssignmentDto>>
{
    public Task<PagedResult<SupervisorAssignmentDto>> Handle(GetSupervisorAssignmentsQuery request, CancellationToken ct) =>
        workflow.ListAsync(request.ProjectId, request.Status, request.Page, request.PageSize, ct);
}

public sealed class GetSupervisorAssignmentQueryHandler(SupervisorAssignmentWorkflow workflow)
    : IRequestHandler<GetSupervisorAssignmentQuery, SupervisorAssignmentDto>
{
    public Task<SupervisorAssignmentDto> Handle(GetSupervisorAssignmentQuery request, CancellationToken ct) =>
        workflow.GetAsync(request.AssignmentId, ct);
}

public sealed class GetSupervisorReplacementCandidatesQueryHandler(
    ISupervisorAssignmentRepository assignments, ISupervisorCandidateRepository candidates,
    SupervisorAccessService access, TimeProvider clock)
    : IRequestHandler<GetSupervisorReplacementCandidatesQuery, PagedResult<SupervisorReplacementCandidateDto>>
{
    public async Task<PagedResult<SupervisorReplacementCandidateDto>> Handle(GetSupervisorReplacementCandidatesQuery request, CancellationToken ct)
    {
        var actor = await access.EnsureCanReadAsync(ct);
        if (!actor.HasActiveAcademicScope || !actor.Roles.Contains(AppRoles.DepartmentStaff) || actor.DepartmentId is not long departmentId)
            throw new ForbiddenException("Only department staff can preview replacement candidates.");
        var assignment = await assignments.GetAsync(request.AssignmentId, ct)
            ?? throw new NotFoundException("SupervisorAssignment", request.AssignmentId);
        if (!await assignments.CanReplaceAsync(assignment.Id, departmentId, ct))
            throw new ForbiddenException("You cannot preview replacements for this assignment.");
        if (assignment.EndedAt.HasValue || assignment.ProjectStatus != "ACTIVE")
            throw new ConflictException("Replacement requires an active assignment on an ACTIVE project.");
        var now = clock.GetUtcNow().UtcDateTime;
        var project = await candidates.GetProjectAsync(assignment.ProjectId, now, ct)
            ?? throw new NotFoundException("Project", assignment.ProjectId);
        if (!project.HasActiveSemester || !project.HasActiveAssignment || project.DepartmentIds.Count == 0
            || now < assignment.AssignedAt)
            throw new ConflictException("The project must have active academic scope and a primary assignment.");
        var policies = await candidates.GetSelectionPoliciesAsync(project.AcademicSemesterId, now, ct, execution: true);
        if (policies.Count != 1 || policies[0].MaxProjectsPerSupervisor is not > 0)
            throw new ConflictException("A supervisor capacity policy is required.");
        var search = new SupervisorCandidateSearch(project.Id, project.AcademicSemesterId, project.DepartmentIds,
            policies[0].MaxProjectsPerSupervisor!.Value, request.Search?.Trim(), null, request.Page, request.PageSize,
            assignment.AssignmentType, assignment.MajorId, assignment.SupervisorProfileId);
        var result = await candidates.SearchAsync(search, ct);
        var responsible = departmentId;
        var items = result.Items.Select(c =>
        {
            var profile = c.Profile.ToDto();
            var capacity = new SupervisorCapacity(c.ProfileLimit, search.SemesterLimit,
                c.ActiveProjects - (c.AlreadyAssignedToProject ? 1 : 0),
                c.SemesterActiveProjects - (c.AlreadyAssignedToProject ? 1 : 0));
            var dto = new SupervisorCandidateDto(profile.Id, profile.UserId, profile.FullName, profile.DepartmentId,
                profile.DepartmentName, profile.Bio, profile.Expertise, c.ActiveProjects, c.SemesterActiveProjects,
                c.ProfileLimit, search.SemesterLimit, capacity.RemainingSlots, policies[0].PeriodId,
                assignment.AssignmentType, assignment.MajorId, responsible, true, Array.Empty<string>());
            return new SupervisorReplacementCandidateDto(dto, assignment.AssignmentType, assignment.MajorId,
                responsible, true, Array.Empty<string>(),
                assignment.AssignmentType == "DISCIPLINE_MENTOR" ? "MATCHED" : "NOT_REQUIRED");
        }).ToArray();
        return new(items, result.Page, result.PageSize, result.TotalCount);
    }
}
