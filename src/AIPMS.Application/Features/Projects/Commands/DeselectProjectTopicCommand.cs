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

public sealed record DeselectProjectTopicCommand(long ProjectId) : IRequest<ProjectDto>;

public sealed class DeselectProjectTopicCommandHandler(
    IProjectRepository repository,
    ICurrentUser currentUser,
    IAuditTrail auditTrail) : IRequestHandler<DeselectProjectTopicCommand, ProjectDto>
{
    public Task<ProjectDto> Handle(DeselectProjectTopicCommand request, CancellationToken cancellationToken) =>
        repository.InTransactionAsync(async ct =>
        {
            if (!currentUser.IsAuthenticated || currentUser.UserId is null)
                throw new UnauthorizedException();
            var actorUserId = currentUser.UserId.Value;

            var project = await repository.GetByIdAsync(request.ProjectId, ct)
                ?? throw new NotFoundException("Project", request.ProjectId);

            if (!await repository.IsTeamLeaderAsync(project.TeamId, actorUserId, ct))
                throw new ForbiddenException("Only the Team Leader can deselect the project's topic.");

            if (project.Status is not ("DRAFT" or "REVISION_REQUIRED"))
                throw new ConflictException("Topic can only be deselected while the project is in DRAFT or REVISION_REQUIRED status.");

            var previousTopicId = project.TopicId;
            var updated = await repository.DeselectTopicAsync(request.ProjectId, ct);

            await auditTrail.RecordAsync(new AuditEntry(
                actorUserId,
                "PROJECT_TOPIC_DESELECTED",
                "PROJECT",
                updated.Id,
                new Dictionary<string, object?>
                {
                    ["previousTopicId"] = previousTopicId,
                    ["proposalSource"] = updated.ProposalSource
                }), ct);

            return updated;
        }, cancellationToken);
}
