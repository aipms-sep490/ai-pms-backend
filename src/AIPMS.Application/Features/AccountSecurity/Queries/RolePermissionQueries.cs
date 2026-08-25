using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AccountSecurity.Abstractions;
using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.AccountSecurity.Services;
using MediatR;

namespace AIPMS.Application.Features.AccountSecurity.Queries;

public sealed record GetRolesQuery(string? Search, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<SecurityRoleDto>>;

public sealed class GetRolesQueryHandler(
    IRolePermissionRepository repository,
    AccountSecurityAccessService accessService)
    : IRequestHandler<GetRolesQuery, PagedResult<SecurityRoleDto>>
{
    public async Task<PagedResult<SecurityRoleDto>> Handle(
        GetRolesQuery request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var result = await repository.GetRolesAsync(
            request.Search?.Trim(), request.Page, request.PageSize, cancellationToken);
        return new PagedResult<SecurityRoleDto>(
            result.Items.Select(static role => role.ToDto()).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }
}

public sealed record GetRoleByIdQuery(long RoleId) : IRequest<SecurityRoleDto>;

public sealed class GetRoleByIdQueryHandler(
    IRolePermissionRepository repository,
    AccountSecurityAccessService accessService)
    : IRequestHandler<GetRoleByIdQuery, SecurityRoleDto>
{
    public async Task<SecurityRoleDto> Handle(
        GetRoleByIdQuery request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        return (await repository.GetRoleAsync(request.RoleId, cancellationToken)
            ?? throw new NotFoundException("Role", request.RoleId)).ToDto();
    }
}

public sealed record GetPermissionsQuery(string? Search, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<SecurityPermissionDto>>;

public sealed class GetPermissionsQueryHandler(
    IRolePermissionRepository repository,
    AccountSecurityAccessService accessService)
    : IRequestHandler<GetPermissionsQuery, PagedResult<SecurityPermissionDto>>
{
    public async Task<PagedResult<SecurityPermissionDto>> Handle(
        GetPermissionsQuery request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var result = await repository.GetPermissionsAsync(
            request.Search?.Trim(), request.Page, request.PageSize, cancellationToken);
        return new PagedResult<SecurityPermissionDto>(
            result.Items.Select(static permission => permission.ToDto()).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }
}

public sealed record GetPermissionByIdQuery(long PermissionId)
    : IRequest<SecurityPermissionDto>;

public sealed class GetPermissionByIdQueryHandler(
    IRolePermissionRepository repository,
    AccountSecurityAccessService accessService)
    : IRequestHandler<GetPermissionByIdQuery, SecurityPermissionDto>
{
    public async Task<SecurityPermissionDto> Handle(
        GetPermissionByIdQuery request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        return (await repository.GetPermissionAsync(request.PermissionId, cancellationToken)
            ?? throw new NotFoundException("Permission", request.PermissionId)).ToDto();
    }
}

public sealed record GetPermissionMatrixQuery : IRequest<PermissionMatrixDto>;

public sealed class GetPermissionMatrixQueryHandler(
    IRolePermissionRepository repository,
    AccountSecurityAccessService accessService)
    : IRequestHandler<GetPermissionMatrixQuery, PermissionMatrixDto>
{
    public async Task<PermissionMatrixDto> Handle(
        GetPermissionMatrixQuery request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var matrix = await repository.GetPermissionMatrixAsync(cancellationToken);
        return new PermissionMatrixDto(
            matrix.Roles.Select(static role => role.ToDto()).ToArray(),
            matrix.Permissions.Select(static permission => permission.ToDto()).ToArray());
    }
}
