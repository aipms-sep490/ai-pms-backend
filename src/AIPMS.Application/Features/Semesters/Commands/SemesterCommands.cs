using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Semesters.Abstractions;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Application.Features.Semesters.Services;
using MediatR;

namespace AIPMS.Application.Features.Semesters.Commands;

// ── Create Semester ───────────────────────────────────────────────────────────

public sealed record CreateSemesterCommand(
    long OrganizationId,
    string Code,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate) : IRequest<SemesterDto>;

public sealed class CreateSemesterCommandHandler(
    ISemesterRepository repository,
    SemesterAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<CreateSemesterCommand, SemesterDto>
{
    public async Task<SemesterDto> Handle(
        CreateSemesterCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureCanManageSemesters();

        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();

        if (await repository.SemesterCodeExistsAsync(
            request.OrganizationId,
            code,
            null,
            cancellationToken))
        {
            throw new ConflictException(
                "A semester with the same code already exists in this organization.");
        }

        var semester = await repository.CreateSemesterAsync(
            request.OrganizationId,
            code,
            name,
            request.StartDate,
            request.EndDate,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                accessService.ActorUserId,
                "SEMESTER_CREATED",
                "SEMESTER",
                semester.Id,
                new Dictionary<string, object?>
                {
                    ["organizationId"] = semester.OrganizationId,
                    ["code"] = semester.Code,
                    ["name"] = semester.Name
                }),
            cancellationToken);

        return semester.ToDto();
    }
}

// ── Update Semester ───────────────────────────────────────────────────────────

public sealed record UpdateSemesterCommand(
    long SemesterId,
    string Code,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate) : IRequest<SemesterDto>;

public sealed class UpdateSemesterCommandHandler(
    ISemesterRepository repository,
    SemesterAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateSemesterCommand, SemesterDto>
{
    public async Task<SemesterDto> Handle(
        UpdateSemesterCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureCanManageSemesters();

        var existing = await repository.GetSemesterAsync(
            request.SemesterId,
            cancellationToken)
            ?? throw new NotFoundException("AcademicSemester", request.SemesterId);

        if (existing.Status is SemesterStatuses.Closed or SemesterStatuses.Archived)
        {
            throw new ConflictException(
                "A closed or archived semester cannot be modified.");
        }

        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();

        if (await repository.SemesterCodeExistsAsync(
            existing.OrganizationId,
            code,
            existing.Id,
            cancellationToken))
        {
            throw new ConflictException(
                "A semester with the same code already exists in this organization.");
        }

        var semester = await repository.UpdateSemesterAsync(
            existing.Id,
            code,
            name,
            request.StartDate,
            request.EndDate,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                accessService.ActorUserId,
                "SEMESTER_UPDATED",
                "SEMESTER",
                semester.Id,
                new Dictionary<string, object?>
                {
                    ["code"] = semester.Code,
                    ["name"] = semester.Name
                }),
            cancellationToken);

        return semester.ToDto();
    }
}

// ── Set Semester Status ───────────────────────────────────────────────────────

public sealed record SetSemesterStatusCommand(
    long SemesterId,
    string Status) : IRequest<SemesterDto>;

public sealed class SetSemesterStatusCommandHandler(
    ISemesterRepository repository,
    SemesterAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<SetSemesterStatusCommand, SemesterDto>
{
    // Forward-only state machine: DRAFT → UPCOMING → ACTIVE → CLOSED → ARCHIVED
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedTransitions =
        new Dictionary<string, IReadOnlySet<string>>
        {
            [SemesterStatuses.Draft] = new HashSet<string>
            {
                SemesterStatuses.Upcoming, SemesterStatuses.Archived
            },
            [SemesterStatuses.Upcoming] = new HashSet<string>
            {
                SemesterStatuses.Active, SemesterStatuses.Draft
            },
            [SemesterStatuses.Active] = new HashSet<string>
            {
                SemesterStatuses.Closed
            },
            [SemesterStatuses.Closed] = new HashSet<string>
            {
                SemesterStatuses.Archived
            },
            [SemesterStatuses.Archived] = new HashSet<string>()
        };

    public async Task<SemesterDto> Handle(
        SetSemesterStatusCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureCanManageSemesters();

        var existing = await repository.GetSemesterAsync(
            request.SemesterId,
            cancellationToken)
            ?? throw new NotFoundException("AcademicSemester", request.SemesterId);

        if (!AllowedTransitions.TryGetValue(existing.Status, out var allowed)
            || !allowed.Contains(request.Status))
        {
            throw new ConflictException(
                $"Cannot transition semester from '{existing.Status}' to '{request.Status}'.");
        }

        var semester = await repository.SetSemesterStatusAsync(
            existing.Id,
            request.Status,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                accessService.ActorUserId,
                "SEMESTER_STATUS_CHANGED",
                "SEMESTER",
                semester.Id,
                new Dictionary<string, object?>
                {
                    ["fromStatus"] = existing.Status,
                    ["toStatus"] = semester.Status
                }),
            cancellationToken);

        return semester.ToDto();
    }
}
