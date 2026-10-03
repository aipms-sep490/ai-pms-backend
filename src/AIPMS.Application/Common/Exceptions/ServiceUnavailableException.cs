namespace AIPMS.Application.Common.Exceptions;

public sealed class ServiceUnavailableException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}
