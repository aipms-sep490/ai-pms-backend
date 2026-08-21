namespace AIPMS.Application.Abstractions.Security;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    long? UserId { get; }

    string? Email { get; }

    string? FullName { get; }

    IReadOnlyCollection<string> Roles { get; }
}
