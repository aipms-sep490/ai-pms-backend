using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.MilestoneTemplates.Abstractions;
using AIPMS.Application.Features.MilestoneTemplates.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Threading.Tasks;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class MilestoneTemplateRepository(AipmsDbContext db, IAuditTrail audit) : IMilestoneTemplateRepository
{
    public async Task<IReadOnlyList<MilestoneTemplateDto>> ListAsync(CancellationToken ct) =>
        (await db.MilestoneTemplates.AsNoTracking().Include(t => t.Versions).ThenInclude(v => v.Items)
            .OrderBy(t => t.Name).ToListAsync(ct)).Select(ToDto).ToArray();

    public async Task<MilestoneTemplateDto> CreateAsync(string name, string? description, long actorId, DateTime now, CancellationToken ct)
    {
        ValidateTemplate(name);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await MilestoneTemplateLock.AcquireAsync(db, ct);
        await EnsureAdminAsync(actorId, ct);
        var entity = new M.MilestoneTemplate { Name = name.Trim(), Description = description?.Trim(), Status = "ACTIVE", CreatedBy = actorId, CreatedAt = now, UpdatedAt = now };
        db.MilestoneTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(actorId, "MILESTONE_TEMPLATE_CREATED", "MILESTONE_TEMPLATE", entity.Id, new Dictionary<string, object?> { ["name"] = entity.Name }), ct);
        await tx.CommitAsync(ct);
        return ToDto(await LoadAsync(entity.Id, ct));
    }

    public async Task UpdateAsync(long templateId, string name, string? description, long actorId, DateTime now, CancellationToken ct)
    {
        ValidateTemplate(name);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await MilestoneTemplateLock.AcquireAsync(db, ct);
        await EnsureAdminAsync(actorId, ct);
        var template = await LoadTemplateForUpdateAsync(templateId, ct);
        if (await db.MilestoneTemplateVersions.AnyAsync(v => v.MilestoneTemplateId == templateId && v.LockedAt != null, ct) ||
            await db.ProjectPeriods.AnyAsync(p => p.MilestoneTemplateId == templateId, ct) ||
            await db.ProjectMilestoneTemplateApplications.AnyAsync(a => a.MilestoneTemplateVersion.MilestoneTemplateId == templateId, ct))
            throw new ConflictException("An assigned template cannot be edited.");
        template.Name = name.Trim();
        template.Description = description?.Trim();
        template.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(actorId, "MILESTONE_TEMPLATE_UPDATED", "MILESTONE_TEMPLATE", templateId, new Dictionary<string, object?>()), ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(long templateId, long actorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await MilestoneTemplateLock.AcquireAsync(db, ct);
        await EnsureAdminAsync(actorId, ct);
        var template = await LoadTemplateForUpdateAsync(templateId, ct);
        if (await db.MilestoneTemplateVersions.AnyAsync(v => v.MilestoneTemplateId == templateId && v.LockedAt != null, ct) ||
            await db.ProjectPeriods.AnyAsync(p => p.MilestoneTemplateId == templateId, ct) ||
            await db.ProjectMilestoneTemplateApplications.AnyAsync(a => a.MilestoneTemplateVersion.MilestoneTemplateId == templateId, ct))
            throw new ConflictException("An assigned template cannot be deleted.");
        var versions = await db.MilestoneTemplateVersions.Where(v => v.MilestoneTemplateId == templateId).ToListAsync(ct);
        db.MilestoneTemplateVersions.RemoveRange(versions);
        db.MilestoneTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(actorId, "MILESTONE_TEMPLATE_DELETED", "MILESTONE_TEMPLATE", templateId, new Dictionary<string, object?>()), ct);
        await tx.CommitAsync(ct);
    }

    public async Task<MilestoneTemplateVersionDto> CreateVersionAsync(long templateId, long actorId, DateTime now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await MilestoneTemplateLock.AcquireAsync(db, ct);
        await EnsureAdminAsync(actorId, ct);
        var template = await LoadTemplateForUpdateAsync(templateId, ct);
        if (template.Status != "ACTIVE") throw new ConflictException("Only active templates can receive a new version.");
        var max = await db.MilestoneTemplateVersions.Where(v => v.MilestoneTemplateId == templateId)
            .Select(v => (int?)v.VersionNumber).MaxAsync(ct) ?? 0;
        var entity = new M.MilestoneTemplateVersion { MilestoneTemplateId = templateId, VersionNumber = max + 1, Status = "DRAFT", CreatedBy = actorId, CreatedAt = now, UpdatedAt = now };
        db.MilestoneTemplateVersions.Add(entity);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(actorId, "MILESTONE_TEMPLATE_VERSION_CREATED", "MILESTONE_TEMPLATE_VERSION", entity.Id, new Dictionary<string, object?> { ["templateId"] = templateId, ["version"] = entity.VersionNumber }), ct);
        await tx.CommitAsync(ct);
        return ToDto(await LoadVersionAsync(entity.Id, ct));
    }

    public async Task PublishVersionAsync(long versionId, long actorId, DateTime now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await MilestoneTemplateLock.AcquireAsync(db, ct);
        await EnsureAdminAsync(actorId, ct);
        var version = await db.MilestoneTemplateVersions.Include(v => v.Items).Include(v => v.MilestoneTemplate)
            .SingleOrDefaultAsync(v => v.Id == versionId, ct) ?? throw new NotFoundException("MilestoneTemplateVersion", versionId);
        if (version.MilestoneTemplate.Status != "ACTIVE" || version.Status != "DRAFT" || version.LockedAt.HasValue || version.Items.Count == 0)
            throw new ConflictException("A non-empty draft version of an active template is required.");
        version.Status = "PUBLISHED";
        version.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(actorId, "MILESTONE_TEMPLATE_VERSION_PUBLISHED", "MILESTONE_TEMPLATE_VERSION", versionId, new Dictionary<string, object?>()), ct);
        await tx.CommitAsync(ct);
    }

    public async Task<MilestoneTemplateVersionDto> AddItemAsync(long versionId, SaveMilestoneTemplateItemRequest request, long actorId, DateTime now, CancellationToken ct)
    {
        Validate(request);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await MilestoneTemplateLock.AcquireAsync(db, ct);
        await EnsureAdminAsync(actorId, ct);
        var version = await EditableVersionAsync(versionId, ct);
        db.MilestoneTemplateItems.Add(new M.MilestoneTemplateItem { MilestoneTemplateVersionId = version.Id, Title = request.Title.Trim(), Description = request.Description?.Trim(), StartOffsetDays = request.StartOffsetDays, DueOffsetDays = request.DueOffsetDays, SortOrder = request.SortOrder, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(actorId, "MILESTONE_TEMPLATE_ITEM_CREATED", "MILESTONE_TEMPLATE_VERSION", versionId, new Dictionary<string, object?>()), ct);
        await tx.CommitAsync(ct);
        return ToDto(await LoadVersionAsync(version.Id, ct));
    }

    public async Task<MilestoneTemplateVersionDto> UpdateItemAsync(long itemId, SaveMilestoneTemplateItemRequest request, long actorId, DateTime now, CancellationToken ct)
    {
        Validate(request);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await MilestoneTemplateLock.AcquireAsync(db, ct);
        await EnsureAdminAsync(actorId, ct);
        var item = await db.MilestoneTemplateItems.Include(i => i.MilestoneTemplateVersion).ThenInclude(v => v.MilestoneTemplate).SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new NotFoundException("MilestoneTemplateItem", itemId);
        await EnsureEditableAsync(item.MilestoneTemplateVersion, ct);
        item.Title = request.Title.Trim(); item.Description = request.Description?.Trim(); item.StartOffsetDays = request.StartOffsetDays; item.DueOffsetDays = request.DueOffsetDays; item.SortOrder = request.SortOrder; item.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(actorId, "MILESTONE_TEMPLATE_ITEM_UPDATED", "MILESTONE_TEMPLATE_ITEM", itemId, new Dictionary<string, object?>()), ct);
        await tx.CommitAsync(ct);
        return ToDto(await LoadVersionAsync(item.MilestoneTemplateVersionId, ct));
    }

    public async Task DeleteItemAsync(long itemId, long actorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await MilestoneTemplateLock.AcquireAsync(db, ct);
        await EnsureAdminAsync(actorId, ct);
        var item = await db.MilestoneTemplateItems.Include(i => i.MilestoneTemplateVersion).ThenInclude(v => v.MilestoneTemplate).SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new NotFoundException("MilestoneTemplateItem", itemId);
        await EnsureEditableAsync(item.MilestoneTemplateVersion, ct);
        db.MilestoneTemplateItems.Remove(item);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(actorId, "MILESTONE_TEMPLATE_ITEM_DELETED", "MILESTONE_TEMPLATE_ITEM", itemId, new Dictionary<string, object?>()), ct);
        await tx.CommitAsync(ct);
    }

    public async Task AssignAsync(long periodId, long templateId, long? versionId, long actorId, DateTime now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await MilestoneTemplateLock.AcquireAsync(db, ct);
        await EnsureAdminAsync(actorId, ct);
        var template = await LoadTemplateForUpdateAsync(templateId, ct);
        if (template.Status != "ACTIVE") throw new ConflictException("Only active templates can be assigned.");
        var period = await db.ProjectPeriods.Include(p => p.AcademicSemester).SingleOrDefaultAsync(p => p.Id == periodId, ct) ?? throw new NotFoundException("ProjectPeriod", periodId);
        if (period.Status is "CLOSED" or "ARCHIVED" || period.AcademicSemester.Status is "CLOSED" or "ARCHIVED" || period.EndAt <= now) throw new ConflictException("The project period is no longer assignable.");
        var version = versionId.HasValue
            ? await db.MilestoneTemplateVersions.SingleOrDefaultAsync(v => v.Id == versionId && v.MilestoneTemplateId == templateId, ct)
            : await db.MilestoneTemplateVersions.Where(v => v.MilestoneTemplateId == templateId && v.Status == "PUBLISHED").OrderByDescending(v => v.VersionNumber).FirstOrDefaultAsync(ct);
        if (version is null || version.Status != "PUBLISHED") throw new ConflictException("The selected template version must be published.");
        period.MilestoneTemplateId = templateId; period.MilestoneTemplateVersionId = version.Id; period.UpdatedAt = now; version.LockedAt ??= now;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(actorId, "MILESTONE_TEMPLATE_ASSIGNED", "PROJECT_PERIOD", periodId, new Dictionary<string, object?> { ["templateId"] = templateId, ["versionId"] = version.Id }), ct);
        await tx.CommitAsync(ct);
    }

    private async Task<M.MilestoneTemplate> LoadTemplateForUpdateAsync(long id, CancellationToken ct) =>
        await db.MilestoneTemplates.FromSqlInterpolated($"SELECT * FROM dbo.milestone_templates WITH (UPDLOCK, HOLDLOCK) WHERE id = {id}").SingleOrDefaultAsync(ct)
        ?? throw new NotFoundException("MilestoneTemplate", id);

    private async Task<M.MilestoneTemplateVersion> EditableVersionAsync(long id, CancellationToken ct)
    {
        var v = await db.MilestoneTemplateVersions.Include(v => v.MilestoneTemplate).SingleOrDefaultAsync(v => v.Id == id, ct)
            ?? throw new NotFoundException("MilestoneTemplateVersion", id);
        await EnsureEditableAsync(v, ct);
        return v;
    }

    private async Task EnsureEditableAsync(M.MilestoneTemplateVersion v, CancellationToken ct)
    {
        if (v.MilestoneTemplate.Status != "ACTIVE" || v.Status != "DRAFT" || v.LockedAt.HasValue ||
            await db.ProjectMilestoneTemplateApplications.AnyAsync(a => a.MilestoneTemplateVersionId == v.Id, ct))
            throw new ConflictException("A published or assigned template version is immutable.");
    }

    private async Task EnsureAdminAsync(long actorId, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == actorId && u.Status == "ACTIVE" && u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"), ct))
            throw new ForbiddenException("An active administrator is required.");
    }

    private static void ValidateTemplate(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ValidationException(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
        if (name.Trim().Length > 200) throw new ValidationException(new Dictionary<string, string[]> { ["name"] = ["Name is too long."] });
    }

    private static void Validate(SaveMilestoneTemplateItemRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Title)) throw new ValidationException(new Dictionary<string, string[]> { ["title"] = ["Title is required."] });
        if (r.Title.Trim().Length > 255) throw new ValidationException(new Dictionary<string, string[]> { ["title"] = ["Title is too long."] });
        if (r.SortOrder < 0 || r.StartOffsetDays is < -36500 or > 36500 || r.DueOffsetDays is < -36500 or > 36500)
            throw new ValidationException(new Dictionary<string, string[]> { ["offsets"] = ["Offsets must be within 100 years and sort order must be nonnegative."] });
        if (r.StartOffsetDays.HasValue && r.DueOffsetDays.HasValue && r.DueOffsetDays < r.StartOffsetDays)
            throw new ValidationException(new Dictionary<string, string[]> { ["dueOffsetDays"] = ["Due offset must not precede start offset."] });
    }

    private Task<M.MilestoneTemplate> LoadAsync(long id, CancellationToken ct) => db.MilestoneTemplates.AsNoTracking().Include(t => t.Versions).ThenInclude(v => v.Items).SingleAsync(t => t.Id == id, ct);
    private Task<M.MilestoneTemplateVersion> LoadVersionAsync(long id, CancellationToken ct) => db.MilestoneTemplateVersions.AsNoTracking().Include(v => v.Items).SingleAsync(v => v.Id == id, ct);
    private static MilestoneTemplateDto ToDto(M.MilestoneTemplate t) => new(t.Id, t.Name, t.Description, t.Status, t.Versions.OrderBy(v => v.VersionNumber).Select(ToDto).ToArray());
    private static MilestoneTemplateVersionDto ToDto(M.MilestoneTemplateVersion v) => new(v.Id, v.VersionNumber, v.Status, v.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => new MilestoneTemplateItemDto(i.Id, i.Title, i.Description, i.StartOffsetDays, i.DueOffsetDays, i.SortOrder)).ToArray());
}



