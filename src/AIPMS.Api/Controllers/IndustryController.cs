using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Industry;
using AIPMS.Application.Features.Industry.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[Route("api/v1")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(503)]
public sealed class IndustryController(IIndustryService industry) : ControllerBase
{
    /// <summary>Admin invites a dedicated expert with no academic roles. The enrollment secret is delivered only by email.</summary>
    [HttpPost("admin/industry/invitations")]
    public Task<ExpertInvitationDto> Invite(InviteExpertRequest input, CancellationToken ct) => industry.Invite(input, ct);
    [HttpGet("admin/industry/invitations")]
    public Task<PagedResult<ExpertInvitationDto>> Invitations(CancellationToken ct, int page = 1, int pageSize = 20) => industry.Invitations(page, pageSize, ct);
    [HttpPost("admin/industry/invitations/{id:long}/revoke")]
    public Task<ExpertInvitationDto> RevokeInvitation(long id, CancellationToken ct) => industry.RevokeInvitation(id, ct);
    /// <summary>Frozen lead staff assigns an expert to COMMON or one MAJOR_SPECIFIC target. Advisory only.</summary>
    [HttpPost("projects/{projectId:long}/industry-assignments")]
    public Task<IndustryAssignmentDto> Assign(long projectId, AssignIndustryRequest input, CancellationToken ct) => industry.Assign(projectId, input, ct);
    [HttpGet("projects/{projectId:long}/industry-assignments")]
    public Task<PagedResult<IndustryAssignmentDto>> ProjectAssignments(long projectId, CancellationToken ct, int page = 1, int pageSize = 20) => industry.Assignments(projectId, page, pageSize, ct);
    [HttpGet("industry/assignments")]
    public Task<PagedResult<IndustryAssignmentDto>> Mine(CancellationToken ct, int page = 1, int pageSize = 20) => industry.Assignments(null, page, pageSize, ct);
    [HttpGet("industry/assignments/{id:long}")]
    public Task<IndustryAssignmentDto> Get(long id, CancellationToken ct) => industry.Get(id, ct);
    [HttpPost("industry/assignments/{id:long}/revoke")]
    public Task<IndustryAssignmentDto> Revoke(long id, IndustryTokenRequest input, CancellationToken ct) => industry.Revoke(id, input.ConcurrencyToken, ct);
    /// <summary>Explicitly share one exact deliverable file version; individual/private evidence is excluded.</summary>
    [HttpPost("industry/assignments/{id:long}/evidence")]
    public Task<IndustryFileDto> Share(long id, ShareIndustryFileRequest input, CancellationToken ct) => industry.Share(id, input, ct);
    [HttpGet("industry/assignments/{id:long}/evidence")]
    public Task<PagedResult<IndustryFileDto>> Evidence(long id, CancellationToken ct, int page = 1, int pageSize = 20) => industry.Evidence(id, page, pageSize, ct);
    [HttpPost("industry/assignments/{id:long}/evidence/{shareId:long}/revoke")]
    public Task<IndustryAssignmentDto> Unshare(long id, long shareId, IndustryTokenRequest input, CancellationToken ct) => industry.Unshare(id, shareId, input.ConcurrencyToken, ct);
    [HttpGet("industry/assignments/{id:long}/evidence/{shareId:long}/download")]
    public async Task<IActionResult> Download(long id, long shareId, CancellationToken ct)
    {
        var file = await industry.Download(id, shareId, ct); Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }
    [HttpPost("industry/assignments/{id:long}/feedback")]
    public Task<IndustryFeedbackDto> Feedback(long id, IndustryFeedbackRequest input, CancellationToken ct) => industry.Feedback(id, input, ct);
    [HttpGet("industry/assignments/{id:long}/feedback")]
    public Task<PagedResult<IndustryFeedbackDto>> FeedbackHistory(long id, CancellationToken ct, int page = 1, int pageSize = 20) => industry.FeedbackHistory(id, page, pageSize, ct);
}
