namespace AIPMS.Application.Abstractions.Security;

public interface IRequestContext
{
    string? IpAddress { get; }

    string? UserAgent { get; }

    Guid CorrelationId { get; }
}
