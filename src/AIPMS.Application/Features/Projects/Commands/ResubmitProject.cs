using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Domain.Entities;
using AIPMS.Domain.Enums;
using MediatR;

namespace AIPMS.Application.Features.Projects.Commands;

public sealed record ResubmitProjectCommand(
    long ProjectId,
    string ConcurrencyToken) : IRequest<ProjectDto>;

public sealed class ResubmitProjectCommandHandler(
    IProjectRepository repository,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<ResubmitProjectCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(
        ResubmitProjectCommand request,
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
            throw new ForbiddenException("Only the Team Leader can resubmit the project.");
        }

        // Validate state machine transition
        var currentStatus = Enum.Parse<ProjectStatus>(project.Status.Replace("_", ""), ignoreCase: true);
        if (!ProjectStateMachine.CanTransition(currentStatus, ProjectStatus.Submitted))
        {
            throw new ConflictException($"Cannot transition project from status {project.Status} to SUBMITTED.");
        }

        // BR-51: Check team status
        if (!await repository.IsTeamEligibleAsync(project.TeamId, cancellationToken))
        {
            throw new ConflictException("Your team is not in ELIGIBLE status to submit a project proposal.");
        }

        // BR-51: Check registration period/window
        var semesterId = await repository.GetSemesterIdByTeamIdAsync(project.TeamId, cancellationToken);
        if (semesterId is null)
        {
            throw new NotFoundException("Semester", project.TeamId);
        }

        var currentUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (!await repository.IsSemesterRegistrationOpenAsync(semesterId.Value, currentUtc, cancellationToken))
        {
            throw new ConflictException("The project registration period is currently closed or the deadline has passed.");
        }

        // BR-51: Check required fields are complete
        if (string.IsNullOrWhiteSpace(project.Title) ||
            string.IsNullOrWhiteSpace(project.ProblemStatement) ||
            string.IsNullOrWhiteSpace(project.Objectives) ||
            string.IsNullOrWhiteSpace(project.ExpectedOutput))
        {
            throw new ConflictException("All structured registration fields (Title, Problem Statement, Objectives, Expected Output) are required before submission.");
        }

        if (project.Majors.Count == 0)
        {
            throw new ConflictException("At least one required major must be configured before submission.");
        }

        if (!project.Tags.Any(t => t.TagType == "DOMAIN") ||
            !project.Tags.Any(t => t.TagType == "TECHNOLOGY") ||
            !project.Tags.Any(t => t.TagType == "KEYWORD"))
        {
            throw new ConflictException("The project proposal must have at least one Domain, one Technology, and one Keyword tag defined.");
        }

        // Update project status to SUBMITTED and write history (all within repository transaction)
        var updatedProject = await repository.UpdateStatusAsync(
            request.ProjectId,
            request.ConcurrencyToken,
            project.Status,
            "SUBMITTED",
            actorUserId,
            null,
            cancellationToken);

        // Audit the resubmission
        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "PROJECT_RESUBMITTED",
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
