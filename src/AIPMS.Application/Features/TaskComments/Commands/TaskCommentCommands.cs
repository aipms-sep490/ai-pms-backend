using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.TaskComments.Abstractions;
using AIPMS.Application.Features.TaskComments.DTOs;
using MediatR;
namespace AIPMS.Application.Features.TaskComments.Commands;
public sealed record CreateTaskCommentCommand(long TaskId, string Content) : IRequest<TaskCommentDto>;
public sealed class CreateTaskCommentHandler(ITaskCommentRepository repository, ICurrentUser currentUser, IAuditTrail audit, TimeProvider clock) : IRequestHandler<CreateTaskCommentCommand, TaskCommentDto>
{
    public Task<TaskCommentDto> Handle(CreateTaskCommentCommand r, CancellationToken ct) => repository.InTransactionAsync(async token =>
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedException();
        var access = await repository.GetAccessAsync(r.TaskId, actor, token) ?? throw new NotFoundException("Task", r.TaskId);
        if (access.ProjectStatus != "ACTIVE") throw new ConflictException("Comments require an ACTIVE project.");
        if (!access.IsActiveMember && !access.IsLeader && !access.IsMentor) throw new ForbiddenException("You cannot comment on this task.");
        var content = r.Content?.Trim() ?? "";
        if (content.Length is < 1 or > 4000) throw new ValidationException(new Dictionary<string, string[]> { ["content"] = ["Comment must contain 1 to 4000 characters."] });
        var result = await repository.CreateAsync(r.TaskId, actor, content, clock.GetUtcNow().UtcDateTime, token);
        await audit.RecordAsync(new(actor, "TASK_COMMENT_CREATED", "TASK_COMMENT", result.Id, new Dictionary<string, object?> { ["taskId"] = r.TaskId }), token);
        return result;
    }, ct);
}
