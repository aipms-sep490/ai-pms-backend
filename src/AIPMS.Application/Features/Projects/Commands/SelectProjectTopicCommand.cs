using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Topics.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Projects.Commands;

public sealed record SelectProjectTopicCommand(
    long ProjectId,
    long TopicId,
    string ConcurrencyToken) : IRequest<ProjectDto>;

public sealed class SelectProjectTopicCommandHandler(
    IProjectRepository projectRepository,
    ITopicSelectionGuard topicSelectionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail) : IRequestHandler<SelectProjectTopicCommand, ProjectDto>
{
    public static Func<Task>? AfterPreflightHook { get; set; }

    public async Task<ProjectDto> Handle(
        SelectProjectTopicCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // 1. Retrieve project (preflight)
        var preflightProject = await projectRepository.GetByIdAsync(request.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);

        // 2. Caller has Project Draft write permission (only Team Leader can modify draft)
        if (!await projectRepository.IsTeamLeaderAsync(preflightProject.TeamId, actorUserId, cancellationToken))
        {
            throw new ForbiddenException("Only the Team Leader can select a topic for the project.");
        }

        // 3. Project Draft is still editable
        if (preflightProject.Status != "DRAFT" && preflightProject.Status != "REVISION_REQUIRED")
        {
            throw new ConflictException("Cannot select a topic on a submitted or non-editable project proposal.");
        }

        // 4. Validate Topic selection via TopicSelectionGuard
        await topicSelectionGuard.ValidateTopicSelectionAsync(
            request.TopicId,
            request.ProjectId,
            actorUserId,
            cancellationToken);

        // Preflight hook for deterministic concurrency / race testing
        if (AfterPreflightHook is not null)
        {
            await AfterPreflightHook();
        }

        // 5 & 6. Persist topic selection and audit atomically in the same transaction
        return await projectRepository.InTransactionAsync(async ct =>
        {
            // 1. Lock project, team, team members, and topic rows
            await projectRepository.LockProjectAndTopicAsync(request.ProjectId, request.TopicId, ct);

            // 2. Re-verify project exists and is still editable under lock
            var project = await projectRepository.GetByIdAsync(request.ProjectId, ct)
                ?? throw new NotFoundException("Project", request.ProjectId);

            if (project.Status != "DRAFT" && project.Status != "REVISION_REQUIRED")
            {
                throw new ConflictException("Cannot select a topic on a submitted or non-editable project proposal.");
            }

            // 3. Re-verify team leadership under lock (throw ForbiddenException if stale/former leader)
            if (!await projectRepository.IsTeamLeaderAsync(project.TeamId, actorUserId, ct))
            {
                throw new ForbiddenException("Only the Team Leader can select a topic for the project.");
            }

            // 4. Re-verify topic selectability / status PUBLISHED under lock (throw ConflictException if closed/unregistered/invalid)
            await topicSelectionGuard.ValidateTopicSelectionAsync(
                request.TopicId,
                request.ProjectId,
                actorUserId,
                ct);

            // 5. Select topic (verifies concurrency token, persists topic_id + proposal_source)
            var result = await projectRepository.SelectTopicAsync(
                request.ProjectId,
                request.TopicId,
                request.ConcurrencyToken,
                ct);

            // 6. Record audit entry in the same transaction
            await auditTrail.RecordAsync(
                new AuditEntry(
                    actorUserId,
                    "PROJECT_TOPIC_SELECTED",
                    "PROJECT",
                    result.Id,
                    new Dictionary<string, object?>
                    {
                        ["projectId"] = result.Id,
                        ["topicId"] = request.TopicId,
                        ["proposalSource"] = "PUBLISHED_TOPIC"
                    }),
                ct);

            return result;
        }, cancellationToken);
    }
}
