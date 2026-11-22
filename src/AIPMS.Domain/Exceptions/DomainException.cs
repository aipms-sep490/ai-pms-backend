namespace AIPMS.Domain.Exceptions;

public sealed record BlockingRequirement(string Code, string Message, string? Target = null);

public sealed record ProblemMetadata(
    IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    IReadOnlyList<BlockingRequirement>? BlockingRequirements = null,
    string? CurrentState = null,
    string? ExpectedState = null,
    string? PolicyVersion = null,
    bool? Retryable = null);

public sealed class DomainException(string message, string? code = null, ProblemMetadata? metadata = null) : Exception(message)
{
    public string? Code { get; } = code;
    public ProblemMetadata? Metadata { get; init; } = metadata;
}
