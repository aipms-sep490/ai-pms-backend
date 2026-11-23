namespace AIPMS.Application.Features.AcademicKnowledge.Abstractions;

public interface IAiSetupService
{
    Task<ApplyProjectSetupResultDto> ApplyProjectSetupAsync(long projectId, ApplyProjectSetupRequest request, CancellationToken ct = default);
    Task<ApplyRubricSetupResultDto> ApplyRubricSetupAsync(ApplyRubricSetupRequest request, CancellationToken ct = default);
    Task<ApplyReviewGateSetupResultDto> ApplyReviewGateSetupAsync(ApplyReviewGateSetupRequest request, CancellationToken ct = default);
    Task<ApplyTopicBatchSetupResultDto> ApplyTopicBatchSetupAsync(ApplyTopicBatchSetupRequest request, CancellationToken ct = default);
    Task<ApplyColdTemplateSetupResultDto> ApplyColdTemplateSetupAsync(ApplyColdTemplateSetupRequest request, CancellationToken ct = default);
}
