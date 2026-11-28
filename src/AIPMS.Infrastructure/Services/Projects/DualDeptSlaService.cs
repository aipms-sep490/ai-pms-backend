using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Application.Features.ReviewGates.Abstractions;
using AIPMS.Application.Features.Semesters;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

/// <summary>
/// POL-02 / QD-02 Dual-Department SLA sweep service shared by DualDeptSlaWorker (background)
/// and the manual staff sweep endpoint. Evaluates strictly elapsed working days in GMT+7 using
/// each period's PeriodCalendar (default Mon–Fri bitmask 62) on the latest registration snapshot
/// and latest gate package round only.
/// </summary>
internal sealed class DualDeptSlaService(AipmsDbContext db, TimeProvider clock) : IDualDeptSlaService
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private const int DefaultWorkingDaysMask = 62; // Mon..Fri

    public async Task<SlaSweepResultDto> SweepAsync(long? actorUserId, CancellationToken ct)
    {
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var nowVn = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)).ToOffset(VietnamOffset);
        var events = new List<SlaEventSummaryDto>();
        var calendarCache = new Dictionary<long, (int Mask, PeriodHoliday[] Holidays)>();

        async Task<(int Mask, PeriodHoliday[] Holidays)> LoadCalendarAsync(long? periodId)
        {
            if (!periodId.HasValue || periodId.Value <= 0)
                return (DefaultWorkingDaysMask, []);
            if (calendarCache.TryGetValue(periodId.Value, out var cached))
                return cached;

            var cal = await db.Set<ProjectPeriodCalendar>().AsNoTracking()
                .SingleOrDefaultAsync(c => c.ProjectPeriodId == periodId.Value, ct);
            var resolved = cal is null
                ? (DefaultWorkingDaysMask, Array.Empty<PeriodHoliday>())
                : (cal.WorkingDaysOfWeek, JsonSerializer.Deserialize<PeriodHoliday[]>(cal.HolidaysJson) ?? []);
            calendarCache[periodId.Value] = resolved;
            return resolved;
        }

        // 1. Project dual-department reviews (latest snapshot per project only) in SUBMITTED / UNDER_REVIEW
        var allSnapshotsWithPending = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking()
            .Include(s => s.Decisions)
            .Where(s => s.Decisions.Any(d => d.Decision == "PENDING"))
            .ToListAsync(ct);

        if (allSnapshotsWithPending.Count > 0)
        {
            var snapProjectIds = allSnapshotsWithPending.Select(x => x.ProjectId).Distinct().ToArray();
            var latestSnapshotIds = (await db.Set<ProjectRegistrationSnapshot>().AsNoTracking()
                .Where(s => snapProjectIds.Contains(s.ProjectId))
                .GroupBy(s => s.ProjectId)
                .Select(g => g.Max(x => x.Id))
                .ToListAsync(ct)).ToHashSet();

            var latestPendingSnapshots = allSnapshotsWithPending
                .Where(s => latestSnapshotIds.Contains(s.Id))
                .ToList();

            var projectStatuses = await db.Projects.AsNoTracking()
                .Where(p => snapProjectIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Status, ct);

            foreach (var snap in latestPendingSnapshots)
            {
                if (!projectStatuses.TryGetValue(snap.ProjectId, out var status)
                    || status is not ("SUBMITTED" or "UNDER_REVIEW"))
                    continue;

                var (mask, holidays) = await LoadCalendarAsync(snap.ProjectPeriodId);
                var submittedVn = new DateTimeOffset(DateTime.SpecifyKind(snap.SubmittedAt, DateTimeKind.Utc))
                    .ToOffset(VietnamOffset);
                var reminderDue = WorkingDayMath.AddElapsedWorkingDays(submittedVn, 2, mask, holidays);
                var escalationDue = WorkingDayMath.AddElapsedWorkingDays(submittedVn, 3, mask, holidays);
                if (nowVn < reminderDue) continue;

                foreach (var dec in snap.Decisions.Where(d => d.Decision == "PENDING"))
                {
                    await TryEmitSlaAsync(
                        "PROJECT_REVIEW", snap.Id, 1, dec.DepartmentId, "REMINDER_48H",
                        $"Nhắc SLA 2 ngày làm việc: Hồ sơ dự án #{snap.ProjectId} chờ thẩm định",
                        $"Khoa của bạn có hồ sơ dự án #{snap.ProjectId} chờ duyệt quá 2 ngày làm việc.",
                        actorUserId, nowUtc, events, ct);

                    if (nowVn >= escalationDue)
                    {
                        await TryEmitSlaAsync(
                            "PROJECT_REVIEW", snap.Id, 1, snap.LeadDepartmentId, "ESCALATION_72H",
                            $"Leo thang SLA 3 ngày làm việc: Hồ sơ dự án #{snap.ProjectId} quá hạn đồng duyệt",
                            $"Đơn vị phối hợp (#{dec.DepartmentId}) chưa phản hồi dự án #{snap.ProjectId} quá 3 ngày làm việc; Khoa chủ trì có thể kích hoạt Lead Override.",
                            actorUserId, nowUtc, events, ct);
                    }
                }
            }
        }

        // 2. Open Review Gate packages — strictly MAX(RoundNumber) per GateInstanceId in SUBMITTED / IN_REVIEW
        var openGatePackages = await db.Set<ReviewGatePackage>().AsNoTracking()
            .Join(db.ProjectGateInstances.AsNoTracking().Where(g => g.Status == "SUBMITTED" || g.Status == "IN_REVIEW"),
                p => p.GateInstanceId, g => g.Id,
                (p, g) => new { Package = p, Gate = g })
            .ToListAsync(ct);

        var latestPackages = openGatePackages
            .GroupBy(x => x.Gate.Id)
            .Select(g => g.OrderByDescending(x => x.Package.RoundNumber).ThenByDescending(x => x.Package.Id).First())
            .ToList();

        if (latestPackages.Count > 0)
        {
            var gateProjectIds = latestPackages.Select(x => x.Gate.ProjectId).Distinct().ToArray();
            var latestProjectPeriods = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking()
                .Where(s => gateProjectIds.Contains(s.ProjectId))
                .GroupBy(s => s.ProjectId)
                .Select(g => new
                {
                    ProjectId = g.Key,
                    PeriodId = g.OrderByDescending(x => x.Id).Select(x => (long?)x.ProjectPeriodId).FirstOrDefault()
                })
                .ToDictionaryAsync(x => x.ProjectId, x => x.PeriodId, ct);

            foreach (var entry in latestPackages)
            {
                var periodId = latestProjectPeriods.GetValueOrDefault(entry.Gate.ProjectId);
                var (mask, holidays) = await LoadCalendarAsync(periodId);
                var submittedVn = new DateTimeOffset(DateTime.SpecifyKind(entry.Package.SubmittedAt, DateTimeKind.Utc))
                    .ToOffset(VietnamOffset);
                var reminderDue = WorkingDayMath.AddElapsedWorkingDays(submittedVn, 2, mask, holidays);
                var escalationDue = WorkingDayMath.AddElapsedWorkingDays(submittedVn, 3, mask, holidays);
                if (nowVn < reminderDue) continue;

                var projScope = await ProjectAcademicScopeReader.ReadAsync(db, entry.Gate.ProjectId, ct, requireFrozenScope: false);
                if (!projScope.LeadDepartmentId.HasValue) continue;

                var decidedScopes = await db.Set<ProjectGateRoundScopeDecision>().AsNoTracking()
                    .Where(d => d.GateInstanceId == entry.Gate.Id
                        && d.RoundNumber == entry.Package.RoundNumber
                        && d.Decision != "PENDING")
                    .Select(d => d.Scope)
                    .ToListAsync(ct);

                var targetDepts = new HashSet<long>();
                if (projScope.MajorDepartmentIds is { Count: > 0 })
                {
                    foreach (var kv in projScope.MajorDepartmentIds)
                        if (!decidedScopes.Contains(kv.Key.ToString()))
                            targetDepts.Add(kv.Value);
                }
                if (targetDepts.Count == 0) targetDepts.Add(projScope.LeadDepartmentId.Value);

                foreach (var deptId in targetDepts)
                {
                    await TryEmitSlaAsync(
                        "REVIEW_GATE", entry.Gate.Id, entry.Package.RoundNumber, deptId, "REMINDER_48H",
                        $"Nhắc SLA 2 ngày làm việc: Cổng {entry.Gate.Code} dự án #{entry.Gate.ProjectId}",
                        $"Gói nộp vòng {entry.Package.RoundNumber} của cổng {entry.Gate.Code} đang chờ phản hồi quá 2 ngày làm việc.",
                        actorUserId, nowUtc, events, ct);

                    if (nowVn >= escalationDue)
                    {
                        await TryEmitSlaAsync(
                            "REVIEW_GATE", entry.Gate.Id, entry.Package.RoundNumber, projScope.LeadDepartmentId.Value, "ESCALATION_72H",
                            $"Leo thang SLA 3 ngày làm việc: Cổng {entry.Gate.Code} dự án #{entry.Gate.ProjectId}",
                            $"Cổng {entry.Gate.Code} vòng {entry.Package.RoundNumber} quá 3 ngày làm việc chưa đủ đồng thuận 2 Khoa; đề nghị LeadScope xử lý.",
                            actorUserId, nowUtc, events, ct);
                    }
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return new SlaSweepResultDto(
            events.Count(e => e.SlaTier == "REMINDER_48H"),
            events.Count(e => e.SlaTier == "ESCALATION_72H"),
            events);
    }

    private async Task TryEmitSlaAsync(
        string entityType,
        long entityId,
        int roundNumber,
        long departmentId,
        string slaTier,
        string title,
        string content,
        long? actorUserId,
        DateTime nowUtc,
        List<SlaEventSummaryDto> collector,
        CancellationToken ct)
    {
        var exists = collector.Any(e => e.EntityType == entityType && e.EntityId == entityId
                && e.RoundNumber == roundNumber && e.DepartmentId == departmentId && e.SlaTier == slaTier)
            || await db.Set<WorkflowSlaNotification>().AsNoTracking()
                .AnyAsync(x => x.EntityType == entityType && x.EntityId == entityId
                    && x.RoundNumber == roundNumber && x.DepartmentId == departmentId && x.SlaTier == slaTier, ct);
        if (exists) return;

        db.Set<WorkflowSlaNotification>().Add(new WorkflowSlaNotification
        {
            EntityType = entityType,
            EntityId = entityId,
            RoundNumber = roundNumber,
            DepartmentId = departmentId,
            SlaTier = slaTier,
            SentAt = nowUtc
        });

        var staffIds = await db.Users.AsNoTracking()
            .Where(u => u.Status == "ACTIVE" && u.DepartmentId == departmentId
                && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"))
            .Select(u => u.Id)
            .ToArrayAsync(ct);

        if (staffIds.Length > 0)
        {
            await V4WorkflowNotificationHelper.EmitAsync(
                db, actorUserId, $"SLA_{slaTier}", title, content, entityType, entityId,
                staffIds, nowUtc, enqueueEmail: true, ct, excludeActor: false);
        }

        collector.Add(new SlaEventSummaryDto(
            entityType, entityId, roundNumber, departmentId, slaTier,
            DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)));
    }
}
