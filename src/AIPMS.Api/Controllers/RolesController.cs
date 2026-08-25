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
[Authorize(Policy = AuthorizationPolicies.AccountSecurityManagement)]
[Route("api/v1/security/roles")]
public sealed class RolesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SecurityRoleDto>>> GetRoles(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetRolesQuery(search, page, pageSize), cancellationToken));

    [HttpGet("{roleId:long}")]
    public async Task<ActionResult<SecurityRoleDto>> GetRole(
        long roleId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetRoleByIdQuery(roleId), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<SecurityRoleDto>> CreateRole(
        CreateSecurityCatalogRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateRoleCommand(request.Code, request.Name, request.Description),
            cancellationToken);
        return CreatedAtAction(nameof(GetRole), new { roleId = result.Id }, result);
    }

    [HttpPut("{roleId:long}")]
    public async Task<ActionResult<SecurityRoleDto>> UpdateRole(
        long roleId,
        UpdateSecurityCatalogRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdateRoleCommand(roleId, request.Code, request.Name, request.Description),
            cancellationToken));

    [HttpDelete("{roleId:long}")]
    public async Task<IActionResult> DeleteRole(long roleId, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteRoleCommand(roleId), cancellationToken);
        return NoContent();
    }

    [HttpPut("{roleId:long}/permissions")]
    public async Task<ActionResult<SecurityRoleDto>> ReplacePermissions(
        long roleId,
        ReplaceRolePermissionsRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new ReplaceRolePermissionsCommand(roleId, request.PermissionIds),
            cancellationToken));
}
