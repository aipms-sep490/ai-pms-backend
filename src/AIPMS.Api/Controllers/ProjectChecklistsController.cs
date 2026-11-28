using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.ProjectChecklists;
using AIPMS.Application.Features.ProjectChecklists.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
public sealed class ProjectChecklistsController(IProjectChecklistService service) : ControllerBase
{
    // Master Checklist Templates (Layer 1 — Pure Core)
    [HttpGet("api/v1/checklist-templates")]
    public async Task<ActionResult<IReadOnlyList<MasterChecklistTemplateDto>>> ListTemplates(
        [FromQuery] long? semesterId,
        [FromQuery] string? trackCode,
        CancellationToken ct) =>
        Ok(await service.ListTemplatesAsync(semesterId, trackCode, ct));

    [HttpPost("api/v1/checklist-templates")]
    public async Task<ActionResult<MasterChecklistTemplateDto>> CreateTemplate(
        [FromBody] CreateMasterChecklistTemplateRequest request,
        CancellationToken ct) =>
        Ok(await service.CreateTemplateAsync(request, ct));

    [HttpPost("api/v1/checklist-templates/{templateId:long}/publish")]
    public async Task<ActionResult<MasterChecklistTemplateDto>> PublishTemplate(long templateId, CancellationToken ct) =>
        Ok(await service.PublishTemplateAsync(templateId, ct));

    // Project Tailored Gate Checklists
    [HttpPost("api/v1/projects/{projectId:long}/review-gates/{gateCode}/checklist")]
    public async Task<ActionResult<ProjectGateChecklistDto>> CreateFromTemplate(
        long projectId,
        string gateCode,
        [FromQuery] long templateId,
        CancellationToken ct) =>
        Ok(await service.CreateProjectChecklistFromTemplateAsync(projectId, gateCode, templateId, ct));

    [HttpGet("api/v1/projects/{projectId:long}/review-gates/{gateCode}/checklist")]
    public async Task<ActionResult<ProjectGateChecklistDto>> GetLatest(long projectId, string gateCode, CancellationToken ct) =>
        Ok(await service.GetLatestChecklistAsync(projectId, gateCode, ct));

    [HttpPut("api/v1/projects/{projectId:long}/checklists/{checklistId:long}/tailor")]
    public async Task<ActionResult<ProjectGateChecklistDto>> Tailor(
        long projectId,
        long checklistId,
        [FromBody] TailorProjectChecklistRequest request,
        CancellationToken ct) =>
        Ok(await service.TailorChecklistAsync(projectId, checklistId, request, ct));

    [HttpGet("api/v1/projects/{projectId:long}/checklists/{checklistId:long}/diff")]
    public async Task<ActionResult<ChecklistDiffPreviewDto>> Diff(long projectId, long checklistId, CancellationToken ct) =>
        Ok(await service.GetDiffPreviewAsync(projectId, checklistId, ct));

    [HttpPost("api/v1/projects/{projectId:long}/checklists/{checklistId:long}/publish")]
    public async Task<ActionResult<ProjectGateChecklistDto>> Publish(long projectId, long checklistId, CancellationToken ct) =>
        Ok(await service.PublishChecklistAsync(projectId, checklistId, ct));

    [HttpPost("api/v1/projects/{projectId:long}/checklists/{checklistId:long}/advisory")]
    public async Task<ActionResult<ProjectGateChecklistDto>> SubmitAdvisory(
        long projectId,
        long checklistId,
        [FromBody] SubmitAdvisoryEvaluationRequest request,
        CancellationToken ct) =>
        Ok(await service.SubmitAdvisoryEvaluationAsync(projectId, checklistId, request, ct));

    [HttpPost("api/v1/projects/{projectId:long}/checklists/{checklistId:long}/reviewer-score")]
    public async Task<ActionResult<ProjectGateChecklistDto>> SubmitReviewerScore(
        long projectId,
        long checklistId,
        [FromBody] SubmitReviewerEvaluationRequest request,
        CancellationToken ct) =>
        Ok(await service.SubmitReviewerEvaluationAsync(projectId, checklistId, request, ct));

    // EACH_STUDENT Expansion & Mid-Semester Withdrawal
    [HttpPost("api/v1/projects/{projectId:long}/review-gates/{gateCode}/expand-student-requirements")]
    public async Task<ActionResult<ExpandStudentRequirementsResultDto>> ExpandEachStudent(long projectId, string gateCode, CancellationToken ct) =>
        Ok(await service.ExpandEachStudentRequirementsAsync(projectId, gateCode, ct));

    [HttpPost("api/v1/projects/{projectId:long}/roster/withdraw-member")]
    public async Task<ActionResult<WithdrawRosterMemberResultDto>> WithdrawMember(
        long projectId,
        [FromBody] WithdrawRosterMemberRequest request,
        CancellationToken ct) =>
        Ok(await service.WithdrawRosterMemberAsync(projectId, request, ct));

    // 2-Step Final Lock + Lead Force-Lock + Reopen (Unified Final + Cold SHA-256 Manifest)
    [HttpGet("api/v1/projects/{projectId:long}/final-submission/lock-status")]
    public async Task<ActionResult<PreDefenseLockStatusDto>> GetFinalLockStatus(long projectId, CancellationToken ct) =>
        Ok(await service.GetPreDefenseLockStatusAsync(projectId, ct));

    [HttpPost("api/v1/projects/{projectId:long}/final-submission/request-lock")]
    public async Task<ActionResult<PreDefenseLockStatusDto>> RequestFinalLock(
        long projectId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RequestFinalLockRequest? request = null,
        CancellationToken ct = default) =>
        Ok(await service.RequestFinalLockAsync(projectId, request?.ConcurrencyToken, ct));

    [HttpPost("api/v1/projects/{projectId:long}/final-submission/supervisor-lock")]
    public async Task<ActionResult<PreDefenseLockStatusDto>> SupervisorLock(
        long projectId,
        [FromBody] SupervisorFinalLockDecisionRequest request,
        CancellationToken ct) =>
        Ok(await service.SupervisorDecideFinalLockAsync(projectId, request, ct));

    [HttpPost("api/v1/projects/{projectId:long}/final-submission/force-lock")]
    public async Task<ActionResult<PreDefenseLockStatusDto>> ForceLock(
        long projectId,
        [FromBody] ForceLockFinalRequest request,
        CancellationToken ct) =>
        Ok(await service.ForceLockFinalAsync(projectId, request, ct));

    [HttpPost("api/v1/projects/{projectId:long}/final-submission/reopen")]
    public async Task<ActionResult<PreDefenseLockStatusDto>> ReopenFinalLock(
        long projectId,
        [FromBody] ReopenFinalLockRequest request,
        CancellationToken ct) =>
        Ok(await service.ReopenFinalLockAsync(projectId, request, ct));
}
