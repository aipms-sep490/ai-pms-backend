using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.ProjectChecklists.Abstractions;

public interface IProjectChecklistService
{
    Task<IReadOnlyList<MasterChecklistTemplateDto>> ListTemplatesAsync(long? semesterId, string? trackCode, CancellationToken ct = default);
    Task<MasterChecklistTemplateDto> CreateTemplateAsync(CreateMasterChecklistTemplateRequest request, CancellationToken ct = default);
    Task<MasterChecklistTemplateDto> PublishTemplateAsync(long templateId, CancellationToken ct = default);

    Task<ProjectGateChecklistDto> CreateProjectChecklistFromTemplateAsync(long projectId, string gateCode, long templateId, CancellationToken ct = default);
    Task<ProjectGateChecklistDto> GetLatestChecklistAsync(long projectId, string gateCode, CancellationToken ct = default);
    Task<ProjectGateChecklistDto> TailorChecklistAsync(long projectId, long checklistId, TailorProjectChecklistRequest request, CancellationToken ct = default);
    Task<ChecklistDiffPreviewDto> GetDiffPreviewAsync(long projectId, long checklistId, CancellationToken ct = default);
    Task<ProjectGateChecklistDto> PublishChecklistAsync(long projectId, long checklistId, CancellationToken ct = default);
    Task<ProjectGateChecklistDto> SubmitAdvisoryEvaluationAsync(long projectId, long checklistId, SubmitAdvisoryEvaluationRequest request, CancellationToken ct = default);
    Task<ProjectGateChecklistDto> SubmitReviewerEvaluationAsync(long projectId, long checklistId, SubmitReviewerEvaluationRequest request, CancellationToken ct = default);

    Task<ExpandStudentRequirementsResultDto> ExpandEachStudentRequirementsAsync(long projectId, string gateCode, CancellationToken ct = default);
    Task<WithdrawRosterMemberResultDto> WithdrawRosterMemberAsync(long projectId, WithdrawRosterMemberRequest request, CancellationToken ct = default);

    Task<PreDefenseLockStatusDto> GetPreDefenseLockStatusAsync(long projectId, CancellationToken ct = default);
    Task<PreDefenseLockStatusDto> RequestFinalLockAsync(long projectId, string? concurrencyToken = null, CancellationToken ct = default);
    Task<PreDefenseLockStatusDto> SupervisorDecideFinalLockAsync(long projectId, SupervisorFinalLockDecisionRequest request, CancellationToken ct = default);
    Task<PreDefenseLockStatusDto> ForceLockFinalAsync(long projectId, ForceLockFinalRequest request, CancellationToken ct = default);
    Task<PreDefenseLockStatusDto> ReopenFinalLockAsync(long projectId, ReopenFinalLockRequest request, CancellationToken ct = default);
}
