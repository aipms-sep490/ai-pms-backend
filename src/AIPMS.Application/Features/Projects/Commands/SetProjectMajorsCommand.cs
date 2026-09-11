using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Projects.Commands;

public sealed record SetProjectMajorsCommand(
    long ProjectId,
    string ConcurrencyToken,
    IReadOnlyList<long> RequiredMajorIds) : IRequest<ProjectDto>;

public sealed class SetProjectMajorsCommandHandler(
    IProjectRepository repository,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<SetProjectMajorsCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(
        SetProjectMajorsCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Retrieve the project
        var project = await repository.GetByIdAsync(request.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);

        // Verify the user is the Team Leader of this project's team
        if (!await repository.IsTeamLeaderAsync(project.TeamId, actorUserId, cancellationToken))
        {
            throw new ForbiddenException("Only the Team Leader can configure the project majors.");
        }

        // Project must be editable
        if (project.Status != "DRAFT" && project.Status != "REVISION_REQUIRED")
        {
            throw new ConflictException("Project majors can only be configured while the project is in DRAFT or REVISION_REQUIRED status.");
        }

        // Validate majors exist
        if (request.RequiredMajorIds.Count > 0 && 
            !await repository.ValidateMajorsExistAsync(request.RequiredMajorIds, cancellationToken))
        {
            throw new NotFoundException("Major", string.Join(", ", request.RequiredMajorIds));
        }

        // We can reuse repository's UpdateDraftAsync by keeping other fields unchanged!
        // To do this, we read project's tags and convert to raw strings.
        var domain = project.Tags.FirstOrDefault(t => t.TagType == "DOMAIN")?.Name ?? string.Empty;
        var technologies = project.Tags.Where(t => t.TagType == "TECHNOLOGY").Select(t => t.Name).ToList();
        var keywords = project.Tags.Where(t => t.TagType == "KEYWORD").Select(t => t.Name).ToList();

        var updatedProject = await repository.UpdateDraftAsync(
            request.ProjectId,
            request.ConcurrencyToken,
            project.Title,
            project.Description,
            project.Objectives,
            project.ProblemStatement,
            project.ExpectedOutput,
            request.RequiredMajorIds,
            domain,
            technologies,
            keywords,
            cancellationToken);

        // Audit the action
        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "PROJECT_MAJORS_CONFIGURED",
                "PROJECT",
                updatedProject.Id,
                new Dictionary<string, object?>
                {
                    ["teamId"] = updatedProject.TeamId,
                    ["title"] = updatedProject.Title,
                    ["majorIds"] = request.RequiredMajorIds
                }),
            cancellationToken);

        return updatedProject;
    }
}
