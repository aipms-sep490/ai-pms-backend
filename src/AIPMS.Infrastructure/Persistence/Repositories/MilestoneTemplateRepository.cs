using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.MilestoneTemplates.Abstractions;
using AIPMS.Application.Features.MilestoneTemplates.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class MilestoneTemplateRepository(AipmsDbContext db) : IMilestoneTemplateRepository
{
    public async System.Threading.Tasks.Task<IReadOnlyList<MilestoneTemplateDto>> ListAsync(CancellationToken ct) =>
        (await db.MilestoneTemplates.AsNoTracking().Include(t => t.Versions).ThenInclude(v => v.Items).OrderBy(t => t.Name).ToListAsync(ct))
        .Select(ToDto).ToArray();

    public async System.Threading.Tasks.Task<MilestoneTemplateDto> CreateAsync(string name, string? description, long actorId, DateTime now, CancellationToken ct)
    {
        var entity = new MilestoneTemplate { Name = name, Description = description, Status = "ACTIVE", CreatedBy = actorId, CreatedAt = now, UpdatedAt = now };
        db.MilestoneTemplates.Add(entity); await db.SaveChangesAsync(ct); return ToDto(await LoadAsync(entity.Id, ct));
    }
    public async System.Threading.Tasks.Task<MilestoneTemplateVersionDto> CreateVersionAsync(long templateId, long actorId, DateTime now, CancellationToken ct)
    {
        var template = await db.MilestoneTemplates.FindAsync([templateId], ct) ?? throw new NotFoundException("MilestoneTemplate", templateId);
        var max = await db.MilestoneTemplateVersions.Where(v => v.MilestoneTemplateId == templateId).Select(v => (int?)v.VersionNumber).MaxAsync(ct) ?? 0;
        var entity = new MilestoneTemplateVersion { MilestoneTemplateId = templateId, VersionNumber = max + 1, Status = "DRAFT", CreatedBy = actorId, CreatedAt = now, UpdatedAt = now };
        db.MilestoneTemplateVersions.Add(entity); await db.SaveChangesAsync(ct); return ToDto(await LoadVersionAsync(entity.Id, ct));
    }
    public async System.Threading.Tasks.Task PublishVersionAsync(long versionId, DateTime now, CancellationToken ct)
    {
        var version = await db.MilestoneTemplateVersions.Include(v => v.Items).SingleOrDefaultAsync(v => v.Id == versionId, ct) ?? throw new NotFoundException("MilestoneTemplateVersion", versionId);
        if (version.Status != "DRAFT" || version.LockedAt.HasValue || version.Items.Count == 0) throw new ConflictException("A non-empty draft version is required.");
        version.Status = "PUBLISHED"; version.UpdatedAt = now; await db.SaveChangesAsync(ct);
    }
    public async System.Threading.Tasks.Task<MilestoneTemplateVersionDto> AddItemAsync(long versionId, SaveMilestoneTemplateItemRequest request, long actorId, DateTime now, CancellationToken ct)
    {
        var version = await EditableVersionAsync(versionId, ct); Validate(request);
        db.MilestoneTemplateItems.Add(new MilestoneTemplateItem { MilestoneTemplateVersionId = version.Id, Title = request.Title.Trim(), Description = request.Description?.Trim(), StartOffsetDays = request.StartOffsetDays, DueOffsetDays = request.DueOffsetDays, SortOrder = request.SortOrder, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync(ct); return ToDto(await LoadVersionAsync(version.Id, ct));
    }
    public async System.Threading.Tasks.Task<MilestoneTemplateVersionDto> UpdateItemAsync(long itemId, SaveMilestoneTemplateItemRequest request, long actorId, DateTime now, CancellationToken ct)
    {
        Validate(request); var item = await db.MilestoneTemplateItems.Include(i => i.MilestoneTemplateVersion).SingleOrDefaultAsync(i => i.Id == itemId, ct) ?? throw new NotFoundException("MilestoneTemplateItem", itemId);
        await EnsureEditableAsync(item.MilestoneTemplateVersion, ct); item.Title = request.Title.Trim(); item.Description = request.Description?.Trim(); item.StartOffsetDays = request.StartOffsetDays; item.DueOffsetDays = request.DueOffsetDays; item.SortOrder = request.SortOrder; item.UpdatedAt = now;
        await db.SaveChangesAsync(ct); return ToDto(await LoadVersionAsync(item.MilestoneTemplateVersionId, ct));
    }
    public async System.Threading.Tasks.Task DeleteItemAsync(long itemId, CancellationToken ct)
    {
        var item = await db.MilestoneTemplateItems.Include(i => i.MilestoneTemplateVersion).SingleOrDefaultAsync(i => i.Id == itemId, ct) ?? throw new NotFoundException("MilestoneTemplateItem", itemId);
        await EnsureEditableAsync(item.MilestoneTemplateVersion, ct); db.MilestoneTemplateItems.Remove(item); await db.SaveChangesAsync(ct);
    }
    public async System.Threading.Tasks.Task AssignAsync(long periodId, long templateId, CancellationToken ct)
    {
        var template = await db.MilestoneTemplates.Include(t => t.Versions).SingleOrDefaultAsync(t => t.Id == templateId, ct) ?? throw new NotFoundException("MilestoneTemplate", templateId);
        var period = await db.ProjectPeriods.SingleOrDefaultAsync(p => p.Id == periodId, ct) ?? throw new NotFoundException("ProjectPeriod", periodId);
        var version = template.Versions.Where(v => v.Status == "PUBLISHED").OrderByDescending(v => v.VersionNumber).FirstOrDefault() ?? throw new ConflictException("The template has no published version.");
        period.MilestoneTemplateId = templateId; period.MilestoneTemplateVersionId = version.Id; version.LockedAt ??= DateTime.UtcNow;
        period.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct);
    }
    private async System.Threading.Tasks.Task<MilestoneTemplateVersion> EditableVersionAsync(long id, CancellationToken ct) { var v = await db.MilestoneTemplateVersions.SingleOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException("MilestoneTemplateVersion", id); await EnsureEditableAsync(v, ct); return v; }
    private async System.Threading.Tasks.Task EnsureEditableAsync(MilestoneTemplateVersion v, CancellationToken ct) { if (v.Status != "DRAFT" || v.LockedAt.HasValue || await db.ProjectMilestoneTemplateApplications.AnyAsync(a => a.MilestoneTemplateVersionId == v.Id, ct)) throw new ConflictException("A published or assigned template version is immutable."); }
    private static void Validate(SaveMilestoneTemplateItemRequest r) { if (string.IsNullOrWhiteSpace(r.Title)) throw new ValidationException(new Dictionary<string, string[]> { ["title"] = ["Title is required."] }); if (r.StartOffsetDays.HasValue && r.DueOffsetDays.HasValue && r.DueOffsetDays < r.StartOffsetDays) throw new ValidationException(new Dictionary<string, string[]> { ["dueOffsetDays"] = ["Due offset must not precede start offset."] }); }
    private System.Threading.Tasks.Task<MilestoneTemplate> LoadAsync(long id, CancellationToken ct) => db.MilestoneTemplates.Include(t => t.Versions).ThenInclude(v => v.Items).SingleAsync(t => t.Id == id, ct);
    private System.Threading.Tasks.Task<MilestoneTemplateVersion> LoadVersionAsync(long id, CancellationToken ct) => db.MilestoneTemplateVersions.Include(v => v.Items).SingleAsync(v => v.Id == id, ct);
    private static MilestoneTemplateDto ToDto(MilestoneTemplate t) => new(t.Id, t.Name, t.Description, t.Status, t.Versions.OrderBy(v => v.VersionNumber).Select(ToDto).ToArray());
    private static MilestoneTemplateVersionDto ToDto(MilestoneTemplateVersion v) => new(v.Id, v.VersionNumber, v.Status, v.Items.OrderBy(i => i.SortOrder).Select(i => new MilestoneTemplateItemDto(i.Id, i.Title, i.Description, i.StartOffsetDays, i.DueOffsetDays, i.SortOrder)).ToArray());
}



