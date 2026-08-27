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

public sealed record UpdateProjectDraftCommand(
    long ProjectId,
    string ConcurrencyToken,
    string Title,
    string? Description,
    string? Objectives,
    string? ProblemStatement,
    string? ExpectedOutput,
    IReadOnlyList<long> RequiredMajorIds,
    string Domain,
    IReadOnlyList<string> Technologies,
    IReadOnlyList<string> Keywords) : IRequest<ProjectDto>;

public sealed class UpdateProjectDraftCommandHandler(
    IProjectRepository repository,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<UpdateProjectDraftCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(
        UpdateProjectDraftCommand request,
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
            throw new ForbiddenException("Only the Team Leader can update the project draft.");
        }

        // Project must be editable
        if (project.Status != "DRAFT" && project.Status != "REVISION_REQUIRED")
        {
            throw new ConflictException("Project details can only be edited while in DRAFT or REVISION_REQUIRED status.");
        }

        // Validate majors exist
        if (request.RequiredMajorIds.Count > 0 && 
            !await repository.ValidateMajorsExistAsync(request.RequiredMajorIds, cancellationToken))
        {
            throw new NotFoundException("Major", string.Join(", ", request.RequiredMajorIds));
        }

        // Update the project
        var updatedProject = await repository.UpdateDraftAsync(
            request.ProjectId,
            request.ConcurrencyToken,
            request.Title,
            request.Description,
            request.Objectives,
            request.ProblemStatement,
            request.ExpectedOutput,
            request.RequiredMajorIds,
            request.Domain,
            request.Technologies,
            request.Keywords,
            cancellationToken);

        // Audit the draft update
        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "PROJECT_DRAFT_UPDATED",
                "PROJECT",
                updatedProject.Id,
                new Dictionary<string, object?>
                {
                    ["teamId"] = updatedProject.TeamId,
                    ["title"] = updatedProject.Title
                }),
            cancellationToken);

        return updatedProject;
    }
}
