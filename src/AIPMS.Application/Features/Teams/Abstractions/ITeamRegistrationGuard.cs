using System;
using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.Teams.Abstractions;

public interface ITeamRegistrationGuard
{
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
    Task ValidateAsync(long teamId, CancellationToken cancellationToken);
    Task ValidateSubmissionEligibilityAsync(
        long teamId,
        long projectId,
        string roundType,
        long? revisionHistoryId,
        CancellationToken cancellationToken);
}
