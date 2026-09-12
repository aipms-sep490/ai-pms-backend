using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.FinalSubmissions.DTOs;

namespace AIPMS.Application.Features.FinalSubmissions.Models;

public sealed record FinalRequirementsRecord(string ConcurrencyToken, IReadOnlyList<long> DeliverableIds);
public sealed record FinalRequiredDeliverable(long Id, string Title);
public sealed record FinalSnapshotFile(ProjectFileDto Metadata, string StorageKey);
public sealed record FinalSnapshotItem(long DeliverableVersionId, long DeliverableId, string Title,
    int VersionNumber, string StatusAtSubmission, bool WasRequired, IReadOnlyList<FinalSnapshotFile> Files);
public sealed record FinalSubmissionRecord(long Id, long ProjectId, long ProjectPeriodId, long SubmittedBy,
    DateTime SubmittedAt, DateTime Deadline, string? Notes, string DraftConcurrencyToken,
    string RequirementsConcurrencyToken, IReadOnlyList<FinalSnapshotItem> Items);

public static class FinalSubmissionMapper
{
    public static FinalSubmissionDto ToDto(this FinalSubmissionRecord row) => new(row.Id, row.ProjectId,
        row.ProjectPeriodId, "LOCKED", true, row.SubmittedBy, row.SubmittedAt, row.Deadline, row.Notes,
        row.DraftConcurrencyToken, row.RequirementsConcurrencyToken,
        row.Items.Select(i => new FinalSubmissionItemDto(i.DeliverableVersionId, i.DeliverableId, i.Title,
            i.VersionNumber, i.StatusAtSubmission, i.WasRequired, i.Files.Select(f => f.Metadata).ToArray())).ToArray());
}
