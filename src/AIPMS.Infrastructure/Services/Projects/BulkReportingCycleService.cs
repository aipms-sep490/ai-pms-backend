using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Application.Features.Semesters;
using AIPMS.Domain.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

public interface IBulkReportingCycleService
{
    Task<GenerateReportingCyclesResultDto> GenerateForPeriodAsync(
        long projectPeriodId,
        GenerateReportingCyclesRequest request,
        CancellationToken ct = default);
}

internal sealed class BulkReportingCycleService(
    AipmsDbContext db,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IBulkReportingCycleService
{
    public async Task<GenerateReportingCyclesResultDto> GenerateForPeriodAsync(
        long projectPeriodId,
        GenerateReportingCyclesRequest request,
        CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long actorId)
            throw new UnauthorizedException();

        var isStaff = currentUser.Roles.Contains(AppRoles.DepartmentStaff, StringComparer.Ordinal);
        var isAdmin = currentUser.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal);
        if (!isStaff)
        {
            throw new ForbiddenException(
                isAdmin
                    ? "Platform administrators without DEPARTMENT_STAFF role cannot generate department reporting cycles."
                    : "Only Department Staff can bulk-generate reporting cycles.",
                isAdmin ? WorkflowErrorCodes.AdminNotAcademicAuthority : WorkflowErrorCodes.Forbidden);
        }

        var cadence = (request.Cadence ?? "WEEKLY").Trim().ToUpperInvariant();
        if (cadence is not ("WEEKLY" or "BIWEEKLY"))
            throw new DomainException("Cadence must be WEEKLY or BIWEEKLY.", WorkflowErrorCodes.ValidationFailed);

        var latePolicy = (request.LatePolicy ?? "FLAG").Trim().ToUpperInvariant();
        if (latePolicy is not ("FLAG" or "BLOCK"))
            throw new DomainException("LatePolicy must be FLAG or BLOCK.", WorkflowErrorCodes.ValidationFailed);

        if (request.DeadlineOffsetDays is < 0 or > 14)
            throw new DomainException("DeadlineOffsetDays must be between 0 and 14.", WorkflowErrorCodes.ValidationFailed);

        var actor = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId && u.Status == "ACTIVE" && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive)
            .Select(u => new { DepartmentId = u.DepartmentId!.Value, OrganizationId = u.Department!.OrganizationId })
            .SingleOrDefaultAsync(ct)
            ?? throw new ForbiddenException("An active department staff scope is required.");

        var period = await db.ProjectPeriods.AsNoTracking()
            .Include(p => p.AcademicSemester)
            .SingleOrDefaultAsync(p => p.Id == projectPeriodId, ct)
            ?? throw new NotFoundException("ProjectPeriod", projectPeriodId);

        if (period.AcademicSemester.OrganizationId != actor.OrganizationId)
            throw new ForbiddenException("Project period is outside your organization scope.", WorkflowErrorCodes.ScopeAccessDenied);

        if (!string.Equals(period.PeriodType, "EXECUTION", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Bulk reporting cycle generation is only supported for EXECUTION periods.", WorkflowErrorCodes.ValidationFailed);

        if (period.EndAt <= period.StartAt)
            throw new DomainException("Execution period must have EndAt > StartAt.", WorkflowErrorCodes.ValidationFailed);

        var calendar = await db.Set<ProjectPeriodCalendar>().AsNoTracking()
            .SingleOrDefaultAsync(c => c.ProjectPeriodId == projectPeriodId, ct);
        var mask = calendar?.WorkingDaysOfWeek ?? 62;
        var holidays = calendar is null
            ? (IReadOnlyList<PeriodHoliday>)Array.Empty<PeriodHoliday>()
            : (JsonSerializer.Deserialize<PeriodHoliday[]>(calendar.HolidaysJson) ?? []);

        var stepDays = cadence == "BIWEEKLY" ? 14 : 7;
        var reportType = "WEEKLY";
        var slots = new List<GeneratedReportingCycleSlotDto>();
        var cursor = DateTime.SpecifyKind(period.StartAt, DateTimeKind.Utc);
        var periodEndUtc = DateTime.SpecifyKind(period.EndAt, DateTimeKind.Utc);

        while (cursor < periodEndUtc && slots.Count < 100)
        {
            var slotEnd = cursor.AddDays(stepDays);
            if (slotEnd > periodEndUtc) slotEnd = periodEndUtc;
            var rawDeadline = slotEnd.AddDays(request.DeadlineOffsetDays);
            // Adjust deadline forward to the next working day (GMT+7) if it falls on weekend/holiday
            var vnDeadline = new DateTimeOffset(rawDeadline, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(7));
            var guard = 0;
            while (!WorkingDayMath.IsWorkingDay(DateOnly.FromDateTime(vnDeadline.Date), mask, holidays) && guard++ < 30)
                vnDeadline = vnDeadline.AddDays(1);
            var deadlineUtc = vnDeadline.UtcDateTime;
            slots.Add(new GeneratedReportingCycleSlotDto(cursor, slotEnd, deadlineUtc));
            cursor = slotEnd;
        }

        // Find all ACTIVE projects in this semester whose LeadDepartmentId matches actor's department
        var activeProjects = await db.Projects.AsNoTracking()
            .Where(p => p.Status == "ACTIVE" && p.Team.AcademicSemesterId == period.AcademicSemesterId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var scopedProjectIds = new List<long>();
        foreach (var pid in activeProjects)
        {
            var scope = await ProjectAcademicScopeReader.ReadAsync(db, pid, ct, requireFrozenScope: false);
            if (scope.LeadDepartmentId == actor.DepartmentId)
                scopedProjectIds.Add(pid);
        }

        var created = 0;
        var skipped = 0;
        var now = clock.GetUtcNow().UtcDateTime;

        if (request.DryRun)
        {
            var existingByProject = scopedProjectIds.Count == 0
                ? new HashSet<(long, DateTime, DateTime)>()
                : (await db.ProgressReportPeriods.AsNoTracking()
                    .Where(c => scopedProjectIds.Contains(c.ProjectId))
                    .Select(c => new { c.ProjectId, c.PeriodStart, c.PeriodEnd })
                    .ToListAsync(ct))
                    .Select(c => (c.ProjectId, c.PeriodStart.Date, c.PeriodEnd.Date))
                    .ToHashSet();

            foreach (var pid in scopedProjectIds)
            {
                foreach (var slot in slots)
                {
                    if (existingByProject.Contains((pid, slot.PeriodStart.Date, slot.PeriodEnd.Date))) skipped++;
                    else created++;
                }
            }

            return new GenerateReportingCyclesResultDto(
                projectPeriodId, cadence, latePolicy, true,
                scopedProjectIds.Count, created, skipped, slots);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var pid in scopedProjectIds)
        {
            var existing = await db.ProgressReportPeriods
                .Where(c => c.ProjectId == pid)
                .Select(c => new { c.PeriodStart, c.PeriodEnd })
                .ToListAsync(ct);

            foreach (var slot in slots)
            {
                var overlaps = existing.Any(e => e.PeriodStart < slot.PeriodEnd && e.PeriodEnd > slot.PeriodStart);
                if (overlaps)
                {
                    skipped++;
                    continue;
                }

                db.ProgressReportPeriods.Add(new AIPMS.Infrastructure.Persistence.Models.ProgressReportPeriod
                {
                    ProjectId = pid,
                    ProjectPeriodId = projectPeriodId,
                    ReportType = reportType,
                    PeriodStart = slot.PeriodStart,
                    PeriodEnd = slot.PeriodEnd,
                    Deadline = slot.Deadline,
                    LatePolicy = latePolicy,
                    CreatedBy = actorId,
                    CreatedAt = now,
                    UpdatedAt = now,
                    ConcurrencyToken = Guid.NewGuid()
                });
                existing.Add(new { slot.PeriodStart, slot.PeriodEnd });
                created++;
            }
        }

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(
            actorId,
            "REPORTING_CYCLES_BULK_GENERATED",
            "PROJECT_PERIOD",
            projectPeriodId,
            new Dictionary<string, object?>
            {
                ["cadence"] = cadence,
                ["latePolicy"] = latePolicy,
                ["projectsProcessed"] = scopedProjectIds.Count,
                ["cyclesCreated"] = created,
                ["cyclesSkipped"] = skipped
            }), ct);
        await tx.CommitAsync(ct);

        return new GenerateReportingCyclesResultDto(
            projectPeriodId, cadence, latePolicy, false,
            scopedProjectIds.Count, created, skipped, slots);
    }
}
