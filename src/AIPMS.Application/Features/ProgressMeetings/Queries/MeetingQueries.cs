using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ProgressMeetings.Abstractions;
using AIPMS.Application.Features.ProgressMeetings.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.ProgressMeetings.Queries;
public sealed record GetMeetingQuery(long Id) : IRequest<MeetingDto>;
public sealed record ListMeetingsQuery(long ProjectId, string? Status, DateTime? From, DateTime? To,
    int PageNumber = 1, int PageSize = 20) : IRequest<PagedResult<MeetingDto>>;
public sealed class ListMeetingsValidator : AbstractValidator<ListMeetingsQuery>
{ public ListMeetingsValidator() { RuleFor(x => x.ProjectId).GreaterThan(0); RuleFor(x => x.PageNumber).GreaterThan(0); RuleFor(x => x.PageSize).InclusiveBetween(1, 100); } }
public sealed class GetMeetingHandler(ICurrentUser user, IProgressMeetingRepository repository) : IRequestHandler<GetMeetingQuery, MeetingDto>
{ public async Task<MeetingDto> Handle(GetMeetingQuery request, CancellationToken ct) { var item = await repository.GetMeetingAsync(request.Id, ct) ?? throw new NotFoundException("Meeting", request.Id); await ProgressMeetingAccess.EnsureProjectAccess(ProgressMeetingAccess.UserId(user), item.ProjectId, repository, ct); return item; } }
public sealed class ListMeetingsHandler(ICurrentUser user, IProgressMeetingRepository repository) : IRequestHandler<ListMeetingsQuery, PagedResult<MeetingDto>>
{ public async Task<PagedResult<MeetingDto>> Handle(ListMeetingsQuery request, CancellationToken ct) { if (!await repository.ProjectExistsAsync(request.ProjectId, ct)) throw new NotFoundException("Project", request.ProjectId); await ProgressMeetingAccess.EnsureProjectAccess(ProgressMeetingAccess.UserId(user), request.ProjectId, repository, ct); return await repository.ListMeetingsAsync(request.ProjectId, new(request.Status?.ToUpperInvariant(), request.From, request.To, request.PageNumber, request.PageSize), ct); } }
