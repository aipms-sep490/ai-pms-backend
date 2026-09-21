using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.Commands;
using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Application.Features.Academic.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
public sealed class AcademicProfilesController(ISender sender) : ControllerBase
{
    [HttpGet("/api/v1/users/me/academic-profile")]
    public Task<AcademicProfileDto> Me(CancellationToken ct) => sender.Send(new GetMyAcademicProfileQuery(), ct);

    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    [HttpGet("/api/v1/academic/profile-verifications")]
    public Task<PagedResult<AcademicProfileDto>> List([FromQuery] string? status, [FromQuery] long? departmentId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => sender.Send(new GetAcademicProfilesQuery(status, departmentId, page, pageSize), ct);

    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    [HttpPost("/api/v1/users/{userId:long}/academic-profile/verify")]
    public Task<AcademicProfileDto> Verify(long userId, CancellationToken ct)
        => sender.Send(new VerifyAcademicProfileCommand(userId), ct);

    [Authorize(Policy = AuthorizationPolicies.AcademicManagement)]
    [HttpPost("/api/v1/users/{userId:long}/academic-profile/reject")]
    public Task<AcademicProfileDto> Reject(long userId, RejectAcademicProfileRequest request, CancellationToken ct)
        => sender.Send(new RejectAcademicProfileCommand(userId, request.Reason), ct);
}
