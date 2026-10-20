using AIPMS.Application.Features.ReviewGates;

namespace AIPMS.Application.Features.ReviewGates.Abstractions;

public interface IReviewGateService
{
    Task<IReadOnlyList<ReviewGateTemplateDto>> Templates(CancellationToken ct);
    Task<ReviewGateTemplateDto?> Template(long id, CancellationToken ct);
    Task<ReviewGateTemplateDto> SaveTemplate(SaveReviewGateTemplateRequest request, long? id, CancellationToken ct);
    Task<ReviewGateTemplateDto> PublishTemplate(long id, CancellationToken ct);
    Task<StudentReviewTimelineDto> Timeline(long projectId, CancellationToken ct);
    Task<StudentReviewTimelineDto> MyTimeline(CancellationToken ct);
    Task<StudentReviewTimelineDto> SubmitEvidence(long projectId, string gateCode, SubmitReviewGateEvidenceRequest request, CancellationToken ct);
    Task<StudentReviewTimelineDto> SubmitForReview(long projectId, string gateCode, SubmitReviewGateForReviewRequest request, CancellationToken ct);
    Task<IReadOnlyList<AssignedReviewItemDto>> AssignedReviews(CancellationToken ct);
    Task<AssignedReviewItemDto> VerifyEvidence(long assignmentId, VerifyReviewGateEvidenceRequest request, CancellationToken ct);
    Task<AssignedReviewItemDto> Decide(long assignmentId, SubmitReviewGateDecisionRequest request, CancellationToken ct);
    Task<AssignedReviewItemDto> Assign(CreateReviewGateAssignmentRequest request, CancellationToken ct);
    Task<ProjectReviewMatrixRowDto> UpdateDecision(long projectId, string gateCode, UpdateReviewGateDecisionRequest request, CancellationToken ct);
    Task<ProjectReviewMatrixRowDto> UpdateScope(long projectId, string scope, UpdateReviewGateScopeRequest request, CancellationToken ct);
    Task<IReadOnlyList<ProjectReviewMatrixRowDto>> Matrix(string? mode, long? departmentId, CancellationToken ct);
    Task<DefenseReadinessDto> DefenseReadiness(long projectId, CancellationToken ct);
    Task<DefenseReadinessDto> DefenseReadinessByKey(string projectKey, CancellationToken ct);
}
