namespace AIPMS.Application.Abstractions.Email;

public interface IPasswordResetNotifier
{
    Task SendAsync(
        string recipientEmail,
        string recipientName,
        string rawResetToken,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default);
}
