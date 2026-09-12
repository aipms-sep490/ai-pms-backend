using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.Results.Services;
using MediatR;

namespace AIPMS.Application.Features.Results.Commands;

public sealed record ConfigureResultPolicyCommand(long ProjectId, ConfigureResultPolicyRequest Input) : IRequest<ResultPolicyDto>;
public sealed record PublishProjectResultCommand(long ProjectId, PublishProjectResultRequest Input) : IRequest<ProjectResultDto>;
public sealed class ConfigureResultPolicyHandler(ProjectResultWorkflow workflow) : IRequestHandler<ConfigureResultPolicyCommand, ResultPolicyDto>
{
    public Task<ResultPolicyDto> Handle(ConfigureResultPolicyCommand request, CancellationToken ct) => workflow.Configure(request.ProjectId, request.Input, ct);
}
public sealed class PublishProjectResultHandler(ProjectResultWorkflow workflow) : IRequestHandler<PublishProjectResultCommand, ProjectResultDto>
{
    public Task<ProjectResultDto> Handle(PublishProjectResultCommand request, CancellationToken ct) => workflow.Publish(request.ProjectId, request.Input, ct);
}
