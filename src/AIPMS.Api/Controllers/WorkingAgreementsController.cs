using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.WorkingAgreements;
using AIPMS.Application.Features.WorkingAgreements.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize, Route("api/v1/projects/{projectId:long}/working-agreement")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
public sealed class WorkingAgreementsController(IWorkingAgreementService service) : ControllerBase
{
    [HttpGet, ProducesResponseType<WorkingAgreementDto>(200), ProducesResponseType(204)]
    public async Task<ActionResult<WorkingAgreementDto>> Get(long projectId, CancellationToken ct)
    {
        var value = await service.Get(projectId, ct);
        return value is null ? NoContent() : Ok(value);
    }
    [HttpGet("history"), ProducesResponseType<PagedResult<WorkingAgreementDto>>(200)]
    public Task<PagedResult<WorkingAgreementDto>> History(long projectId, int page = 1, int pageSize = 20, CancellationToken ct = default) => service.History(projectId, page, pageSize, ct);
    [HttpPut("draft"), ProducesResponseType<WorkingAgreementDto>(200)]
    public Task<WorkingAgreementDto> Save(long projectId, SaveWorkingAgreementRequest input, CancellationToken ct) => service.Save(projectId, input, ct);
    [HttpPost("publish"), ProducesResponseType<WorkingAgreementDto>(200)]
    public Task<WorkingAgreementDto> Publish(long projectId, AgreementTokenRequest input, CancellationToken ct) => service.Publish(projectId, input, ct);
    [HttpPost("acceptances"), ProducesResponseType<WorkingAgreementDto>(200)]
    public Task<WorkingAgreementDto> Accept(long projectId, AgreementTokenRequest input, CancellationToken ct) => service.Accept(projectId, input, ct);
}
