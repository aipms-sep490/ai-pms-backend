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
[Route("api/v1/security/permissions")]
public sealed class PermissionsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<SecurityPermissionDto>>> GetPermissions(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetPermissionsQuery(search, page, pageSize), cancellationToken));

    [HttpGet("{permissionId:long}")]
    public async Task<ActionResult<SecurityPermissionDto>> GetPermission(
        long permissionId,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPermissionByIdQuery(permissionId), cancellationToken));

    [HttpGet("matrix")]
    public async Task<ActionResult<PermissionMatrixDto>> GetMatrix(
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPermissionMatrixQuery(), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<SecurityPermissionDto>> CreatePermission(
        CreateSecurityCatalogRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreatePermissionCommand(request.Code, request.Name, request.Description),
            cancellationToken);
        return CreatedAtAction(nameof(GetPermission), new { permissionId = result.Id }, result);
    }

    [HttpPut("{permissionId:long}")]
    public async Task<ActionResult<SecurityPermissionDto>> UpdatePermission(
        long permissionId,
        UpdateSecurityCatalogRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdatePermissionCommand(
                permissionId,
                request.Code,
                request.Name,
                request.Description),
            cancellationToken));

    [HttpDelete("{permissionId:long}")]
    public async Task<IActionResult> DeletePermission(
        long permissionId,
        CancellationToken cancellationToken)
    {
        await sender.Send(new DeletePermissionCommand(permissionId), cancellationToken);
        return NoContent();
    }
}
