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
    DateTime EndAt,
    int? MinTeamSize = 3,
    int? MaxTeamSize = 5,
    int? MinDistinctMajors = 1,
    int? MaxProjectsPerSupervisor = 5,
    long? MilestoneTemplateId = null,
    long? RubricId = null) : IRequest<ProjectPeriodDto>;

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

        // Validate period dates against semester dates
        var semStart = semester.StartDate.ToDateTime(TimeOnly.MinValue);
        var semEnd = semester.EndDate.ToDateTime(TimeOnly.MaxValue);
        if (request.StartAt < semStart || request.EndAt > semEnd)
        {
            throw new ConflictException(
                $"Project Period window ({request.StartAt:yyyy-MM-dd} - {request.EndAt:yyyy-MM-dd}) must fall within parent Academic Semester window ({semester.StartDate:yyyy-MM-dd} - {semester.EndDate:yyyy-MM-dd}).");
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

        if (request.MilestoneTemplateId.HasValue)
        {
            throw new ConflictException(
                "Milestone Template module is not available yet for this Project Period (DEFERRED).");
        }

        if (request.RubricId.HasValue)
        {
            var isRubricUsable = await repository.ValidateRubricUsableAsync(
                request.RubricId.Value,
                request.AcademicSemesterId,
                cancellationToken);

            if (!isRubricUsable)
            {
                throw new ConflictException(
                    $"Rubric with ID {request.RubricId.Value} does not exist, is inactive, or does not belong to this semester.");
            }
        }

        int effMinTeam = request.MinTeamSize ?? 3;
        int effMaxTeam = request.MaxTeamSize ?? 5;
        int effMinMajors = request.MinDistinctMajors ?? 1;
        int effMaxProjects = request.MaxProjectsPerSupervisor ?? 5;

        if (effMinTeam < 1 || effMaxTeam < effMinTeam)
        {
            throw new ConflictException(
                $"Invalid team size configuration: minTeamSize ({effMinTeam}) must be >= 1 and <= maxTeamSize ({effMaxTeam}).");
        }

        if (effMinMajors < 1 || effMinMajors > effMaxTeam)
        {
            throw new ConflictException(
                $"Invalid distinct majors configuration: minDistinctMajors ({effMinMajors}) must be >= 1 and <= maxTeamSize ({effMaxTeam}).");
        }

        if (effMaxProjects < 1)
        {
            throw new ConflictException(
                $"Invalid supervisor quota configuration: maxProjectsPerSupervisor ({effMaxProjects}) must be >= 1.");
        }

        // Check for chronological overlap with existing periods of the same type in this semester
        var existingPeriods = await repository.GetProjectPeriodsAsync(
            request.AcademicSemesterId,
            null,
            null,
            request.PeriodType,
            1,
            1000,
            cancellationToken);

        bool hasOverlap = existingPeriods.Items.Any(p =>
            p.Status != SemesterStatuses.Archived
            && p.StartAt < request.EndAt
            && request.StartAt < p.EndAt);

        if (hasOverlap)
        {
            throw new ConflictException(
                $"Project Period window overlaps with an existing '{request.PeriodType}' period in this semester.");
        }

        return await repository.ExecuteInTransactionAsync(async () =>
        {
            var period = await repository.CreateProjectPeriodAsync(
                request.AcademicSemesterId,
                code,
                name,
                request.PeriodType,
                request.StartAt,
                request.EndAt,
                request.MinTeamSize,
                request.MaxTeamSize,
                request.MinDistinctMajors,
                request.MaxProjectsPerSupervisor,
                request.MilestoneTemplateId,
                request.RubricId,
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
                        ["periodType"] = period.PeriodType,
                        ["minTeamSize"] = period.MinTeamSize,
                        ["maxTeamSize"] = period.MaxTeamSize,
                        ["minDistinctMajors"] = period.MinDistinctMajors
                    }),
                cancellationToken);

            return period.ToDto();
        }, cancellationToken);
    }
}

// ── Update ProjectPeriod ──────────────────────────────────────────────────────

public sealed record UpdateProjectPeriodCommand(
    long PeriodId,
    string Code,
    string Name,
    string PeriodType,
    DateTime StartAt,
    DateTime EndAt,
    int? MinTeamSize = null,
    int? MaxTeamSize = null,
    int? MinDistinctMajors = null,
    int? MaxProjectsPerSupervisor = null,
    long? MilestoneTemplateId = null,
    long? RubricId = null) : IRequest<ProjectPeriodDto>;

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

        var semester = await repository.GetSemesterAsync(
            existing.AcademicSemesterId,
            cancellationToken)
            ?? throw new NotFoundException("AcademicSemester", existing.AcademicSemesterId);

        if (semester.Status is SemesterStatuses.Closed or SemesterStatuses.Archived)
        {
            throw new ConflictException(
                "Cannot modify a project period belonging to a closed or archived semester.");
        }

        // Validate period dates against semester dates
        var semStart = semester.StartDate.ToDateTime(TimeOnly.MinValue);
        var semEnd = semester.EndDate.ToDateTime(TimeOnly.MaxValue);
        if (request.StartAt < semStart || request.EndAt > semEnd)
        {
            throw new ConflictException(
                $"Project Period window ({request.StartAt:yyyy-MM-dd} - {request.EndAt:yyyy-MM-dd}) must fall within parent Academic Semester window ({semester.StartDate:yyyy-MM-dd} - {semester.EndDate:yyyy-MM-dd}).");
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

        // Check for chronological overlap with existing periods of the same type
        var existingPeriods = await repository.GetProjectPeriodsAsync(
            existing.AcademicSemesterId,
            null,
            null,
            request.PeriodType,
            1,
            1000,
            cancellationToken);

        bool hasOverlap = existingPeriods.Items.Any(p =>
            p.Id != existing.Id
            && p.Status != SemesterStatuses.Archived
            && p.StartAt < request.EndAt
            && request.StartAt < p.EndAt);

        if (hasOverlap)
        {
            throw new ConflictException(
                $"Project Period window overlaps with an existing '{request.PeriodType}' period in this semester.");
        }

        if (request.MilestoneTemplateId.HasValue)
        {
            throw new ConflictException(
                "Milestone Template module is not available yet for this Project Period (DEFERRED).");
        }

        if (request.RubricId.HasValue && request.RubricId.Value != existing.RubricId)
        {
            var isRubricUsable = await repository.ValidateRubricUsableAsync(
                request.RubricId.Value,
                existing.AcademicSemesterId,
                cancellationToken);

            if (!isRubricUsable)
            {
                throw new ConflictException(
                    $"Rubric with ID {request.RubricId.Value} does not exist, is inactive, or does not belong to this semester.");
            }
        }

        int effMinTeam = request.MinTeamSize ?? existing.MinTeamSize ?? 3;
        int effMaxTeam = request.MaxTeamSize ?? existing.MaxTeamSize ?? 5;
        int effMinMajors = request.MinDistinctMajors ?? existing.MinDistinctMajors ?? 1;
        int effMaxProjects = request.MaxProjectsPerSupervisor ?? existing.MaxProjectsPerSupervisor ?? 5;

        if (effMinTeam < 1 || effMaxTeam < effMinTeam)
        {
            throw new ConflictException(
                $"Invalid team size configuration: minTeamSize ({effMinTeam}) must be >= 1 and <= maxTeamSize ({effMaxTeam}).");
        }

        if (effMinMajors < 1 || effMinMajors > effMaxTeam)
        {
            throw new ConflictException(
                $"Invalid distinct majors configuration: minDistinctMajors ({effMinMajors}) must be >= 1 and <= maxTeamSize ({effMaxTeam}).");
        }

        if (effMaxProjects < 1)
        {
            throw new ConflictException(
                $"Invalid supervisor quota configuration: maxProjectsPerSupervisor ({effMaxProjects}) must be >= 1.");
        }

        // Check for unsafe retroactive updates when active projects exist in the semester
        bool hasActiveProjects = await repository.HasActiveProjectsAsync(
            existing.AcademicSemesterId,
            cancellationToken);

        if (hasActiveProjects)
        {
            if (request.PeriodType != existing.PeriodType)
            {
                throw new ConflictException(
                    "Cannot modify period type of a project period when active projects exist in the semester.");
            }

            if (request.MaxTeamSize.HasValue && request.MaxTeamSize.Value < existing.MaxTeamSize)
            {
                throw new ConflictException(
                    "Cannot decrease max team size when active projects exist in the semester.");
            }

            if (request.MinTeamSize.HasValue && request.MinTeamSize.Value > existing.MinTeamSize)
            {
                throw new ConflictException(
                    "Cannot increase min team size when active projects exist in the semester.");
            }

            if (request.MinDistinctMajors.HasValue && request.MinDistinctMajors.Value > existing.MinDistinctMajors)
            {
                throw new ConflictException(
                    "Cannot increase min distinct majors when active projects exist in the semester.");
            }

            if (request.EndAt < timeProvider.GetUtcNow().UtcDateTime)
            {
                throw new ConflictException(
                    "Cannot set period end date to the past when active projects exist in the semester.");
            }
        }

        return await repository.ExecuteInTransactionAsync(async () =>
        {
            var period = await repository.UpdateProjectPeriodAsync(
                existing.Id,
                code,
                name,
                request.PeriodType,
                request.StartAt,
                request.EndAt,
                request.MinTeamSize ?? existing.MinTeamSize,
                request.MaxTeamSize ?? existing.MaxTeamSize,
                request.MinDistinctMajors ?? existing.MinDistinctMajors,
                request.MaxProjectsPerSupervisor ?? existing.MaxProjectsPerSupervisor,
                request.MilestoneTemplateId ?? existing.MilestoneTemplateId,
                request.RubricId ?? existing.RubricId,
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
                        ["periodType"] = period.PeriodType,
                        ["before"] = new Dictionary<string, object?>
                        {
                            ["code"] = existing.Code,
                            ["name"] = existing.Name,
                            ["periodType"] = existing.PeriodType,
                            ["startAt"] = existing.StartAt,
                            ["endAt"] = existing.EndAt,
                            ["minTeamSize"] = existing.MinTeamSize,
                            ["maxTeamSize"] = existing.MaxTeamSize,
                            ["minDistinctMajors"] = existing.MinDistinctMajors,
                            ["maxProjectsPerSupervisor"] = existing.MaxProjectsPerSupervisor,
                            ["milestoneTemplateId"] = existing.MilestoneTemplateId,
                            ["rubricId"] = existing.RubricId
                        },
                        ["after"] = new Dictionary<string, object?>
                        {
                            ["code"] = period.Code,
                            ["name"] = period.Name,
                            ["periodType"] = period.PeriodType,
                            ["startAt"] = period.StartAt,
                            ["endAt"] = period.EndAt,
                            ["minTeamSize"] = period.MinTeamSize,
                            ["maxTeamSize"] = period.MaxTeamSize,
                            ["minDistinctMajors"] = period.MinDistinctMajors,
                            ["maxProjectsPerSupervisor"] = period.MaxProjectsPerSupervisor,
                            ["milestoneTemplateId"] = period.MilestoneTemplateId,
                            ["rubricId"] = period.RubricId
                        }
                    }),
                cancellationToken);

            return period.ToDto();
        }, cancellationToken);
    }
}

// ── Set ProjectPeriod Status ──────────────────────────────────────────────────

public sealed record SetProjectPeriodStatusCommand(
    long PeriodId,
    string Status,
    string? ExpectedStatus = null) : IRequest<ProjectPeriodDto>;

public sealed class SetProjectPeriodStatusCommandHandler(
    ISemesterRepository repository,
    SemesterAccessService accessService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<SetProjectPeriodStatusCommand, ProjectPeriodDto>
{
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

        var semester = await repository.GetSemesterAsync(
            existing.AcademicSemesterId,
            cancellationToken)
            ?? throw new NotFoundException("AcademicSemester", existing.AcademicSemesterId);

        if (semester.Status == SemesterStatuses.Archived)
        {
            throw new ConflictException(
                "Cannot change status of a project period in an archived semester.");
        }

        if (semester.Status == SemesterStatuses.Closed && request.Status != SemesterStatuses.Archived)
        {
            throw new ConflictException(
                "Cannot change status of a project period in a closed semester unless archiving it.");
        }

        // When activating (transitioning to ACTIVE), verify no overlap with another ACTIVE period of same type
        if (request.Status == SemesterStatuses.Active)
        {
            var existingPeriods = await repository.GetProjectPeriodsAsync(
                existing.AcademicSemesterId,
                null,
                SemesterStatuses.Active,
                existing.PeriodType,
                1,
                1000,
                cancellationToken);

            bool hasActiveOverlap = existingPeriods.Items.Any(p =>
                p.Id != existing.Id
                && p.StartAt < existing.EndAt
                && existing.StartAt < p.EndAt);

            if (hasActiveOverlap)
            {
                throw new ConflictException(
                    $"Cannot activate project period: it overlaps with another active '{existing.PeriodType}' period in this semester.");
            }
        }

        return await repository.ExecuteInTransactionAsync(async () =>
        {
            var period = await repository.SetProjectPeriodStatusAsync(
                existing.Id,
                request.Status,
                request.ExpectedStatus ?? existing.Status,
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
        }, cancellationToken);
    }
}
