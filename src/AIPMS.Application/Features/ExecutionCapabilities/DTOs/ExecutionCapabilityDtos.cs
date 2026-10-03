namespace AIPMS.Application.Features.ExecutionCapabilities.DTOs;

public sealed record ExecutionActionDto(string Code, bool Allowed, IReadOnlyList<string> Reasons);

public sealed record ExecutionCapabilityDto(
    string ResourceType,
    long ResourceId,
    long ProjectId,
    string ProjectStatus,
    string ConcurrencyToken,
    IReadOnlyList<ExecutionActionDto> Actions,
    string? ResourceStatus = null);
