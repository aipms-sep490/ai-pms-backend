namespace AIPMS.Application.Features.ExecutionCapabilities.DTOs;

public sealed record ExecutionActionDto(string Code, bool Allowed, IReadOnlyList<string> Reasons);

public sealed record ProjectCapabilitiesDto(long ProjectId, string ProjectStatus, string ConcurrencyToken,
    string AcademicScopeProvenance, long? LeadDepartmentId, IReadOnlyList<long> ManagedDepartmentIds,
    long? RegistrationSnapshotId, long? SchemeId, string? SchemeConcurrencyToken, long? PolicyVersionId,
    IReadOnlyList<ExecutionActionDto> Actions,
    AIPMS.Application.Features.WorkingAgreements.WorkingAgreementReadiness? WorkingAgreement = null);

public sealed record ExecutionCapabilityDto(
    string ResourceType,
    long ResourceId,
    long ProjectId,
    string ProjectStatus,
    string ConcurrencyToken,
    IReadOnlyList<ExecutionActionDto> Actions,
    string? ResourceStatus = null);
