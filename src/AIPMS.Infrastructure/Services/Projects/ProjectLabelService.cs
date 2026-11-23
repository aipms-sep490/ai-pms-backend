using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ProjectLabels;
using AIPMS.Application.Features.ProjectLabels.Abstractions;
using AIPMS.Domain.Exceptions;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ProjectLabelService(
    AipmsDbContext db,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    TimeProvider clock) : IProjectLabelService
{
    private const int MaxLabelsPerProject = 30;
    private const int MaxLabelsPerTask = 10;
    private static readonly Regex SlugRegex = new("^[a-z0-9][a-z0-9-]{0,31}$", RegexOptions.Compiled);
    private static readonly Regex HexRegex = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    private static readonly (string Slug, string Name, string Hex, string Desc, bool ItMktOnly)[] DefaultLabels =
    [
        ("bug", "Bug", "#DC2626", "Lỗi cần xử lý", false),
        ("enhancement", "Enhancement", "#0F766E", "Tính năng / cải tiến mới", false),
        ("documentation", "Documentation", "#1D4ED8", "Tài liệu / đặc tả", false),
        ("help-wanted", "Help Wanted", "#15803D", "Cần hỗ trợ đồng đội / Mentor", false),
        ("question", "Question", "#7E22CE", "Cần làm rõ nghiệp vụ", false),
        ("priority-review", "Priority Review", "#CA8A04", "Cần GVHD xem gấp", false),
        ("duplicate", "Duplicate", "#64748B", "Trùng lặp với đầu việc khác", false),
        ("wontfix", "Won't Fix", "#475569", "Không xử lý trong phạm vi kỳ này", false),
        ("integration", "Integration", "#115E59", "Điểm giao liên ngành IT <-> MKT", true),
        ("pilot-data", "Pilot Data", "#0369A1", "Dữ liệu Pilot / thử nghiệm thị trường", true)
    ];

    private long ActorId => currentUser.IsAuthenticated && currentUser.UserId.HasValue
        ? currentUser.UserId.Value
        : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static ProjectLabelDto ToDto(ProjectLabel l) =>
        new(l.Id, l.ProjectId, l.Slug, l.Name, l.ColorHex, l.Description, l.IsSystem);

    private async Task EnsureSeededAsync(long projectId, CancellationToken ct)
    {
        if (await db.ProjectLabels.AnyAsync(l => l.ProjectId == projectId, ct)) return;
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);
        var isItMkt = project.TrackCode == AcademicTrackCodes.ItMkt
            || await db.ProjectMajors.AsNoTracking().CountAsync(pm => pm.ProjectId == projectId, ct) > 1;
        var now = Now;
        foreach (var item in DefaultLabels)
        {
            if (item.ItMktOnly && !isItMkt) continue;
            db.ProjectLabels.Add(new ProjectLabel
            {
                ProjectId = projectId,
                Slug = item.Slug,
                Name = item.Name,
                ColorHex = item.Hex,
                Description = item.Desc,
                IsSystem = true,
                CreatedBy = ActorId,
                CreatedAt = now
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task RequireLeaderOrSupervisorAsync(long projectId, CancellationToken ct)
    {
        var isLeader = await db.TeamMembers.AsNoTracking()
            .AnyAsync(m => m.Team.Project != null && m.Team.Project.Id == projectId && m.UserId == ActorId && m.IsLeader && m.LeftAt == null, ct);
        if (isLeader) return;
        var isSupervisor = await db.SupervisorAssignments.AsNoTracking()
            .AnyAsync(a => a.ProjectId == projectId && a.EndedAt == null && a.SupervisorProfile.UserId == ActorId, ct);
        if (isSupervisor) return;
        var isStaffOrAdmin = await db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == ActorId && u.Status == "ACTIVE" && u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN" || r.Role.Code == "DEPARTMENT_STAFF"), ct);
        if (isStaffOrAdmin) return;
        throw new ForbiddenException("Only the team leader or assigned supervisor can create, edit, or delete project label definitions.");
    }

    public async Task<IReadOnlyList<ProjectLabelDto>> ListAsync(long projectId, CancellationToken ct = default)
    {
        if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, ct))
            throw new NotFoundException("Project", projectId);
        await EnsureSeededAsync(projectId, ct);
        var rows = await db.ProjectLabels.AsNoTracking()
            .Where(l => l.ProjectId == projectId)
            .OrderByDescending(l => l.IsSystem).ThenBy(l => l.Slug)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToArray();
    }

    public async Task<ProjectLabelDto> CreateAsync(long projectId, SaveProjectLabelRequest request, CancellationToken ct = default)
    {
        await RequireLeaderOrSupervisorAsync(projectId, ct);
        await EnsureSeededAsync(projectId, ct);

        var slug = (request.Slug ?? "").Trim().ToLowerInvariant();
        if (!SlugRegex.IsMatch(slug))
            throw new DomainException("Label slug must be lowercase ASCII alphanumeric with hyphens (1..32 chars).", "INVALID_LABEL_SLUG");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 60)
            throw new DomainException("Label name is required (1..60 chars).", "INVALID_LABEL_NAME");
        var hex = (request.ColorHex ?? "").Trim().ToUpperInvariant();
        if (!HexRegex.IsMatch(hex))
            throw new DomainException("ColorHex must be a valid #RRGGBB hex code.", "INVALID_LABEL_COLOR");

        var currentCount = await db.ProjectLabels.CountAsync(l => l.ProjectId == projectId, ct);
        if (currentCount >= MaxLabelsPerProject)
            throw new DomainException($"A project can have at most {MaxLabelsPerProject} custom labels.", "MAX_PROJECT_LABELS_EXCEEDED");

        if (await db.ProjectLabels.AnyAsync(l => l.ProjectId == projectId && l.Slug == slug, ct))
            throw new ConflictException($"Label slug '{slug}' already exists in this project.");

        var entity = new ProjectLabel
        {
            ProjectId = projectId,
            Slug = slug,
            Name = request.Name.Trim(),
            ColorHex = hex,
            Description = request.Description?.Trim(),
            IsSystem = false,
            CreatedBy = ActorId,
            CreatedAt = Now
        };
        db.ProjectLabels.Add(entity);
        await db.SaveChangesAsync(ct);
        await auditTrail.RecordAsync(new AuditEntry(ActorId, "PROJECT_LABEL_CREATED", "PROJECT_LABEL", entity.Id,
            new Dictionary<string, object?> { ["projectId"] = projectId, ["slug"] = entity.Slug }), ct);
        return ToDto(entity);
    }

    public async Task<ProjectLabelDto> UpdateAsync(long projectId, long labelId, SaveProjectLabelRequest request, CancellationToken ct = default)
    {
        await RequireLeaderOrSupervisorAsync(projectId, ct);
        var entity = await db.ProjectLabels.SingleOrDefaultAsync(l => l.Id == labelId && l.ProjectId == projectId, ct)
            ?? throw new NotFoundException("ProjectLabel", labelId);

        var slug = (request.Slug ?? "").Trim().ToLowerInvariant();
        if (!SlugRegex.IsMatch(slug))
            throw new DomainException("Label slug must be lowercase ASCII alphanumeric with hyphens.", "INVALID_LABEL_SLUG");
        var hex = (request.ColorHex ?? "").Trim().ToUpperInvariant();
        if (!HexRegex.IsMatch(hex))
            throw new DomainException("ColorHex must be #RRGGBB.", "INVALID_LABEL_COLOR");
        if (await db.ProjectLabels.AnyAsync(l => l.ProjectId == projectId && l.Slug == slug && l.Id != labelId, ct))
            throw new ConflictException($"Label slug '{slug}' already exists in this project.");

        entity.Slug = slug;
        entity.Name = request.Name.Trim();
        entity.ColorHex = hex;
        entity.Description = request.Description?.Trim();
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task DeleteAsync(long projectId, long labelId, CancellationToken ct = default)
    {
        await RequireLeaderOrSupervisorAsync(projectId, ct);
        var entity = await db.ProjectLabels.SingleOrDefaultAsync(l => l.Id == labelId && l.ProjectId == projectId, ct)
            ?? throw new NotFoundException("ProjectLabel", labelId);
        db.ProjectLabels.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task<TaskTaxonomyStateDto> UpdateTaskTaxonomyAsync(long taskId, UpdateTaskTaxonomyRequest request, CancellationToken ct = default)
    {
        var task = await db.Tasks.Include(t => t.Milestone).Include(t => t.BoardLabels)
            .SingleOrDefaultAsync(t => t.Id == taskId && t.DeletedAt == null, ct)
            ?? throw new NotFoundException("Task", taskId);

        var projectId = task.Milestone.ProjectId;
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId, ct);
        if (project.Status != "ACTIVE")
            throw new ConflictException("Project must be ACTIVE to modify task taxonomy.", WorkflowErrorCodes.ProjectNotActive);

        // Authorization: active member, leader, or assigned supervisor
        var isMember = await db.TeamMembers.AsNoTracking().AnyAsync(m => m.TeamId == project.TeamId && m.UserId == ActorId && m.LeftAt == null, ct);
        var isSupervisor = await db.SupervisorAssignments.AsNoTracking().AnyAsync(a => a.ProjectId == projectId && a.EndedAt == null && a.SupervisorProfile.UserId == ActorId, ct);
        if (!isMember && !isSupervisor)
            throw new ForbiddenException("Only active project members or supervisors can update task taxonomy.");

        if (!string.IsNullOrWhiteSpace(request.ConcurrencyToken) &&
            (!Guid.TryParse(request.ConcurrencyToken, out var tok) || tok != task.ConcurrencyToken))
            throw new ConflictException("Task was modified concurrently.", WorkflowErrorCodes.StaleConcurrencyToken);

        // Validate TaskCategory against primaryMajorId (9 IT / 8 MKT / 5 Integration)
        if (!string.IsNullOrWhiteSpace(request.TaskCategory))
        {
            var discipline = TaskTaxonomyCatalog.ResolveCategoryDiscipline(request.TaskCategory);
            if (discipline == "UNKNOWN")
                throw new DomainException($"Unknown task category '{request.TaskCategory}'.", WorkflowErrorCodes.InvalidCategoryForMajor);

            var targetMajorId = request.PrimaryMajorId
                ?? await db.TaskDisciplines.AsNoTracking().Where(d => d.TaskId == taskId && d.Role == "PRIMARY").Select(d => (long?)d.MajorId).FirstOrDefaultAsync(ct)
                ?? await db.ProjectMajors.AsNoTracking().Where(pm => pm.ProjectId == projectId).Select(pm => (long?)pm.MajorId).FirstOrDefaultAsync(ct);

            if (targetMajorId.HasValue && discipline is "IT" or "MKT")
            {
                var major = await db.Majors.AsNoTracking().SingleOrDefaultAsync(m => m.Id == targetMajorId.Value, ct);
                if (major is not null)
                {
                    var text = $"{major.Code} {major.Name}".ToUpperInvariant();
                    var isMktMajor = text.Contains("MKT") || text.Contains("MARKETING");
                    var isItMajor = !isMktMajor && (text.Contains("IT") || text.Contains("SE") || text.Contains("SOFTWARE") || text.Contains("COMPUTER") || text.Contains("TECH"));
                    if ((discipline == "MKT" && isItMajor) || (discipline == "IT" && isMktMajor))
                    {
                        throw new DomainException(
                            $"Task category '{request.TaskCategory}' ({discipline}) does not match target major '{major.Code}'.",
                            WorkflowErrorCodes.InvalidCategoryForMajor);
                    }
                }
            }
            task.TaskCategory = request.TaskCategory.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.DisciplineSubStatus))
        {
            var sub = request.DisciplineSubStatus.Trim().ToUpperInvariant();
            if (!TaskTaxonomyCatalog.ItSubStatuses.Contains(sub) && !TaskTaxonomyCatalog.MktSubStatuses.Contains(sub))
                throw new DomainException($"Invalid discipline sub-status '{request.DisciplineSubStatus}'.", "INVALID_SUB_STATUS");
            task.DisciplineSubStatus = sub;
        }

        var now = Now;
        if (!string.IsNullOrWhiteSpace(request.HandoffFlag))
        {
            var flag = request.HandoffFlag.Trim().ToUpperInvariant();
            if (!TaskTaxonomyCatalog.HandoffFlags.Contains(flag))
                throw new DomainException($"Invalid handoff flag '{request.HandoffFlag}'.", "INVALID_HANDOFF_FLAG");
            task.HandoffFlag = flag;
            task.HandoffFlaggedAt = flag == "NONE" ? null : (task.HandoffFlaggedAt ?? now);
        }

        if (request.Labels is not null)
        {
            var distinct = request.Labels.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim().ToLowerInvariant()).Distinct().ToList();
            if (distinct.Count > MaxLabelsPerTask)
                throw new DomainException($"A task can have at most {MaxLabelsPerTask} labels.", "MAX_TASK_LABELS_EXCEEDED");
            if (distinct.Any(l => !SlugRegex.IsMatch(l)))
                throw new DomainException("Invalid label slug format.", "INVALID_LABEL_SLUG");

            var toRemove = task.BoardLabels.Where(b => !distinct.Contains(b.Label)).ToList();
            db.RemoveRange(toRemove);
            foreach (var slug in distinct.Where(s => task.BoardLabels.All(b => b.Label != s)))
                task.BoardLabels.Add(new TaskBoardLabel { TaskId = taskId, Label = slug });
        }

        task.IsUserCustomized = true;
        task.UpdatedAt = now;
        task.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);

        return new TaskTaxonomyStateDto(
            task.Id,
            task.TaskCategory ?? "UNCLASSIFIED",
            task.DisciplineSubStatus,
            task.HandoffFlag ?? "NONE",
            task.HandoffFlaggedAt,
            task.BoardLabels.Select(b => b.Label).OrderBy(x => x).ToArray(),
            task.ConcurrencyToken.ToString("N"));
    }
}
