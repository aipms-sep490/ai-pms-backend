using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AccountSecurity.Abstractions;
using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.AccountSecurity.Services;
using MediatR;

namespace AIPMS.Application.Features.AccountSecurity.Commands;

public sealed record CreateRoleCommand(string Code, string Name, string? Description)
    : IRequest<SecurityRoleDto>;

public sealed class CreateRoleCommandHandler(
    IRolePermissionRepository repository,
    AccountSecurityAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : IRequestHandler<CreateRoleCommand, SecurityRoleDto>
{
    public async Task<SecurityRoleDto> Handle(
        CreateRoleCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var code = NormalizeCode(request.Code);
        if (await repository.RoleCodeExistsAsync(code, null, cancellationToken))
        {
            throw new ConflictException("A role with the same code already exists.");
        }

        var role = await repository.CreateRoleAsync(
            code,
            request.Name.Trim(),
            NormalizeOptional(request.Description),
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await AuditAsync(auditTrail, accessService.ActorUserId, "ROLE_CREATED", role.Id, code, cancellationToken);
        return role.ToDto();
    }

    internal static string NormalizeCode(string value) =>
        value.Trim().ToUpperInvariant();

    internal static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static Task AuditAsync(
        IAuditTrail auditTrail,
        long actorUserId,
        string action,
        long entityId,
        string code,
        CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                action,
                "ROLE",
                entityId,
                new Dictionary<string, object?> { ["code"] = code }),
            cancellationToken);
}

public sealed record UpdateRoleCommand(long RoleId, string Code, string Name, string? Description)
    : IRequest<SecurityRoleDto>;

public sealed class UpdateRoleCommandHandler(
    IRolePermissionRepository repository,
    AccountSecurityAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : IRequestHandler<UpdateRoleCommand, SecurityRoleDto>
{
    public async Task<SecurityRoleDto> Handle(
        UpdateRoleCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var existing = await repository.GetRoleAsync(request.RoleId, cancellationToken)
            ?? throw new NotFoundException("Role", request.RoleId);
        var code = CreateRoleCommandHandler.NormalizeCode(request.Code);

        if (existing.IsSystemRole && !string.Equals(existing.Code, code, StringComparison.Ordinal))
        {
            throw new ConflictException("The code of a system role cannot be changed.");
        }

        if (await repository.RoleCodeExistsAsync(code, request.RoleId, cancellationToken))
        {
            throw new ConflictException("A role with the same code already exists.");
        }

        var role = await repository.UpdateRoleAsync(
            request.RoleId,
            code,
            request.Name.Trim(),
            CreateRoleCommandHandler.NormalizeOptional(request.Description),
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        await CreateRoleCommandHandler.AuditAsync(
            auditTrail, accessService.ActorUserId, "ROLE_UPDATED", role.Id, role.Code, cancellationToken);
        return role.ToDto();
    }
}

public sealed record DeleteRoleCommand(long RoleId) : IRequest;

public sealed class DeleteRoleCommandHandler(
    IRolePermissionRepository repository,
    AccountSecurityAccessService accessService,
    IAuditTrail auditTrail) : IRequestHandler<DeleteRoleCommand>
{
    public async Task Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var role = await repository.GetRoleAsync(request.RoleId, cancellationToken)
            ?? throw new NotFoundException("Role", request.RoleId);
        if (role.IsSystemRole)
        {
            throw new ConflictException("System roles cannot be deleted.");
        }

        await repository.DeleteRoleAsync(request.RoleId, cancellationToken);
        await CreateRoleCommandHandler.AuditAsync(
            auditTrail, accessService.ActorUserId, "ROLE_DELETED", role.Id, role.Code, cancellationToken);
    }
}

public sealed record CreatePermissionCommand(string Code, string Name, string? Description)
    : IRequest<SecurityPermissionDto>;

public sealed class CreatePermissionCommandHandler(
    IRolePermissionRepository repository,
    AccountSecurityAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : IRequestHandler<CreatePermissionCommand, SecurityPermissionDto>
{
    public async Task<SecurityPermissionDto> Handle(
        CreatePermissionCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var code = CreateRoleCommandHandler.NormalizeCode(request.Code);
        if (await repository.PermissionCodeExistsAsync(code, null, cancellationToken))
        {
            throw new ConflictException("A permission with the same code already exists.");
        }

        var permission = await repository.CreatePermissionAsync(
            code,
            request.Name.Trim(),
            CreateRoleCommandHandler.NormalizeOptional(request.Description),
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        await AuditPermissionAsync(
            auditTrail, accessService.ActorUserId, "PERMISSION_CREATED", permission.Id, code, cancellationToken);
        return permission.ToDto();
    }

    internal static Task AuditPermissionAsync(
        IAuditTrail auditTrail,
        long actorUserId,
        string action,
        long entityId,
        string code,
        CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                action,
                "PERMISSION",
                entityId,
                new Dictionary<string, object?> { ["code"] = code }),
            cancellationToken);
}

public sealed record UpdatePermissionCommand(
    long PermissionId,
    string Code,
    string Name,
    string? Description) : IRequest<SecurityPermissionDto>;

public sealed class UpdatePermissionCommandHandler(
    IRolePermissionRepository repository,
    AccountSecurityAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : IRequestHandler<UpdatePermissionCommand, SecurityPermissionDto>
{
    public async Task<SecurityPermissionDto> Handle(
        UpdatePermissionCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var existing = await repository.GetPermissionAsync(request.PermissionId, cancellationToken)
            ?? throw new NotFoundException("Permission", request.PermissionId);
        var code = CreateRoleCommandHandler.NormalizeCode(request.Code);
        if (existing.IsSystemPermission && !string.Equals(existing.Code, code, StringComparison.Ordinal))
        {
            throw new ConflictException("The code of a system permission cannot be changed.");
        }

        if (await repository.PermissionCodeExistsAsync(code, request.PermissionId, cancellationToken))
        {
            throw new ConflictException("A permission with the same code already exists.");
        }

        var permission = await repository.UpdatePermissionAsync(
            request.PermissionId,
            code,
            request.Name.Trim(),
            CreateRoleCommandHandler.NormalizeOptional(request.Description),
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        await CreatePermissionCommandHandler.AuditPermissionAsync(
            auditTrail, accessService.ActorUserId, "PERMISSION_UPDATED", permission.Id, code, cancellationToken);
        return permission.ToDto();
    }
}

public sealed record DeletePermissionCommand(long PermissionId) : IRequest;

public sealed class DeletePermissionCommandHandler(
    IRolePermissionRepository repository,
    AccountSecurityAccessService accessService,
    IAuditTrail auditTrail) : IRequestHandler<DeletePermissionCommand>
{
    public async Task Handle(DeletePermissionCommand request, CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var permission = await repository.GetPermissionAsync(request.PermissionId, cancellationToken)
            ?? throw new NotFoundException("Permission", request.PermissionId);
        if (permission.IsSystemPermission)
        {
            throw new ConflictException("System permissions cannot be deleted.");
        }

        await repository.DeletePermissionAsync(request.PermissionId, cancellationToken);
        await CreatePermissionCommandHandler.AuditPermissionAsync(
            auditTrail,
            accessService.ActorUserId,
            "PERMISSION_DELETED",
            permission.Id,
            permission.Code,
            cancellationToken);
    }
}

public sealed record ReplaceRolePermissionsCommand(
    long RoleId,
    IReadOnlyCollection<long> PermissionIds) : IRequest<SecurityRoleDto>;

public sealed class ReplaceRolePermissionsCommandHandler(
    IRolePermissionRepository repository,
    AccountSecurityAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : IRequestHandler<ReplaceRolePermissionsCommand, SecurityRoleDto>
{
    public async Task<SecurityRoleDto> Handle(
        ReplaceRolePermissionsCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var role = await repository.GetRoleAsync(request.RoleId, cancellationToken)
            ?? throw new NotFoundException("Role", request.RoleId);
        var permissionIds = request.PermissionIds.Distinct().ToArray();
        foreach (var permissionId in permissionIds)
        {
            _ = await repository.GetPermissionAsync(permissionId, cancellationToken)
                ?? throw new NotFoundException("Permission", permissionId);
        }

        await repository.ReplaceRolePermissionsAsync(
            request.RoleId,
            permissionIds,
            accessService.ActorUserId,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await CreateRoleCommandHandler.AuditAsync(
            auditTrail,
            accessService.ActorUserId,
            "ROLE_PERMISSIONS_REPLACED",
            role.Id,
            role.Code,
            cancellationToken);

        return (await repository.GetRoleAsync(role.Id, cancellationToken))!.ToDto();
    }
}
