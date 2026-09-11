using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Services;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Queries;

public sealed record GetSupervisorByIdQuery(long ProfileId) : IRequest<SupervisorProfileDto>;

public sealed class GetSupervisorByIdQueryHandler(ISupervisorProfileRepository repository, SupervisorAccessService access)
    : IRequestHandler<GetSupervisorByIdQuery, SupervisorProfileDto>
{
    public async Task<SupervisorProfileDto> Handle(GetSupervisorByIdQuery request, CancellationToken ct)
    {
        await access.EnsureCanReadAsync(ct);
        return (await repository.GetAsync(request.ProfileId, ct)
            ?? throw new NotFoundException("SupervisorProfile", request.ProfileId)).ToDto();
    }
}
