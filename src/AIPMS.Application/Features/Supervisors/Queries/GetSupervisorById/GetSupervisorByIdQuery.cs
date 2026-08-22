using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Queries.GetSupervisorById;

public sealed record GetSupervisorByIdQuery(long Id) : IRequest<SupervisorDetailDto>;

public sealed class GetSupervisorByIdQueryHandler(ISupervisorRepository supervisorRepository)
    : IRequestHandler<GetSupervisorByIdQuery, SupervisorDetailDto>
{
    public async Task<SupervisorDetailDto> Handle(
        GetSupervisorByIdQuery request,
        CancellationToken cancellationToken)
    {
        var supervisor = await supervisorRepository.GetByIdAsync(request.Id, cancellationToken);
        if (supervisor == null)
        {
            throw new NotFoundException("Supervisor", request.Id);
        }

        return supervisor;
    }
}
