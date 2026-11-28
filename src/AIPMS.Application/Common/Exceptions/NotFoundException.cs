namespace AIPMS.Application.Common.Exceptions;

public sealed class NotFoundException(string resourceName, object key, string? errorCode = null)
    : Exception($"{resourceName} with key '{key}' was not found.")
{
    public string? ErrorCode { get; } = errorCode;
}
