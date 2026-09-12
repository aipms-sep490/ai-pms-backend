using AIPMS.Application.Features.Deliverables.DTOs;

namespace AIPMS.Application.Features.FinalSubmissions.DTOs;

public sealed record ConfigureFinalRequirementsRequest(IReadOnlyList<long> DeliverableIds, string? ConcurrencyToken);
public sealed record SubmitFinalSubmissionRequest(string DraftConcurrencyToken, string RequirementsConcurrencyToken);
public sealed record FinalRequirementDto(long DeliverableId, string Title, long? SelectedVersionId, bool IsComplete);
public sealed record FinalRequirementDefinitionDto(long DeliverableId, string Title);
public sealed record FinalRequirementsDto(long ProjectId, string? ConcurrencyToken, IReadOnlyList<FinalRequirementDefinitionDto> Items);
public sealed record FinalSubmissionChecklistDto(long ProjectId, long? ProjectPeriodId, DateTime? Deadline,
    string? DraftConcurrencyToken, string? RequirementsConcurrencyToken, bool CanSubmit,
    IReadOnlyList<string> Blockers, IReadOnlyList<FinalRequirementDto> Items);
public sealed record FinalSubmissionItemDto(long DeliverableVersionId, long DeliverableId, string Title,
    int VersionNumber, string StatusAtSubmission, bool WasRequired, IReadOnlyList<ProjectFileDto> Files);
public sealed record FinalSubmissionDto(long Id, long ProjectId, long ProjectPeriodId, string Status, bool IsLocked,
    long SubmittedBy, DateTime SubmittedAt, DateTime Deadline, string? Notes,
    string DraftConcurrencyToken, string RequirementsConcurrencyToken, IReadOnlyList<FinalSubmissionItemDto> Items);
