using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Deliverables.Abstractions;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappings;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Db = AIPMS.Infrastructure.Persistence.Generated.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class DeliverableRepository(AipmsDbContext db, TimeProvider clock) : IDeliverableRepository
{
    public Task<bool> ProjectExistsAsync(long projectId, CancellationToken ct) => db.Projects.AnyAsync(x => x.Id == projectId, ct);
    public Task<bool> IsProjectActiveAsync(long projectId, CancellationToken ct) => db.Projects.AnyAsync(x => x.Id == projectId && x.Status == "ACTIVE", ct);
    public Task<bool> IsActiveTeamMemberAsync(long userId, long projectId, CancellationToken ct) => db.Projects.AnyAsync(x => x.Id == projectId && x.Team.TeamMembers.Any(m => m.UserId == userId && m.LeftAt == null), ct);
    public Task<bool> IsCurrentUserAssignedSupervisorAsync(long userId, long projectId, CancellationToken ct) => db.SupervisorAssignments.AnyAsync(x => x.ProjectId == projectId && x.EndedAt == null && x.SupervisorProfile.UserId == userId, ct);

    public async Task<DeliverableDto?> GetByIdAsync(long id, CancellationToken ct) => await db.Deliverables.AsNoTracking().Where(x => x.Id == id).Select(DeliverableMappings.ToDto).FirstOrDefaultAsync(ct);
    public async Task<PagedResult<DeliverableDto>> GetPagedAsync(long projectId, int page, int size, CancellationToken ct)
    {
        var q = db.Deliverables.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.CreatedAt);
        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * size).Take(size).Select(DeliverableMappings.ToDto).ToListAsync(ct);
        return new PagedResult<DeliverableDto>(items, page, size, total);
    }
    public async Task<long> CreateAsync(long projectId, long? milestoneId, string title, string? description, string? type, DateTime? dueAt, long userId, CancellationToken ct)
    {
        var now=clock.GetUtcNow().UtcDateTime;var item = new Db.Deliverable { ProjectId = projectId, MilestoneId = milestoneId, Title = title, Description = description, DeliverableType = type, DueAt = dueAt, Status = "DRAFT", CreatedBy = userId, CreatedAt = now, UpdatedAt = now };
        db.Deliverables.Add(item); await db.SaveChangesAsync(ct); return item.Id;
    }
    public async Task UpdateAsync(long id, string title, string? description, string? type, DateTime? dueAt, CancellationToken ct)
    { var x = await db.Deliverables.FindAsync([id], ct); if (x == null) return; x.Title = title; x.Description = description; x.DeliverableType = type; x.DueAt = dueAt; x.UpdatedAt = clock.GetUtcNow().UtcDateTime; await db.SaveChangesAsync(ct); }
    public async Task DeleteAsync(long id, CancellationToken ct) { var x = await db.Deliverables.FindAsync([id], ct); if (x != null) { db.Deliverables.Remove(x); await db.SaveChangesAsync(ct); } }
    public async Task<SubmissionTarget?> GetSubmissionTargetAsync(long id, CancellationToken ct) => await db.Deliverables.AsNoTracking().Where(x => x.Id == id).Select(x => new SubmissionTarget(x.Id, x.ProjectId, x.DueAt, x.Status)).FirstOrDefaultAsync(ct);
    public async Task<long?> GetActiveSupervisorAssignmentIdAsync(long id, CancellationToken ct) => await db.SupervisorAssignments.Where(x => x.ProjectId == id && x.EndedAt == null).Select(x => (long?)x.Id).FirstOrDefaultAsync(ct);
    public async Task<long> AddVersionAndFileAsync(VersionSubmission s, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        var lastNumber=await db.DeliverableVersions
            .FromSqlInterpolated($"SELECT * FROM deliverable_versions WITH (UPDLOCK,HOLDLOCK) WHERE deliverable_id={s.DeliverableId}")
            .AsNoTracking().MaxAsync(x=>(int?)x.VersionNumber,ct)??0;
        var number=lastNumber+1;
        var now=clock.GetUtcNow().UtcDateTime;
        var version = new Db.DeliverableVersion { DeliverableId=s.DeliverableId, VersionNumber=number, SubmittedBy=s.SubmittedBy, SubmissionNote=s.Note, Status="SUBMITTED", SubmittedAt=now, CreatedAt=now, UpdatedAt=now };
        db.DeliverableVersions.Add(version); await db.SaveChangesAsync(ct);
        db.Files.Add(new Db.File { UploadedBy=s.SubmittedBy, DeliverableVersionId=version.Id, OriginalFileName=s.OriginalFileName, StoredFileName=Path.GetFileName(s.StorageKey), StoragePath=s.StorageKey, MimeType=s.MimeType, FileSizeBytes=s.Size, ChecksumSha256=s.Checksum, CreatedAt=now, UpdatedAt=now });
        if(s.ReviewerAssignmentId.HasValue)await AddNotificationAsync(version.Id,s.SubmittedBy,s.ReviewerAssignmentId.Value,now,ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return version.Id;
    }
    public async Task<IReadOnlyList<DeliverableVersionDto>> GetHistoryAsync(long id, CancellationToken ct) => await db.DeliverableVersions.AsNoTracking().Where(x => x.DeliverableId == id).OrderByDescending(x=>x.VersionNumber).Select(DeliverableMappings.VersionToDto).ToListAsync(ct);
    public async Task<FileDownloadTarget?> GetFileForDownloadAsync(long id, CancellationToken ct) => await db.Files.AsNoTracking().Where(x=>x.Id==id && x.DeliverableVersionId != null).Select(x=>new FileDownloadTarget(x.Id,x.DeliverableVersion!.Deliverable.ProjectId,x.StoragePath,x.OriginalFileName,x.MimeType,true)).FirstOrDefaultAsync(ct);
    public async Task<(long ProjectId, long DeliverableId)?> GetVersionParentAsync(long id, CancellationToken ct) { var x = await db.DeliverableVersions.AsNoTracking().Where(x=>x.Id==id).Select(x=>new { x.Deliverable.ProjectId, x.DeliverableId }).FirstOrDefaultAsync(ct); return x == null ? null : (x.ProjectId, x.DeliverableId); }
    public Task DeleteUnsubmittedFileAsync(long id,CancellationToken ct)=>Task.FromException(new ConflictException("Submitted deliverable files are immutable."));
    public async Task AddFeedbackAsync(long versionId, long assignmentId, long projectId, string text, CancellationToken ct) { var now=clock.GetUtcNow().UtcDateTime;db.SupervisorFeedbacks.Add(new Db.SupervisorFeedback { DeliverableVersionId=versionId, SupervisorAssignmentId=assignmentId, ProjectId=projectId, FeedbackText=text, CreatedAt=now, UpdatedAt=now }); await db.SaveChangesAsync(ct); }
    public async Task RequestRevisionAsync(long deliverableId,long versionId,long assignmentId,long projectId,string reason,CancellationToken ct){await using var tx=await db.Database.BeginTransactionAsync(ct);var deliverable=await db.Deliverables.SingleAsync(x=>x.Id==deliverableId,ct);if(deliverable.Status is not("ACCEPTED" or "CLOSED"))throw new ConflictException("Only an accepted or closed deliverable can enter revision.");var now=clock.GetUtcNow().UtcDateTime;deliverable.Status="OPEN";deliverable.UpdatedAt=now;db.SupervisorFeedbacks.Add(new Db.SupervisorFeedback{DeliverableVersionId=versionId,SupervisorAssignmentId=assignmentId,ProjectId=projectId,FeedbackText=$"REVISION: {reason}",CreatedAt=now,UpdatedAt=now});await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
    private async Task AddNotificationAsync(long versionId,long actorId,long assignmentId,DateTime now,CancellationToken ct){var reviewer=await db.SupervisorAssignments.Where(x=>x.Id==assignmentId&&x.EndedAt==null).Select(x=>x.SupervisorProfile.UserId).SingleAsync(ct);var notification=new Db.Notification{CreatedBy=actorId,NotificationType="DELIVERABLE_SUBMITTED",Title="Deliverable submitted",Content="A deliverable version requires review.",RelatedEntityType="DeliverableVersion",RelatedEntityId=versionId,CreatedAt=now,UpdatedAt=now};db.Notifications.Add(notification);await db.SaveChangesAsync(ct);db.NotificationRecipients.Add(new Db.NotificationRecipient{NotificationId=notification.Id,UserId=reviewer,IsRead=false,CreatedAt=now,UpdatedAt=now});}
}
