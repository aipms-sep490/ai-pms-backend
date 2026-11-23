using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ProjectHub;
using AIPMS.Application.Features.ProjectHub.Abstractions;
using AIPMS.Domain.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ProjectHubService(
    AipmsDbContext db,
    ICurrentUser current,
    IAuditTrail audit,
    TimeProvider clock) : IProjectHubService
{
    private long Actor => current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private sealed record ProjectAccessContext(
        M.Project Project,
        bool IsAdmin,
        bool IsScopedStaff,
        bool IsPrimarySupervisor,
        bool IsMentor,
        bool IsTeamMember,
        bool IsTeamLeader,
        string? ActorMajorCode,
        string? ActorMajorName);

    private static bool MatchesChannelDiscipline(string scope, string? majorCode, string? majorName)
    {
        var combined = $"{majorCode ?? ""} {majorName ?? ""}".ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(combined)) return false;
        var isMkt = combined.Contains("MKT") || combined.Contains("MARKETING") || combined.Contains("BRAND")
            || combined.Contains("COMMUNICATION") || combined.Contains("DIGITAL MEDIA");
        if (scope == "MKT") return isMkt;
        if (scope == "IT") return !isMkt && (combined.Contains("IT") || combined.Contains("SE") || combined.Contains("SOFTWARE")
            || combined.Contains("CS") || combined.Contains("COMPUTER") || combined.Contains("IS") || combined.Contains("CNTT"));
        return true;
    }

    private async Task<ProjectAccessContext> LoadAccessAsync(long projectId, CancellationToken ct)
    {
        var actorId = Actor;
        var project = await db.Projects
            .Include(p => p.Team).ThenInclude(t => t.TeamMembers)
            .Include(p => p.ProjectMajors).ThenInclude(pm => pm.Major)
            .SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

        var user = await db.Users.AsNoTracking()
            .Include(u => u.Major)
            .SingleOrDefaultAsync(u => u.Id == actorId && u.Status == "ACTIVE", ct)
            ?? throw new ForbiddenException();

        var isAdmin = current.Roles.Contains(AppRoles.Admin);
        var projectDeptIds = project.ProjectMajors.Select(pm => pm.Major.DepartmentId).Distinct().ToHashSet();
        var isScopedStaff = current.Roles.Contains(AppRoles.DepartmentStaff)
            && user.DepartmentId.HasValue && projectDeptIds.Contains(user.DepartmentId.Value);

        var supAssigns = await db.SupervisorAssignments.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.SupervisorProfile.UserId == actorId && a.EndedAt == null)
            .Select(a => new { a.IsPrimary, a.AssignmentType }).ToArrayAsync(ct);
        var isPrimary = supAssigns.Any(a => a.IsPrimary || a.AssignmentType == "PRIMARY");
        var isMentor = supAssigns.Length > 0 && !isPrimary;

        var member = project.Team.TeamMembers.FirstOrDefault(m => m.UserId == actorId && m.LeftAt == null);
        return new(project, isAdmin, isScopedStaff, isPrimary, isMentor, member is not null, member?.IsLeader == true,
            user.Major?.Code, user.Major?.Name);
    }

    private static void RequireProjectMemberOrStaff(ProjectAccessContext ctx)
    {
        if (!ctx.IsAdmin && !ctx.IsScopedStaff && !ctx.IsPrimarySupervisor && !ctx.IsMentor && !ctx.IsTeamMember)
            throw new ForbiddenException("You do not have access to this project workspace.");
    }

    private static void RequireExecutiveOrSupervisorScope(ProjectAccessContext ctx)
    {
        if (!ctx.IsAdmin && !ctx.IsScopedStaff && !ctx.IsPrimarySupervisor && !ctx.IsMentor)
            throw new ForbiddenException("Only authorized Department Staff, Supervisors, or Administrators can view executive pulse and audit timeline.");
    }

    private static string NormalizeChannelScope(string scope)
    {
        var normalized = scope?.Trim().ToUpperInvariant() ?? "";
        if (normalized is not ("GENERAL" or "IT" or "MKT"))
            throw new DomainException("Channel scope must be GENERAL, IT, or MKT.", WorkflowErrorCodes.ValidationFailed);
        return normalized;
    }

    private static void RequireChannelAccess(ProjectAccessContext ctx, string channelScope)
    {
        RequireProjectMemberOrStaff(ctx);
        if (channelScope == "GENERAL") return;
        if (ctx.IsAdmin || ctx.IsPrimarySupervisor || ctx.IsTeamLeader || ctx.IsScopedStaff) return;
        if (MatchesChannelDiscipline(channelScope, ctx.ActorMajorCode, ctx.ActorMajorName)) return;
        throw new ForbiddenException($"You do not have access to the '{channelScope}' discipline channel.", WorkflowErrorCodes.ScopeAccessDenied);
    }

    public async Task<SpecVersionDto> CreateSpecVersionAsync(long projectId, long deliverableId, CreateSpecVersionRequest input, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        var deliverable = await db.Deliverables.SingleOrDefaultAsync(d => d.Id == deliverableId && d.ProjectId == projectId, ct)
            ?? throw new NotFoundException("Deliverable", deliverableId);

        var maxVer = await db.DeliverableVersions.Where(v => v.DeliverableId == deliverableId)
            .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;
        var nextVer = maxVer + 1;
        var changelog = input.Changelog?.Trim();
        if (nextVer >= 2 && (string.IsNullOrWhiteSpace(changelog) || changelog.Length < 10))
            throw new DomainException("Revision versions (v2+) require a changelog of at least 10 characters.", WorkflowErrorCodes.ValidationFailed);

        var now = Now;
        var version = new M.DeliverableVersion
        {
            DeliverableId = deliverable.Id,
            VersionNumber = nextVer,
            SubmittedBy = Actor,
            SubmissionNote = input.SubmissionNote?.Trim(),
            Changelog = changelog,
            Status = "SUBMITTED",
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.DeliverableVersions.Add(version);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(Actor, "SPEC_VERSION_CREATED", "DELIVERABLE_VERSION", version.Id, new Dictionary<string, object?>
        {
            ["projectId"] = projectId,
            ["deliverableId"] = deliverableId,
            ["versionNumber"] = version.VersionNumber
        }), ct);

        return new(version.Id, version.DeliverableId, version.VersionNumber, version.Status,
            version.SubmissionNote, version.Changelog, version.SubmittedBy, version.SubmittedAt);
    }

    public async Task<SpecVersionCompareDto> CompareSpecVersionsAsync(long projectId, long deliverableId, int fromVersion, int toVersion, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        var versions = await db.DeliverableVersions.AsNoTracking()
            .Include(v => v.Files)
            .Where(v => v.DeliverableId == deliverableId && v.Deliverable.ProjectId == projectId
                && (v.VersionNumber == fromVersion || v.VersionNumber == toVersion))
            .ToArrayAsync(ct);

        var from = versions.FirstOrDefault(v => v.VersionNumber == fromVersion)
            ?? throw new NotFoundException("DeliverableVersion", fromVersion);
        var to = versions.FirstOrDefault(v => v.VersionNumber == toVersion)
            ?? throw new NotFoundException("DeliverableVersion", toVersion);

        var fromSet = from.Files.Select(f => $"{f.OriginalFileName}:{f.ChecksumSha256}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toSet = to.Files.Select(f => $"{f.OriginalFileName}:{f.ChecksumSha256}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = to.Files.Where(f => !fromSet.Contains($"{f.OriginalFileName}:{f.ChecksumSha256}")).Select(f => f.OriginalFileName).ToArray();
        var removed = from.Files.Where(f => !toSet.Contains($"{f.OriginalFileName}:{f.ChecksumSha256}")).Select(f => f.OriginalFileName).ToArray();
        var unchanged = to.Files.Where(f => fromSet.Contains($"{f.OriginalFileName}:{f.ChecksumSha256}")).Select(f => f.OriginalFileName).ToArray();

        static SpecVersionDto Map(M.DeliverableVersion v) =>
            new(v.Id, v.DeliverableId, v.VersionNumber, v.Status, v.SubmissionNote, v.Changelog, v.SubmittedBy, v.SubmittedAt);
        return new(deliverableId, Map(from), Map(to), added, removed, unchanged);
    }

    public async Task<IReadOnlyList<PinnedResourceDto>> ListPinnedResourcesAsync(long projectId, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        return await db.Set<ProjectPinnedResource>().AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new PinnedResourceDto(r.Id, r.ProjectId, r.ResourceType, r.Title, r.Url, r.CreatedBy, r.CreatedAt))
            .ToArrayAsync(ct);
    }

    public async Task<PinnedResourceDto> AddPinnedResourceAsync(long projectId, CreatePinnedResourceRequest input, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        var type = input.ResourceType?.Trim().ToUpperInvariant() ?? "OTHER";
        if (type is not ("FIGMA" or "DRIVE" or "DOC" or "REPO_LINK" or "OTHER"))
            throw new DomainException("Resource type must be FIGMA, DRIVE, DOC, REPO_LINK, or OTHER.", WorkflowErrorCodes.ValidationFailed);
        if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Url))
            throw new DomainException("Pinned resource title and URL are required.", WorkflowErrorCodes.ValidationFailed);

        var item = new ProjectPinnedResource
        {
            ProjectId = projectId,
            ResourceType = type,
            Title = input.Title.Trim(),
            Url = input.Url.Trim(),
            CreatedBy = Actor,
            CreatedAt = Now
        };
        db.Set<ProjectPinnedResource>().Add(item);
        await db.SaveChangesAsync(ct);
        return new(item.Id, item.ProjectId, item.ResourceType, item.Title, item.Url, item.CreatedBy, item.CreatedAt);
    }

    public async Task DeletePinnedResourceAsync(long projectId, long resourceId, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        var item = await db.Set<ProjectPinnedResource>().SingleOrDefaultAsync(r => r.Id == resourceId && r.ProjectId == projectId, ct)
            ?? throw new NotFoundException("ProjectPinnedResource", resourceId);
        db.Set<ProjectPinnedResource>().Remove(item);
        await db.SaveChangesAsync(ct);
    }
}
