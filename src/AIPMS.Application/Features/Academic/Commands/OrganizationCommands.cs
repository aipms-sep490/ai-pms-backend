using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Application.Features.Academic.Services;
using MediatR;

namespace AIPMS.Application.Features.Academic.Commands;

public sealed record CreateOrganizationCommand(
    string Code,
    string Name,
    string? Description) : IRequest<OrganizationDto>;

public sealed class CreateOrganizationCommandHandler(
    IAcademicStructureRepository repository,
    AcademicAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<CreateOrganizationCommand, OrganizationDto>
{
    public async Task<OrganizationDto> Handle(
        CreateOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureCanManageOrganizations();

        var code = AcademicInputNormalizer.NormalizeCode(request.Code);
        var name = AcademicInputNormalizer.NormalizeName(request.Name);
        var description = AcademicInputNormalizer.NormalizeDescription(request.Description);

        if (await repository.OrganizationCodeOrNameExistsAsync(
            code,
            name,
            null,
            cancellationToken))
        {
            throw new ConflictException(
                "An organization with the same code or name already exists.");
        }

        var organization = await repository.CreateOrganizationAsync(
            code,
            name,
            description,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                accessService.ActorUserId,
                "ACADEMIC_ORGANIZATION_CREATED",
                "ORGANIZATION",
                organization.Id,
                new Dictionary<string, object?>
                {
                    ["code"] = organization.Code,
                    ["name"] = organization.Name
                }),
            cancellationToken);

        return organization.ToDto();
    }
}

public sealed record UpdateOrganizationCommand(
    long OrganizationId,
    string Code,
    string Name,
    string? Description) : IRequest<OrganizationDto>;

public sealed class UpdateOrganizationCommandHandler(
    IAcademicStructureRepository repository,
    AcademicAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateOrganizationCommand, OrganizationDto>
{
    public async Task<OrganizationDto> Handle(
        UpdateOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureCanManageOrganizations();

        _ = await repository.GetOrganizationAsync(request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Organization", request.OrganizationId);

        var code = AcademicInputNormalizer.NormalizeCode(request.Code);
        var name = AcademicInputNormalizer.NormalizeName(request.Name);
        var description = AcademicInputNormalizer.NormalizeDescription(request.Description);

        if (await repository.OrganizationCodeOrNameExistsAsync(
            code,
            name,
            request.OrganizationId,
            cancellationToken))
        {
            throw new ConflictException(
                "An organization with the same code or name already exists.");
        }

        var organization = await repository.UpdateOrganizationAsync(
            request.OrganizationId,
            code,
            name,
            description,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                accessService.ActorUserId,
                "ACADEMIC_ORGANIZATION_UPDATED",
                "ORGANIZATION",
                organization.Id,
                new Dictionary<string, object?>
                {
                    ["code"] = organization.Code,
                    ["name"] = organization.Name
                }),
            cancellationToken);

        return organization.ToDto();
    }
}

public sealed record SetOrganizationStatusCommand(
    long OrganizationId,
    bool IsActive) : IRequest<OrganizationDto>;

public sealed class SetOrganizationStatusCommandHandler(
    IAcademicStructureRepository repository,
    AcademicAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<SetOrganizationStatusCommand, OrganizationDto>
{
    public async Task<OrganizationDto> Handle(
        SetOrganizationStatusCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureCanManageOrganizations();

        _ = await repository.GetOrganizationAsync(request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Organization", request.OrganizationId);

        var organization = await repository.SetOrganizationActiveAsync(
            request.OrganizationId,
            request.IsActive,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                accessService.ActorUserId,
                request.IsActive
                    ? "ACADEMIC_ORGANIZATION_ACTIVATED"
                    : "ACADEMIC_ORGANIZATION_DEACTIVATED",
                "ORGANIZATION",
                organization.Id,
                new Dictionary<string, object?>
                {
                    ["isActive"] = request.IsActive,
                    ["descendantsDeactivated"] = !request.IsActive
                }),
            cancellationToken);

        return organization.ToDto();
    }
}
