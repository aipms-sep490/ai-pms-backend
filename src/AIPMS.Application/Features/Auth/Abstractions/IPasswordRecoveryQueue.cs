namespace AIPMS.Application.Features.Auth.Abstractions;

public interface IPasswordRecoveryQueue
{
    Task EnqueueAsync(string email, CancellationToken ct);
    Task<bool> ProcessOneAsync(CancellationToken ct);
    Task CleanupAsync(CancellationToken ct);
}
