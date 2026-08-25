using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Application.Features.Academic.Models;
using AIPMS.Application.Features.Academic.Services;
using MediatR;

namespace AIPMS.Application.Features.Academic.Commands;

public sealed record CreateMajorCommand(
    long DepartmentId,
    string Code,
    string Name,
    string? Description) : IRequest<MajorDto>;

public sealed class CreateMajorCommandHandler(
    IAcademicStructureRepository repository,
    AcademicAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<CreateMajorCommand, MajorDto>
{
    public async Task<MajorDto> Handle(
        CreateMajorCommand request,
        CancellationToken cancellationToken)
    {
        var department = await repository.GetDepartmentAsync(
            request.DepartmentId,
            cancellationToken)
            ?? throw new NotFoundException("Department", request.DepartmentId);

        await accessService.EnsureCanManageMajorInDepartmentAsync(
            department.Id,
            cancellationToken);

        if (!department.IsActive)
        {
            throw new ConflictException(
                "A major cannot be created under an inactive department.");
        }

        var organization = await repository.GetOrganizationAsync(
            department.OrganizationId,
            cancellationToken)
            ?? throw new NotFoundException("Organization", department.OrganizationId);

        if (!organization.IsActive)
        {
            throw new ConflictException(
                "A major cannot be created under an inactive organization.");
        }

        var code = AcademicInputNormalizer.NormalizeCode(request.Code);
        var name = AcademicInputNormalizer.NormalizeName(request.Name);
        var description = AcademicInputNormalizer.NormalizeDescription(request.Description);

        if (await repository.MajorCodeOrNameExistsAsync(
            department.Id,
            code,
            name,
            null,
            cancellationToken))
        {
            throw new ConflictException(
                "A major with the same code or name already exists in this department.");
        }

        var major = await repository.CreateMajorAsync(
            department.Id,
            code,
            name,
            description,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await AuditAsync(
            auditTrail,
            accessService.ActorUserId,
            "ACADEMIC_MAJOR_CREATED",
            major,
            cancellationToken);

        return major.ToDto();
    }

    internal static Task AuditAsync(
        IAuditTrail auditTrail,
        long actorUserId,
        string action,
        AcademicMajor major,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, object?>? additionalContext = null)
    {
        var context = new Dictionary<string, object?>
        {
            ["organizationId"] = major.OrganizationId,
            ["departmentId"] = major.DepartmentId,
            ["code"] = major.Code,
            ["name"] = major.Name
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
                "MAJOR",
                major.Id,
                context),
            cancellationToken);
    }
}

public sealed record UpdateMajorCommand(
    long MajorId,
    long DepartmentId,
    string Code,
    string Name,
    string? Description) : IRequest<MajorDto>;

public sealed class UpdateMajorCommandHandler(
    IAcademicStructureRepository repository,
    AcademicAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateMajorCommand, MajorDto>
{
    public async Task<MajorDto> Handle(
        UpdateMajorCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetMajorAsync(request.MajorId, cancellationToken)
            ?? throw new NotFoundException("Major", request.MajorId);

        await accessService.EnsureCanManageMajorInDepartmentAsync(
            existing.DepartmentId,
            cancellationToken);

        var targetDepartment = await repository.GetDepartmentAsync(
            request.DepartmentId,
            cancellationToken)
            ?? throw new NotFoundException("Department", request.DepartmentId);

        await accessService.EnsureCanManageMajorInDepartmentAsync(
            targetDepartment.Id,
            cancellationToken);

        if (existing.DepartmentId != targetDepartment.Id && !targetDepartment.IsActive)
        {
            throw new ConflictException(
                "A major cannot be moved to an inactive department.");
        }

        if (existing.DepartmentId != targetDepartment.Id)
        {
            var targetOrganization = await repository.GetOrganizationAsync(
                targetDepartment.OrganizationId,
                cancellationToken)
                ?? throw new NotFoundException(
                    "Organization",
                    targetDepartment.OrganizationId);

            if (!targetOrganization.IsActive)
            {
                throw new ConflictException(
                    "A major cannot be moved to a department in an inactive organization.");
            }
        }

        var code = AcademicInputNormalizer.NormalizeCode(request.Code);
        var name = AcademicInputNormalizer.NormalizeName(request.Name);
        var description = AcademicInputNormalizer.NormalizeDescription(request.Description);

        if (await repository.MajorCodeOrNameExistsAsync(
            targetDepartment.Id,
            code,
            name,
            existing.Id,
            cancellationToken))
        {
            throw new ConflictException(
                "A major with the same code or name already exists in the target department.");
        }

        var major = await repository.UpdateMajorAsync(
            existing.Id,
            targetDepartment.Id,
            code,
            name,
            description,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await CreateMajorCommandHandler.AuditAsync(
            auditTrail,
            accessService.ActorUserId,
            "ACADEMIC_MAJOR_UPDATED",
            major,
            cancellationToken,
            new Dictionary<string, object?>
            {
                ["previousDepartmentId"] = existing.DepartmentId
            });

        return major.ToDto();
    }
}

public sealed record SetMajorStatusCommand(
    long MajorId,
    bool IsActive) : IRequest<MajorDto>;

public sealed class SetMajorStatusCommandHandler(
    IAcademicStructureRepository repository,
    AcademicAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<SetMajorStatusCommand, MajorDto>
{
    public async Task<MajorDto> Handle(
        SetMajorStatusCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetMajorAsync(request.MajorId, cancellationToken)
            ?? throw new NotFoundException("Major", request.MajorId);

        await accessService.EnsureCanManageMajorInDepartmentAsync(
            existing.DepartmentId,
            cancellationToken);

        if (request.IsActive)
        {
            var department = await repository.GetDepartmentAsync(
                existing.DepartmentId,
                cancellationToken)
                ?? throw new NotFoundException("Department", existing.DepartmentId);

            if (!department.IsActive)
            {
                throw new ConflictException(
                    "A major cannot be activated while its department is inactive.");
            }

            var organization = await repository.GetOrganizationAsync(
                department.OrganizationId,
                cancellationToken)
                ?? throw new NotFoundException("Organization", department.OrganizationId);

            if (!organization.IsActive)
            {
                throw new ConflictException(
                    "A major cannot be activated while its organization is inactive.");
            }
        }

        var major = await repository.SetMajorActiveAsync(
            existing.Id,
            request.IsActive,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await CreateMajorCommandHandler.AuditAsync(
            auditTrail,
            accessService.ActorUserId,
            request.IsActive
                ? "ACADEMIC_MAJOR_ACTIVATED"
                : "ACADEMIC_MAJOR_DEACTIVATED",
            major,
            cancellationToken,
            new Dictionary<string, object?> { ["isActive"] = request.IsActive });

        return major.ToDto();
    }
}
