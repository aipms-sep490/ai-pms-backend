using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ProgressMeetings.Abstractions;
using AIPMS.Application.Features.ProgressMeetings.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.ProgressMeetings.Queries;

public sealed record GetProgressReportQuery(long Id) : IRequest<ProgressReportDto>;
public sealed record ListProgressReportsQuery(long ProjectId, string? ReportType, string? Status,
    DateOnly? From, DateOnly? To, int PageNumber = 1, int PageSize = 20) : IRequest<PagedResult<ProgressReportDto>>;
public sealed class ListProgressReportsValidator : AbstractValidator<ListProgressReportsQuery>
{
    public ListProgressReportsValidator() { RuleFor(x => x.ProjectId).GreaterThan(0); RuleFor(x => x.PageNumber).GreaterThan(0); RuleFor(x => x.PageSize).InclusiveBetween(1, 100); }
}
public sealed class GetProgressReportHandler(ICurrentUser user, IProgressMeetingRepository repository) : IRequestHandler<GetProgressReportQuery, ProgressReportDto>
{
    public async Task<ProgressReportDto> Handle(GetProgressReportQuery request, CancellationToken ct)
    { var item = await repository.GetReportAsync(request.Id, ct) ?? throw new NotFoundException("ProgressReport", request.Id); await ProgressMeetingAccess.EnsureProjectAccess(ProgressMeetingAccess.UserId(user), item.ProjectId, repository, ct); return item; }
}
public sealed class ListProgressReportsHandler(ICurrentUser user, IProgressMeetingRepository repository) : IRequestHandler<ListProgressReportsQuery, PagedResult<ProgressReportDto>>
{
    public async Task<PagedResult<ProgressReportDto>> Handle(ListProgressReportsQuery request, CancellationToken ct)
    { if (!await repository.ProjectExistsAsync(request.ProjectId, ct)) throw new NotFoundException("Project", request.ProjectId); await ProgressMeetingAccess.EnsureProjectAccess(ProgressMeetingAccess.UserId(user), request.ProjectId, repository, ct); return await repository.ListReportsAsync(request.ProjectId, new(request.ReportType?.ToUpperInvariant(), request.Status?.ToUpperInvariant(), request.From, request.To, request.PageNumber, request.PageSize), ct); }
}
