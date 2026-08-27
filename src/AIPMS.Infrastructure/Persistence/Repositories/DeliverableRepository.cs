using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.Abstractions;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using Db = AIPMS.Infrastructure.Persistence.Generated.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class DeliverableRepository(AipmsDbContext db) : IDeliverableRepository
{
    public Task<bool> ProjectExistsAsync(long projectId, CancellationToken ct) => db.Projects.AnyAsync(x => x.Id == projectId, ct);
    public Task<bool> IsProjectActiveAsync(long projectId, CancellationToken ct) => db.Projects.AnyAsync(x => x.Id == projectId && x.Status == "ACTIVE", ct);
    public Task<bool> IsActiveTeamMemberAsync(long userId, long projectId, CancellationToken ct) => db.Projects.AnyAsync(x => x.Id == projectId && x.Team.TeamMembers.Any(m => m.UserId == userId && m.LeftAt == null), ct);
    public Task<bool> IsCurrentUserAssignedSupervisorAsync(long userId, long projectId, CancellationToken ct) => db.SupervisorAssignments.AnyAsync(x => x.ProjectId == projectId && x.EndedAt == null && x.SupervisorProfile.UserId == userId, ct);

    public async Task<DeliverableDto?> GetByIdAsync(long id, CancellationToken ct) => await db.Deliverables.AsNoTracking().Where(x => x.Id == id).Select(Map).FirstOrDefaultAsync(ct);
    public async Task<PagedResult<DeliverableDto>> GetPagedAsync(long projectId, int page, int size, CancellationToken ct)
    {
        var q = db.Deliverables.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.CreatedAt);
        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * size).Take(size).Select(Map).ToListAsync(ct);
        return new PagedResult<DeliverableDto>(items, page, size, total);
    }
    public async Task<long> CreateAsync(long projectId, long? milestoneId, string title, string? description, string? type, DateTime? dueAt, long userId, CancellationToken ct)
    {
        var item = new Db.Deliverable { ProjectId = projectId, MilestoneId = milestoneId, Title = title, Description = description, DeliverableType = type, DueAt = dueAt, Status = "DRAFT", CreatedBy = userId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Deliverables.Add(item); await db.SaveChangesAsync(ct); return item.Id;
    }
    public async Task UpdateAsync(long id, string title, string? description, string? type, DateTime? dueAt, CancellationToken ct)
    { var x = await db.Deliverables.FindAsync([id], ct); if (x == null) return; x.Title = title; x.Description = description; x.DeliverableType = type; x.DueAt = dueAt; x.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
    public async Task DeleteAsync(long id, CancellationToken ct) { var x = await db.Deliverables.FindAsync([id], ct); if (x != null) { db.Deliverables.Remove(x); await db.SaveChangesAsync(ct); } }
    public async Task<SubmissionTarget?> GetSubmissionTargetAsync(long id, CancellationToken ct) => await db.Deliverables.AsNoTracking().Where(x => x.Id == id).Select(x => new SubmissionTarget(x.Id, x.ProjectId, x.DueAt, x.Status)).FirstOrDefaultAsync(ct);
    public async Task<int> GetNextVersionNumberAsync(long id, CancellationToken ct) => (await db.DeliverableVersions.Where(x => x.DeliverableId == id).MaxAsync(x => (int?)x.VersionNumber, ct) ?? 0) + 1;
    public async Task<long?> GetActiveSupervisorAssignmentIdAsync(long id, CancellationToken ct) => await db.SupervisorAssignments.Where(x => x.ProjectId == id && x.EndedAt == null).Select(x => (long?)x.Id).FirstOrDefaultAsync(ct);
    public async Task<long> AddVersionAndFileAsync(VersionSubmission s, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var version = new Db.DeliverableVersion { DeliverableId=s.DeliverableId, VersionNumber=s.VersionNumber, SubmittedBy=s.SubmittedBy, SubmissionNote=s.Note, Status="SUBMITTED", SubmittedAt=DateTime.UtcNow, CreatedAt=DateTime.UtcNow, UpdatedAt=DateTime.UtcNow };
        db.DeliverableVersions.Add(version); await db.SaveChangesAsync(ct);
        db.Files.Add(new Db.File { UploadedBy=s.SubmittedBy, DeliverableVersionId=version.Id, OriginalFileName=s.OriginalFileName, StoredFileName=Path.GetFileName(s.StorageKey), StoragePath=s.StorageKey, MimeType=s.MimeType, FileSizeBytes=s.Size, ChecksumSha256=s.Checksum, CreatedAt=DateTime.UtcNow, UpdatedAt=DateTime.UtcNow });
        if (s.ReviewerAssignmentId.HasValue) { var a = await db.SupervisorAssignments.Include(x => x.SupervisorProfile).FirstAsync(x => x.Id == s.ReviewerAssignmentId, ct); var n = new Db.Notification { CreatedBy=s.SubmittedBy, NotificationType="DELIVERABLE_SUBMITTED", Title="Deliverable submitted", Content="A deliverable version requires review.", RelatedEntityType="DeliverableVersion", RelatedEntityId=version.Id, CreatedAt=DateTime.UtcNow, UpdatedAt=DateTime.UtcNow }; db.Notifications.Add(n); await db.SaveChangesAsync(ct); db.NotificationRecipients.Add(new Db.NotificationRecipient { NotificationId=n.Id, UserId=a.SupervisorProfile.UserId, IsRead=false, CreatedAt=DateTime.UtcNow, UpdatedAt=DateTime.UtcNow }); }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return version.Id;
    }
    public async Task<IReadOnlyList<DeliverableVersionDto>> GetHistoryAsync(long id, CancellationToken ct) => await db.DeliverableVersions.AsNoTracking().Where(x => x.DeliverableId == id).OrderByDescending(x=>x.VersionNumber).Select(x => new DeliverableVersionDto(x.Id,x.DeliverableId,x.VersionNumber,x.SubmittedBy,x.SubmissionNote,x.Status,x.SubmittedAt,x.Files.Select(f=>new FileMetadataDto(f.Id,f.OriginalFileName,f.MimeType,f.FileSizeBytes,f.ChecksumSha256,f.CreatedAt,f.UploadedBy)).ToList(),x.SupervisorFeedbacks.Select(f=>new SupervisorFeedbackDto(f.Id,f.SupervisorAssignmentId,f.FeedbackText,f.CreatedAt)).ToList())).ToListAsync(ct);
    public async Task<FileDownloadTarget?> GetFileForDownloadAsync(long id, CancellationToken ct) => await db.Files.AsNoTracking().Where(x=>x.Id==id && x.DeliverableVersionId != null).Select(x=>new FileDownloadTarget(x.Id,x.DeliverableVersion!.Deliverable.ProjectId,x.StoragePath,x.OriginalFileName,x.MimeType,true)).FirstOrDefaultAsync(ct);
    public async Task<(long ProjectId, long DeliverableId)?> GetVersionParentAsync(long id, CancellationToken ct) { var x = await db.DeliverableVersions.AsNoTracking().Where(x=>x.Id==id).Select(x=>new { x.Deliverable.ProjectId, x.DeliverableId }).FirstOrDefaultAsync(ct); return x == null ? null : (x.ProjectId, x.DeliverableId); }
    public Task DeleteUnsubmittedFileAsync(long id, CancellationToken ct) => Task.FromException(new InvalidOperationException("Submitted deliverable files are immutable."));
    public async Task AddFeedbackAsync(long versionId, long assignmentId, long projectId, string text, CancellationToken ct) { db.SupervisorFeedbacks.Add(new Db.SupervisorFeedback { DeliverableVersionId=versionId, SupervisorAssignmentId=assignmentId, ProjectId=projectId, FeedbackText=text, CreatedAt=DateTime.UtcNow, UpdatedAt=DateTime.UtcNow }); await db.SaveChangesAsync(ct); }
    private static readonly System.Linq.Expressions.Expression<Func<Db.Deliverable, DeliverableDto>> Map = x => new DeliverableDto(x.Id,x.ProjectId,x.MilestoneId,x.Title,x.Description,x.DeliverableType,x.DueAt,x.Status,x.CreatedBy,x.CreatedAt);
}
