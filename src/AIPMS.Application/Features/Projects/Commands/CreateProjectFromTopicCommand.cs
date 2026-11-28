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
using AIPMS.Application.Features.Topics.Abstractions;
using AIPMS.Application.Features.Topics.Services;
using MediatR;

namespace AIPMS.Application.Features.Projects.Commands;

public sealed record CreateProjectFromTopicCommand(long TopicId) : IRequest<ProjectDto>;

public sealed class CreateProjectFromTopicCommandHandler(
    IProjectRepository repository,
    TopicWorkflow topicWorkflow,
    ITopicSelectionGuard topicSelectionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    ITeamRegistrationGuard registrationGuard,
    TimeProvider timeProvider) : IRequestHandler<CreateProjectFromTopicCommand, ProjectDto>
{
    public Task<ProjectDto> Handle(CreateProjectFromTopicCommand request, CancellationToken cancellationToken) =>
        registrationGuard.InTransactionAsync(token => HandleInTransactionAsync(request, token), cancellationToken);

    private async Task<ProjectDto> HandleInTransactionAsync(CreateProjectFromTopicCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            throw new UnauthorizedException();

        var actorUserId = currentUser.UserId.Value;
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var semesterId = await repository.GetActiveRegistrationSemesterIdAsync(actorUserId, utcNow, ct);
        if (semesterId is null)
            throw new ConflictException("The project registration period is currently closed.");

        var teamId = await repository.GetUserActiveTeamIdAsync(actorUserId, semesterId.Value, ct);
        if (teamId is null)
            throw new ForbiddenException("You must belong to an active, eligible team in the current semester to create a project draft.");

        if (!await repository.IsTeamLeaderAsync(teamId.Value, actorUserId, ct))
            throw new ForbiddenException("Only the Team Leader can select a topic and create a project draft.");

        await registrationGuard.ValidateAsync(teamId.Value, ct);

        if (await repository.HasActiveProjectAsync(teamId.Value, ct))
            throw new ConflictException("The team already has an active or unfinished project proposal.");

        // Pessimistic lock on topic row before checking max_teams and creating draft
        await repository.LockTopicAsync(request.TopicId, ct);

        var topic = await topicWorkflow.GetForSelection(request.TopicId, ct)
            ?? throw new NotFoundException("Topic", request.TopicId);

        if (!string.Equals(topic.Status, "PUBLISHED", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Only published topics can be selected.");

        var selectedCount = await repository.CountNonRejectedTopicSelectionsAsync(request.TopicId, null, ct);
        if (selectedCount >= topic.MaxTeams)
        {
            throw new ConflictException(
                $"This topic has reached its maximum team capacity ({topic.MaxTeams}).",
                WorkflowErrorCodes.TopicCapacityFull);
        }

        var majorIds = topic.Requirements.Select(r => r.MajorId).Distinct().ToArray();
        var domain = string.IsNullOrWhiteSpace(topic.Domain) ? "General" : topic.Domain.Trim();
        var draft = await repository.CreateDraftAsync(
            teamId.Value,
            actorUserId,
            topic.Title,
            topic.Description,
            topic.Objectives,
            topic.ProblemStatement,
            topic.ExpectedOutput,
            majorIds,
            domain,
            topic.Technologies,
            topic.Keywords,
            ct,
            request.TopicId);

        await repository.LockProjectAndTopicAsync(draft.Id, request.TopicId, ct);
        await topicSelectionGuard.ValidateTopicSelectionAsync(request.TopicId, draft.Id, actorUserId, ct);
        var linked = await repository.SelectTopicAsync(draft.Id, request.TopicId, draft.ConcurrencyToken, ct);

        await auditTrail.RecordAsync(new AuditEntry(
            actorUserId,
            "PROJECT_DRAFT_CREATED_FROM_TOPIC",
            "PROJECT",
            linked.Id,
            new Dictionary<string, object?>
            {
                ["teamId"] = teamId.Value,
                ["topicId"] = request.TopicId,
                ["title"] = linked.Title
            }), ct);

        return linked;
    }
}
