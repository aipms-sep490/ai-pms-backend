using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Milestones.Queries;

public sealed record ProjectExecutionWindowDto(
    long ProjectPeriodId,
    string Code,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record GetProjectExecutionWindowQuery(long ProjectId) : IRequest<ProjectExecutionWindowDto?>;

public sealed class GetProjectExecutionWindowQueryHandler(
    IExecutionWindowService executionWindowService,
    IProjectAccessService projectAccessService,
    ICurrentUser currentUser)
    : IRequestHandler<GetProjectExecutionWindowQuery, ProjectExecutionWindowDto?>
{
    private static DateOnly AcademicDate(DateTime utc) =>
        DateOnly.FromDateTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(7));

    public async Task<ProjectExecutionWindowDto?> Handle(
        GetProjectExecutionWindowQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            throw new UnauthorizedException();

        if (!await projectAccessService.CanAccessAsync(currentUser.UserId.Value, request.ProjectId, cancellationToken))
            throw new ForbiddenException("You do not have access to this project.");

        var window = await executionWindowService.GetExecutionWindowAsync(request.ProjectId, cancellationToken);
        if (window is null) return null;

        return new ProjectExecutionWindowDto(
            window.ProjectPeriodId,
            window.Code,
            window.Name,
            AcademicDate(window.StartAt),
            AcademicDate(window.EndAt));
    }
}
