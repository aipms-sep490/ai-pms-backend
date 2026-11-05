namespace AIPMS.Application.Common.Exceptions;

public sealed class PayloadTooLargeException(long maxBytes, string message = "Uploaded payload exceeds maximum allowed size.") : Exception(message)
{
    public long MaxBytes { get; } = maxBytes;
}
