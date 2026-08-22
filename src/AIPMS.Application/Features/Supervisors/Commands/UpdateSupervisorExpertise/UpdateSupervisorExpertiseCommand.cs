using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Domain.Entities;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Commands.UpdateSupervisorExpertise;

public sealed record UpdateSupervisorExpertiseCommand(
    IReadOnlyList<SupervisorExpertiseDto> Expertises) : IRequest<Unit>;

public sealed class UpdateSupervisorExpertiseCommandHandler(
    ICurrentUser currentUser,
    ISupervisorRepository supervisorRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateSupervisorExpertiseCommand, Unit>
{
    public async Task<Unit> Handle(
        UpdateSupervisorExpertiseCommand request,
        CancellationToken cancellationToken)
    {
        var currentUserId = currentUser.UserId;
        if (currentUserId == null)
        {
            throw new ForbiddenException("User is not authenticated.");
        }

        var profile = await supervisorRepository.GetProfileByUserIdAsync(currentUserId.Value, cancellationToken);
        if (profile == null)
        {
            throw new ForbiddenException("You do not have a supervisor profile.");
        }

        var duplicateNames = request.Expertises
            .GroupBy(e => e.ExpertiseName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateNames.Any())
        {
            throw new ConflictException($"Duplicate expertise names are not allowed: {string.Join(", ", duplicateNames)}");
        }

        var domainExpertises = request.Expertises.Select(e => new SupervisorExpertise
        {
            SupervisorProfileId = profile.Id,
            ExpertiseName = e.ExpertiseName.Trim(),
            ProficiencyLevel = e.ProficiencyLevel?.Trim()
        }).ToList();

        await supervisorRepository.UpdateExpertisesAsync(profile.Id, domainExpertises, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
