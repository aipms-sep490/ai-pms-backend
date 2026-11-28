using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AcademicTracking;
using AIPMS.Application.Features.AcademicTracking.Abstractions;
using AIPMS.Application.Features.Semesters;
using AIPMS.Domain.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class DefenseSchedulerV4Service(
    AipmsDbContext db,
    ICurrentUser current,
    IAuditTrail audit,
    TimeProvider clock) : IDefenseSchedulerV4Service
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private long Actor => current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task AcquireScheduleLockAsync(CancellationToken ct)
    {
        var result = await db.Database
            .SqlQueryRaw<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'AI_PMS_DEFENSE_SCHEDULE', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; SELECT @r AS Value;")
            .ToListAsync(ct);
        if (result.Single() < 0)
            throw new ConflictException("Defense schedule is busy; reload and retry.", WorkflowErrorCodes.ScheduleConflict);
    }

    private void RequireAdmin()
    {
        if (!current.IsAuthenticated || !current.UserId.HasValue) throw new UnauthorizedException();
        if (!current.Roles.Contains(AppRoles.Admin))
            throw new ForbiddenException("Only Administrators can toggle optional defense room scheduling.");
    }

    private async Task<(long ActorId, long DepartmentId)> RequireDepartmentStaffAsync(CancellationToken ct)
    {
        if (!current.IsAuthenticated || !current.UserId.HasValue) throw new UnauthorizedException();
        var staff = await db.Users.AsNoTracking()
            .Where(u => u.Id == current.UserId.Value && u.Status == "ACTIVE"
                && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.DepartmentStaff)
                && u.DepartmentId != null && u.Department != null && u.Department.IsActive)
            .Select(u => new { u.Id, DepartmentId = u.DepartmentId!.Value })
            .SingleOrDefaultAsync(ct);
        if (staff is null)
            throw new ForbiddenException("Only active Department Staff can operate defense room scheduling and standby pools.", WorkflowErrorCodes.ScopeAccessDenied);
        return (staff.Id, staff.DepartmentId);
    }

    private async Task RequireProjectInDepartmentScopeAsync(long projectId, long staffDepartmentId, CancellationToken ct)
    {
        var inScope = await db.ProjectMajors.AsNoTracking()
            .AnyAsync(pm => pm.ProjectId == projectId && pm.Major.DepartmentId == staffDepartmentId, ct);
        if (!inScope)
            throw new ForbiddenException("This project is outside your department scope.", WorkflowErrorCodes.ScopeAccessDenied);
    }

    private async Task RequireLeadDepartmentScopeAsync(long projectId, long staffDepartmentId, CancellationToken ct)
    {
        var leadDeptId = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.Id)
            .Select(s => (long?)s.LeadDepartmentId)
            .FirstOrDefaultAsync(ct);
        if (leadDeptId.HasValue)
        {
            if (leadDeptId.Value != staffDepartmentId)
                throw new ForbiddenException("Only the Lead Department staff can schedule defense rooms for this project.", WorkflowErrorCodes.ScopeAccessDenied);
            return;
        }
        await RequireProjectInDepartmentScopeAsync(projectId, staffDepartmentId, ct);
    }

    public async Task<PeriodDefenseRoomSchedulingDto> GetPeriodSchedulingConfigAsync(long periodId, CancellationToken ct)
    {
        if (!current.IsAuthenticated || !current.UserId.HasValue) throw new UnauthorizedException();
        var period = await db.ProjectPeriods.AsNoTracking()
            .Where(p => p.Id == periodId)
            .Select(p => new
            {
                p.Id,
                p.EnableDefenseRoomScheduling,
                p.ColdWindowEndAt,
                p.DefenseBufferHours,
                OrganizationId = p.AcademicSemester.OrganizationId
            })
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("ProjectPeriod", periodId);

        if (!current.Roles.Contains(AppRoles.Admin))
        {
            var isOrgStaff = await db.Users.AsNoTracking()
                .AnyAsync(u => u.Id == Actor && u.Status == "ACTIVE"
                    && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.DepartmentStaff)
                    && u.Department != null && u.Department.IsActive
                    && u.Department.OrganizationId == period.OrganizationId, ct);
            if (!isOrgStaff)
                throw new ForbiddenException("Only Administrators or active Department Staff can read defense room scheduling settings.", WorkflowErrorCodes.ScopeAccessDenied);
        }

        return new(period.Id, period.EnableDefenseRoomScheduling, period.ColdWindowEndAt, period.DefenseBufferHours);
    }

    public async Task<PeriodDefenseRoomSchedulingDto> UpdatePeriodSchedulingConfigAsync(
        long periodId, UpdatePeriodDefenseRoomSchedulingRequest input, CancellationToken ct)
    {
        RequireAdmin();
        if (input.DefenseBufferHours is < 0 or > 72)
            throw new DomainException("Defense buffer hours must be between 0 and 72.", WorkflowErrorCodes.ValidationFailed);

        var period = await db.ProjectPeriods.SingleOrDefaultAsync(p => p.Id == periodId, ct)
            ?? throw new NotFoundException("ProjectPeriod", periodId);
        period.EnableDefenseRoomScheduling = input.EnableDefenseRoomScheduling;
        period.ColdWindowEndAt = input.ColdWindowEndAt.HasValue
            ? DateTime.SpecifyKind(input.ColdWindowEndAt.Value, DateTimeKind.Utc)
            : null;
        period.DefenseBufferHours = input.DefenseBufferHours;
        period.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(Actor, "DEFENSE_ROOM_SCHEDULING_UPDATED", "PROJECT_PERIOD", periodId, new Dictionary<string, object?>
        {
            ["enableDefenseRoomScheduling"] = period.EnableDefenseRoomScheduling,
            ["coldWindowEndAt"] = period.ColdWindowEndAt,
            ["defenseBufferHours"] = period.DefenseBufferHours
        }), ct);

        return new(period.Id, period.EnableDefenseRoomScheduling, period.ColdWindowEndAt, period.DefenseBufferHours);
    }

    public async Task<DefenseTechCheckDto> RecordTechCheckAsync(RecordDefenseTechCheckRequest input, CancellationToken ct)
    {
        var (_, staffDeptId) = await RequireDepartmentStaffAsync(ct);
        var status = input.Status?.Trim().ToUpperInvariant() ?? "";
        if (status is not ("PENDING" or "PASSED" or "FAILED"))
            throw new DomainException("Tech check status must be PENDING, PASSED, or FAILED.", WorkflowErrorCodes.ValidationFailed);

        if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == input.ProjectId, ct))
            throw new NotFoundException("Project", input.ProjectId);
        await RequireProjectInDepartmentScopeAsync(input.ProjectId, staffDeptId, ct);
        if (!await db.Set<Room>().AsNoTracking().AnyAsync(r => r.Id == input.RoomId, ct))
            throw new NotFoundException("Room", input.RoomId);

        var check = new DefenseTechCheck
        {
            ProjectId = input.ProjectId,
            RoomId = input.RoomId,
            CheckedBy = Actor,
            Status = status,
            Notes = input.Notes?.Trim(),
            CheckedAt = Now
        };
        db.Set<DefenseTechCheck>().Add(check);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(Actor, "DEFENSE_TECH_CHECK_RECORDED", "DEFENSE_TECH_CHECK", check.Id, new Dictionary<string, object?>
        {
            ["projectId"] = check.ProjectId,
            ["roomId"] = check.RoomId,
            ["status"] = check.Status
        }), ct);

        return new(check.Id, check.ProjectId, check.RoomId, check.CheckedBy, check.Status, check.Notes, check.CheckedAt);
    }

    public async Task<DefenseSessionV4Dto> ScheduleSessionV4Async(ScheduleDefenseV4Request input, CancellationToken ct)
    {
        var (_, staffDeptId) = await RequireDepartmentStaffAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AcquireScheduleLockAsync(ct);

        var project = await db.Projects
            .Include(p => p.ProjectMajors).ThenInclude(pm => pm.Major)
            .Include(p => p.Team).ThenInclude(t => t.TeamMembers)
            .SingleOrDefaultAsync(p => p.Id == input.ProjectId, ct)
            ?? throw new NotFoundException("Project", input.ProjectId);
        await RequireLeadDepartmentScopeAsync(project.Id, staffDeptId, ct);

        if (project.Status != "FINAL_SUBMISSION")
            throw new ConflictException("Defense room scheduling requires a project in FINAL_SUBMISSION status.", WorkflowErrorCodes.FinalPackageRequired);

        var projectGates = await db.ProjectGateInstances.AsNoTracking()
            .Where(g => g.ProjectId == input.ProjectId)
            .Select(g => new { g.Sequence, g.BlocksDefense, g.Status })
            .ToArrayAsync(ct);
        if (projectGates.Length > 0 && projectGates.Any(g => (g.Sequence == 3 || g.BlocksDefense) && g.Status != "PASSED"))
            throw new ConflictException("Gate 3 (and all defense-blocking gates) must be PASSED before scheduling defense.", WorkflowErrorCodes.Gate3NotPassed);

        var snapshotPeriodId = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking()
            .Where(s => s.ProjectId == input.ProjectId)
            .OrderByDescending(s => s.Id)
            .Select(s => (long?)s.ProjectPeriodId)
            .FirstOrDefaultAsync(ct);

        var period = await db.ProjectPeriods.AsNoTracking()
            .Where(p => p.AcademicSemesterId == project.Team.AcademicSemesterId && p.PeriodType == "EVALUATION")
            .OrderByDescending(p => p.EnableDefenseRoomScheduling)
            .ThenByDescending(p => p.Status == "ACTIVE")
            .ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync(ct);
        if (period is null && snapshotPeriodId.HasValue)
            period = await db.ProjectPeriods.AsNoTracking().SingleOrDefaultAsync(p => p.Id == snapshotPeriodId.Value, ct);

        if (period is null || !period.EnableDefenseRoomScheduling)
            throw new ConflictException("Optional defense room scheduling is disabled for this project period.", WorkflowErrorCodes.DefenseRoomSchedulingDisabled);

        if (await db.Set<DefenseSession>().AsNoTracking().AnyAsync(s => s.ProjectId == input.ProjectId && s.Status == "SCHEDULED", ct))
            throw new ConflictException("This project already has a scheduled defense session.", WorkflowErrorCodes.DefenseAlreadyScheduled);

        if (input.Date.DayOfWeek == DayOfWeek.Sunday)
            throw new ConflictException("Defense sessions cannot be scheduled on Sunday.", WorkflowErrorCodes.DefenseNonWorkingDay);

        var calendar = await db.Set<ProjectPeriodCalendar>().AsNoTracking()
            .SingleOrDefaultAsync(c => c.ProjectPeriodId == period.Id, ct)
            ?? (snapshotPeriodId.HasValue
                ? await db.Set<ProjectPeriodCalendar>().AsNoTracking().SingleOrDefaultAsync(c => c.ProjectPeriodId == snapshotPeriodId.Value, ct)
                : null);
        if (calendar is not null && !WorkingDayMath.IsWorkingDay(input.Date, calendar.WorkingDaysOfWeek,
            JsonSerializer.Deserialize<PeriodHoliday[]>(calendar.HolidaysJson) ?? []))
            throw new ConflictException("The selected date is configured as a non-working day.", WorkflowErrorCodes.DefenseNonWorkingDay);

        var projectMajorIds = project.ProjectMajors.Select(m => m.MajorId).Distinct().ToArray();
        var isInterdisciplinary = string.Equals(project.TrackCode, "IT_MKT", StringComparison.OrdinalIgnoreCase)
            || projectMajorIds.Length > 1;

        (DateTimeOffset Start, DateTimeOffset End, int DurationMinutes) slot;
        try
        {
            slot = DefenseSchedulingRules.SlotV4(input.Date, input.SlotNumber, isInterdisciplinary);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new DomainException(ex.Message, WorkflowErrorCodes.ValidationFailed);
        }

        var startUtc = slot.Start.UtcDateTime;
        var endUtc = slot.End.UtcDateTime;

        if (!DefenseSchedulingRules.MeetsColdBuffer(slot.Start.ToUniversalTime(), period.ColdWindowEndAt, period.DefenseBufferHours))
            throw new ConflictException(
                $"Defense start must be at least {period.DefenseBufferHours} hours after the Cold evaluation window closes.",
                WorkflowErrorCodes.ColdWindowNotCompleted);

        // If active COLD assignments exist for this project, require all of them to have FINALIZED evaluations
        var coldAssignIds = await db.Set<EvaluationAssignment>().AsNoTracking()
            .Where(a => a.ProjectId == input.ProjectId && a.Stage == "COLD" && a.Status != "REVOKED")
            .Select(a => a.Id).ToArrayAsync(ct);
        if (coldAssignIds.Length > 0)
        {
            var finalizedColdCount = await (
                from ds in db.Set<EvaluationDraftState>().AsNoTracking()
                join e in db.Evaluations.AsNoTracking() on ds.EvaluationId equals e.Id
                where coldAssignIds.Contains(ds.AssignmentId) && e.Status == "FINALIZED"
                select ds.AssignmentId).Distinct().CountAsync(ct);
            if (finalizedColdCount < coldAssignIds.Length)
                throw new ConflictException("All Cold evaluation assignments must be FINALIZED before scheduling a defense room.", WorkflowErrorCodes.ColdWindowNotCompleted);
        }

        var committee = await db.Set<DefenseCommittee>()
            .Include(c => c.Members)
            .SingleOrDefaultAsync(c => c.Id == input.CommitteeId && c.ProjectId == input.ProjectId, ct)
            ?? throw new NotFoundException("DefenseCommittee", input.CommitteeId);

        var memberUserIds = committee.Members.Select(m => m.UserId).Distinct().ToArray();
        var memberMajorIds = committee.Members.Select(m => m.MajorId).Distinct().ToArray();

        // D17 / QD-06 COI check (primary/former primary supervisors and active/ended discipline mentors)
        foreach (var memberUserId in memberUserIds)
        {
            if (await AcademicAssignmentGuard.HasSupervisorOrMentorConflictAsync(db, input.ProjectId, memberUserId, ct))
            {
                throw new ConflictException("Primary/former primary supervisors and discipline mentors cannot sit on the defense committee (D17).", WorkflowErrorCodes.EvaluatorRoleConflict);
            }
        }

        // Quorum check
        var missingMajor = projectMajorIds.Except(memberMajorIds).Any();
        if (missingMajor || (isInterdisciplinary && committee.Members.Count < 4) || (!isInterdisciplinary && committee.Members.Count < 1))
            throw new DomainException(
                isInterdisciplinary
                    ? "Interdisciplinary defense committee requires at least 4 members covering every project major."
                    : "Defense committee must cover the project's major.",
                WorkflowErrorCodes.CouncilQuorumUnmet);

        if (committee.Status != "CONFIRMED")
            throw new ConflictException("The defense committee must be CONFIRMED before scheduling a defense session.", WorkflowErrorCodes.DefenseCommitteeRequired);

        var room = await db.Set<Room>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == input.RoomId && r.Status == "ACTIVE", ct)
            ?? throw new NotFoundException("Room", input.RoomId);

        if (isInterdisciplinary && !string.IsNullOrWhiteSpace(room.RoomClusterType)
            && !string.Equals(room.RoomClusterType, "INTERDISCIPLINARY", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Interdisciplinary defense requires an INTERDISCIPLINARY cluster room.", WorkflowErrorCodes.ValidationFailed);

        var activeStudentCount = project.Team.TeamMembers.Count(m => m.LeftAt == null);
        if (room.Capacity < activeStudentCount + memberUserIds.Length)
            throw new ConflictException("The selected room has insufficient capacity for the team and defense committee.", WorkflowErrorCodes.DefenseRoomCapacity);

        // 3D conflict check: Room, Evaluator, Student
        var overlapping = await db.Set<DefenseSession>().AsNoTracking()
            .Where(s => s.Status == "SCHEDULED" && s.StartAt < endUtc && s.EndAt > startUtc)
            .ToArrayAsync(ct);

        if (overlapping.Any(s => s.RoomId == room.Id || (s.RoomId == null && s.Room == room.Code)))
            throw new ConflictException("The selected room is already booked during this slot.", WorkflowErrorCodes.ScheduleConflict);

        if (overlapping.Length > 0)
        {
            var overlapCommitteeIds = overlapping.Select(s => s.CommitteeId).Distinct().ToArray();
            if (await db.Set<DefenseCommitteeMember>().AsNoTracking()
                .AnyAsync(m => overlapCommitteeIds.Contains(m.CommitteeId) && memberUserIds.Contains(m.UserId), ct))
                throw new ConflictException("One or more committee evaluators have a conflicting defense session.", WorkflowErrorCodes.ScheduleConflict);

            var studentIds = project.Team.TeamMembers.Where(m => m.LeftAt == null).Select(m => m.UserId).ToArray();
            var overlapProjectIds = overlapping.Select(s => s.ProjectId).Distinct().ToArray();
            if (studentIds.Length > 0 && await db.Projects.AsNoTracking()
                .AnyAsync(p => overlapProjectIds.Contains(p.Id) && p.Team.TeamMembers.Any(tm => tm.LeftAt == null && studentIds.Contains(tm.UserId)), ct))
                throw new ConflictException("One or more students in this project have a conflicting defense session.", WorkflowErrorCodes.ScheduleConflict);
        }

        var session = new DefenseSession
        {
            ProjectId = input.ProjectId,
            CommitteeId = committee.Id,
            StartAt = startUtc,
            EndAt = endUtc,
            RoomId = room.Id,
            Room = room.Code,
            Status = "SCHEDULED",
            SchedulePolicyVersion = DefenseSchedulingRules.V4PolicyVersion,
            SlotDurationMinutes = slot.DurationMinutes,
            CreatedBy = Actor,
            CreatedAt = Now,
            ConcurrencyToken = Guid.NewGuid()
        };
        db.Set<DefenseSession>().Add(session);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(Actor, "DEFENSE_SESSION_V4_SCHEDULED", "DEFENSE_SESSION", session.Id, new Dictionary<string, object?>
        {
            ["projectId"] = session.ProjectId,
            ["roomId"] = session.RoomId,
            ["policyVersion"] = session.SchedulePolicyVersion,
            ["durationMinutes"] = session.SlotDurationMinutes,
            ["startAt"] = session.StartAt
        }), ct);

        await tx.CommitAsync(ct);
        return new(session.Id, session.ProjectId, session.CommitteeId, session.RoomId, session.Room,
            new DateTimeOffset(DateTime.SpecifyKind(session.StartAt, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(session.EndAt, DateTimeKind.Utc)),
            session.Status, session.SchedulePolicyVersion, session.SlotDurationMinutes, session.ConcurrencyToken.ToString("N"));
    }

    public async Task<DefenseChangeProposalV4Dto> ProposeChangeV4Async(CreateDefenseChangeProposalV4Request input, CancellationToken ct)
    {
        if (!current.IsAuthenticated || !current.UserId.HasValue) throw new UnauthorizedException();
        var session = await db.Set<DefenseSession>().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == input.DefenseSessionId, ct)
            ?? throw new NotFoundException("DefenseSession", input.DefenseSessionId);

        var project = await db.Projects.AsNoTracking()
            .Include(p => p.ProjectMajors).ThenInclude(pm => pm.Major)
            .SingleAsync(p => p.Id == session.ProjectId, ct);

        var isScopedStaff = false;
        if (current.Roles.Contains(AppRoles.DepartmentStaff))
        {
            var staffDeptId = await db.Users.AsNoTracking()
                .Where(u => u.Id == Actor && u.Status == "ACTIVE")
                .Select(u => u.DepartmentId)
                .SingleOrDefaultAsync(ct);
            isScopedStaff = staffDeptId.HasValue && project.ProjectMajors.Any(pm => pm.Major.DepartmentId == staffDeptId.Value);
        }
        if (!isScopedStaff && !await db.Set<DefenseCommitteeMember>().AsNoTracking()
            .AnyAsync(m => m.CommitteeId == session.CommitteeId && m.UserId == Actor && (m.Role == "CHAIR" || m.Role == "SECRETARY"), ct))
            throw new ForbiddenException("Only scoped Department Staff, Chair, or Secretary may request a defense slot change.", WorkflowErrorCodes.ScopeAccessDenied);

        var startUtc = DateTime.SpecifyKind(session.StartAt, DateTimeKind.Utc);
        if (startUtc - Now < TimeSpan.FromHours(24))
            throw new DomainException("Defense schedule changes require at least 24 hours advance notice.", "CHANGE_REQUEST_WINDOW_EXPIRED");

        var isInterdisciplinary = string.Equals(project.TrackCode, "IT_MKT", StringComparison.OrdinalIgnoreCase)
            || project.ProjectMajors.Count > 1;
        var memberIds = await db.Set<DefenseCommitteeMember>().AsNoTracking()
            .Where(m => m.CommitteeId == session.CommitteeId).Select(m => m.UserId).ToArrayAsync(ct);
        var activeRooms = await db.Set<Room>().AsNoTracking()
            .Where(r => r.Status == "ACTIVE" && (!isInterdisciplinary || r.RoomClusterType == null || r.RoomClusterType == "INTERDISCIPLINARY"))
            .OrderBy(r => r.Id).Take(10).ToArrayAsync(ct);

        var baseDate = DateOnly.FromDateTime(new DateTimeOffset(startUtc).ToOffset(VietnamOffset).Date);
        var windowEnd = baseDate.AddDays(3).ToDateTime(TimeOnly.MaxValue);
        var scheduled = await db.Set<DefenseSession>().AsNoTracking()
            .Where(s => s.Id != session.Id && s.Status == "SCHEDULED" && s.StartAt >= Now && s.StartAt <= windowEnd)
            .ToArrayAsync(ct);
        var scheduledCommitteeIds = scheduled.Select(s => s.CommitteeId).Distinct().ToArray();
        var busyMembersByCommittee = await db.Set<DefenseCommitteeMember>().AsNoTracking()
            .Where(m => scheduledCommitteeIds.Contains(m.CommitteeId))
            .GroupBy(m => m.CommitteeId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.UserId).ToHashSet(), ct);

        var candidates = new List<DefenseSwapCandidateDto>();
        for (var d = 0; d < 3 && candidates.Count < 8; d++)
        {
            var date = baseDate.AddDays(d);
            if (date.DayOfWeek == DayOfWeek.Sunday) continue;
            foreach (var slot in DefenseSchedulingRules.AllSlotsV4(date, isInterdisciplinary))
            {
                if (candidates.Count >= 8) break;
                var slotStartUtc = slot.Start.UtcDateTime;
                var slotEndUtc = slot.End.UtcDateTime;
                if (slotStartUtc == session.StartAt && slotEndUtc == session.EndAt) continue;
                if (slotStartUtc <= Now.AddHours(24)) continue;

                var conflicts = scheduled.Where(s => s.StartAt < slotEndUtc && s.EndAt > slotStartUtc).ToArray();
                var evaluatorBusy = conflicts.Any(s => busyMembersByCommittee.TryGetValue(s.CommitteeId, out var set) && memberIds.Any(set.Contains));
                if (evaluatorBusy) continue;

                foreach (var room in activeRooms)
                {
                    if (conflicts.Any(s => s.RoomId == room.Id || (s.RoomId == null && s.Room == room.Code))) continue;
                    candidates.Add(new(room.Id, room.Code, date, slot.SlotNumber, slot.Start, slot.End, slot.DurationMinutes));
                    break;
                }
            }
        }

        return new(session.Id,
            new DateTimeOffset(startUtc),
            new DateTimeOffset(DateTime.SpecifyKind(session.EndAt, DateTimeKind.Utc)),
            candidates);
    }
}
