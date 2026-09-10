namespace AIPMS.Application.Features.Teams.Abstractions;

public interface ITeamRegistrationGuard
{
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
    Task ValidateAsync(long teamId, CancellationToken cancellationToken);
}
