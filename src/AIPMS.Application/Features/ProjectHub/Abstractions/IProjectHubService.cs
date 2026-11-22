namespace AIPMS.Application.Features.ProjectHub.Abstractions;

public interface IProjectHubService
{
    Task<SpecVersionDto> CreateSpecVersionAsync(long projectId, long deliverableId, CreateSpecVersionRequest input, CancellationToken ct);
    Task<SpecVersionCompareDto> CompareSpecVersionsAsync(long projectId, long deliverableId, int fromVersion, int toVersion, CancellationToken ct);
    Task<IReadOnlyList<PinnedResourceDto>> ListPinnedResourcesAsync(long projectId, CancellationToken ct);
    Task<PinnedResourceDto> AddPinnedResourceAsync(long projectId, CreatePinnedResourceRequest input, CancellationToken ct);
    Task DeletePinnedResourceAsync(long projectId, long resourceId, CancellationToken ct);
    Task<HubChannelDto> GetChannelAsync(long projectId, string channelScope, CancellationToken ct);
    Task<HubChannelMessageDto> PostChannelMessageAsync(long projectId, string channelScope, PostChannelMessageRequest input, CancellationToken ct);
    Task<ChatConvertedTaskDto> ConvertMessageToTaskAsync(long projectId, long messageId, ConvertMessageToTaskRequest input, CancellationToken ct);
    Task<ChatConvertedDecisionDto> ConvertMessageToDecisionAsync(long projectId, long messageId, ConvertMessageToDecisionRequest input, CancellationToken ct);
    Task<IReadOnlyList<AnchoredCommentDto>> ListAnchoredCommentsAsync(long projectId, string gateCode, CancellationToken ct);
    Task<AnchoredCommentDto> CreateAnchoredCommentAsync(long projectId, string gateCode, CreateAnchoredCommentRequest input, CancellationToken ct);
    Task<AnchoredCommentDto> ResolveAnchoredCommentAsync(long projectId, string gateCode, long commentId, CancellationToken ct);
    Task<InternalContributionHeatmapDto> GetInternalHeatmapAsync(long projectId, int weeks, CancellationToken ct);
    Task<EvidenceRubricLinkDto> LinkEvidenceToRubricAsync(long projectId, LinkEvidenceToRubricRequest input, CancellationToken ct);
    Task<IReadOnlyList<EvidenceRubricLinkDto>> ListEvidenceRubricLinksAsync(long projectId, CancellationToken ct);
    Task<ProjectHealthPulseDto> GetHealthPulseAsync(long projectId, CancellationToken ct);
    Task<IReadOnlyList<ProjectAuditTimelineEntryDto>> GetAuditTimelineAsync(long projectId, int limit, CancellationToken ct);
}
