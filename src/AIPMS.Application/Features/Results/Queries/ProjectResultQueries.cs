using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.Results.Services;
using MediatR;

namespace AIPMS.Application.Features.Results.Queries;

public sealed record GetResultPolicyQuery(long ProjectId) : IRequest<ResultPolicyDto?>;
public sealed record PreviewProjectResultQuery(long ProjectId) : IRequest<ProjectResultPreviewDto>;
public sealed record GetProjectResultQuery(long ProjectId) : IRequest<ProjectResultDto>;
public sealed class GetResultPolicyHandler(ProjectResultWorkflow workflow) : IRequestHandler<GetResultPolicyQuery, ResultPolicyDto?>
{
    public Task<ResultPolicyDto?> Handle(GetResultPolicyQuery request, CancellationToken ct) => workflow.Policy(request.ProjectId, ct);
}
public sealed class PreviewProjectResultHandler(ProjectResultWorkflow workflow) : IRequestHandler<PreviewProjectResultQuery, ProjectResultPreviewDto>
{
    public Task<ProjectResultPreviewDto> Handle(PreviewProjectResultQuery request, CancellationToken ct) => workflow.Preview(request.ProjectId, ct);
}
public sealed class GetProjectResultHandler(ProjectResultWorkflow workflow) : IRequestHandler<GetProjectResultQuery, ProjectResultDto>
{
    public Task<ProjectResultDto> Handle(GetProjectResultQuery request, CancellationToken ct) => workflow.Get(request.ProjectId, ct);
}
