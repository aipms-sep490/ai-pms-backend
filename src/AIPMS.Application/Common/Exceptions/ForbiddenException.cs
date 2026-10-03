namespace AIPMS.Application.Common.Exceptions;

public sealed class ForbiddenException(string message = "You are not allowed to perform this action.", string? code = null)
    : Exception(message)
{
    public string? Code { get; } = code;
}
