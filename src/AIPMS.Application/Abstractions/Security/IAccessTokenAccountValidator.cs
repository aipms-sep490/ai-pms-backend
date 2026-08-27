namespace AIPMS.Application.Abstractions.Security;

public interface IAccessTokenAccountValidator
{
    Task<bool> IsValidAsync(
        long userId,
        DateTime? passwordChangedAtUtc,
        CancellationToken cancellationToken = default);
}
