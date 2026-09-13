using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Meetings.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Meetings.Queries;

public sealed record GetMeetingByIdQuery(long Id) : IRequest<MeetingDetailDto>;

public sealed class GetMeetingByIdQueryHandler(
    IMeetingRepository repository,
    IProjectAccessService projectAccess,
    ICurrentUser currentUser) : IRequestHandler<GetMeetingByIdQuery, MeetingDetailDto>
{
    public async Task<MeetingDetailDto> Handle(GetMeetingByIdQuery query, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new UnauthorizedException("User is not authenticated.");
        var meeting = await repository.GetDetailByIdAsync(query.Id, cancellationToken)
            ?? throw new NotFoundException("Meeting", query.Id);

        if (!await projectAccess.CanAccessAsync(actorId, meeting.ProjectId, cancellationToken))
            throw new ForbiddenException("You cannot access this project's meetings.");

        return meeting;
    }
}
