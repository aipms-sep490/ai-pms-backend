using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Semesters.Abstractions;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Application.Features.Semesters.Services;
using MediatR;

namespace AIPMS.Application.Features.Semesters.Commands;

// ── Create ProjectPeriod ──────────────────────────────────────────────────────

public sealed record CreateProjectPeriodCommand(
    long AcademicSemesterId,
    string Code,
    string Name,
    string PeriodType,
    DateTime StartAt,
    DateTime EndAt) : IRequest<ProjectPeriodDto>;

public sealed class CreateProjectPeriodCommandHandler(
    ISemesterRepository repository,
    SemesterAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<CreateProjectPeriodCommand, ProjectPeriodDto>
{
    public async Task<ProjectPeriodDto> Handle(
        CreateProjectPeriodCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureCanManageProjectPeriods();

        var semester = await repository.GetSemesterAsync(
            request.AcademicSemesterId,
            cancellationToken)
            ?? throw new NotFoundException("AcademicSemester", request.AcademicSemesterId);

        if (semester.Status is SemesterStatuses.Closed or SemesterStatuses.Archived)
        {
            throw new ConflictException(
                "Cannot add project periods to a closed or archived semester.");
        }

        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();

        if (await repository.PeriodCodeExistsAsync(
            request.AcademicSemesterId,
            code,
            null,
            cancellationToken))
        {
            throw new ConflictException(
                "A project period with the same code already exists in this semester.");
        }

        var period = await repository.CreateProjectPeriodAsync(
            request.AcademicSemesterId,
            code,
            name,
            request.PeriodType,
            request.StartAt,
            request.EndAt,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                accessService.ActorUserId,
                "PROJECT_PERIOD_CREATED",
                "PROJECT_PERIOD",
                period.Id,
                new Dictionary<string, object?>
                {
                    ["semesterId"] = period.AcademicSemesterId,
                    ["code"] = period.Code,
                    ["name"] = period.Name,
                    ["periodType"] = period.PeriodType
                }),
            cancellationToken);

        return period.ToDto();
    }
}

// ── Update ProjectPeriod ──────────────────────────────────────────────────────

public sealed record UpdateProjectPeriodCommand(
    long PeriodId,
    string Code,
    string Name,
    string PeriodType,
    DateTime StartAt,
    DateTime EndAt) : IRequest<ProjectPeriodDto>;

public sealed class UpdateProjectPeriodCommandHandler(
    ISemesterRepository repository,
    SemesterAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateProjectPeriodCommand, ProjectPeriodDto>
{
    public async Task<ProjectPeriodDto> Handle(
        UpdateProjectPeriodCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureCanManageProjectPeriods();

        var existing = await repository.GetProjectPeriodAsync(
            request.PeriodId,
            cancellationToken)
            ?? throw new NotFoundException("ProjectPeriod", request.PeriodId);

        if (existing.Status is SemesterStatuses.Closed or SemesterStatuses.Archived)
        {
            throw new ConflictException(
                "A closed or archived project period cannot be modified.");
        }

        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();

        if (await repository.PeriodCodeExistsAsync(
            existing.AcademicSemesterId,
            code,
            existing.Id,
            cancellationToken))
        {
            throw new ConflictException(
                "A project period with the same code already exists in this semester.");
        }

        var period = await repository.UpdateProjectPeriodAsync(
            existing.Id,
            code,
            name,
            request.PeriodType,
            request.StartAt,
            request.EndAt,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                accessService.ActorUserId,
                "PROJECT_PERIOD_UPDATED",
                "PROJECT_PERIOD",
                period.Id,
                new Dictionary<string, object?>
                {
                    ["code"] = period.Code,
                    ["name"] = period.Name,
                    ["periodType"] = period.PeriodType
                }),
            cancellationToken);

        return period.ToDto();
    }
}

// ── Set ProjectPeriod Status ──────────────────────────────────────────────────

public sealed record SetProjectPeriodStatusCommand(
    long PeriodId,
    string Status) : IRequest<ProjectPeriodDto>;

public sealed class SetProjectPeriodStatusCommandHandler(
    ISemesterRepository repository,
    SemesterAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<SetProjectPeriodStatusCommand, ProjectPeriodDto>
{
    // Same lifecycle as semester: DRAFT → UPCOMING → ACTIVE → CLOSED → ARCHIVED
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

    public async Task<ProjectPeriodDto> Handle(
        SetProjectPeriodStatusCommand request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureCanManageProjectPeriods();

        var existing = await repository.GetProjectPeriodAsync(
            request.PeriodId,
            cancellationToken)
            ?? throw new NotFoundException("ProjectPeriod", request.PeriodId);

        if (!AllowedTransitions.TryGetValue(existing.Status, out var allowed)
            || !allowed.Contains(request.Status))
        {
            throw new ConflictException(
                $"Cannot transition project period from '{existing.Status}' to '{request.Status}'.");
        }

        var period = await repository.SetProjectPeriodStatusAsync(
            existing.Id,
            request.Status,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                accessService.ActorUserId,
                "PROJECT_PERIOD_STATUS_CHANGED",
                "PROJECT_PERIOD",
                period.Id,
                new Dictionary<string, object?>
                {
                    ["fromStatus"] = existing.Status,
                    ["toStatus"] = period.Status
                }),
            cancellationToken);

        return period.ToDto();
    }
}
