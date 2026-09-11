using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Teams.Abstractions;

namespace AIPMS.UnitTests.Application;

internal sealed class StubRegistrationGuard(StubProjectRepository repository) : ITeamRegistrationGuard
{
    public bool InTransaction { get; private set; }
    public int Validations { get; private set; }
    public bool Reject { get; set; }
    public bool Committed { get; private set; }

    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        InTransaction = true;
        try
        {
            var result = await action(ct);
            Committed = true;
            return result;
        }
        finally { InTransaction = false; }
    }

    public Task ValidateAsync(long teamId, CancellationToken ct)
    {
        Assert.True(InTransaction);
        Validations++;
        if (Reject || !repository.IsTeamEligible) throw new ConflictException("Team is not eligible.");
        return Task.CompletedTask;
    }
}
