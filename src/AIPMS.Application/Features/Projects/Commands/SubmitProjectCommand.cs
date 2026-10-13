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
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Domain.Projects;
using MediatR;

namespace AIPMS.Application.Features.Projects.Commands;

public sealed record SubmitProjectCommand(
    long ProjectId,
    string ConcurrencyToken) : IRequest<ProjectDto>;

public sealed class SubmitProjectCommandHandler(
    IProjectRepository repository,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    TimeProvider timeProvider,
    ITeamRegistrationGuard registrationGuard,
    AIPMS.Application.Features.Topics.Abstractions.ITopicSelectionGuard? topicSelectionGuard = null)
    : IRequestHandler<SubmitProjectCommand, ProjectDto>
{
    public async Task<ProjectDto> Handle(
        SubmitProjectCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await registrationGuard.InTransactionAsync(
                token => HandleInTransactionAsync(request, token), cancellationToken);
        }
        catch (ConflictException ex)
        {
            if (currentUser.IsAuthenticated && currentUser.UserId.HasValue)
            {
                await auditTrail.RecordAsync(
                    new AuditEntry(
                        currentUser.UserId.Value,
                        "PROJECT_SUBMISSION_GUARD_DENIED",
                        "PROJECT",
                        request.ProjectId,
                        new Dictionary<string, object?>
                        {
                            ["reason"] = ex.Message
                        },
                        Outcome: "DENIED"),
                    CancellationToken.None);
            }
            throw;
        }
    }

    private async Task<ProjectDto> HandleInTransactionAsync(
        SubmitProjectCommand request,
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
            throw new ForbiddenException("Only the Team Leader can submit the project.");
        }

        // Validate state machine transition
        var currentStatus = Enum.Parse<ProjectStatus>(project.Status.Replace("_", ""), ignoreCase: true);
        if (!ProjectStateMachine.CanTransition(currentStatus, ProjectStatus.Submitted))
        {
            throw new ConflictException($"Cannot transition project from status {project.Status} to SUBMITTED.");
        }

        // Re-read leadership, roster, academic accounts and period policy under the
        // same transaction as the status update, history and audit.
        await registrationGuard.ValidateSubmissionEligibilityAsync(
            project.TeamId, project.Id, "INITIAL", null, cancellationToken);

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

        // BR-58: Re-validate topic selection if proposal source is PUBLISHED_TOPIC
        if (string.Equals(project.ProposalSource, "PUBLISHED_TOPIC", StringComparison.OrdinalIgnoreCase))
        {
            if (project.TopicId is null)
            {
                throw new ConflictException("A published-topic proposal must reference a valid topic.");
            }

            if (topicSelectionGuard is not null)
            {
                await topicSelectionGuard.ValidateTopicSelectionAsync(
                    project.TopicId.Value,
                    project.Id,
                    actorUserId,
                    cancellationToken);
            }
        }

        // Task 3: Pre-check content completeness for STUDENT_PROPOSAL
        var domainTag = project.Tags.FirstOrDefault(t => t.TagType == "DOMAIN")?.Name ?? string.Empty;
        var techTags = project.Tags.Where(t => t.TagType == "TECHNOLOGY").Select(t => t.Name).ToArray();
        var keywordTags = project.Tags.Where(t => t.TagType == "KEYWORD").Select(t => t.Name).ToArray();
        var completenessIssues = AIPMS.Domain.Topics.TopicRules.ProposalCompletenessIssues(
            project.ProposalSource,
            project.ProblemStatement,
            project.Objectives,
            project.ExpectedOutput,
            domainTag,
            techTags,
            keywordTags);
        if (completenessIssues.Count > 0)
        {
            throw new AIPMS.Domain.Exceptions.DomainException(
                $"Student proposal content is incomplete ({completenessIssues[0]}).",
                completenessIssues[0]);
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

        // Task 2: Check duplicate title and similarity for STUDENT_PROPOSAL
        AIPMS.Application.Features.Topics.DTOs.TopicSimilarityResultDto? similarity = null;
        if (string.Equals(project.ProposalSource, "STUDENT_PROPOSAL", StringComparison.Ordinal))
        {
            similarity = await repository.CheckProposalSimilarityAsync(
                project.TeamId,
                project.Title,
                keywordTags,
                domainTag,
                project.Id,
                cancellationToken);
            if (similarity.ExactMatch is not null)
            {
                throw new AIPMS.Domain.Exceptions.DomainException(
                    "Proposal title duplicates an existing topic or approved project in the last 3 periods.",
                    "DUPLICATE_TOPIC_TITLE");
            }
        }

        // Task 1 (D18): Require confirmed primary supervisor and required discipline mentors before submission
        var confirmation = await repository.GetSupervisorConfirmationAsync(project.Id, cancellationToken);
        if (!confirmation.HasConfirmedPrimarySupervisor)
        {
            throw new AIPMS.Domain.Exceptions.DomainException(
                "A primary supervisor must accept the invitation before submission.",
                "SUPERVISOR_NOT_CONFIRMED");
        }
        if (confirmation.MissingMentorMajorIds.Count > 0)
        {
            var missingMajorId = confirmation.MissingMentorMajorIds[0];
            throw new AIPMS.Domain.Exceptions.DomainException(
                $"A discipline mentor must accept the invitation for major {missingMajorId} before submission.",
                $"MENTOR_NOT_CONFIRMED:{missingMajorId}");
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

        // Audit the submission
        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "PROJECT_SUBMITTED",
                "PROJECT",
                updatedProject.Id,
                new Dictionary<string, object?>
                {
                    ["teamId"] = updatedProject.TeamId,
                    ["title"] = updatedProject.Title
                }),
            cancellationToken);

        if (similarity is { Similar.Count: > 0 })
        {
            await auditTrail.RecordAsync(
                new AuditEntry(
                    actorUserId,
                    "PROPOSAL_SIMILARITY_WARNING",
                    "PROJECT",
                    updatedProject.Id,
                    new Dictionary<string, object?>
                    {
                        ["teamId"] = updatedProject.TeamId,
                        ["similarCount"] = similarity.Similar.Count,
                        ["topMatchId"] = similarity.Similar[0].Id,
                        ["topMatchScore"] = similarity.Similar[0].Score
                    }),
                cancellationToken);
        }

        return updatedProject;
    }
}
