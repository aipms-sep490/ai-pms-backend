using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Projects.Queries;

public sealed record GetProjectProgressAnalysisQuery(long ProjectId) : IRequest<ProjectProgressAnalysisDto>;

public sealed class GetProjectProgressAnalysisQueryHandler(IScopedProgressAnalysisService analysis)
    : IRequestHandler<GetProjectProgressAnalysisQuery, ProjectProgressAnalysisDto>
{
    public Task<ProjectProgressAnalysisDto> Handle(GetProjectProgressAnalysisQuery request, CancellationToken cancellationToken)
        => analysis.AnalyzeAsync(request.ProjectId, false, cancellationToken);
}
