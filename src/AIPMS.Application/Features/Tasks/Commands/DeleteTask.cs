using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Tasks.Abstractions;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Tasks.Commands;

public sealed record DeleteTaskCommand(long Id) : IRequest;

public sealed class DeleteTaskCommandValidator : AbstractValidator<DeleteTaskCommand>
{
    public DeleteTaskCommandValidator()
    {
        RuleFor(static x => x.Id)
            .GreaterThan(0).WithMessage("Task ID must be greater than 0.");
    }
}

public sealed class DeleteTaskCommandHandler(
    ITaskRepository repository,
    IMilestoneRepository milestoneRepository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
    : IRequestHandler<DeleteTaskCommand>
{
    public async Task Handle(
        DeleteTaskCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Retrieve existing task
        var task = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Task", request.Id);

        // Verify project is ACTIVE (strict guard)
        await executionGuard.MustBeActiveForTaskAsync(request.Id, cancellationToken);

        // Retrieve milestone
        var milestone = await milestoneRepository.GetByIdAsync(task.MilestoneId, cancellationToken)
            ?? throw new NotFoundException("Milestone", task.MilestoneId);

        var projectId = milestone.ProjectId;

        // Verify authorization: Student Leader or Assigned Supervisor
        if (!await repository.IsProjectLeaderOrSupervisorAsync(projectId, actorUserId, cancellationToken))
        {
            throw new ForbiddenException("You are not authorized to delete tasks for this project.");
        }

        // Delete Task Policy: Reject if meaningful historical execution data exists
        if (await repository.HasHistoricalDataAsync(request.Id, cancellationToken))
        {
            throw new ConflictException("Task cannot be deleted because it contains historical execution data, assignees, or dependencies. Update the status to CANCELLED instead.");
        }

        await repository.DeleteAsync(request.Id, cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                "TASK_DELETED",
                "TASK",
                request.Id,
                new Dictionary<string, object?>
                {
                    ["projectId"] = projectId,
                    ["milestoneId"] = task.MilestoneId,
                    ["title"] = task.Title
                }),
            cancellationToken);
    }
}
