using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.Industry;
using AIPMS.Application.Features.Industry.Abstractions;
using AIPMS.Infrastructure.Identity.Configuration;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class IndustryService(AipmsDbContext db, IEvaluationDraftRepository transactions,
    ICurrentUser current, IAuditTrail audit, TimeProvider clock, IOptions<IndustrySettings> settings,
    IFileStorage storage, IDataProtectionProvider protection, IOpaqueTokenService opaque, IPasswordHashingService passwords,
    IGoogleIdentityVerifier verifier, IOptions<GoogleAuthSettings> google, IAccessTokenService accessTokens,
    IAccountSecurityPolicy accountPolicy, IRequestContext request) : IIndustryService
{
    internal const string NotificationType = "INDUSTRY_INVITATION";
    internal const string ProtectorName = "AI-PMS.Industry.Invitation.v1";
    private long Actor => current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime Seconds(DateTimeOffset value) => new(value.UtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    private static ValidationException Invalid(string message) => new(new Dictionary<string, string[]> { ["industry"] = [message] });
    private static void Page(int page, int size) { if (page is < 1 or > 1000 || size is < 1 or > 100) throw Invalid("page 1..1000 and pageSize 1..100 required."); }
    private void Enabled() { if (!settings.Value.Enabled) throw new NotFoundException("IndustryFeature", 0); }
    private static void Token(IndustryAssignment row, string token)
    { if (!Guid.TryParse(token, out var parsed) || row.ConcurrencyToken != parsed) throw new ConflictException("Reload the industry assignment.", "INDUSTRY_STALE"); }
    private async Task ActiveActor(CancellationToken ct)
    {
        Enabled();
        if (!await db.Users.AnyAsync(u => u.Id == Actor && u.Status == "ACTIVE", ct)) throw new ForbiddenException();
    }
    private async Task Admin(CancellationToken ct)
    {
        await ActiveActor(ct);
        if (!await db.UserRoles.AnyAsync(r => r.UserId == Actor && r.Role.Code == "ADMIN", ct)) throw new ForbiddenException();
    }
    private async Task<EvaluationProject> Lead(long projectId, CancellationToken ct)
    {
        await ActiveActor(ct);
        var actor = await transactions.GetActorAsync(Actor, ct) ?? throw new ForbiddenException();
        var project = await transactions.GetProjectAsync(projectId, ct) ?? throw new NotFoundException("Project", projectId);
        if (!AcademicAuthority.IsLead(actor, project)) throw new ForbiddenException("Frozen lead department required.");
        return project;
    }
    private static void Writable(EvaluationProject project)
    { if (project.Status is not ("ACTIVE" or "FINAL_SUBMISSION")) throw new ConflictException("Project is read-only.", "PROJECT_READ_ONLY"); }
    private async Task<bool> Expert(long id, bool enrolled, CancellationToken ct) =>
        await db.Set<IndustryExpert>().AnyAsync(e => e.UserId == id && (!enrolled || e.EnrolledAt != null), ct)
        && await db.Users.AnyAsync(u => u.Id == id && u.Status == "ACTIVE", ct)
        && !await db.UserRoles.AnyAsync(r => r.UserId == id, ct);
    private bool Live(IndustryAssignment row) => row.RevokedAt is null && row.StartsAt <= Now && Now < row.EndsAt;
    private async Task<IndustryAssignment> Row(long id, CancellationToken ct) =>
        await db.Set<IndustryAssignment>().SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("IndustryAssignment", id);
    private async Task<(IndustryAssignment Row, bool Lead)> Read(long id, CancellationToken ct)
    {
        await ActiveActor(ct); var row = await Row(id, ct);
        if (row.ExpertId == Actor && await Expert(Actor, true, ct))
        {
            if (!Live(row)) throw new ForbiddenException("Industry assignment is not active.");
            return (row, false);
        }
        await Lead(row.ProjectId, ct); return (row, true);
    }
    private Task Audit(string action, string type, long id, long? projectId, CancellationToken ct) => audit.RecordAsync(new(
        Actor, action, type, id, new Dictionary<string, object?> { ["projectId"] = projectId, ["advisoryOnly"] = true }), ct);
    private async Task<IndustryAssignmentDto> Dto(IndustryAssignment row, bool lead, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().Where(p => p.Id == row.ProjectId).Select(p => new { p.Code, p.Title, p.Status }).SingleAsync(ct);
        return Map(row, project.Code, project.Title, project.Status, lead, await Expert(row.ExpertId, true, ct));
    }
    private IndustryAssignmentDto Map(IndustryAssignment row, string code, string title, string projectStatus, bool lead, bool enrolled)
    {
        var status = row.RevokedAt is not null ? "REVOKED" : Now < row.StartsAt ? "SCHEDULED" : Now >= row.EndsAt ? "EXPIRED" : "ACTIVE";
        var actions = new List<string> { "READ" };
        if (lead && row.RevokedAt is null) { actions.Add("REVOKE"); actions.Add("UNSHARE"); }
        if (Live(row) && projectStatus is "ACTIVE" or "FINAL_SUBMISSION")
        { if (lead) actions.Add("SHARE"); else if (enrolled) actions.Add("SUBMIT_FEEDBACK"); }
        return new(row.Id, row.ProjectId, code, title, row.ExpertId, row.Scope, row.MajorId, Utc(row.StartsAt), Utc(row.EndsAt), status,
            row.ConcurrencyToken.ToString("N"), actions);
    }
    public Task<IndustryAssignmentDto> Assign(long projectId, AssignIndustryRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        await transactions.LockProjectAsync(projectId, ct); Writable(await Lead(projectId, ct));
        if (input.Scope == "COMMON" ? input.MajorId is not null : input.Scope != "MAJOR_SPECIFIC" || input.MajorId is null)
            throw Invalid("COMMON has no major; MAJOR_SPECIFIC requires majorId.");
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct, true);
        if (input.MajorId.HasValue && !scope.MajorIds.Contains(input.MajorId.Value)) throw new ForbiddenException("Major outside frozen scope.");
        if (!await Expert(input.ExpertId, false, ct)) throw new ForbiddenException("Active invited expert required.");
        var start = Seconds(input.StartsAt); var end = Seconds(input.EndsAt);
        if (start >= end || end <= Now || end-start > TimeSpan.FromDays(366)) throw Invalid("Assignment needs a future end and a range up to 366 days.");
        if (await db.Set<IndustryAssignment>().AnyAsync(a => a.ProjectId == projectId && a.ExpertId == input.ExpertId && a.Scope == input.Scope
            && a.MajorId == input.MajorId && a.RevokedAt == null && a.StartsAt < end && start < a.EndsAt, ct)) throw new ConflictException("Overlapping expert assignment.");
        var row = new IndustryAssignment { ProjectId = projectId, ExpertId = input.ExpertId, Scope = input.Scope, MajorId = input.MajorId,
            StartsAt = start, EndsAt = end, AssignedBy = Actor, CreatedAt = Now };
        db.Add(row); await db.SaveChangesAsync(ct); await Audit("INDUSTRY_ASSIGNED", "INDUSTRY_ASSIGNMENT", row.Id, projectId, ct);
        return await Dto(row, true, ct);
    }, ct);
    public async Task<IndustryAssignmentDto> Get(long id, CancellationToken ct)
    { var access = await Read(id, ct); return await Dto(access.Row, access.Lead, ct); }
    public async Task<PagedResult<IndustryAssignmentDto>> Assignments(long? projectId, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); await ActiveActor(ct);
        var query = db.Set<IndustryAssignment>().AsNoTracking(); var lead = projectId.HasValue;
        if (projectId is long project) { await Lead(project, ct); query = query.Where(a => a.ProjectId == project); }
        else {
            if (!await Expert(Actor, true, ct)) throw new ForbiddenException();
            query = query.Where(a => a.ExpertId == Actor && a.RevokedAt == null && a.StartsAt <= Now && Now < a.EndsAt);
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.Id).Skip((page-1)*pageSize).Take(pageSize).ToArrayAsync(ct);
        var ids = rows.Select(a => a.ProjectId).Distinct().ToArray();
        var projects = await db.Projects.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Code, p.Title, p.Status }).ToDictionaryAsync(p => p.Id, ct);
        var expertIds = rows.Select(a => a.ExpertId).Distinct().ToArray();
        var enrolled = await db.Set<IndustryExpert>().Where(e => expertIds.Contains(e.UserId) && e.EnrolledAt != null
            && db.Users.Any(u => u.Id == e.UserId && u.Status == "ACTIVE") && !db.UserRoles.Any(r => r.UserId == e.UserId)).Select(e => e.UserId).ToArrayAsync(ct);
        return new(rows.Select(a => Map(a, projects[a.ProjectId].Code, projects[a.ProjectId].Title, projects[a.ProjectId].Status, lead, enrolled.Contains(a.ExpertId))).ToArray(), page, pageSize, count);
    }
    public Task<IndustryAssignmentDto> Revoke(long id, string token, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        var projectId = (await Row(id, ct)).ProjectId; db.ChangeTracker.Clear(); await transactions.LockProjectAsync(projectId, ct);
        await Lead(projectId, ct); var row = await Row(id, ct); Token(row, token);
        if (row.RevokedAt is not null) throw new ConflictException("Already revoked.");
        row.RevokedAt = Now; row.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(ct);
        await Audit("INDUSTRY_REVOKED", "INDUSTRY_ASSIGNMENT", id, projectId, ct); return await Dto(row, true, ct);
    }, ct);
}
