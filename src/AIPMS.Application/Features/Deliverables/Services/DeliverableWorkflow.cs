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
using AIPMS.Application.Features.Notifications.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace AIPMS.Application.Features.Deliverables.Services;

public sealed class DeliverableWorkflow(IDeliverableRepository repository, IFileStorage storage,
    ICurrentUser currentUser, ISupervisorProfileRepository accounts, IProjectAccessService projectAccess,
    IAuditTrail audit, TimeProvider clock, ILogger<DeliverableWorkflow> logger, IPublisher publisher,
    IFileSecurityScanner scanner)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task ScanOrQuarantineAsync(long actorId, Stream stream, string fileName, string contentType, string flow, CancellationToken ct)
    {
        if (stream.CanSeek) stream.Position = 0;
        var scan = await scanner.ScanAsync(stream, fileName, contentType, ct);
        if (stream.CanSeek) stream.Position = 0;
        if (!scan.IsSafe)
        {
            await audit.RecordAsync(new AuditEntry(actorId, "FILE_SCAN_QUARANTINED", "FILE", null,
                new Dictionary<string, object?> { ["flow"] = flow, ["provider"] = scan.Provider, ["reason"] = scan.Details, ["fileName"] = fileName, ["verdict"] = "QUARANTINED" },
                Outcome: "DENIED"), ct);
            throw new AIPMS.Domain.Exceptions.DomainException(
                scan.Details ?? "File failed security scan and was quarantined.",
                WorkflowErrorCodes.FileQuarantined);
        }
    }
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

    private static IReadOnlyList<long>? ResolveAllowedMajorIds(SupervisorAccount actor, DeliverableProject project)
    {
        if (actor.Roles.Contains(AppRoles.Admin)) return null;
        if (IsSupervisor(actor, project) && project.IsPrimarySupervisor) return null;
        if (actor.Roles.Contains(AppRoles.DepartmentStaff) && project.IsLeadDepartmentStaff) return null;
        if (IsStudent(actor, project)) return null;
        if (IsSupervisor(actor, project)) return project.MentorMajorIds ?? [];
        if (actor.Roles.Contains(AppRoles.DepartmentStaff)) return project.StaffMajorIds ?? [];
        return [];
    }

    private static void RequireDeliverableScope(SupervisorAccount actor, DeliverableProject project, DeliverableDto item)
    {
        var allowedMajors = ResolveAllowedMajorIds(actor, project);
        if (allowedMajors is not null)
        {
            var isScoped = item.EvidenceScope is not (null or "COMMON" or "INTEGRATION");
            if (isScoped && (!item.TargetMajorId.HasValue || !allowedMajors.Contains(item.TargetMajorId.Value)))
                throw new ForbiddenException("The deliverable is outside your assigned academic scope.", WorkflowErrorCodes.ScopeAccessDenied);
            return;
        }
        if (IsStudent(actor, project) && !actor.Roles.Contains(AppRoles.Admin))
        {
            if (item.EvidenceScope == "INDIVIDUAL" && item.TargetStudentId.HasValue && item.TargetStudentId.Value != actor.UserId)
                throw new ForbiddenException("Individual deliverable evidence is restricted to the target student.", WorkflowErrorCodes.ScopeAccessDenied);
            if (item.EvidenceScope is "MAJOR_SPECIFIC" or "INDIVIDUAL" && project.StudentMajorId.HasValue
                && item.TargetMajorId.HasValue && item.TargetMajorId.Value != project.StudentMajorId.Value)
                throw new ForbiddenException("The deliverable is outside your academic major scope.", WorkflowErrorCodes.ScopeAccessDenied);
        }
    }

    private static void RequireParentScope(SupervisorAccount actor, DeliverableProject project, FileParent parent)
    {
        var allowedMajors = ResolveAllowedMajorIds(actor, project);
        if (allowedMajors is not null)
        {
            if (parent.Type == "TASK")
            {
                if (parent.MajorIds is { Count: > 0 } && !parent.MajorIds.Any(id => allowedMajors.Contains(id)))
                    throw new ForbiddenException("The task attachment is outside your assigned major scope.", WorkflowErrorCodes.ScopeAccessDenied);
                return;
            }
            if (parent.Type is "VERSION" or "FEEDBACK")
            {
                var isScoped = parent.EvidenceScope is not (null or "COMMON" or "INTEGRATION");
                if (isScoped && (!parent.TargetMajorId.HasValue || !allowedMajors.Contains(parent.TargetMajorId.Value)))
                    throw new ForbiddenException("The requested file is outside your assigned major scope.", WorkflowErrorCodes.ScopeAccessDenied);
            }
            return;
        }
        if (IsStudent(actor, project) && !actor.Roles.Contains(AppRoles.Admin) && parent.Type == "VERSION")
        {
            if (parent.EvidenceScope == "INDIVIDUAL" && parent.TargetStudentId.HasValue && parent.TargetStudentId.Value != actor.UserId)
                throw new ForbiddenException("Individual evidence is restricted to the target student.", WorkflowErrorCodes.ScopeAccessDenied);
            if (parent.EvidenceScope is "MAJOR_SPECIFIC" or "INDIVIDUAL" && project.StudentMajorId.HasValue
                && parent.TargetMajorId.HasValue && parent.TargetMajorId.Value != project.StudentMajorId.Value)
                throw new ForbiddenException("The requested file is outside your academic major scope.", WorkflowErrorCodes.ScopeAccessDenied);
        }
    }
    private static void RequirePlanner(SupervisorAccount actor, DeliverableProject project)
    {
        if (!(IsStudent(actor, project) && project.IsLeader) && !IsSupervisor(actor, project))
            throw new ForbiddenException("Only the current student leader or assigned supervisor can manage deliverables.");
    }

    private static void RequireMutable(DeliverableProject project)
    {
        if (project.Status != "ACTIVE") throw new ConflictException("Project mutations require ACTIVE status.", WorkflowErrorCodes.ProjectNotActive);
    }

    private async Task RequireSubmissionWindowAsync(DeliverableProject project, DeliverableDto deliverable, CancellationToken ct)
    {
        RequireMutable(project);
        if (deliverable.Status is "ACCEPTED" or "CLOSED") throw new ConflictException("The deliverable is locked.", WorkflowErrorCodes.DeliverableLocked);
        var now = Now;
        if (deliverable.DueAt.HasValue && now >= deliverable.DueAt.Value)
            throw new ConflictException("The deliverable deadline has passed.", WorkflowErrorCodes.DeliverableDeadlinePassed);
        if (!await repository.HasExecutionWindowAsync(project.SemesterId, now, ct))
            throw new ConflictException("One active execution window in the current semester is required.", WorkflowErrorCodes.ExecutionWindowRequired);
    }

    public async Task<DeliverableDto> GetAsync(long id, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var item = await ItemAsync(id, ct);
        var project = await ProjectAsync(actor, item.ProjectId, ct);
        RequireDeliverableScope(actor, project, item);
        return item;
    }

    public async Task<PagedResult<DeliverableDto>> ListAsync(DeliverableSearch search, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var project = await ProjectAsync(actor, search.ProjectId, ct);
        var allowedMajors = ResolveAllowedMajorIds(actor, project);
        var isRestrictedStudent = IsStudent(actor, project) && !actor.Roles.Contains(AppRoles.Admin);
        return await repository.SearchAsync(search with
        {
            AllowedMajorIds = allowedMajors,
            RestrictedStudentId = isRestrictedStudent ? actor.UserId : null,
            StudentMajorId = isRestrictedStudent ? project.StudentMajorId : null
        }, ct);
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
            if (before?.Status is "ACCEPTED" or "CLOSED") throw new ConflictException("The deliverable is locked.", WorkflowErrorCodes.DeliverableLocked);
            if (data.MilestoneId.HasValue && !await repository.MilestoneBelongsAsync(data.MilestoneId.Value, actualProjectId, token))
                throw new ConflictException("The milestone must belong to this project.", WorkflowErrorCodes.MilestoneNotInProject);
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
            throw new ConflictException("Deliverables with submitted history cannot be deleted.", WorkflowErrorCodes.DeliverableHasSubmittedHistory);
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
        var file = await UploadValidator.ReadAsync(upload, ct, allowArchiveVideoTier: false, allowQuarantineFallback: true);
        using (var scanStream = new MemoryStream(file.Bytes, writable: false))
            await ScanOrQuarantineAsync(actor.UserId, scanStream, file.FileName, file.ContentType, "DELIVERABLE_VERSION", ct);
        string? key = null;
        return await repository.InTransactionAsync(async token =>
        {
            actor = await ActorAsync(token);
            await repository.LockProjectAsync(item.ProjectId, token);
            item = await ItemAsync(id, token);
            var project = await ProjectAsync(actor, item.ProjectId, token);
            if (!IsStudent(actor, project)) throw new ForbiddenException("Only active student team members can submit a deliverable version.");
            if (item.LatestVersion != expectedVersion) throw new ConflictException("The latest version changed. Reload before submitting.", WorkflowErrorCodes.StaleLatestVersion);
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
            RequireDeliverableScope(actor, project, item);
            item = await ItemAsync(item.Id, token);
            if (version.Status != "SUBMITTED" || item.Status is "ACCEPTED" or "CLOSED" || version.VersionNumber != item.LatestVersion)
                throw new ConflictException("Only the latest pending version can be reviewed.", WorkflowErrorCodes.NotLatestPendingVersion);
            var result = await repository.ReviewAsync(versionId, project.AssignmentId!.Value, actor.UserId, decision, feedback.Trim(), Now, token);
            await AuditAsync(actor.UserId, "DELIVERABLE_VERSION_REVIEWED", "DELIVERABLE_VERSION", versionId,
                new { version.Status }, new { Status = decision, Feedback = result }, token);
            await publisher.Publish(new WorkflowNotificationEvent(
                WorkflowNotificationKind.SupervisorFeedbackAdded, result.Id, actor.UserId, result.CreatedAt), token);
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
        var actor = await ActorAsync(ct);
        var project = await ProjectAsync(actor, stored.ProjectId, ct);
        var parent = await ParentAsync(stored.Dto.ParentType, stored.Dto.ParentId, ct);
        return stored.Dto with { CanDelete = CanDeleteAttachment(actor, project, parent, stored.Dto) };
    }

    public async Task<PagedResult<ProjectFileDto>> FilesAsync(FileSearch search, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var project = await ProjectAsync(actor, search.ProjectId, ct);
        var allowedMajors = ResolveAllowedMajorIds(actor, project);
        var isRestrictedStudent = IsStudent(actor, project) && !actor.Roles.Contains(AppRoles.Admin);
        var scopedSearch = search with
        {
            AllowedMajorIds = allowedMajors,
            RestrictedStudentId = isRestrictedStudent ? actor.UserId : null,
            StudentMajorId = isRestrictedStudent ? project.StudentMajorId : null
        };
        var result = await repository.FilesAsync(scopedSearch, ct);
        var parents = new Dictionary<(string Type, long Id), FileParent>();
        var items = new List<ProjectFileDto>(result.Items.Count);
        foreach (var file in result.Items)
        {
            var key = (file.ParentType, file.ParentId);
            if (!parents.TryGetValue(key, out var parent))
                parents[key] = parent = await ParentAsync(key.ParentType, key.ParentId, ct);
            items.Add(file with { CanDelete = CanDeleteAttachment(actor, project, parent, file) });
        }
        return result with { Items = items };
    }

    public async Task<PagedResult<ProjectFileDto>> TaskEvidenceAsync(long taskId, int page, int pageSize, CancellationToken ct)
    {
        var parent = await ParentAsync("TASK", taskId, ct);
        var actor = await ActorAsync(ct);
        var project = await ProjectAsync(actor, parent.ProjectId, ct);
        RequireParentScope(actor, project, parent);
        var result = await repository.FilesAsync(new(parent.ProjectId, null, page, pageSize, null, null, null, null, "TASK", taskId), ct);
        return result with { Items = result.Items.Select(file => file with { CanDelete = CanDeleteAttachment(actor, project, parent, file) }).ToArray() };
    }

    public async Task<FileDownload> DownloadAsync(long id, CancellationToken ct)
    {
        var file = await ReadableFileAsync(id, ct);
        try
        {
            var content = storage is IStreamingFileStorage streaming
                ? await streaming.OpenSequentialReadAsync(file.StorageKey, ct)
                : await storage.OpenReadAsync(file.StorageKey, ct);
            return new(content, file.Dto.ContentType, file.Dto.FileName, file.Dto.SizeBytes);
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { throw new NotFoundException("File content", id); }
    }

    public async Task<ProjectFileDto> AttachAsync(string type, long parentId, UploadContent upload, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var parent = await ParentAsync(type, parentId, ct);
        var initialProject = await ProjectAsync(actor, parent.ProjectId, ct);
        RequireParentScope(actor, initialProject, parent);
        RequireAttachmentWriter(actor, initialProject, parent);
        var file = await UploadValidator.ReadAsync(upload, ct, allowArchiveVideoTier: false, allowQuarantineFallback: true);
        using (var scanStream = new MemoryStream(file.Bytes, writable: false))
            await ScanOrQuarantineAsync(actor.UserId, scanStream, file.FileName, file.ContentType, "PROJECT_FILE", ct);
        string? key = null;
        return await repository.InTransactionAsync(async token =>
        {
            actor = await ActorAsync(token);
            await repository.LockProjectAsync(parent.ProjectId, token);
            parent = await ParentAsync(type, parentId, token);
            var project = await ProjectAsync(actor, parent.ProjectId, token);
            RequireParentScope(actor, project, parent);
            RequireAttachmentWriter(actor, project, parent);
            var newKey = Guid.NewGuid().ToString("N");
            using var content = new MemoryStream(file.Bytes, writable: false);
            await storage.WriteAsync(newKey, content, token);
            key = newKey;
            var result = await repository.AttachAsync(parent, key, file, actor.UserId, Now, token);
            await AuditAsync(actor.UserId, "PROJECT_FILE_UPLOADED", "FILE", result.Id, null, result, token);
            return result;
        }, ct, () => CleanupAsync(key));
    }

    public async Task AuthorizeAttachmentAsync(string type, long parentId, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var parent = await ParentAsync(type, parentId, ct);
        var project = await ProjectAsync(actor, parent.ProjectId, ct);
        RequireParentScope(actor, project, parent);
        RequireAttachmentWriter(actor, project, parent);
    }

    public async Task AuthorizeAttachmentReadAsync(string type, long parentId, CancellationToken ct)
    {
        var actor = await ActorAsync(ct);
        var parent = await ParentAsync(type, parentId, ct);
        var project = await ProjectAsync(actor, parent.ProjectId, ct);
        RequireParentScope(actor, project, parent);
    }

    // Only the durable upload worker supplies actorId, never a client request parameter.
    // Persisted identity/scope/state are checked before transfer and again at the metadata commit.
    public async Task<ProjectFileDto> AttachPreparedForActorAsync(long actorId, string type, long parentId,
        string storageKey, UploadMetadata file, Stream content, CancellationToken ct)
    {
        if (storageKey.Length != 32 || storageKey.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("Invalid upload storage key.", nameof(storageKey));
        ProjectFileUploadValidator.ValidateMetadata(file.FileName, file.ContentType, file.Length);
        if (!content.CanSeek || content.Length != file.Length)
            throw new InvalidOperationException("Prepared upload must be a seekable, validated file.");
        var actor = await ActiveActorAsync(actorId, ct);
        var parent = await ParentAsync(type, parentId, ct);
        var project = await ProjectAsync(actor, parent.ProjectId, ct);
        var existing = await repository.FindFileByStorageKeyAsync(storageKey, ct);
        if (existing is not null) return ConfirmPreparedFile(existing, parent, actor, project, file);
        RequireParentScope(actor, project, parent);
        RequireAttachmentWriter(actor, project, parent);
        await ScanOrQuarantineAsync(actorId, content, file.FileName, file.ContentType, "PROJECT_FILE_CHUNKED", ct);

        // A worker restart may occur after storage completed but before SQL committed. Reuse only
        // an object whose length and checksum match this session; never overwrite uncertain data.
        var stored = false;
        try
        {
            await using var previous = await storage.OpenReadAsync(storageKey, ct);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(previous, ct)).ToLowerInvariant();
            if (hash != file.Sha256 || previous.CanSeek && previous.Length != file.Length)
                throw new ConflictException("Nội dung tệp đang lưu không khớp phiên tải. Hãy hủy phiên và tải lại.");
            stored = true;
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
        if (!stored)
        {
            content.Position = 0;
            await storage.WriteAsync(storageKey, content, ct);
        }

        // The slow content transfer deliberately occurs outside the project's SQL lock.
        return await repository.InTransactionAsync(async token =>
        {
            actor = await ActiveActorAsync(actorId, token);
            await repository.LockProjectAsync(parent.ProjectId, token);
            parent = await ParentAsync(type, parentId, token);
            project = await ProjectAsync(actor, parent.ProjectId, token);
            existing = await repository.FindFileByStorageKeyAsync(storageKey, token);
            if (existing is not null) return ConfirmPreparedFile(existing, parent, actor, project, file);
            RequireParentScope(actor, project, parent);
            RequireAttachmentWriter(actor, project, parent);
            var result = await repository.AttachAsync(parent, storageKey, file, actor.UserId, Now, token);
            await AuditAsync(actor.UserId, "PROJECT_FILE_UPLOADED", "FILE", result.Id, null, result, token);
            return result with { CanDelete = CanDeleteAttachment(actor, project, parent, result) };
        }, ct, () => CleanupAsync(storageKey));
    }

    private async Task<SupervisorAccount> ActiveActorAsync(long actorId, CancellationToken ct)
    {
        var actor = await accounts.GetAccountAsync(actorId, ct);
        if (actor is null || !actor.IsActive || actor.UserId != actorId)
            throw new ForbiddenException("Tài khoản tải tệp không còn hoạt động.");
        return actor;
    }

    // Worker-only reconciliation while the durable session's processing lock is held. A database
    // failure must retain the session/object for another attempt. Any confirmed SQL reference wins,
    // including a commit whose acknowledgement was lost; never delete that file on cancellation.
    public async Task CleanupPreparedForActorAsync(long actorId, string type, long parentId,
        string storageKey, CancellationToken ct)
    {
        if (actorId <= 0 || parentId <= 0 || type is not ("TASK" or "REPORT" or "MEETING")
            || storageKey.Length != 32 || storageKey.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("Invalid owned upload session.");
        if (await repository.FindFileByStorageKeyAsync(storageKey, ct) is not null) return;
        await storage.DeleteAsync(storageKey, ct);
    }

    private static ProjectFileDto ConfirmPreparedFile(StoredProjectFile stored, FileParent parent,
        SupervisorAccount actor, DeliverableProject project, UploadMetadata expected)
    {
        var file = stored.Dto;
        if (stored.ParentCount != 1 || stored.ProjectId != parent.ProjectId || file.ParentType != parent.Type
            || file.ParentId != parent.Id || file.UploadedBy != actor.UserId || file.Sha256 != expected.Sha256
            || file.SizeBytes != expected.Length || file.FileName != expected.FileName || file.ContentType != expected.ContentType)
            throw new ConflictException("Phiên tải không khớp tệp đã lưu. Hãy tải lại thông tin trước khi tiếp tục.");
        return file with { CanDelete = CanDeleteAttachment(actor, project, parent, file) };
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
        if (file.ParentCount == 0) throw new NotFoundException("Project file", id);
        if (file.ParentCount != 1) throw new ConflictException("The file must have exactly one parent.");
        var project = await ProjectAsync(actor, file.ProjectId, ct);
        var parent = await ParentAsync(file.Dto.ParentType, file.Dto.ParentId, ct);
        RequireParentScope(actor, project, parent);
        return file;
    }

    private static void RequireAttachmentWriter(SupervisorAccount actor, DeliverableProject project, FileParent parent)
    {
        RequireMutable(project);
        if (parent.Type == "TASK" && !IsEditableTask(parent))
            throw new ConflictException("Không thể thay đổi tệp của công việc đang chờ nghiệm thu, đã hoàn thành hoặc đã hủy. Hãy đưa công việc về trạng thái được phép chỉnh sửa theo quyền được cấp.");
        if (parent.Type == "REPORT" && parent.Status != "DRAFT" || parent.Type == "MEETING" && parent.Status != "SCHEDULED"
            || parent.Type is not ("REPORT" or "MEETING" or "TASK"))
            throw new ConflictException("Submitted versions, feedback and completed records retain immutable files.");
        if (!IsStudent(actor, project) && !IsSupervisor(actor, project))
            throw new ForbiddenException("Only current project participants may manage attachments.");
        if (parent.Type == "REPORT" && !IsStudent(actor, project))
            throw new ForbiddenException("Only student team members may edit a report draft's attachments.");
    }

    private static bool IsEditableTask(FileParent parent) => parent.Status is "TODO" or "IN_PROGRESS" or "BLOCKED";

    private static bool CanDeleteAttachment(SupervisorAccount actor, DeliverableProject project, FileParent parent, ProjectFileDto file) =>
        project.Status == "ACTIVE"
        && (IsStudent(actor, project) || IsSupervisor(actor, project))
        && (parent.Type == "TASK" && IsEditableTask(parent)
            || parent.Type == "REPORT" && parent.Status == "DRAFT" && IsStudent(actor, project)
            || parent.Type == "MEETING" && parent.Status == "SCHEDULED")
        && (file.UploadedBy == actor.UserId || project.IsLeader || IsSupervisor(actor, project));

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
