using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.ExecutionCapabilities.DTOs;
using AIPMS.Application.Features.WorkingAgreements;
using AIPMS.Application.Features.WorkingAgreements.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class WorkingAgreementService(AipmsDbContext db, IEvaluationDraftRepository transactions,
    ICurrentUser current, IAuditTrail audit, TimeProvider clock, IOptions<WorkingAgreementSettings> settings) : IWorkingAgreementService
{
    private long Actor => current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new UnauthorizedException();
    private sealed record Access(bool Leader, bool Member, bool Writable);
    private static ValidationException Invalid(string value) => new(new Dictionary<string, string[]> { ["workingAgreement"] = [value] });
    private async Task<Access> Scope(long projectId, CancellationToken ct)
    {
        if (!settings.Value.Enabled) throw new NotFoundException("WorkingAgreementFeature", projectId);
        var actor = await transactions.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        var project = await transactions.GetProjectAsync(projectId, ct) ?? throw new NotFoundException("Project", projectId);
        var member = await db.TeamMembers.AsNoTracking().Where(m => m.Team.Project != null && m.Team.Project.Id == projectId
            && m.LeftAt == null && m.UserId == Actor && m.User.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"))
            .Select(m => new { m.IsLeader }).SingleOrDefaultAsync(ct);
        var primary = actor.IsLecturer && await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == projectId
            && a.AssignmentType == "PRIMARY" && a.IsPrimary && a.EndedAt == null && a.SupervisorProfile.UserId == Actor, ct);
        if (member is null && !primary && !AcademicAuthority.HasScope(actor, project)) throw new ForbiddenException();
        var writable = project.Status is "DRAFT" or "REVISION_REQUIRED" or "SUBMITTED" or "APPROVED" or "SUPERVISOR_PENDING" or "ACTIVE";
        writable &= project.ActiveScope || project.Status is "DRAFT" or "REVISION_REQUIRED";
        return new(member?.IsLeader == true, member is not null, writable);
    }
    private static void Token(WorkingAgreement row, string? token)
    {
        if (!Guid.TryParse(token, out var value) || row.ConcurrencyToken != value)
            throw new ConflictException("Agreement version changed; reload.", "WORKING_AGREEMENT_STALE");
    }
    private static void Write(Access access, bool leader)
    {
        if (leader ? !access.Leader : !access.Member) throw new ForbiddenException();
        if (!access.Writable) throw new ConflictException("The project is read-only.", "WORKING_AGREEMENT_READ_ONLY");
    }
    private Task Audit(string action, WorkingAgreement row, CancellationToken ct) => audit.RecordAsync(new(Actor, action,
        "WORKING_AGREEMENT", row.Id, new Dictionary<string, object?> { ["projectId"] = row.ProjectId, ["version"] = row.Version }), ct);
    private static ExecutionActionDto Action(string code, bool allowed, string reason) => new(code, allowed, allowed ? [] : [reason]);
    private async Task<IReadOnlyList<ExecutionActionDto>> Actions(long projectId, Access access, CancellationToken ct)
    {
        var latest = await db.Set<WorkingAgreement>().Include(a => a.Members).Where(a => a.ProjectId == projectId)
            .OrderByDescending(a => a.Version).FirstOrDefaultAsync(ct);
        var active = latest?.Status == "ACTIVE" ? latest : await db.Set<WorkingAgreement>().Include(a => a.Members)
            .SingleOrDefaultAsync(a => a.ProjectId == projectId && a.Status == "ACTIVE", ct);
        var roster = await WorkingAgreementGuard.Roster(db, projectId, ct);
        var currentRoster = active is not null && WorkingAgreementGuard.Matches(active, roster);
        var mine = active?.Members.SingleOrDefault(m => m.UserId == Actor);
        return [Action("manage_working_agreement", access.Leader && access.Writable, "ACTIVE_LEADER_AND_WRITABLE_PROJECT_REQUIRED"),
            Action("publish_working_agreement", access.Leader && access.Writable && latest?.Status == "DRAFT" && roster.Length > 0, "WRITABLE_DRAFT_AND_LEADER_REQUIRED"),
            Action("accept_working_agreement", access.Member && access.Writable && currentRoster && mine is not null && !mine.AcceptedAt.HasValue,
                "CURRENT_UNACCEPTED_AGREEMENT_REQUIRED")];
    }
    private static WorkingAgreementDto Map(WorkingAgreement row, long actor, WorkingAgreementReadiness readiness,
        IReadOnlyList<ExecutionActionDto> actions) => new(row.Id,
        row.ProjectId, row.Version, row.Status, row.ContentJson, row.CreatedBy, DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc),
        row.ConcurrencyToken.ToString("N"), readiness.Required,
        row.Members.Count, row.Members.Any(m => m.UserId == actor && m.AcceptedAt.HasValue),
        row.Members.Where(m => m.AcceptedAt.HasValue).Select(m => new AgreementAcceptanceDto(m.UserId,
            DateTime.SpecifyKind(m.AcceptedAt!.Value, DateTimeKind.Utc))).OrderBy(m => m.UserId).ToArray(),
        readiness, actions.Select(a => a.Code == "accept_working_agreement" && row.Status != "ACTIVE"
            || a.Code == "publish_working_agreement" && row.Status != "DRAFT"
                ? Action(a.Code, false, "AGREEMENT_VERSION_NOT_ACTIONABLE") : a).ToArray());
    private async Task<WorkingAgreementDto> Dto(WorkingAgreement row, Access access, CancellationToken ct) =>
        Map(row, Actor, await WorkingAgreementGuard.Read(db, row.ProjectId, ct), await Actions(row.ProjectId, access, ct));
    public async Task<IReadOnlyList<ExecutionActionDto>> Capabilities(long projectId, CancellationToken ct)
    {
        if (!settings.Value.Enabled) return [Action("manage_working_agreement", false, "FEATURE_DISABLED"),
            Action("publish_working_agreement", false, "FEATURE_DISABLED"), Action("accept_working_agreement", false, "FEATURE_DISABLED")];
        try { return await Actions(projectId, await Scope(projectId, ct), ct); }
        catch (ForbiddenException) { return [Action("manage_working_agreement", false, "AGREEMENT_SCOPE_REQUIRED"),
            Action("publish_working_agreement", false, "AGREEMENT_SCOPE_REQUIRED"), Action("accept_working_agreement", false, "AGREEMENT_SCOPE_REQUIRED")]; }
    }
    public async Task<WorkingAgreementReadiness?> Readiness(long projectId, CancellationToken ct)
    {
        if (!settings.Value.Enabled) return null;
        try { await Scope(projectId, ct); return await WorkingAgreementGuard.Read(db, projectId, ct); }
        catch (ForbiddenException) { return null; }
    }
    public async Task<WorkingAgreementDto?> Get(long projectId, CancellationToken ct)
    {
        var access = await Scope(projectId, ct);
        var row = await db.Set<WorkingAgreement>().Include(a => a.Members).Where(a => a.ProjectId == projectId)
            .OrderByDescending(a => a.Version).FirstOrDefaultAsync(ct);
        return row is null ? null : await Dto(row, access, ct);
    }
    public async Task<PagedResult<WorkingAgreementDto>> History(long projectId, int page, int pageSize, CancellationToken ct)
    {
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100) throw Invalid("Invalid bounded paging.");
        var access = await Scope(projectId, ct);
        var query = db.Set<WorkingAgreement>().Include(a => a.Members).Where(a => a.ProjectId == projectId);
        var count = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(a => a.Version).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var readiness = await WorkingAgreementGuard.Read(db, projectId, ct);
        var actions = await Actions(projectId, access, ct);
        var items = rows.Select(row => Map(row, Actor, readiness, actions)).ToArray();
        return new(items, page, pageSize, count);
    }
    public Task<WorkingAgreementDto> Save(long projectId, SaveWorkingAgreementRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await transactions.LockProjectAsync(projectId, ct);
        var access = await Scope(projectId, ct); Write(access, true);
        if (input.ExpectedLatestVersion < 0 || input.Sections is null || input.Sections.Count is < 1 or > 20
            || input.Sections.Any(s => s is null || string.IsNullOrWhiteSpace(s.Title) || s.Title.Length > 200
                || string.IsNullOrWhiteSpace(s.Body) || s.Body.Length > 4000)) throw Invalid("Provide 1-20 sections with title <=200 and body <=4000 characters.");
        var latest = await db.Set<WorkingAgreement>().Include(a => a.Members).Where(a => a.ProjectId == projectId).OrderByDescending(a => a.Version).FirstOrDefaultAsync(ct);
        if ((latest?.Version ?? 0) != input.ExpectedLatestVersion) throw new ConflictException("Agreement latest version changed.", "WORKING_AGREEMENT_STALE");
        WorkingAgreement row;
        if (latest?.Status == "DRAFT") { Token(latest, input.ConcurrencyToken); row = latest; }
        else
        {
            if (input.ConcurrencyToken is not null) throw Invalid("A successor uses expectedLatestVersion and no draft token.");
            row = new() { ProjectId = projectId, Version = (latest?.Version ?? 0) + 1, CreatedBy = Actor, CreatedAt = clock.GetUtcNow().UtcDateTime };
            db.Add(row);
        }
        row.ContentJson = JsonSerializer.Serialize(new { sections = input.Sections.Select(s => new { title = s.Title.Trim(), body = s.Body.Trim() }) });
        row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await Audit("WORKING_AGREEMENT_DRAFT_SAVED", row, ct);
        return await Dto(row, access, ct);
    }, ct);
    private async Task<WorkingAgreement> Find(long projectId, long id, CancellationToken ct) =>
        await db.Set<WorkingAgreement>().Include(a => a.Members).SingleOrDefaultAsync(a => a.ProjectId == projectId && a.Id == id, ct)
            ?? throw new NotFoundException("WorkingAgreement", id);
    public Task<WorkingAgreementDto> Publish(long projectId, AgreementTokenRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await transactions.LockProjectAsync(projectId, ct);
        var access = await Scope(projectId, ct); Write(access, true);
        var row = await Find(projectId, input.AgreementId, ct); Token(row, input.ConcurrencyToken);
        if (row.Status != "DRAFT") throw new ConflictException("Only a draft can be published.");
        var roster = await WorkingAgreementGuard.Roster(db, projectId, ct);
        if (roster.Length == 0) throw new ConflictException("A current roster is required.");
        var previous = await db.Set<WorkingAgreement>().SingleOrDefaultAsync(a => a.ProjectId == projectId && a.Status == "ACTIVE", ct);
        if (previous is not null) { previous.Status = "SUPERSEDED"; previous.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(ct); }
        row.Status = "ACTIVE"; row.PublishedAt = clock.GetUtcNow().UtcDateTime; row.ConcurrencyToken = Guid.NewGuid();
        row.Members = roster.Select(m => new AgreementMember { UserId = m.UserId, TeamMemberId = m.TeamMemberId }).ToList();
        await db.SaveChangesAsync(ct); await Audit("WORKING_AGREEMENT_PUBLISHED", row, ct);
        return await Dto(row, access, ct);
    }, ct);
    public Task<WorkingAgreementDto> Accept(long projectId, AgreementTokenRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await transactions.LockProjectAsync(projectId, ct);
        var access = await Scope(projectId, ct); Write(access, false);
        var row = await Find(projectId, input.AgreementId, ct); Token(row, input.ConcurrencyToken);
        if (row.Status != "ACTIVE") throw new ConflictException("Only the active agreement can be accepted.");
        var roster = await WorkingAgreementGuard.Roster(db, projectId, ct);
        if (!WorkingAgreementGuard.Matches(row, roster)) throw new ConflictException("Publish a new version for the changed roster.", "WORKING_AGREEMENT_ROSTER_CHANGED");
        var mine = row.Members.SingleOrDefault(m => m.UserId == Actor) ?? throw new ForbiddenException();
        if (!mine.AcceptedAt.HasValue)
        {
            mine.AcceptedAt = clock.GetUtcNow().UtcDateTime;
            // Acceptances do not alter the immutable content token; concurrent members can accept the same version.
            await db.SaveChangesAsync(ct); await Audit("WORKING_AGREEMENT_ACCEPTED", row, ct);
        }
        return await Dto(row, access, ct);
    }, ct);
}
