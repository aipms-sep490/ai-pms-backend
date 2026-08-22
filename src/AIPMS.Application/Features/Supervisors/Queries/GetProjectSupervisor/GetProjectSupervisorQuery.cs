using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using MediatR;
using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.Supervisors.Queries.GetProjectSupervisor;

public sealed record GetProjectSupervisorQuery(long ProjectId) : IRequest<SupervisorDto?>;

public sealed class GetProjectSupervisorQueryHandler(
    ISupervisorAssignmentRepository assignmentRepository,
    ISupervisorRepository supervisorRepository) : IRequestHandler<GetProjectSupervisorQuery, SupervisorDto?>
{
    public async Task<SupervisorDto?> Handle(
        GetProjectSupervisorQuery request,
        CancellationToken cancellationToken)
    {
        var activeAssignment = await assignmentRepository.GetActiveAssignmentByProjectAsync(request.ProjectId, cancellationToken);
        if (activeAssignment == null)
        {
            return null;
        }

        var supervisorDetail = await supervisorRepository.GetByIdAsync(activeAssignment.SupervisorProfileId, cancellationToken);
        if (supervisorDetail == null)
        {
            return null;
        }

        return new SupervisorDto(
            supervisorDetail.Id,
            supervisorDetail.UserId,
            supervisorDetail.FullName,
            supervisorDetail.Email,
            supervisorDetail.Title,
            supervisorDetail.Bio,
            supervisorDetail.MaxActiveProjects,
            supervisorDetail.IsAvailable
        );
    }
}
