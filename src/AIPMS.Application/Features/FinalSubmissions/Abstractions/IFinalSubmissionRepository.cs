using AIPMS.Application.Features.FinalSubmissions.Models;

namespace AIPMS.Application.Features.FinalSubmissions.Abstractions;

public interface IFinalSubmissionRepository
{
    Task<bool> CanManageAsync(long projectId, long actorId, CancellationToken ct);
    Task<bool> CanReadAsync(long projectId, long actorId, CancellationToken ct);
    Task<FinalRequirementsRecord?> RequirementsAsync(long projectId, CancellationToken ct);
    Task<IReadOnlyList<FinalRequiredDeliverable>> DeliverablesAsync(long projectId, IReadOnlyList<long> ids, CancellationToken ct);
    Task<FinalRequirementsRecord> ConfigureAsync(long projectId, IReadOnlyList<long> ids, long actorId, DateTime now, CancellationToken ct);
    Task<FinalSubmissionRecord?> GetAsync(long projectId, CancellationToken ct);
    Task<IReadOnlyList<FinalSnapshotFile>> FilesAsync(IReadOnlyList<long> versionIds, CancellationToken ct);
    Task<FinalSubmissionRecord> SubmitAsync(FinalSubmissionRecord submission, CancellationToken ct);
}
