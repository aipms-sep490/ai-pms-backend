using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AcademicTracking;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class AcademicTrackingService
{
    private static CommitteeDto CommitteeDto(DefenseCommittee row) => new(row.Id, row.ProjectId, row.Name,
        row.Members.OrderBy(m => m.MajorId).ThenBy(m => m.UserId).Select(m => new CommitteeMemberInput(m.UserId, m.MajorId, m.Role)).ToArray(),
        ConcurrencyToken: row.ConcurrencyToken.ToString("N"));
    private DefenseSessionDto DefenseDto(DefenseSession row, Access? access) => new(row.Id, row.ProjectId, row.CommitteeId,
        Utc(row.StartAt), Utc(row.EndAt), row.Room, row.Status, row.ConcurrencyToken.ToString("N"), row.CancellationReason,
        access is { Lead: true, Writable: true } && row.Status == "SCHEDULED" && row.StartAt > Now);
    private async Task Qualified(Access access, IReadOnlyList<CommitteeMemberInput> members, CancellationToken ct)
    {
        var majors = access.Registration.Scope.Requirements.Select(r => r.MajorId).ToArray();
        if (members.Count is < 1 or > 20 || members.Any(m => m is null || m.UserId <= 0 || !majors.Contains(m.MajorId))
            || members.Select(m => (m.UserId, m.MajorId)).Distinct().Count() != members.Count
            || members.Any(m => m.Role is not ("MEMBER" or "CHAIR" or "SECRETARY"))
            || members.Count(m => m.Role == "CHAIR") > 1 || members.Count(m => m.Role == "SECRETARY") > 1
            || members.Any(m => m.Role == "CHAIR" && members.Any(s => s.Role == "SECRETARY" && s.UserId == m.UserId)))
            throw Invalid("Committee must contain 1..20 unique scoped members and at most one chair and secretary.");
        if (majors.Except(members.Select(m => m.MajorId)).Any()) throw new ConflictException("DEFENSE_MAJOR_COVERAGE_REQUIRED", "DEFENSE_MAJOR_COVERAGE_REQUIRED");
        var users = members.Select(m => m.UserId).Distinct().ToArray();
        if (await db.SupervisorAssignments.AsNoTracking().AnyAsync(a => a.ProjectId == access.Project.Id
            && a.EndedAt == null && users.Contains(a.SupervisorProfile.UserId), ct))
            throw new ConflictException("An active supervisor or mentor cannot evaluate their own project defense.",
                "DEFENSE_SUPERVISOR_CONFLICT");
        var profiles = await db.SupervisorProfiles.AsNoTracking().Where(p => users.Contains(p.UserId) && p.IsAvailable && p.User.Status == "ACTIVE"
            && p.User.Department != null && p.User.Department.IsActive && p.User.Department.Organization.IsActive
            && p.User.UserRoleUsers.Any(r => r.Role.Code == "LECTURER"))
            .Select(p => new { p.UserId, p.User.DepartmentId, Expertise = p.SupervisorExpertises.Select(e => e.ExpertiseName).ToArray() }).ToArrayAsync(ct);
        var definitions = await db.Majors.Where(m => majors.Contains(m.Id) && m.IsActive).Select(m => new { m.Id, m.Code, m.Name }).ToArrayAsync(ct);
        foreach (var member in members)
        {
            var profile = profiles.SingleOrDefault(p => p.UserId == member.UserId);
            var major = definitions.SingleOrDefault(m => m.Id == member.MajorId);
            if (profile is null || major is null || profile.DepartmentId != access.Registration.MajorDepartmentIds![member.MajorId]
                || !profile.Expertise.Any(e => string.Equals(e.Trim(), major.Code, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(e.Trim(), major.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ConflictException("A current lecturer with exact major expertise and the frozen responsible department is required.", "DEFENSE_MEMBER_NOT_QUALIFIED");
        }
    }
    private async Task ScheduleLock(CancellationToken ct)
    {
        // A transaction-owned mutex closes cross-project room/lecturer booking races.
        var result = await db.Database.SqlQueryRaw<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'AI_PMS_DEFENSE_SCHEDULE', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; SELECT @r AS Value;").ToListAsync(ct);
        if (result.Single() < 0) throw new ConflictException("Defense schedule is busy; reload and retry.", "DEFENSE_SCHEDULE_BUSY");
    }
    public async Task<PagedResult<DefenseCommitteeCandidateDto>> CommitteeCandidates(long projectId, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize);
        var access = await Scope(projectId, ct);
        if (!access.Lead) throw new ForbiddenException("Only the frozen lead department can arrange the defense committee.");
        var departments = access.Registration.MajorDepartmentIds!;
        var majorIds = access.Registration.Scope.Requirements.Select(r => r.MajorId).Distinct().ToArray();
        var majors = await db.Majors.AsNoTracking().Where(m => majorIds.Contains(m.Id) && m.IsActive)
            .Select(m => new { m.Id, m.Code, m.Name }).ToArrayAsync(ct);
        var departmentIds = departments.Values.Distinct().ToArray();
        var profiles = await db.SupervisorProfiles.AsNoTracking()
            .Where(p => p.User.DepartmentId.HasValue && departmentIds.Contains(p.User.DepartmentId.Value)
                && p.User.Status == "ACTIVE" && p.User.Department != null && p.User.Department.IsActive
                && p.User.Department.Organization.IsActive
                && p.User.UserRoleUsers.Any(r => r.Role.Code == "LECTURER"))
            .Select(p => new { p.UserId, p.User.FullName, p.User.DepartmentId, p.IsAvailable,
                Expertise = p.SupervisorExpertises.Select(e => e.ExpertiseName).ToArray() }).ToArrayAsync(ct);
        var supervisors = (await db.SupervisorAssignments.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.EndedAt == null)
            .Select(a => a.SupervisorProfile.UserId).Distinct().ToArrayAsync(ct)).ToHashSet();
        var rows = (from major in majors
                    from profile in profiles
                    where profile.DepartmentId == departments[major.Id]
                        && profile.Expertise.Any(e => string.Equals(e.Trim(), major.Code, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(e.Trim(), major.Name, StringComparison.OrdinalIgnoreCase))
                    let reason = supervisors.Contains(profile.UserId) ? "SUPERVISOR_CONFLICT"
                        : !profile.IsAvailable ? "PROFILE_UNAVAILABLE" : null
                    select new DefenseCommitteeCandidateDto(profile.UserId, profile.FullName, major.Id, major.Name,
                        reason is null, reason))
            .OrderBy(x => x.MajorName).ThenBy(x => x.FullName).ThenBy(x => x.UserId).ToArray();
        return new PagedResult<DefenseCommitteeCandidateDto>(rows.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
            page, pageSize, rows.LongLength);
    }
    public Task<CommitteeDto> CreateCommittee(long projectId, CreateCommitteeRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct);
        if (!access.Lead) throw new ForbiddenException("The frozen lead department manages defense committees.");
        Write(access, null);
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 || input.Members is null) throw Invalid("A name and scoped committee members are required.");
        await Qualified(access, input.Members, ct);
        var row = new DefenseCommittee { ProjectId = projectId, Name = input.Name.Trim(), CreatedBy = Actor, CreatedAt = Now,
            Members = input.Members.Select(m => new DefenseCommitteeMember { UserId = m.UserId, MajorId = m.MajorId, Role = m.Role }).ToArray() };
        db.Add(row); await db.SaveChangesAsync(ct); await Audit("DEFENSE_COMMITTEE_CREATED", "DEFENSE_COMMITTEE", row.Id, new { projectId, members = input.Members }, ct);
        return CommitteeDto(row);
    }, ct);
    public Task<PagedResult<CommitteeDto>> Committees(long projectId, int page, int pageSize, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        Page(page, pageSize); var access = await Scope(projectId, ct);
        if (!access.Lead && !access.Primary) throw new ForbiddenException();
        var query = db.Set<DefenseCommittee>().AsNoTracking().Where(c => c.ProjectId == projectId);
        var count = await query.LongCountAsync(ct);
        var rows = await query.Include(c => c.Members).OrderBy(c => c.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new PagedResult<CommitteeDto>(rows.Select(CommitteeDto).ToArray(), page, pageSize, count);
    }, ct);
    public Task<CommitteeDto> AssignCommitteeRoles(long id, SaveCommitteeRolesRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        var projectId = await db.Set<DefenseCommittee>().Where(c => c.Id == id).Select(c => (long?)c.ProjectId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("DefenseCommittee", id);
        await transactions.LockProjectAsync(projectId, ct);
        var access = await Scope(projectId, ct);
        if (!access.Lead) throw new ForbiddenException();
        Write(access, null);
        var row = await db.Set<DefenseCommittee>().Include(c => c.Members).SingleAsync(c => c.Id == id, ct);
        CheckToken(row.ConcurrencyToken, input.ConcurrencyToken);
        if (input.Members is null || input.Members.Count != row.Members.Count
            || input.Members.Select(m => (m.UserId, m.MajorId)).Distinct().Count() != input.Members.Count
            || input.Members.Any(m => m.Role is not ("MEMBER" or "CHAIR" or "SECRETARY")
                || !row.Members.Any(x => x.UserId == m.UserId && x.MajorId == m.MajorId))
            || input.Members.Count(m => m.Role == "CHAIR") > 1 || input.Members.Count(m => m.Role == "SECRETARY") > 1
            || input.Members.Any(m => m.Role == "CHAIR" && input.Members.Any(s => s.Role == "SECRETARY" && s.UserId == m.UserId)))
            throw Invalid("Roles must match the existing committee with at most one chair and one secretary.");
        foreach (var member in row.Members)
            member.Role = input.Members.Single(m => m.UserId == member.UserId && m.MajorId == member.MajorId).Role;
        row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await Audit("DEFENSE_COMMITTEE_ROLES_UPDATED", "DEFENSE_COMMITTEE", id,
            new { projectId, members = input.Members }, ct);
        return CommitteeDto(row);
    }, ct);
    public Task<DefenseSessionDto> ScheduleDefense(long projectId, ScheduleDefenseRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await ScheduleLock(ct); await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct);
        if (!access.Lead) throw new ForbiddenException(); Write(access, null);
        if (input.StartAt <= clock.GetUtcNow() || input.EndAt <= input.StartAt || input.EndAt - input.StartAt > TimeSpan.FromHours(8)
            || input.StartAt.Ticks % TimeSpan.TicksPerSecond != 0 || input.EndAt.Ticks % TimeSpan.TicksPerSecond != 0
            || input.StartAt.Year > 2200 || string.IsNullOrWhiteSpace(input.Room) || input.Room.Trim().Length > 50) throw Invalid("A future 1-second to 8-hour session at whole-second precision and explicit room code are required.");
        var committee = await db.Set<DefenseCommittee>().Include(c => c.Members).SingleOrDefaultAsync(c => c.Id == input.CommitteeId && c.ProjectId == projectId, ct)
            ?? throw new NotFoundException("DefenseCommittee", input.CommitteeId);
        await OfflineGateEligibility.RequirePreviousGateAndWindow(db, projectId, 4, input.StartAt, input.EndAt, ct);
        await Qualified(access, committee.Members.Select(m => new CommitteeMemberInput(m.UserId, m.MajorId, m.Role)).ToArray(), ct);
        var users = committee.Members.Select(m => m.UserId).Distinct().ToArray();
        var room = input.Room.Trim().ToUpperInvariant(); var start = input.StartAt.UtcDateTime; var end = input.EndAt.UtcDateTime;
        var roomRecord = await db.Set<Room>().SingleOrDefaultAsync(r => r.Code == room, ct);
        if (roomRecord is null)
        {
            // The legacy API accepts room text. Create a catalog record so old clients keep working.
            roomRecord = new Room { Code = room, Building = "Chưa xác định", Floor = 0, Capacity = 40,
                CreatedAt = Now, UpdatedAt = Now };
            db.Add(roomRecord); await db.SaveChangesAsync(ct);
        }
        if (roomRecord.Status != "ACTIVE") throw new ConflictException("The room is unavailable for defense scheduling.", "DEFENSE_ROOM_UNAVAILABLE");
        if (await db.Set<DefenseSession>().AnyAsync(s => s.ProjectId == projectId && s.Status == "SCHEDULED", ct))
            throw new ConflictException("This project already has a scheduled defense; cancel it explicitly before rescheduling.", "DEFENSE_ALREADY_SCHEDULED");
        if (await db.Set<DefenseSession>().AnyAsync(s => s.Status == "SCHEDULED" && s.StartAt < end && s.EndAt > start
            && (s.ProjectId == projectId || s.RoomId == roomRecord.Id || s.Room == room || db.Set<DefenseCommitteeMember>().Any(m => m.CommitteeId == s.CommitteeId && users.Contains(m.UserId))), ct))
            throw new ConflictException("The project, room or lecturer has an overlapping defense.", "DEFENSE_SCHEDULE_CONFLICT");
        if (await db.Set<GateSessionScript>().AnyAsync(s => s.Mode == "OFFLINE" && s.RoomId == roomRecord.Id
            && s.ScheduledStartAt < input.EndAt && s.ScheduledEndAt > input.StartAt, ct))
            throw new ConflictException("The room is occupied by an offline review gate.", "DEFENSE_SCHEDULE_CONFLICT");
        if (await db.Set<GateSessionScript>().AnyAsync(s => s.Mode == "OFFLINE"
            && s.ScheduledStartAt < input.EndAt && s.ScheduledEndAt > input.StartAt
            && (db.ProjectGateInstances.Any(g => g.Id == s.ProjectGateInstanceId && g.ProjectId == projectId)
                || db.GateReviewerAssignments.Any(a => a.GateInstanceId == s.ProjectGateInstanceId
                    && a.Status != "REVOKED" && users.Contains(a.ReviewerId))), ct))
            throw new ConflictException("The group or committee member has an overlapping offline review.",
                "DEFENSE_SCHEDULE_CONFLICT");
        var row = new DefenseSession { ProjectId = projectId, CommitteeId = committee.Id, StartAt = start, EndAt = end,
            Room = room, RoomId = roomRecord.Id, CreatedBy = Actor, CreatedAt = Now };
        db.Add(row); await db.SaveChangesAsync(ct); await Audit("DEFENSE_SCHEDULED", "DEFENSE_SESSION", row.Id,
            new { projectId, committeeId = committee.Id, start, end, room }, ct);
        await QueueDefenseNotification(row, access, ct);
        return DefenseDto(row, access);
    }, ct);
    public Task<DefenseSessionDto> CancelDefense(long id, CancelDefenseRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await ScheduleLock(ct);
        var projectId = await db.Set<DefenseSession>().Where(s => s.Id == id).Select(s => (long?)s.ProjectId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("DefenseSession", id);
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct);
        if (!access.Lead) throw new ForbiddenException(); Write(access, null);
        var row = await db.Set<DefenseSession>().SingleAsync(s => s.Id == id, ct); CheckToken(row.ConcurrencyToken, input.ConcurrencyToken);
        if (row.Status != "SCHEDULED") throw new ConflictException("The session has already been cancelled.");
        if (row.StartAt <= Now) throw new ConflictException("A started defense cannot be cancelled to create another attempt.");
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000) throw Invalid("Cancellation requires a reason.");
        row.Status = "CANCELLED"; row.CancellationReason = input.Reason.Trim(); row.CancelledBy = Actor; row.CancelledAt = Now; row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await Audit("DEFENSE_CANCELLED", "DEFENSE_SESSION", id, new { projectId, row.ConcurrencyToken }, ct);
        await QueueDefenseNotification(row, access, ct);
        return DefenseDto(row, access);
    }, ct);
    public Task<PagedResult<DefenseSessionDto>> DefenseSessions(long projectId, int page, int pageSize, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        Page(page, pageSize); var access = await Scope(projectId, ct);
        var query = db.Set<DefenseSession>().AsNoTracking().Where(s => s.ProjectId == projectId);
        var count = await query.LongCountAsync(ct);
        var rows = await query.OrderBy(s => s.StartAt).ThenBy(s => s.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new PagedResult<DefenseSessionDto>(rows.Select(s => DefenseDto(s, access)).ToArray(), page, pageSize, count);
    }, ct);
    public Task<DefenseSessionDto> DefenseSession(long id, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        var row = await db.Set<DefenseSession>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("DefenseSession", id);
        var actor = await transactions.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        var project = await transactions.GetProjectAsync(row.ProjectId, ct);
        if (project is not { ActiveScope: true }) throw new ForbiddenException();
        // Committee membership permits only this schedule; it grants no project or evaluator access.
        if (actor.IsLecturer && await db.Set<DefenseCommitteeMember>().AnyAsync(m => m.CommitteeId == row.CommitteeId && m.UserId == Actor, ct))
            return DefenseDto(row, actor.IsStaff && actor.DepartmentId == project.LeadDepartmentId ? await Scope(row.ProjectId, ct) : null);
        return DefenseDto(row, await Scope(row.ProjectId, ct));
    }, ct);
    private async Task QueueDefenseNotification(DefenseSession row, Access access, CancellationToken ct)
    {
        var students = access.Registration.Members.Select(m => m.UserId).ToArray();
        var recipients = await db.Users.Where(u => u.Status == "ACTIVE" && u.Id != Actor && (
            u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER") && db.Set<DefenseCommitteeMember>().Any(m => m.CommitteeId == row.CommitteeId && m.UserId == u.Id)
            || students.Contains(u.Id) && u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT") && u.TeamMembers.Any(m => m.LeftAt == null && m.Team.Project != null && m.Team.Project.Id == row.ProjectId)))
            .Select(u => u.Id).Distinct().ToArrayAsync(ct);
        if (recipients.Length == 0) return;
        var title = row.Status == "SCHEDULED" ? "A defense session has been scheduled" : "A defense session has been cancelled";
        var notification = new Persistence.Generated.Models.Notification { CreatedBy = Actor, NotificationType = "DEFENSE_" + row.Status,
            Title = title, Content = title + ". Open your authorized schedule for details.", RelatedEntityType = "DEFENSE_SESSION", RelatedEntityId = row.Id,
            CreatedAt = Now, UpdatedAt = Now, NotificationRecipients = recipients.Select(id => new Persistence.Generated.Models.NotificationRecipient {
                UserId = id, CreatedAt = Now, UpdatedAt = Now, DeliveredAt = Now }).ToArray() };
        db.Add(notification); await db.SaveChangesAsync(ct);
        // Delivery workers see the queue only after the schedule and audit transaction commits.
        db.AddRange(notification.NotificationRecipients.Select(r => new NotificationEmailDelivery { NotificationRecipientId = r.Id, NextAttemptAt = Now }));
        await db.SaveChangesAsync(ct);
    }
}
