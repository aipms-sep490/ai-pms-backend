using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Application.Features.Academic.Services;
using MediatR;

namespace AIPMS.Application.Features.Academic.Commands;

public sealed record CreateDepartmentCommand(
    long OrganizationId,
    string Code,
    string Name,
    string? Description) : IRequest<DepartmentDto>;

public sealed class CreateDepartmentCommandHandler(
    IAcademicStructureRepository repository,
    AcademicAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<CreateDepartmentCommand, DepartmentDto>
{
    public async Task<DepartmentDto> Handle(
        CreateDepartmentCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureCanCreateDepartment();

        var organization = await repository.GetOrganizationAsync(
            request.OrganizationId,
            cancellationToken)
            ?? throw new NotFoundException("Organization", request.OrganizationId);

        if (!organization.IsActive)
        {
            throw new ConflictException(
                "A department cannot be created under an inactive organization.");
        }

        var code = AcademicInputNormalizer.NormalizeCode(request.Code);
        var name = AcademicInputNormalizer.NormalizeName(request.Name);
        var description = AcademicInputNormalizer.NormalizeDescription(request.Description);

        if (await repository.DepartmentCodeOrNameExistsAsync(
            request.OrganizationId,
            code,
            name,
            null,
            cancellationToken))
        {
            throw new ConflictException(
                "A department with the same code or name already exists in this organization.");
        }

        var department = await repository.CreateDepartmentAsync(
            request.OrganizationId,
            code,
            name,
            description,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await AuditAsync(
            auditTrail,
            accessService.ActorUserId,
            "ACADEMIC_DEPARTMENT_CREATED",
            department,
            cancellationToken);

        return department.ToDto();
    }

    internal static Task AuditAsync(
        IAuditTrail auditTrail,
        long actorUserId,
        string action,
        Models.AcademicDepartment department,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, object?>? additionalContext = null)
    {
        var context = new Dictionary<string, object?>
        {
            ["organizationId"] = department.OrganizationId,
            ["code"] = department.Code,
            ["name"] = department.Name
        };

        if (additionalContext is not null)
        {
            foreach (var item in additionalContext)
            {
                context[item.Key] = item.Value;
            }
        }

        return auditTrail.RecordAsync(
            new AuditEntry(
                actorUserId,
                action,
                "DEPARTMENT",
                department.Id,
                context),
            cancellationToken);
    }
}

public sealed record UpdateDepartmentCommand(
    long DepartmentId,
    string Code,
    string Name,
    string? Description) : IRequest<DepartmentDto>;

public sealed class UpdateDepartmentCommandHandler(
    IAcademicStructureRepository repository,
    AcademicAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateDepartmentCommand, DepartmentDto>
{
    public async Task<DepartmentDto> Handle(
        UpdateDepartmentCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetDepartmentAsync(
            request.DepartmentId,
            cancellationToken)
            ?? throw new NotFoundException("Department", request.DepartmentId);

        await accessService.EnsureCanManageDepartmentAsync(
            existing.Id,
            cancellationToken);

        var code = AcademicInputNormalizer.NormalizeCode(request.Code);
        var name = AcademicInputNormalizer.NormalizeName(request.Name);
        var description = AcademicInputNormalizer.NormalizeDescription(request.Description);

        if (await repository.DepartmentCodeOrNameExistsAsync(
            existing.OrganizationId,
            code,
            name,
            existing.Id,
            cancellationToken))
        {
            throw new ConflictException(
                "A department with the same code or name already exists in this organization.");
        }

        var department = await repository.UpdateDepartmentAsync(
            existing.Id,
            code,
            name,
            description,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await CreateDepartmentCommandHandler.AuditAsync(
            auditTrail,
            accessService.ActorUserId,
            "ACADEMIC_DEPARTMENT_UPDATED",
            department,
            cancellationToken);

        return department.ToDto();
    }
}

public sealed record SetDepartmentStatusCommand(
    long DepartmentId,
    bool IsActive) : IRequest<DepartmentDto>;

public sealed class SetDepartmentStatusCommandHandler(
    IAcademicStructureRepository repository,
    AcademicAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<SetDepartmentStatusCommand, DepartmentDto>
{
    public async Task<DepartmentDto> Handle(
        SetDepartmentStatusCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetDepartmentAsync(
            request.DepartmentId,
            cancellationToken)
            ?? throw new NotFoundException("Department", request.DepartmentId);

        await accessService.EnsureCanManageDepartmentAsync(existing.Id, cancellationToken);

        if (request.IsActive)
        {
            var organization = await repository.GetOrganizationAsync(
                existing.OrganizationId,
                cancellationToken)
                ?? throw new NotFoundException("Organization", existing.OrganizationId);

            if (!organization.IsActive)
            {
                throw new ConflictException(
                    "A department cannot be activated while its organization is inactive.");
            }
        }

        var department = await repository.SetDepartmentActiveAsync(
            existing.Id,
            request.IsActive,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await CreateDepartmentCommandHandler.AuditAsync(
            auditTrail,
            accessService.ActorUserId,
            request.IsActive
                ? "ACADEMIC_DEPARTMENT_ACTIVATED"
                : "ACADEMIC_DEPARTMENT_DEACTIVATED",
            department,
            cancellationToken,
            new Dictionary<string, object?>
            {
                ["isActive"] = request.IsActive,
                ["descendantsDeactivated"] = !request.IsActive
            });

        return department.ToDto();
    }
}
