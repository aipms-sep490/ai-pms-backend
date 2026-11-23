using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AIPMS.Api.Controllers;

[ApiController]
[Route("api/v1/academic/pre-semester")]
[Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class PreSemesterSetupController(IPreSemesterSetupService service) : ControllerBase
{
    [HttpGet("readiness")]
    [ProducesResponseType<PreSemesterReadinessDto>(StatusCodes.Status200OK)]
    public Task<PreSemesterReadinessDto> Readiness(
        [FromQuery] long? semesterId,
        [FromQuery] long? departmentId,
        CancellationToken ct) =>
        service.GetReadinessAsync(semesterId, departmentId, ct);

    [HttpPost("rollover/preview")]
    [ProducesResponseType<RolloverPreviewDto>(StatusCodes.Status200OK)]
    public Task<RolloverPreviewDto> PreviewRollover(
        [FromBody] RolloverPreviewRequest request,
        CancellationToken ct) =>
        service.PreviewRolloverAsync(request, ct);

    [HttpPost("rollover/apply")]
    [ProducesResponseType<RolloverApplyResultDto>(StatusCodes.Status200OK)]
    public Task<RolloverApplyResultDto> ApplyRollover(
        [FromBody] RolloverApplyRequest request,
        CancellationToken ct) =>
        service.ApplyRolloverAsync(request, ct);

    [HttpPost("ai-diff-check")]
    [EnableRateLimiting("ai-assistant")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadPolicies.MultipartTransportCeiling)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadPolicies.MultipartTransportCeiling)]
    [ProducesResponseType<PreSemesterAiDiffResponseDto>(StatusCodes.Status200OK)]
    public async Task<PreSemesterAiDiffResponseDto> AiDiffCheck(
        [FromForm] PreSemesterAiDiffForm input,
        CancellationToken ct)
    {
        if (input.File is null || input.File.Length is 0 or > UploadPolicies.AcademicAssistantFileLimit)
            throw new ArgumentException("Cần tải lên tệp kế hoạch học kỳ (PDF, DOCX, XLSX, TXT, MD), dung lượng tối đa 10 MB.");
        using var ms = new MemoryStream();
        await input.File.CopyToAsync(ms, ct);
        return await service.AiDiffCheckAsync(
            input.TargetSemesterId,
            input.DepartmentId,
            new AssistantUpload(input.File.FileName, ms.ToArray()),
            ct);
    }
}

public sealed class PreSemesterAiDiffForm
{
    public long TargetSemesterId { get; set; }
    public long? DepartmentId { get; set; }
    public IFormFile? File { get; set; }
}
