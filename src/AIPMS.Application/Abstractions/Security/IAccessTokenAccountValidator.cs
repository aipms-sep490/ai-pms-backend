namespace AIPMS.Application.Abstractions.Security;

public interface IAccessTokenAccountValidator
{
    Task<bool> IsValidAsync(
        long userId,
        DateTime? passwordChangedAtUtc,
        IReadOnlyCollection<string> claimedRoles,
        CancellationToken cancellationToken = default);
}
