using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Models;

namespace AIPMS.Application.Features.FinalSubmissions.DTOs;

public sealed record CreateFinalSubmissionDraftRequest(long ProjectPeriodId, string? Notes,
    IReadOnlyList<long> DeliverableVersionIds);

public sealed record UpdateFinalSubmissionDraftRequest(long ProjectPeriodId, string? Notes,
    IReadOnlyList<long> DeliverableVersionIds, string ConcurrencyToken);

public sealed record FinalSubmissionPeriodDto(long Id, string Name, DateTime StartAt, DateTime EndAt);
public sealed record FinalSubmissionPeriodOptionDto(long Id, string Name, string Status, DateTime StartAt,
    DateTime EndAt, bool CanPrepareDraft, IReadOnlyList<string> Blockers);
public sealed record FinalSubmissionDraftItemDto(long DeliverableVersionId, long DeliverableId,
    string Title, int VersionNumber, string VersionStatus, bool IsEligible,
    IReadOnlyList<ProjectFileDto> Files);

public sealed record FinalSubmissionDraftDto(long Id, long ProjectId, long ProjectPeriodId,
    string Status, bool IsLocked, string? Notes, long CreatedBy, long UpdatedBy,
    DateTime CreatedAt, DateTime UpdatedAt, string ConcurrencyToken,
    FinalSubmissionPeriodDto? Period, bool CanEdit, IReadOnlyList<string> EditBlockers,
    IReadOnlyList<FinalSubmissionDraftItemDto> Items);

public static class FinalSubmissionDraftMapper
{
    public static FinalSubmissionDraftDto ToDto(this FinalDraftRecord draft, FinalDraftPeriod? period,
        IReadOnlyList<string> blockers, IReadOnlyList<FinalDraftVersion> versions) =>
        new(draft.Id, draft.ProjectId, draft.ProjectPeriodId, "DRAFT", false, draft.Notes,
            draft.CreatedBy, draft.UpdatedBy, draft.CreatedAt, draft.UpdatedAt, draft.ConcurrencyToken,
            period is null ? null : new(period.Id, period.Name, period.StartAt, period.EndAt),
            blockers.Count == 0, blockers, versions.OrderBy(v => v.DeliverableId).ThenBy(v => v.Id)
                .Select(v => new FinalSubmissionDraftItemDto(v.Id, v.DeliverableId, v.Title, v.VersionNumber,
                    v.Status, v.FilesValid && v.Status is "SUBMITTED" or "ACCEPTED", v.Files)).ToArray());
}
