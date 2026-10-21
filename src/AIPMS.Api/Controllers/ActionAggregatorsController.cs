using AIPMS.Application.Features.ActionAggregators;
using AIPMS.Application.Features.ActionAggregators.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[Route("api/v1")]
public sealed class ActionAggregatorsController(IActionAggregatorService service) : ControllerBase
{
    [HttpGet("students/me/next-actions")]
    [ProducesResponseType<StudentNextActionsDto>(200)]
    public Task<StudentNextActionsDto> Student(CancellationToken ct) => service.StudentNextActions(ct);

    [HttpGet("supervisors/me/triage-queue")]
    [ProducesResponseType<SupervisorTriageQueueDto>(200)]
    public Task<SupervisorTriageQueueDto> Supervisor(CancellationToken ct) => service.SupervisorTriage(ct);

    [HttpGet("departments/{departmentId:long}/urgent-actions")]
    [ProducesResponseType<DepartmentUrgentActionsDto>(200)]
    public Task<DepartmentUrgentActionsDto> Department(long departmentId, CancellationToken ct) => service.DepartmentUrgent(departmentId, ct);
}
