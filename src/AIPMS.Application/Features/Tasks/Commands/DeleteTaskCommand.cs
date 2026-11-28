using AIPMS.Application.Abstractions.Projects;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Tasks.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Tasks.Commands;

public sealed record DeleteTaskCommand(long Id, string? ConcurrencyToken = null) : IRequest, IExecutionMutation
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<ExecutionWrite> Writes => [new(ExecutionResource.Task, Id, ConcurrencyToken)];
}

public sealed class DeleteTaskCommandHandler(
    ITaskRepository repository,
    IMilestoneRepository milestoneRepository,
    IProjectExecutionGuard executionGuard,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    TimeProvider? clock = null)
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

        // TK-W2-02: Student Leader / Assigned Supervisor can delete any non-historical task;
        // active STUDENT_MEMBER can soft-delete non-core tasks they created or are assigned to within their own majorScope.
        var isLeaderOrSupervisor = await repository.IsProjectLeaderOrSupervisorAsync(projectId, actorUserId, cancellationToken);
        if (!isLeaderOrSupervisor)
        {
            if (task.IsCoreGateTask || task.GateRequirementId.HasValue)
            {
                throw new ForbiddenException("Only the team leader or assigned primary supervisor can delete core gate tasks.");
            }

            var isActiveMember = await repository.IsUserActiveTeamMemberAsync(projectId, actorUserId, cancellationToken);
            var isCreatorOrAssignee = task.CreatedBy == actorUserId
                || await repository.IsTaskAssigneeAsync(request.Id, actorUserId, cancellationToken);
            if (!isActiveMember || !isCreatorOrAssignee)
            {
                throw new ForbiddenException("You are not authorized to delete tasks for this project.");
            }

            var taskMajors = await repository.GetTaskMajorIdsAsync(request.Id, cancellationToken);
            if (taskMajors.Count > 0)
            {
                var actorMajor = await repository.GetUserMajorIdAsync(projectId, actorUserId, cancellationToken);
                if (actorMajor.HasValue && taskMajors.Any(majorId => majorId != actorMajor.Value))
                {
                    throw new ForbiddenException("Team members can only delete tasks within their own major scope.");
                }
            }
        }

        // Delete Task Policy: Reject if core gate task or meaningful historical execution data or locked evidence references exist
        if (task.IsCoreGateTask || await repository.HasHistoricalDataAsync(request.Id, cancellationToken))
        {
            throw new ConflictException(
                "Task cannot be deleted because it is a core gate task or contains historical execution data, locked evidence references, assignees, or dependencies. Update the status to CANCELLED instead.",
                WorkflowErrorCodes.RestrictedDelete);
        }

        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        await repository.SoftDeleteAsync(request.Id, actorUserId, now, cancellationToken);

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
