using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.PeerEvaluations;
using AIPMS.Application.Features.PeerEvaluations.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class PeerEvaluationService(AipmsDbContext db, IEvaluationDraftRepository transactions,
    ICurrentUser current, IAuditTrail audit, TimeProvider clock, IOptions<PeerEvaluationSettings> settings) : IPeerEvaluationService
{
    private long Actor => current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private sealed record Access(EvaluationProject Project, bool Student, long? PrimaryId, bool Lead, long? StaffDepartment);
    private static ValidationException Invalid(string message) => new(new Dictionary<string, string[]> { ["peerEvaluation"] = [message] });
    private static void Paging(int page, int size)
    { if (page is < 1 or > 100000 || size is < 1 or > 100) throw Invalid("page 1..100000 and pageSize 1..100 required."); }
    private static void Token(Guid expected, string? value)
    { if (!Guid.TryParse(value, out var parsed) || parsed != expected) throw new ConflictException("Reload the changed peer version.", "PEER_STALE"); }
    private async Task<Access> Scope(long projectId, CancellationToken ct)
    {
        if (!settings.Value.Enabled) throw new NotFoundException("PeerEvaluationFeature", projectId);
        var actor = await transactions.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        var project = await transactions.GetProjectAsync(projectId, ct) ?? throw new NotFoundException("Project", projectId);
        var student = await db.TeamMembers.AnyAsync(m => m.Team.Project != null && m.Team.Project.Id == projectId
            && m.UserId == Actor && m.LeftAt == null && m.User.Status == "ACTIVE"
            && m.User.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"), ct);
        var primary = actor.IsLecturer ? await db.SupervisorAssignments.Where(a => a.ProjectId == projectId
            && a.EndedAt == null && a.AssignedAt <= Now && a.IsPrimary && a.AssignmentType == "PRIMARY" && a.SupervisorProfile.UserId == Actor)
            .Select(a => (long?)a.Id).SingleOrDefaultAsync(ct) : null;
        var staff = AcademicAuthority.HasScope(actor, project);
        if (!student && !primary.HasValue && !staff) throw new ForbiddenException("Peer evaluations require membership, Primary assignment or frozen department scope.");
        if (!project.ActiveScope || project.ScopeProvenance != "FROZEN_REGISTRATION_SNAPSHOT")
            throw new ConflictException("Peer evaluations require frozen academic scope.", WorkflowErrorCodes.AcademicScopeUnknown);
        return new(project, student, primary, AcademicAuthority.IsLead(actor, project), staff ? actor.DepartmentId : null);
    }
    private static bool Writable(Access access) => access.Project.Status is "ACTIVE" or "FINAL_SUBMISSION";
    private static void Manage(Access access)
    {
        if (!access.PrimaryId.HasValue) throw new ForbiddenException("Only the current Primary may manage peer rounds.");
        if (!Writable(access)) throw new ConflictException("The project is read-only.", "PEER_PROJECT_READ_ONLY");
    }
    private async Task<PeerRound> Find(long projectId, long roundId, CancellationToken ct) =>
        await db.Set<PeerRound>().Include(r => r.Members).SingleOrDefaultAsync(r => r.ProjectId == projectId && r.Id == roundId, ct)
            ?? throw new NotFoundException("PeerRound", roundId);
    private async Task<string[]> Reasons(PeerRound round, Access access, CancellationToken ct)
    {
        var reasons = new List<string>();
        if (!Writable(access)) reasons.Add("PEER_PROJECT_READ_ONLY");
        if (round.Status != "OPEN" || Now < round.OpensAt || Now >= round.ClosesAt) reasons.Add("PEER_ROUND_NOT_OPEN");
        var roster = await WorkingAgreementGuard.Roster(db, round.ProjectId, ct);
        var captured = round.Members.Select(m => new WorkingAgreementGuard.RosterMember(m.UserId, m.TeamMemberId,
            m.MembershipJoinedAt, m.MembershipGeneration)).OrderBy(m => m.UserId).ToArray();
        if (!captured.SequenceEqual(roster)) reasons.Add("PEER_ROSTER_CHANGED");
        return reasons.ToArray();
    }
    private async Task SendGuard(PeerRound round, Access access, CancellationToken ct)
    {
        if (!access.Student || !round.Members.Any(m => m.UserId == Actor)) throw new ForbiddenException();
        var reasons = await Reasons(round, access, ct);
        if (reasons.Length > 0) throw new ConflictException(string.Join(", ", reasons), reasons[0]);
    }
    private PeerRoundDto Map(PeerRound row, Access access, string[] reasons)
    {
        var members = access.Student || access.PrimaryId.HasValue || access.Lead ? row.Members
            : row.Members.Where(m => m.DepartmentId == access.StaffDepartment);
        var roster = members.OrderBy(m => m.UserId).Select(m => new PeerRosterMember(m.UserId, m.MajorId, m.FullName)).ToArray();
        return new(row.Id, row.ProjectId, row.Title, row.Status == "OPEN"
            ? Now < row.OpensAt ? "SCHEDULED" : Now >= row.ClosesAt ? "EXPIRED" : "OPEN" : "CLOSED",
            Utc(row.OpensAt), Utc(row.ClosesAt), row.ClosedAt is DateTime closed ? Utc(closed) : null,
            row.ConcurrencyToken.ToString("N"), roster.Length, access.PrimaryId.HasValue && Writable(access),
            access.Student && row.Members.Any(m => m.UserId == Actor) && reasons.Length == 0, reasons, roster);
    }
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static PeerEvaluationDto Map(PeerSubmission row, IEnumerable<PeerSubmissionItem>? items = null) => new(row.Id, row.RoundId,
        row.AuthorId, row.Status, row.ConcurrencyToken.ToString("N"), Utc(row.UpdatedAt), row.SubmittedAt is DateTime at ? Utc(at) : null,
        (items ?? row.Items).OrderBy(i => i.TargetUserId).Select(i => new PeerRatingInput(i.TargetUserId, i.Rating, i.Comment)).ToArray());
    private Task Audit(string action, string type, long id, long projectId, CancellationToken ct) => audit.RecordAsync(new(Actor, action,
        type, id, new Dictionary<string, object?> { ["projectId"] = projectId }), ct);

    public Task<PeerRoundDto> Create(long projectId, CreatePeerRoundRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct); Manage(access);
        var opensAt = new DateTime(input.OpensAt.UtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        var closesAt = new DateTime(input.ClosesAt.UtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 200 || opensAt >= closesAt
            || closesAt <= Now || closesAt - opensAt > TimeSpan.FromDays(90))
            throw Invalid("Title <=200 and a future closing time within a window of at most 90 days required.");
        if (await db.Set<PeerRound>().AnyAsync(r => r.ProjectId == projectId && r.Status == "OPEN", ct))
            throw new ConflictException("Close the existing round explicitly before opening another.", "PEER_ROUND_EXISTS");
        var roster = await WorkingAgreementGuard.Roster(db, projectId, ct);
        if (roster.Length is < 2 or > 100) throw new ConflictException("Peer rounds require 2..100 current members.");
        var snapshot = await db.Set<ProjectRegistrationSnapshot>().Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.Id).Select(s => s.SnapshotJson).FirstAsync(ct);
        var frozen = ProjectAcademicScopeReader.ParseEvidence(snapshot)!;
        var ids = roster.Select(r => r.UserId).ToArray();
        var eligible = await db.Users.CountAsync(u => ids.Contains(u.Id) && u.Status == "ACTIVE" && u.AcademicProfileStatus == "VERIFIED"
            && u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"), ct);
        if (eligible != roster.Length || roster.Any(r => frozen.Members.Count(m => m.UserId == r.UserId) != 1
            || !frozen.MajorDepartmentIds!.ContainsKey(frozen.Members.Single(m => m.UserId == r.UserId).MajorId)))
            throw new ConflictException("Current students require verified frozen major targets.", "PEER_ROSTER_PROVENANCE_UNKNOWN");
        var row = new PeerRound { ProjectId = projectId, Title = input.Title.Trim(), OpensAt = opensAt,
            ClosesAt = closesAt, CreatedAt = Now, CreatedBy = Actor, PrimaryAssignmentId = access.PrimaryId!.Value,
            Members = roster.Select(r => { var member = frozen.Members.Single(m => m.UserId == r.UserId);
                return new PeerRoundMember { UserId = r.UserId, MajorId = member.MajorId, DepartmentId = frozen.MajorDepartmentIds![member.MajorId],
                    FullName = member.FullName, TeamMemberId = r.TeamMemberId, MembershipJoinedAt = r.JoinedAt, MembershipGeneration = r.MembershipGeneration }; }).ToArray() };
        db.Add(row); await db.SaveChangesAsync(ct); await Audit("PEER_ROUND_CREATED", "PEER_ROUND", row.Id, projectId, ct);
        return Map(row, access, await Reasons(row, access, ct));
    }, ct);
    public Task<PeerRoundDto> Close(long projectId, long roundId, PeerTokenRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct); Manage(access);
        var row = await Find(projectId, roundId, ct); Token(row.ConcurrencyToken, input.ConcurrencyToken);
        if (row.Status != "CLOSED") {
            row.Status = "CLOSED"; row.ClosedAt = Now;
            await db.SaveChangesAsync(ct); await Audit("PEER_ROUND_CLOSED", "PEER_ROUND", row.Id, projectId, ct);
        }
        return Map(row, access, await Reasons(row, access, ct));
    }, ct);
    public async Task<PagedResult<PeerRoundDto>> Rounds(long projectId, int page, int pageSize, CancellationToken ct)
    {
        Paging(page, pageSize); var access = await Scope(projectId, ct);
        var query = db.Set<PeerRound>().AsNoTracking().Include(r => r.Members).Where(r => r.ProjectId == projectId);
        if (!access.PrimaryId.HasValue && !access.Lead)
            query = access.Student ? query.Where(r => r.Members.Any(m => m.UserId == Actor))
                : query.Where(r => r.Members.Any(m => m.DepartmentId == access.StaffDepartment));
        var count = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var ids = rows.Select(r => r.Id).ToArray();
        var submitted = await db.Set<PeerSubmission>().Where(s => ids.Contains(s.RoundId) && s.AuthorId == Actor && s.Status == "SUBMITTED")
            .Select(s => s.RoundId).ToArrayAsync(ct);
        // Only one open round can be writable; closed rows need no roster query.
        var items = new List<PeerRoundDto>();
        foreach (var row in rows)
        {
            var reasons = row.Status == "OPEN" ? await Reasons(row, access, ct) : ["PEER_ROUND_NOT_OPEN"];
            if (submitted.Contains(row.Id)) reasons = [.. reasons, "PEER_SUBMITTED"];
            items.Add(Map(row, access, reasons));
        }
        return new(items, page, pageSize, count);
    }
    public async Task<MyPeerEvaluationDto> Mine(long projectId, long? roundId, CancellationToken ct)
    {
        var access = await Scope(projectId, ct); if (!access.Student) throw new ForbiddenException();
        var id = roundId ?? await db.Set<PeerRound>().Where(r => r.ProjectId == projectId && r.Status == "OPEN"
            && r.OpensAt <= Now && r.ClosesAt > Now).Select(r => (long?)r.Id).SingleOrDefaultAsync(ct);
        if (id is null) return new(null, "NO_OPEN_ROUND", null);
        var round = await Find(projectId, id.Value, ct);
        if (!round.Members.Any(m => m.UserId == Actor)) throw new ForbiddenException();
        var row = await db.Set<PeerSubmission>().Include(s => s.Items).SingleOrDefaultAsync(s => s.RoundId == id && s.AuthorId == Actor, ct);
        return new(id, row?.Status ?? "NOT_STARTED", row is null ? null : Map(row));
    }
    public Task<PeerEvaluationDto> Save(long projectId, long roundId, SavePeerEvaluationRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct);
        var round = await Find(projectId, roundId, ct); await SendGuard(round, access, ct);
        if (input.Items is null || input.Items.Count > 99 || input.Items.Any(i => i is null || i.Rating is < 1 or > 5 || i.Comment?.Length > 2000)
            || input.Items.Select(i => i.TargetUserId).Distinct().Count() != input.Items.Count) throw Invalid("Distinct targets, ratings 1..5, comments <=2000 required.");
        if (input.Items.Any(i => i.TargetUserId == Actor || !round.Members.Any(m => m.UserId == i.TargetUserId)))
            throw new ForbiddenException("Rate only other members of this round's roster.");
        var row = await db.Set<PeerSubmission>().Include(s => s.Items).SingleOrDefaultAsync(s => s.RoundId == roundId && s.AuthorId == Actor, ct);
        if (row is null) {
            if (input.ConcurrencyToken is not null) throw new ConflictException("No draft matches this token.", "PEER_STALE");
            row = new() { RoundId = roundId, AuthorId = Actor }; db.Add(row);
        } else {
            Token(row.ConcurrencyToken, input.ConcurrencyToken);
            if (row.Status != "DRAFT") throw new ConflictException("Submitted peer evidence is immutable.", "PEER_SUBMITTED");
            db.RemoveRange(row.Items); row.Items.Clear(); await db.SaveChangesAsync(ct);
        }
        row.Items = input.Items.Select(i => new PeerSubmissionItem { TargetUserId = i.TargetUserId, Rating = i.Rating, Comment = i.Comment?.Trim() }).ToArray();
        row.UpdatedAt = Now; row.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(ct);
        await Audit("PEER_DRAFT_SAVED", "PEER_SUBMISSION", row.Id, projectId, ct); return Map(row);
    }, ct);
    public Task<PeerEvaluationDto> Submit(long projectId, long roundId, PeerTokenRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct);
        if (!access.Student) throw new ForbiddenException();
        var round = await Find(projectId, roundId, ct);
        var row = await db.Set<PeerSubmission>().Include(s => s.Items).SingleOrDefaultAsync(s => s.RoundId == roundId && s.AuthorId == Actor, ct)
            ?? throw new NotFoundException("PeerDraft", roundId);
        Token(row.ConcurrencyToken, input.ConcurrencyToken);
        if (row.Status == "SUBMITTED") return Map(row);
        await SendGuard(round, access, ct);
        if (!row.Items.Select(i => i.TargetUserId).Order().SequenceEqual(round.Members.Where(m => m.UserId != Actor).Select(m => m.UserId).Order()))
            throw new ConflictException("Complete every other roster member before submission.", "PEER_INCOMPLETE");
        row.Status = "SUBMITTED"; row.SubmittedAt = Now; row.UpdatedAt = Now;
        await db.SaveChangesAsync(ct); await Audit("PEER_SUBMITTED", "PEER_SUBMISSION", row.Id, projectId, ct); return Map(row);
    }, ct);
    public async Task<PagedResult<PeerEvaluationDto>> Review(long projectId, long roundId, int page, int pageSize, CancellationToken ct)
    {
        Paging(page, pageSize); var access = await Scope(projectId, ct);
        if (!access.PrimaryId.HasValue && !access.StaffDepartment.HasValue) throw new ForbiddenException();
        var round = await Find(projectId, roundId, ct);
        var broad = access.PrimaryId.HasValue || access.Lead;
        var targets = round.Members.Where(m => broad || m.DepartmentId == access.StaffDepartment).Select(m => m.UserId).ToArray();
        var query = db.Set<PeerSubmission>().AsNoTracking().Where(s => s.RoundId == roundId && s.Status == "SUBMITTED"
            && s.Items.Any(i => targets.Contains(i.TargetUserId)));
        var count = await query.LongCountAsync(ct);
        var rows = await query.Include(s => s.Items.Where(i => targets.Contains(i.TargetUserId)))
            .OrderBy(s => s.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(r => Map(r)).ToArray(), page, pageSize, count);
    }
}
