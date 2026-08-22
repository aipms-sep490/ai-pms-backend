using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Supervisors.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Commands.UpdateSupervisorProfile;

public sealed record UpdateSupervisorProfileCommand(
    string? Bio,
    int? MaxActiveProjects,
    bool IsAvailable) : IRequest<Unit>;

public sealed class UpdateSupervisorProfileCommandHandler(
    ICurrentUser currentUser,
    ISupervisorRepository supervisorRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateSupervisorProfileCommand, Unit>
{
    public async Task<Unit> Handle(
        UpdateSupervisorProfileCommand request,
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

        profile.Bio = request.Bio;
        profile.MaxActiveProjects = request.MaxActiveProjects;
        profile.IsAvailable = request.IsAvailable;

        await supervisorRepository.UpdateProfileAsync(profile, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
