namespace AIPMS.Application.Common.Exceptions;

public sealed class ValidationException : Exception
{
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors.ToDictionary(
            static error => error.Key,
            static error => error.Value.ToArray());
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
