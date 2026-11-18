using AIPMS.Application.Features.Tasks.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Tasks.Queries;

public sealed record GetTaskDependencyImpactsQuery(long TaskId) : IRequest<TaskDependencyImpactsDto>;

public sealed class GetTaskDependencyImpactsQueryHandler(ITaskDependencyImpactService service)
    : IRequestHandler<GetTaskDependencyImpactsQuery, TaskDependencyImpactsDto>
{
    public Task<TaskDependencyImpactsDto> Handle(GetTaskDependencyImpactsQuery request, CancellationToken ct) =>
        service.GetAsync(request.TaskId, ct);
}
