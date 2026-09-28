namespace AIPMS.Application.Common.Exceptions;

public sealed class ConflictException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}
