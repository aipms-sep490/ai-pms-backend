using AIPMS.Application.Abstractions.Projects;
using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Tasks.Commands;

public sealed record ResolveTaskDependencyImpactCommand(
    long TaskId, long ImpactId, string Decision, string Reason, string? ConcurrencyToken = null)
    : IRequest<TaskDto>, IExecutionMutation
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<ExecutionWrite> Writes => [new(ExecutionResource.Task, TaskId, ConcurrencyToken)];
}

public sealed class ResolveTaskDependencyImpactCommandHandler(ITaskDependencyImpactService service)
    : IRequestHandler<ResolveTaskDependencyImpactCommand, TaskDto>
{
    public Task<TaskDto> Handle(ResolveTaskDependencyImpactCommand request, CancellationToken ct) =>
        service.ResolveAsync(request.TaskId, request.ImpactId, request.Decision, request.Reason, ct);
}
