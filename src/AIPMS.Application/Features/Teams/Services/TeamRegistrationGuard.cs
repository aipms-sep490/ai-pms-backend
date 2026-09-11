using AIPMS.Application.Features.Teams.Abstractions;

namespace AIPMS.Application.Features.Teams.Services;

// Shares the roster transaction boundary with BE-04. Validation must happen inside it,
// not as a preflight check that can become stale before the project is saved.
public sealed class TeamRegistrationGuard(ITeamRepository repository, TeamWorkflow workflow)
    : ITeamRegistrationGuard
{
    public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) =>
        repository.InTransactionAsync(action, cancellationToken);

    public Task ValidateAsync(long teamId, CancellationToken cancellationToken) =>
        workflow.ValidateRegistrationAsync(teamId, cancellationToken);
}
