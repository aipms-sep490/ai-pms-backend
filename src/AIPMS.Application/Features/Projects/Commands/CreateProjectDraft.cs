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

public sealed record CreateProjectDraftCommand(
    string Title,
    string? Description,
    string? Objectives,
    string? ProblemStatement,
    string? ExpectedOutput,
    IReadOnlyList<long> RequiredMajorIds,
    string Domain,
    IReadOnlyList<string> Technologies,
    IReadOnlyList<string> Keywords) : IRequest<ProjectDto>;

public sealed class CreateProjectDraftCommandHandler(
    IProjectRepository repository,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<CreateProjectDraftCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(
        CreateProjectDraftCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Get the active registration semester
        var semesterId = await repository.GetActiveRegistrationSemesterIdAsync(DateTime.UtcNow, cancellationToken);
        if (semesterId is null)
        {
            throw new ConflictException("The project registration period is currently closed.");
        }

        // Get the active team of the current user for this semester
        var teamId = await repository.GetUserActiveTeamIdAsync(actorUserId, semesterId.Value, cancellationToken);
        if (teamId is null)
        {
            throw new ForbiddenException("You must belong to an active, eligible team in the current semester to create a project draft.");
        }

        // Verify user is the Team Leader
        if (!await repository.IsTeamLeaderAsync(teamId.Value, actorUserId, cancellationToken))
        {
            throw new ForbiddenException("Only the Team Leader can create a project draft.");
        }

        // Rule: One unfinished project per team
        if (await repository.HasActiveProjectAsync(teamId.Value, cancellationToken))
        {
            throw new ConflictException("The team already has an active or unfinished project proposal.");
        }

        // Validate majors exist
        if (request.RequiredMajorIds.Count > 0 && 
            !await repository.ValidateMajorsExistAsync(request.RequiredMajorIds, cancellationToken))
        {
            throw new NotFoundException("Major", string.Join(", ", request.RequiredMajorIds));
        }

        // Create the draft
        var project = await repository.CreateDraftAsync(
            teamId.Value,
            actorUserId,
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

        // Audit the draft creation
        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "PROJECT_DRAFT_CREATED",
                "PROJECT",
                project.Id,
                new Dictionary<string, object?>
                {
                    ["teamId"] = teamId.Value,
                    ["title"] = project.Title
                }),
            cancellationToken);

        return project;
    }
}
