using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Tasks.Commands;

public sealed record RestoreTaskCommand(long Id) : IRequest<TaskDto>;

public sealed class RestoreTaskCommandHandler(
    ITaskRepository repository,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    TimeProvider? clock = null) : IRequestHandler<RestoreTaskCommand, TaskDto>
{
    public async Task<TaskDto> Handle(RestoreTaskCommand request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            throw new UnauthorizedException();

        var actorUserId = currentUser.UserId.Value;
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var restored = await repository.RestoreAsync(request.Id, actorUserId, now, cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "TASK_RESTORED",
                "TASK",
                restored.Id,
                new Dictionary<string, object?> { ["milestoneId"] = restored.MilestoneId, ["title"] = restored.Title }),
            cancellationToken);

        return restored;
    }
}
