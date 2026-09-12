using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ProgressReports.Abstractions;
using AIPMS.Application.Features.ProgressReports.DTOs;
using MediatR;

namespace AIPMS.Application.Features.ProgressReports.Queries;

public sealed record GetProgressReportByIdQuery(long Id) : IRequest<ProgressReportDetailDto>;

public sealed class GetProgressReportByIdQueryHandler(
    IProgressReportRepository repository,
    IProjectAccessService projectAccess,
    ICurrentUser currentUser) : IRequestHandler<GetProgressReportByIdQuery, ProgressReportDetailDto>
{
    public async Task<ProgressReportDetailDto> Handle(GetProgressReportByIdQuery query, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var report = await repository.GetDetailByIdAsync(query.Id, cancellationToken)
            ?? throw new NotFoundException("ProgressReport", query.Id);

        if (!await projectAccess.CanAccessAsync(actorId, report.ProjectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's progress reports.");

        return report;
    }
}
