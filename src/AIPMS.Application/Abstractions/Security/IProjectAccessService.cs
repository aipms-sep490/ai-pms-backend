namespace AIPMS.Application.Abstractions.Security;

public interface IProjectAccessService
{
    Task<bool> CanAccessAsync(
        long userId,
        long projectId,
        CancellationToken cancellationToken = default);
}
