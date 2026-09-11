using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Deliverables.Abstractions;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class DeliverableRepository(AipmsDbContext db, IConfiguration configuration,
    ILogger<DeliverableRepository> logger) : IDeliverableRepository
{
    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct, Func<Task>? onRollback = null)
    {
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("File workflows must own the transaction and its storage cleanup boundary.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var commitAttempted = false;
        try
        {
            var result = await action(ct);
            commitAttempted = true;
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            var rolledBack = false;
            try { await tx.RollbackAsync(CancellationToken.None); rolledBack = true; }
            catch (Exception rollbackError) { logger.LogError(rollbackError, "Could not confirm rollback of file transaction"); }
            db.ChangeTracker.Clear();
            // A lost commit acknowledgement may mean SQL committed. Never delete its object on uncertainty.
            if (!commitAttempted && rolledBack && onRollback is not null) await onRollback();
            if (commitAttempted) logger.LogError(ex, "File transaction commit outcome requires reconciliation; storage retained");
            for (Exception? cause = ex; cause is not null; cause = cause.InnerException)
                if (cause is DbUpdateConcurrencyException || cause is SqlException { Number: 1205 or 1222 or 2601 or 2627 or 547 })
                    throw new ConflictException("The deliverable, version or related record changed. Reload and retry.");
            throw;
        }
    }

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Deliverable writes require a transaction.");
    }

    public async Task LockProjectAsync(long projectId, CancellationToken ct)
    {
        RequireTransaction();
        var rows = await db.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.projects WITH (UPDLOCK, HOLDLOCK) WHERE id = {projectId}").ToListAsync(ct);
        if (rows.Count == 0) throw new NotFoundException("Project", projectId);
    }

    public Task<DeliverableProject?> GetProjectAsync(long id, long actorId, CancellationToken ct) =>
        db.Projects.AsNoTracking().Where(p => p.Id == id).Select(p => new DeliverableProject(p.Id,
            p.Team.AcademicSemesterId, p.Status,
            p.Team.TeamMembers.Any(m => m.UserId == actorId && m.LeftAt == null),
            p.Team.TeamMembers.Any(m => m.UserId == actorId && m.LeftAt == null && m.IsLeader),
            db.SupervisorAssignments.Where(a => a.ProjectId == id && a.EndedAt == null && a.SupervisorProfile.UserId == actorId)
                .Select(a => (long?)a.Id).FirstOrDefault())).SingleOrDefaultAsync(ct);

    public async Task<bool> HasExecutionWindowAsync(long semesterId, DateTime now, CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(now);
        return await db.ProjectPeriods.CountAsync(p => p.AcademicSemesterId == semesterId
            && p.PeriodType == "EXECUTION" && p.Status == "ACTIVE" && p.StartAt <= now && now < p.EndAt
            && p.AcademicSemester.Status == "ACTIVE" && p.AcademicSemester.StartDate <= day && day <= p.AcademicSemester.EndDate
            && p.AcademicSemester.Organization.IsActive, ct) == 1;
    }

    public Task<bool> MilestoneBelongsAsync(long id, long projectId, CancellationToken ct) =>
        db.Milestones.AsNoTracking().AnyAsync(m => m.Id == id && m.ProjectId == projectId, ct);
    public Task<DeliverableDto?> GetAsync(long id, CancellationToken ct) =>
        db.Deliverables.AsNoTracking().Where(d => d.Id == id).Select(DeliverableMapper.Projection).SingleOrDefaultAsync(ct);

    public async Task<PagedResult<DeliverableDto>> SearchAsync(DeliverableSearch search, CancellationToken ct)
    {
        var query = db.Deliverables.AsNoTracking().Where(d => d.ProjectId == search.ProjectId);
        if (search.Status is not null) query = query.Where(d => d.Status == search.Status);
        if (search.MilestoneId.HasValue) query = query.Where(d => d.MilestoneId == search.MilestoneId);
        if (!string.IsNullOrWhiteSpace(search.DeliverableType)) query = query.Where(d => d.DeliverableType == search.DeliverableType.Trim());
        if (!string.IsNullOrWhiteSpace(search.Search)) query = query.Where(d => d.Title.Contains(search.Search.Trim()));
        var count = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(d => d.Id).Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize).Select(DeliverableMapper.Projection).ToListAsync(ct);
        return new(items, search.Page, search.PageSize, count);
    }

    public async Task<DeliverableDto> SaveAsync(long? id, long projectId, SaveDeliverableRequest data, long actorId, DateTime now, CancellationToken ct)
    {
        RequireTransaction();
        var item = id.HasValue ? await db.Deliverables.SingleAsync(d => d.Id == id, ct)
            : new M.Deliverable { ProjectId = projectId, CreatedBy = actorId, CreatedAt = now, Status = "OPEN" };
        item.MilestoneId = data.MilestoneId;
        item.Title = data.Title.Trim();
        item.Description = data.Description?.Trim();
        item.DeliverableType = data.DeliverableType?.Trim();
        item.DueAt = data.DueAt;
        item.UpdatedAt = now;
        if (!id.HasValue) db.Deliverables.Add(item);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(item.Id, ct))!;
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        RequireTransaction();
        db.Deliverables.Remove(await db.Deliverables.SingleAsync(d => d.Id == id, ct));
        await db.SaveChangesAsync(ct);
    }

    public async Task<DeliverableVersionDto?> GetVersionAsync(long id, CancellationToken ct) =>
        (await db.DeliverableVersions.AsNoTracking().Include(v => v.Files).SingleOrDefaultAsync(v => v.Id == id, ct))?.ToDto();

    public async Task<PagedResult<DeliverableVersionDto>> VersionsAsync(long id, int page, int pageSize, CancellationToken ct)
    {
        var query = db.DeliverableVersions.AsNoTracking().Where(v => v.DeliverableId == id);
        var count = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(v => v.VersionNumber).Skip((page - 1) * pageSize).Take(pageSize).Include(v => v.Files).ToListAsync(ct);
        return new(items.Select(v => v.ToDto()).ToArray(), page, pageSize, count);
    }

    public async Task<DeliverableVersionDto> SubmitAsync(long id, int number, long actorId, string? note,
        string storageKey, ValidatedUpload file, DateTime now, CancellationToken ct)
    {
        RequireTransaction();
        var item = await db.Deliverables.SingleAsync(d => d.Id == id, ct);
        var version = new M.DeliverableVersion { DeliverableId = id, VersionNumber = number, SubmittedBy = actorId,
            SubmissionNote = note, SubmittedAt = now, CreatedAt = now, UpdatedAt = now, Status = "SUBMITTED" };
        var metadata = NewFile(storageKey, file, actorId, now);
        version.Files.Add(metadata);
        db.DeliverableVersions.Add(version);
        item.Status = "SUBMITTED";
        item.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        if (!bool.TryParse(configuration["Deliverables:NotifyReviewer"], out var notify) || notify)
        {
            var reviewers = await db.SupervisorAssignments.Where(a => a.ProjectId == item.ProjectId && a.EndedAt == null
                && a.SupervisorProfile.User.Status == "ACTIVE"
                && a.SupervisorProfile.User.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Lecturer))
                .Select(a => a.SupervisorProfile.UserId).Distinct().ToListAsync(ct);
            if (reviewers.Count > 0)
            {
                db.Notifications.Add(new() { CreatedBy = actorId, NotificationType = "DELIVERABLE_SUBMITTED",
                    Title = "Deliverable version submitted", Content = $"A new version of deliverable {id} is ready for review.",
                    RelatedEntityType = "DELIVERABLE_VERSION", RelatedEntityId = version.Id, CreatedAt = now, UpdatedAt = now,
                    NotificationRecipients = reviewers.Select(user => new M.NotificationRecipient { UserId = user, CreatedAt = now, UpdatedAt = now }).ToArray() });
                await db.SaveChangesAsync(ct);
            }
        }
        return (await GetVersionAsync(version.Id, ct))!;
    }

    public async Task<DeliverableFeedbackDto> ReviewAsync(long versionId, long assignmentId, long actorId,
        string decision, string feedback, DateTime now, CancellationToken ct)
    {
        RequireTransaction();
        var version = await db.DeliverableVersions.Include(v => v.Deliverable).SingleAsync(v => v.Id == versionId, ct);
        version.Status = decision;
        version.UpdatedAt = now;
        version.Deliverable.Status = decision;
        version.Deliverable.UpdatedAt = now;
        var result = new M.SupervisorFeedback { ProjectId = version.Deliverable.ProjectId, SupervisorAssignmentId = assignmentId,
            DeliverableVersionId = versionId, FeedbackText = feedback, CreatedAt = now, UpdatedAt = now };
        db.SupervisorFeedbacks.Add(result);
        await db.SaveChangesAsync(ct);
        return new(result.Id, result.ProjectId, versionId, assignmentId, actorId, result.FeedbackText, now);
    }

    public async Task<PagedResult<DeliverableFeedbackDto>> FeedbackAsync(long id, int page, int pageSize, CancellationToken ct)
    {
        var query = db.SupervisorFeedbacks.AsNoTracking().Where(f => f.DeliverableVersionId == id);
        var count = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(f => f.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(DeliverableMapper.FeedbackProjection).ToListAsync(ct);
        return new(items, page, pageSize, count);
    }

    public Task<FileParent?> GetParentAsync(string type, long id, CancellationToken ct) => type switch
    {
        "VERSION" => db.DeliverableVersions.AsNoTracking().Where(v => v.Id == id).Select(v => new FileParent(type, id, v.Deliverable.ProjectId, v.Status, v.SubmittedBy)).SingleOrDefaultAsync(ct),
        "REPORT" => db.ProgressReports.AsNoTracking().Where(r => r.Id == id).Select(r => new FileParent(type, id, r.ProjectId, r.Status, r.SubmittedBy)).SingleOrDefaultAsync(ct),
        "MEETING" => db.Meetings.AsNoTracking().Where(m => m.Id == id).Select(m => new FileParent(type, id, m.ProjectId, m.Status, m.CreatedBy)).SingleOrDefaultAsync(ct),
        "FEEDBACK" => db.SupervisorFeedbacks.AsNoTracking().Where(f => f.Id == id).Select(f => new FileParent(type, id, f.ProjectId, "LOCKED", f.SupervisorAssignment.SupervisorProfile.UserId)).SingleOrDefaultAsync(ct),
        _ => Task.FromResult<FileParent?>(null)
    };

    public async Task<StoredProjectFile?> GetFileAsync(long id, CancellationToken ct)
    {
        var file = await db.Files.AsNoTracking().Include(f => f.DeliverableVersion).ThenInclude(v => v!.Deliverable)
            .Include(f => f.ProgressReport).Include(f => f.Meeting).Include(f => f.SupervisorFeedback).SingleOrDefaultAsync(f => f.Id == id, ct);
        if (file is null) return null;
        return new(file.ToDto(), file.StoragePath,
            file.DeliverableVersion?.Deliverable.ProjectId ?? file.ProgressReport?.ProjectId ?? file.Meeting?.ProjectId ?? file.SupervisorFeedback?.ProjectId ?? 0,
            new[] { file.DeliverableVersionId, file.ProgressReportId, file.MeetingId, file.SupervisorFeedbackId }.Count(x => x.HasValue));
    }

    public async Task<PagedResult<ProjectFileDto>> FilesAsync(FileSearch search, CancellationToken ct)
    {
        var query = db.Files.AsNoTracking().Where(f =>
            (f.DeliverableVersion != null && f.DeliverableVersion.Deliverable.ProjectId == search.ProjectId)
            || (f.ProgressReport != null && f.ProgressReport.ProjectId == search.ProjectId)
            || (f.Meeting != null && f.Meeting.ProjectId == search.ProjectId)
            || (f.SupervisorFeedback != null && f.SupervisorFeedback.ProjectId == search.ProjectId));
        if (!string.IsNullOrWhiteSpace(search.Search)) query = query.Where(f => f.OriginalFileName.Contains(search.Search.Trim()));
        if (!string.IsNullOrWhiteSpace(search.ContentType)) query = query.Where(f => f.MimeType == search.ContentType.Trim());
        if (search.UploadedBy.HasValue) query = query.Where(f => f.UploadedBy == search.UploadedBy);
        if (search.From.HasValue) query = query.Where(f => f.CreatedAt >= search.From);
        if (search.To.HasValue) query = query.Where(f => f.CreatedAt < search.To);
        query = search.ParentType switch
        {
            "VERSION" => query.Where(f => f.DeliverableVersionId != null && (!search.ParentId.HasValue || f.DeliverableVersionId == search.ParentId)),
            "REPORT" => query.Where(f => f.ProgressReportId != null && (!search.ParentId.HasValue || f.ProgressReportId == search.ParentId)),
            "MEETING" => query.Where(f => f.MeetingId != null && (!search.ParentId.HasValue || f.MeetingId == search.ParentId)),
            "FEEDBACK" => query.Where(f => f.SupervisorFeedbackId != null && (!search.ParentId.HasValue || f.SupervisorFeedbackId == search.ParentId)),
            _ => query
        };
        var count = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(f => f.Id).Skip((search.Page - 1) * search.PageSize).Take(search.PageSize).Select(DeliverableMapper.FileProjection).ToListAsync(ct);
        return new(items, search.Page, search.PageSize, count);
    }

    public async Task<ProjectFileDto> AttachAsync(FileParent parent, string key, ValidatedUpload file, long actorId, DateTime now, CancellationToken ct)
    {
        RequireTransaction();
        var result = NewFile(key, file, actorId, now);
        if (parent.Type == "REPORT") result.ProgressReportId = parent.Id;
        else if (parent.Type == "MEETING") result.MeetingId = parent.Id;
        else throw new InvalidOperationException("Standalone uploads require an editable report or meeting parent.");
        db.Files.Add(result);
        await db.SaveChangesAsync(ct);
        return result.ToDto();
    }

    public async Task DeleteFileAsync(long id, CancellationToken ct)
    {
        RequireTransaction();
        db.Files.Remove(await db.Files.SingleAsync(f => f.Id == id, ct));
        await db.SaveChangesAsync(ct);
    }

    private static M.File NewFile(string key, ValidatedUpload file, long actor, DateTime now) => new()
    {
        UploadedBy = actor, OriginalFileName = file.FileName, StoredFileName = key, StoragePath = key,
        MimeType = file.ContentType, FileSizeBytes = file.Bytes.LongLength, ChecksumSha256 = file.Sha256, CreatedAt = now, UpdatedAt = now
    };
}
