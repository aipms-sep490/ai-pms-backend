using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Projects.Queries;

public sealed record GetProjectAcademicReviewQuery(long ProjectId) : IRequest<ProjectAcademicReviewDto>;

public sealed class GetProjectAcademicReviewQueryHandler(ISender sender, IProjectRepository repository)
    : IRequestHandler<GetProjectAcademicReviewQuery, ProjectAcademicReviewDto>
{
    public async Task<ProjectAcademicReviewDto> Handle(GetProjectAcademicReviewQuery request, CancellationToken ct)
    {
        await sender.Send(new GetProjectByIdQuery(request.ProjectId), ct);
        return await repository.GetAcademicReviewAsync(request.ProjectId, ct);
    }
}
