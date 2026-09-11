using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Deliverables.Abstractions;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.Models;
using Microsoft.Extensions.Logging;

namespace AIPMS.Application.Features.Deliverables.Services;

public sealed class DeliverableWorkflow(IDeliverableRepository repository, IFileStorage storage,
    ICurrentUser currentUser, ISupervisorProfileRepository accounts, IProjectAccessService projectAccess,
    IAuditTrail audit, TimeProvider clock, ILogger<DeliverableWorkflow> logger)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private async Task<SupervisorAccount> ActorAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long id) throw new UnauthorizedException();
        var actor = await accounts.GetAccountAsync(id, ct);
        if (actor is null || !actor.IsActive) throw new ForbiddenException("The account is not active.");
        return actor;
    }

    private async Task<DeliverableProject> ProjectAsync(SupervisorAccount actor, long id, CancellationToken ct)
    {
        if ((!actor.Roles.Contains(AppRoles.Admin) && !actor.HasActiveAcademicScope)
            || !await projectAccess.CanAccessAsync(actor.UserId, id, ct))
            throw new ForbiddenException("You cannot access this project's files or deliverables.");
        return await repository.GetProjectAsync(id, actor.UserId, ct) ?? throw new NotFoundException("Project", id);
    }

    private static bool IsStudent(SupervisorAccount actor, DeliverableProject project) =>
        actor.HasActiveAcademicScope && actor.Roles.Contains(AppRoles.Student) && project.IsMember;
    private static bool IsSupervisor(SupervisorAccount actor, DeliverableProject project) =>
        actor.HasActiveAcademicScope && actor.Roles.Contains(AppRoles.Lecturer) && project.AssignmentId.HasValue;
    private static void RequirePlanner(SupervisorAccount actor, DeliverableProject project)
    {
        if (!(IsStudent(actor, project) && project.IsLeader) && !IsSupervisor(actor, project))
            throw new ForbiddenException("Only the current student leader or assigned supervisor can manage deliverables.");
    }

    private static void RequireMutable(DeliverableProject project)
    {
        if (project.Status != "ACTIVE") throw new ConflictException("Project mutations require ACTIVE status.");
    }

    private async Task RequireSubmissionWindowAsync(DeliverableProject project, DeliverableDto deliverable, CancellationToken ct)
    {
        RequireMutable(project);
        if (deliverable.Status is "ACCEPTED" or "CLOSED") throw new ConflictException("The deliverable is locked.");
        var now = Now;
        if (deliverable.DueAt.HasValue && now >= deliverable.DueAt.Value)
            throw new ConflictException("The deliverable deadline has passed.");
        if (!await repository.HasExecutionWindowAsync(project.SemesterId, now, ct))
            throw new ConflictException("One active execution window in the current semester is required.");
    }

    public async Task<DeliverableDto> GetAsync(long id, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var item = await ItemAsync(id, ct);
        await ProjectAsync(actor, item.ProjectId, ct);
        return item;
    }

    public async Task<PagedResult<DeliverableDto>> ListAsync(DeliverableSearch search, CancellationToken ct)
    {
        await ProjectAsync(await ActorAsync(ct), search.ProjectId, ct);
        return await repository.SearchAsync(search, ct);
    }

    public Task<DeliverableDto> SaveAsync(long? id, long projectId, SaveDeliverableRequest data, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var actor = await ActorAsync(token);
            var before = id.HasValue ? await ItemAsync(id.Value, token) : null;
            var actualProjectId = before?.ProjectId ?? projectId;
            await repository.LockProjectAsync(actualProjectId, token);
            var project = await ProjectAsync(actor, actualProjectId, token);
            RequireMutable(project);
            RequirePlanner(actor, project);
            if (before?.Status is "ACCEPTED" or "CLOSED") throw new ConflictException("The deliverable is locked.");
            if (data.MilestoneId.HasValue && !await repository.MilestoneBelongsAsync(data.MilestoneId.Value, actualProjectId, token))
                throw new ConflictException("The milestone must belong to this project.");
            var after = await repository.SaveAsync(id, actualProjectId, data, actor.UserId, Now, token);
            await AuditAsync(actor.UserId, before is null ? "DELIVERABLE_CREATED" : "DELIVERABLE_UPDATED", "DELIVERABLE", after.Id, before, after, token);
            return after;
        }, ct);

    public Task<bool> DeleteAsync(long id, CancellationToken ct) => repository.InTransactionAsync(async token =>
    {
        var actor = await ActorAsync(token);
        var before = await ItemAsync(id, token);
        await repository.LockProjectAsync(before.ProjectId, token);
        var project = await ProjectAsync(actor, before.ProjectId, token);
        RequireMutable(project);
        RequirePlanner(actor, project);
        if (before.LatestVersion > 0 || before.Status is not ("DRAFT" or "OPEN"))
            throw new ConflictException("Deliverables with submitted history cannot be deleted.");
        await repository.DeleteAsync(id, token);
        await AuditAsync(actor.UserId, "DELIVERABLE_DELETED", "DELIVERABLE", id, before, null, token);
        return true;
    }, ct);

    public async Task<PagedResult<DeliverableVersionDto>> VersionsAsync(long id, int page, int pageSize, CancellationToken ct)
    {
        await GetAsync(id, ct);
        return await repository.VersionsAsync(id, page, pageSize, ct);
    }

    public async Task<DeliverableVersionDto> VersionAsync(long id, CancellationToken ct)
    {
        var version = await repository.GetVersionAsync(id, ct) ?? throw new NotFoundException("DeliverableVersion", id);
        await GetAsync(version.DeliverableId, ct);
        return version;
    }

    public async Task<DeliverableVersionDto> SubmitAsync(long id, int expectedVersion, string? note, UploadContent upload, CancellationToken ct)
    {
        // Authorize before buffering the file; all mutable checks are repeated under the transaction.
        var actor = await ActorAsync(ct);
        var item = await GetAsync(id, ct);
        if (!IsStudent(actor, await ProjectAsync(actor, item.ProjectId, ct)))
            throw new ForbiddenException("Only active student team members can submit a deliverable version.");
        var file = await UploadValidator.ReadAsync(upload, ct);
        string? key = null;
        return await repository.InTransactionAsync(async token =>
        {
            actor = await ActorAsync(token);
            await repository.LockProjectAsync(item.ProjectId, token);
            item = await ItemAsync(id, token);
            var project = await ProjectAsync(actor, item.ProjectId, token);
            if (!IsStudent(actor, project)) throw new ForbiddenException("Only active student team members can submit a deliverable version.");
            if (item.LatestVersion != expectedVersion) throw new ConflictException("The latest version changed. Reload before submitting.");
            await RequireSubmissionWindowAsync(project, item, token);
            var newKey = Guid.NewGuid().ToString("N");
            using var content = new MemoryStream(file.Bytes, writable: false);
            await storage.WriteAsync(newKey, content, token);
            key = newKey;
            // A slow upload may cross the deadline while holding the project lock.
            await RequireSubmissionWindowAsync(project, item, token);
            var result = await repository.SubmitAsync(id, checked(expectedVersion + 1), actor.UserId, note?.Trim(), key, file, Now, token);
            await AuditAsync(actor.UserId, "DELIVERABLE_VERSION_SUBMITTED", "DELIVERABLE_VERSION", result.Id, null, result, token);
            return result;
        }, ct, () => CleanupAsync(key));
    }

    public Task<DeliverableFeedbackDto> ReviewAsync(long versionId, string decision, string feedback, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var actor = await ActorAsync(token);
            var version = await repository.GetVersionAsync(versionId, token) ?? throw new NotFoundException("DeliverableVersion", versionId);
            var item = await ItemAsync(version.DeliverableId, token);
            await repository.LockProjectAsync(item.ProjectId, token);
            var project = await ProjectAsync(actor, item.ProjectId, token);
            RequireMutable(project);
            if (!IsSupervisor(actor, project)) throw new ForbiddenException("Only the assigned supervisor can review this version.");
            item = await ItemAsync(item.Id, token);
            if (version.Status != "SUBMITTED" || item.Status is "ACCEPTED" or "CLOSED" || version.VersionNumber != item.LatestVersion)
                throw new ConflictException("Only the latest pending version can be reviewed.");
            var result = await repository.ReviewAsync(versionId, project.AssignmentId!.Value, actor.UserId, decision, feedback.Trim(), Now, token);
            await AuditAsync(actor.UserId, "DELIVERABLE_VERSION_REVIEWED", "DELIVERABLE_VERSION", versionId,
                new { version.Status }, new { Status = decision, Feedback = result }, token);
            return result;
        }, ct);

    public async Task<PagedResult<DeliverableFeedbackDto>> FeedbackAsync(long id, int page, int pageSize, CancellationToken ct)
    {
        await VersionAsync(id, ct);
        return await repository.FeedbackAsync(id, page, pageSize, ct);
    }

    public async Task<ProjectFileDto> FileAsync(long id, CancellationToken ct)
    {
        var stored = await ReadableFileAsync(id, ct);
        return stored.Dto;
    }

    public async Task<PagedResult<ProjectFileDto>> FilesAsync(FileSearch search, CancellationToken ct)
    {
        await ProjectAsync(await ActorAsync(ct), search.ProjectId, ct);
        return await repository.FilesAsync(search, ct);
    }

    public async Task<FileDownload> DownloadAsync(long id, CancellationToken ct)
    {
        var file = await ReadableFileAsync(id, ct);
        try { return new(await storage.OpenReadAsync(file.StorageKey, ct), file.Dto.ContentType, file.Dto.FileName); }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { throw new NotFoundException("File content", id); }
    }

    public async Task<ProjectFileDto> AttachAsync(string type, long parentId, UploadContent upload, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var parent = await ParentAsync(type, parentId, ct);
        RequireAttachmentWriter(actor, await ProjectAsync(actor, parent.ProjectId, ct), parent);
        var file = await UploadValidator.ReadAsync(upload, ct);
        string? key = null;
        return await repository.InTransactionAsync(async token =>
        {
            actor = await ActorAsync(token);
            await repository.LockProjectAsync(parent.ProjectId, token);
            parent = await ParentAsync(type, parentId, token);
            RequireAttachmentWriter(actor, await ProjectAsync(actor, parent.ProjectId, token), parent);
            var newKey = Guid.NewGuid().ToString("N");
            using var content = new MemoryStream(file.Bytes, writable: false);
            await storage.WriteAsync(newKey, content, token);
            key = newKey;
            var result = await repository.AttachAsync(parent, key, file, actor.UserId, Now, token);
            await AuditAsync(actor.UserId, "PROJECT_FILE_UPLOADED", "FILE", result.Id, null, result, token);
            return result;
        }, ct, () => CleanupAsync(key));
    }

    public async Task<bool> DeleteFileAsync(long id, CancellationToken ct)
    {
        string? key = null;
        await repository.InTransactionAsync(async token =>
        {
            var actor = await ActorAsync(token);
            var file = await ReadableFileAsync(id, token);
            await repository.LockProjectAsync(file.ProjectId, token);
            var parent = await ParentAsync(file.Dto.ParentType, file.Dto.ParentId, token);
            var project = await ProjectAsync(actor, file.ProjectId, token);
            RequireAttachmentWriter(actor, project, parent);
            if (file.Dto.UploadedBy != actor.UserId && !project.IsLeader && !IsSupervisor(actor, project))
                throw new ForbiddenException("You cannot delete another member's attachment.");
            await repository.DeleteFileAsync(id, token);
            await AuditAsync(actor.UserId, "PROJECT_FILE_DELETED", "FILE", id, file.Dto, null, token);
            key = file.StorageKey;
            return true;
        }, ct);
        // Remove content only after confirmed commit. Failure leaves an inaccessible orphan, never a broken reference.
        await CleanupAsync(key);
        return true;
    }

    private async Task<StoredProjectFile> ReadableFileAsync(long id, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var file = await repository.GetFileAsync(id, ct) ?? throw new NotFoundException("File", id);
        if (file.ParentCount != 1) throw new ConflictException("The file must have exactly one parent.");
        await ProjectAsync(actor, file.ProjectId, ct);
        return file;
    }

    private static void RequireAttachmentWriter(SupervisorAccount actor, DeliverableProject project, FileParent parent)
    {
        RequireMutable(project);
        if (parent.Type == "REPORT" && parent.Status != "DRAFT" || parent.Type == "MEETING" && parent.Status != "SCHEDULED"
            || parent.Type is not ("REPORT" or "MEETING"))
            throw new ConflictException("Submitted versions, feedback and completed records retain immutable files.");
        if (!IsStudent(actor, project) && !IsSupervisor(actor, project))
            throw new ForbiddenException("Only current project participants may manage attachments.");
        if (parent.Type == "REPORT" && !IsStudent(actor, project))
            throw new ForbiddenException("Only student team members may edit a report draft's attachments.");
    }

    private async Task<FileParent> ParentAsync(string type, long id, CancellationToken ct) =>
        await repository.GetParentAsync(type, id, ct) ?? throw new NotFoundException("File parent", id);
    private async Task<DeliverableDto> ItemAsync(long id, CancellationToken ct) =>
        await repository.GetAsync(id, ct) ?? throw new NotFoundException("Deliverable", id);
    private Task AuditAsync(long actor, string action, string entity, long id, object? before, object? after, CancellationToken ct) =>
        audit.RecordAsync(new(actor, action, entity, id, new Dictionary<string, object?> { ["before"] = before, ["after"] = after }), ct);
    private async Task CleanupAsync(string? key)
    {
        if (key is null) return;
        try { await storage.DeleteAsync(key, CancellationToken.None); }
        catch (Exception ex) { logger.LogError(ex, "Private file cleanup requires retry for object {StorageKey}", key); }
    }
}
