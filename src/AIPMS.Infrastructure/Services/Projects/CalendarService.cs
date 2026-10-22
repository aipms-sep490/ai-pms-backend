using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Calendar.Abstractions;
using AIPMS.Application.Features.Calendar.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class CalendarService(AipmsDbContext db, ICurrentUser currentUser) : ICalendarService
{
    private sealed record Cursor(int Version, DateTime DueAt, long Id, int SourceOrder, string Query);
    private sealed class CalendarRow
    {
        public int SourceOrder { get; init; }
        public string SourceType { get; init; } = "";
        public long SourceId { get; init; }
        public long ProjectId { get; init; }
        public string ProjectCode { get; init; } = "";
        public string ProjectTitle { get; init; } = "";
        public string Title { get; init; } = "";
        public DateTime? StartAt { get; init; }
        public DateTime? EndAt { get; init; }
        public DateTime DueAt { get; init; }
        public string Status { get; init; } = "";
        public string? DeepLink { get; init; }
    }
    private sealed record AccessScope(long Actor, long? DepartmentId, bool Lecturer, long[] Projects, long[] WholeProjects, string[] MajorKeys,
        long[] CheckpointProjects, long[] StudentProjects, string[] StudentMajorKeys);
    private sealed class ScopeRow
    {
        public long ProjectId { get; set; }
        public long OrganizationId { get; set; }
        public string SnapshotJson { get; set; } = "";
        public bool Student { get; set; }
        public bool Primary { get; set; }
    }
    private static ValidationException Invalid(string text) => new(new Dictionary<string, string[]> { ["calendar"] = [text] });
    public async Task<CalendarResponseDto> GetAsync(DateTimeOffset from, DateTimeOffset to, string? cursor, int pageSize, string? sourceType, CancellationToken ct = default)
    {
        if (from >= to || to - from > TimeSpan.FromDays(366)) throw Invalid("from/to must be a bounded range of at most 366 days.");
        if (pageSize is < 1 or > 100) throw Invalid("pageSize must be 1..100.");
        sourceType = sourceType?.Trim().ToUpperInvariant();
        var sources = new[] { "TASK", "MILESTONE", "MEETING", "DELIVERABLE", "FINAL_SUBMISSION", "EVALUATION_ASSIGNMENT", "CHECKPOINT", "DEFENSE_SESSION", "PROGRESS_REPORT" };
        if (sourceType is not null && !sources.Contains(sourceType)) throw Invalid("Unknown sourceType.");
        var actor = currentUser.IsAuthenticated ? currentUser.UserId ?? throw new UnauthorizedException() : throw new UnauthorizedException();
        var queryKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{actor}|{from.UtcTicks}|{to.UtcTicks}|{sourceType}")));
        var after = Decode(cursor, queryKey, from.UtcDateTime, to.UtcDateTime);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var scope = await Scope(actor, ct); var rows = new List<CalendarRow>();
        for (var index = 0; index < sources.Length; index++)
        {
            if (sourceType is not null && sourceType != sources[index]) continue;
            var query = Source(index, scope).Where(x => x.DueAt >= from.UtcDateTime && x.DueAt < to.UtcDateTime);
            if (after is not null) query = query.Where(x => x.DueAt > after.DueAt || x.DueAt == after.DueAt && (x.SourceId > after.Id
                || x.SourceId == after.Id && x.SourceOrder > after.SourceOrder));
            rows.AddRange(await query.OrderBy(x => x.DueAt).ThenBy(x => x.SourceId).Take(pageSize + 1).ToArrayAsync(ct));
        }
        var sorted = rows.OrderBy(x => x.DueAt).ThenBy(x => x.SourceId).ThenBy(x => x.SourceOrder).ToArray();
        var page = sorted.Take(pageSize).ToArray(); var more = sorted.Length > pageSize;
        var next = more ? Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Cursor(2,
            page[^1].DueAt, page[^1].SourceId, page[^1].SourceOrder, queryKey)))) : null;
        await tx.CommitAsync(ct);
        return new(page.Select(x => new CalendarItemDto(x.SourceType, x.SourceId, x.ProjectId, x.ProjectCode, x.ProjectTitle, x.Title,
            Utc(x.StartAt), Utc(x.EndAt), DateTime.SpecifyKind(x.DueAt, DateTimeKind.Utc), x.Status, x.DeepLink)).ToArray(), next, more, true);
    }
    private static DateTime? Utc(DateTime? date) => date.HasValue ? DateTime.SpecifyKind(date.Value, DateTimeKind.Utc) : null;
    private static Cursor? Decode(string? text, string query, DateTime from, DateTime to)
    {
        if (text is null) return null;
        try
        {
            if (text.Length > 1024) throw Invalid("Invalid cursor.");
            var value = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(text));
            if (value is null || value.Version != 2 || value.Query != query || value.Id <= 0 || value.SourceOrder is < 0 or > 8
                || value.DueAt < from || value.DueAt >= to) throw Invalid("The cursor does not match this actor/range/source; restart paging.");
            return value;
        }
        catch (Exception e) when (e is FormatException or JsonException) { throw Invalid("Invalid cursor."); }
    }
    private async Task<AccessScope> Scope(long actor, CancellationToken ct)
    {
        var account = await db.Users.AsNoTracking().Where(u => u.Id == actor && u.Status == "ACTIVE")
            .Select(u => new { u.DepartmentId, Active = u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive,
                Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"), Student = u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"),
                Lecturer = u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER") }).SingleOrDefaultAsync(ct) ?? throw new ForbiddenException();
        if (!account.Active) return new(actor, account.DepartmentId, false, [], [], [], [], [], []);
        // Bound discovery and batch frozen authority; avoid one query per project or calendar item.
        var candidates = await db.Database.SqlQuery<ScopeRow>($"""
            SELECT TOP (1001) p.id ProjectId, sem.organization_id OrganizationId, snap.snapshot_json SnapshotJson,
              CAST(CASE WHEN {account.Student}=1 AND EXISTS(SELECT 1 FROM dbo.team_members m WHERE m.team_id=p.team_id AND m.user_id={actor} AND m.left_at IS NULL) THEN 1 ELSE 0 END AS bit) Student,
              CAST(CASE WHEN {account.Lecturer}=1 AND EXISTS(SELECT 1 FROM dbo.supervisor_assignments a JOIN dbo.supervisor_profiles sp ON sp.id=a.supervisor_profile_id WHERE a.project_id=p.id AND a.ended_at IS NULL AND a.assignment_type='PRIMARY' AND a.is_primary=1 AND sp.user_id={actor}) THEN 1 ELSE 0 END AS bit) [Primary]
            FROM dbo.projects p JOIN dbo.teams t ON t.id=p.team_id JOIN dbo.academic_semesters sem ON sem.id=t.academic_semester_id
            CROSS APPLY (SELECT TOP (1) s.snapshot_json FROM dbo.project_registration_snapshots s WHERE s.project_id=p.id ORDER BY s.id DESC) snap
            WHERE ({account.Student}=1 AND EXISTS(SELECT 1 FROM dbo.team_members m WHERE m.team_id=p.team_id AND m.user_id={actor} AND m.left_at IS NULL))
              OR ({account.Lecturer}=1 AND (EXISTS(SELECT 1 FROM dbo.supervisor_assignments a JOIN dbo.supervisor_profiles sp ON sp.id=a.supervisor_profile_id WHERE a.project_id=p.id AND a.ended_at IS NULL AND sp.user_id={actor})
                OR EXISTS(SELECT 1 FROM dbo.evaluation_assignments a WHERE a.project_id=p.id AND a.evaluator_id={actor} AND a.status='ACTIVE')
                OR EXISTS(SELECT 1 FROM dbo.defense_committees c JOIN dbo.defense_committee_members m ON m.committee_id=c.id WHERE c.project_id=p.id AND m.user_id={actor})))
              OR ({account.Staff}=1 AND EXISTS(SELECT 1 FROM OPENJSON(CASE WHEN ISJSON(snap.snapshot_json)=1 THEN snap.snapshot_json ELSE '[]' END,'$.DepartmentIds') d WHERE TRY_CONVERT(bigint,d.value)={account.DepartmentId}))
            ORDER BY p.id
            """).ToArrayAsync(ct);
        if (candidates.Length > 1000) throw Invalid("Calendar scope exceeds 1000 projects; use a scoped project schedule.");
        var parsed = candidates.Select(c => (Row: c, Scope: ProjectAcademicScopeReader.ParseEvidence(c.SnapshotJson))).Where(x => x.Scope is not null).ToArray();
        var departmentIds = parsed.SelectMany(x => x.Scope!.DepartmentIds).Distinct().ToArray();
        var departments = await db.Departments.Where(d => departmentIds.Contains(d.Id) && d.IsActive && d.Organization.IsActive)
            .Select(d => new { d.Id, d.OrganizationId }).ToDictionaryAsync(d => d.Id, d => d.OrganizationId, ct);
        var majorIds = parsed.SelectMany(x => x.Scope!.Scope.Requirements.Select(r => r.MajorId)).Distinct().ToArray();
        var activeMajors = (await db.Majors.Where(m => majorIds.Contains(m.Id) && m.IsActive).Select(m => m.Id).ToArrayAsync(ct)).ToHashSet();
        var valid = parsed.Where(x => x.Scope!.DepartmentIds.All(d => departments.GetValueOrDefault(d) == x.Row.OrganizationId)
            && x.Scope!.Scope.Requirements.All(r => activeMajors.Contains(r.MajorId))).ToArray();
        var projectIds = valid.Select(x => x.Row.ProjectId).ToArray();
        var mentors = account.Lecturer ? await db.SupervisorAssignments.Where(a => projectIds.Contains(a.ProjectId) && a.EndedAt == null
            && a.AssignmentType == "DISCIPLINE_MENTOR" && a.MajorId.HasValue && a.SupervisorProfile.UserId == actor)
            .Select(a => new { a.ProjectId, MajorId = a.MajorId!.Value }).ToArrayAsync(ct) : [];
        var whole = new HashSet<long>(); var majors = new HashSet<string>(); var checkpoints = new HashSet<long>();
        var students = new HashSet<long>(); var studentMajors = new HashSet<string>();
        foreach (var (row, registration) in valid)
        {
            if (row.Primary || account.Staff && registration!.Scope.LeadDepartmentId == account.DepartmentId)
            { whole.Add(row.ProjectId); checkpoints.Add(row.ProjectId); }
            var student = registration!.Members.SingleOrDefault(m => m.UserId == actor);
            if (row.Student && student is not null) { whole.Add(row.ProjectId); students.Add(row.ProjectId); studentMajors.Add($"{row.ProjectId}:{student.MajorId}"); }
            foreach (var major in registration!.Scope.Requirements)
                if (account.Staff && registration.MajorDepartmentIds![major.MajorId] == account.DepartmentId || mentors.Any(a => a.ProjectId == row.ProjectId && a.MajorId == major.MajorId))
                    majors.Add($"{row.ProjectId}:{major.MajorId}");
        }
        return new(actor, account.DepartmentId, account.Lecturer, projectIds, whole.ToArray(), majors.ToArray(), checkpoints.ToArray(), students.ToArray(), studentMajors.ToArray());
    }
}
