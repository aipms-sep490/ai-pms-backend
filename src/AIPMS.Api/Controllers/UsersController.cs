using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AccountSecurity.Commands;
using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.AccountSecurity.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/users")]
public sealed class UsersController(ISender sender) : ControllerBase
{
    [Authorize(Policy = AuthorizationPolicies.AccountSecurityManagement)]
    [HttpGet]
    [ProducesResponseType<PagedResult<UserAccountDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<UserAccountDto>>> GetUsers(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetUsersQuery(search, status, page, pageSize), cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AccountSecurityManagement)]
    [HttpGet("{userId:long}")]
    [ProducesResponseType<UserAccountDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserAccountDto>> GetUser(
        long userId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetUserByIdQuery(userId), cancellationToken));

    [HttpGet("me/profile")]
    [ProducesResponseType<UserAccountDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserAccountDto>> GetMyProfile(
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetMyProfileQuery(), cancellationToken));

    [HttpPut("me/profile")]
    [ProducesResponseType<UserAccountDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserAccountDto>> UpdateMyProfile(
        UpdateMyProfileRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdateMyProfileCommand(request.FullName, request.Phone, request.Title),
            cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AccountSecurityManagement)]
    [HttpPost]
    [ProducesResponseType<UserAccountDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<UserAccountDto>> CreateUser(
        CreateUserAccountRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateUserAccountCommand(
                request.DepartmentId,
                request.MajorId,
                request.Email,
                request.Password,
                request.FullName,
                request.Phone,
                request.StudentCode,
                request.EmployeeCode,
                request.Title,
                request.RoleIds),
            cancellationToken);
        return CreatedAtAction(nameof(GetUser), new { userId = result.Id }, result);
    }

    [Authorize(Policy = AuthorizationPolicies.AccountSecurityManagement)]
    [HttpPost("import")]
    [ProducesResponseType<IReadOnlyList<UserAccountDto>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<IReadOnlyList<UserAccountDto>>> ImportUsers(
        ImportUserAccountsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ImportUserAccountsCommand(request.Accounts),
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [Authorize(Policy = AuthorizationPolicies.AccountSecurityManagement)]
    [HttpPatch("{userId:long}/status")]
    [ProducesResponseType<UserAccountDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserAccountDto>> SetStatus(
        long userId,
        SetUserStatusRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new SetUserStatusCommand(userId, request.Status), cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AccountSecurityManagement)]
    [HttpPost("{userId:long}/activate")]
    [ProducesResponseType<UserAccountDto>(StatusCodes.Status200OK)]
    public Task<ActionResult<UserAccountDto>> Activate(
        long userId,
        CancellationToken cancellationToken) =>
        ChangeStatus(userId, "ACTIVE", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.AccountSecurityManagement)]
    [HttpPost("{userId:long}/deactivate")]
    [ProducesResponseType<UserAccountDto>(StatusCodes.Status200OK)]
    public Task<ActionResult<UserAccountDto>> Deactivate(
        long userId,
        CancellationToken cancellationToken) =>
        ChangeStatus(userId, "INACTIVE", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.AccountSecurityManagement)]
    [HttpPost("{userId:long}/block")]
    [ProducesResponseType<UserAccountDto>(StatusCodes.Status200OK)]
    public Task<ActionResult<UserAccountDto>> Block(
        long userId,
        CancellationToken cancellationToken) =>
        ChangeStatus(userId, "SUSPENDED", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.AccountSecurityManagement)]
    [HttpPost("{userId:long}/unblock")]
    [ProducesResponseType<UserAccountDto>(StatusCodes.Status200OK)]
    public Task<ActionResult<UserAccountDto>> Unblock(
        long userId,
        CancellationToken cancellationToken) =>
        ChangeStatus(userId, "ACTIVE", cancellationToken);

    [Authorize(Policy = AuthorizationPolicies.AccountSecurityManagement)]
    [HttpPut("{userId:long}/roles/{roleId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AssignRole(
        long userId,
        long roleId,
        CancellationToken cancellationToken)
    {
        await sender.Send(new AssignUserRoleCommand(userId, roleId), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = AuthorizationPolicies.AccountSecurityManagement)]
    [HttpDelete("{userId:long}/roles/{roleId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveRole(
        long userId,
        long roleId,
        CancellationToken cancellationToken)
    {
        await sender.Send(new RemoveUserRoleCommand(userId, roleId), cancellationToken);
        return NoContent();
    }

    private async Task<ActionResult<UserAccountDto>> ChangeStatus(
        long userId,
        string status,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new SetUserStatusCommand(userId, status), cancellationToken));
}
