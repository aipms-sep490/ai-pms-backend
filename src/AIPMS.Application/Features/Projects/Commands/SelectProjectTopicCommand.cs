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
    public async Task<ProjectDto> Handle(
        SelectProjectTopicCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // 1. Retrieve project
        var project = await projectRepository.GetByIdAsync(request.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);

        // 2. Caller has Project Draft write permission (only Team Leader can modify draft)
        if (!await projectRepository.IsTeamLeaderAsync(project.TeamId, actorUserId, cancellationToken))
        {
            throw new ForbiddenException("Only the Team Leader can select a topic for the project.");
        }

        // 3. Project Draft is still editable
        if (project.Status != "DRAFT" && project.Status != "REVISION_REQUIRED")
        {
            throw new ConflictException("Cannot select a topic on a submitted or non-editable project proposal.");
        }

        // 4. Validate Topic selection via TopicSelectionGuard
        await topicSelectionGuard.ValidateTopicSelectionAsync(
            request.TopicId,
            request.ProjectId,
            actorUserId,
            cancellationToken);

        // 5 & 6. Persist topic selection and audit atomically in the same transaction
        return await projectRepository.InTransactionAsync(async ct =>
        {
            var result = await projectRepository.SelectTopicAsync(
                request.ProjectId,
                request.TopicId,
                request.ConcurrencyToken,
                ct);

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
