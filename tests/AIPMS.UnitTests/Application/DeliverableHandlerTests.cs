using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Deliverables.Abstractions;
using AIPMS.Application.Features.Deliverables.Commands;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Deliverables.Services;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIPMS.UnitTests.Application;

public sealed class DeliverableHandlerTests
{
    [Fact]
    public async Task Submit_assigns_next_version_and_trims_note_inside_audited_transaction()
    {
        var f = new Fixture();
        var result = await f.Submit();
        Assert.Equal(3, result.VersionNumber);
        Assert.Equal("Evidence", result.Note);
        Assert.Equal(10, result.SubmittedBy);
        Assert.Equal("DELIVERABLE_VERSION_SUBMITTED", Assert.Single(f.Audit.Entries).Action);
        Assert.Equal(1, f.Storage.Writes);
        Assert.Equal(0, f.Storage.Deletes);
    }

    [Fact]
    public async Task Persisted_role_is_rechecked_inside_transaction_before_storage_write()
    {
        var f = new Fixture();
        f.Repository.OnBegin = () => f.Accounts.Actor = f.Accounts.Actor with { Roles = [AppRoles.Admin] };
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Submit());
        Assert.Equal(0, f.Storage.Writes);
        Assert.Empty(f.Audit.Entries);
    }

    [Theory]
    [InlineData("staleVersion", WorkflowErrorCodes.StaleLatestVersion)]
    [InlineData("closed", WorkflowErrorCodes.DeliverableLocked)]
    [InlineData("deadline", WorkflowErrorCodes.DeliverableDeadlinePassed)]
    [InlineData("noPeriod", WorkflowErrorCodes.ExecutionWindowRequired)]
    public async Task Invalid_submission_cannot_write_file_or_audit(string reason, string expectedCode)
    {
        var f = new Fixture();
        if (reason == "staleVersion") f.Repository.Item = f.Repository.Item with { LatestVersion = 3 };
        if (reason == "closed") f.Repository.Item = f.Repository.Item with { Status = "CLOSED" };
        if (reason == "deadline") f.Repository.Item = f.Repository.Item with { DueAt = DateTime.UnixEpoch };
        if (reason == "noPeriod") f.Repository.HasWindow = false;
        var ex = await Assert.ThrowsAsync<ConflictException>(() => f.Submit());
        Assert.Equal(expectedCode, ex.Code);
        Assert.Equal(0, f.Storage.Writes);
        Assert.Empty(f.Audit.Entries);
    }

    [Fact]
    public async Task Storage_create_failure_does_not_delete_an_object_it_does_not_own()
    {
        var f = new Fixture();
        f.Storage.FailCreate = true;
        await Assert.ThrowsAsync<IOException>(() => f.Submit());
        Assert.Equal(0, f.Storage.Deletes);
        Assert.Empty(f.Audit.Entries);
    }

    [Fact]
    public async Task Audit_failure_cleans_successful_write_after_rollback()
    {
        var f = new Fixture();
        f.Audit.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Submit());
        Assert.Equal(1, f.Storage.Writes);
        Assert.Equal(1, f.Storage.Deletes);
        Assert.False(f.Repository.InTransaction);
    }

    [Theory]
    [InlineData("IN_REVIEW")]
    [InlineData("DONE")]
    [InlineData("CANCELLED")]
    public async Task Task_files_cannot_be_uploaded_or_deleted_after_submission_for_review(string status)
    {
        var f = new Fixture();
        f.Repository.Parent = f.Repository.Parent with { Status = status };
        Assert.False(Assert.Single((await f.Evidence()).Items).CanDelete);
        await Assert.ThrowsAsync<ConflictException>(() => f.Attach());
        await Assert.ThrowsAsync<ConflictException>(() => f.DeleteFile());
        Assert.Equal(0, f.Storage.Writes);
        Assert.Equal(0, f.Storage.Deletes);
        Assert.Equal(0, f.Repository.FileDeletes);
        Assert.Empty(f.Audit.Entries);
    }

    [Theory]
    [InlineData("TODO")]
    [InlineData("IN_PROGRESS")]
    [InlineData("BLOCKED")]
    public async Task Uploader_can_delete_editable_task_evidence_with_audit_and_cleanup_after_transaction(string status)
    {
        var f = new Fixture();
        f.Repository.Parent = f.Repository.Parent with { Status = status };
        Assert.True(Assert.Single((await f.Evidence()).Items).CanDelete);
        await f.DeleteFile();
        Assert.Equal(1, f.Repository.FileDeletes);
        Assert.Equal(1, f.Storage.Deletes);
        Assert.Equal("PROJECT_FILE_DELETED", Assert.Single(f.Audit.Entries).Action);
    }

    [Fact]
    public async Task Another_member_cannot_delete_someone_elses_task_evidence()
    {
        var f = new Fixture();
        f.Repository.File = f.Repository.File with { Dto = f.Repository.File.Dto with { UploadedBy = 99 } };
        Assert.False(Assert.Single((await f.Evidence()).Items).CanDelete);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.DeleteFile());
        Assert.Equal(0, f.Repository.FileDeletes);
        Assert.Equal(0, f.Storage.Deletes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Leader_or_assigned_supervisor_can_delete_another_uploaders_editable_evidence(bool leader)
    {
        var f = new Fixture();
        f.Repository.File = f.Repository.File with { Dto = f.Repository.File.Dto with { UploadedBy = 99 } };
        f.Repository.Project = f.Repository.Project with { IsLeader = leader, AssignmentId = leader ? null : 123 };
        if (!leader) f.Accounts.Actor = f.Accounts.Actor with { Roles = [AppRoles.Lecturer] };
        Assert.True(Assert.Single((await f.Evidence()).Items).CanDelete);
        await f.DeleteFile();
        Assert.Equal(1, f.Repository.FileDeletes);
    }

    [Fact]
    public async Task Administrator_read_access_does_not_grant_attachment_deletion()
    {
        var f = new Fixture();
        f.Accounts.Actor = f.Accounts.Actor with { Roles = [AppRoles.Admin] };
        Assert.False(Assert.Single((await f.Evidence()).Items).CanDelete);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.DeleteFile());
        Assert.Equal(0, f.Storage.Deletes);
    }

    [Fact]
    public async Task Task_status_is_rechecked_in_transaction_before_attachment_mutation()
    {
        var f = new Fixture();
        f.Repository.OnBegin = () => f.Repository.Parent = f.Repository.Parent with { Status = "DONE" };
        await Assert.ThrowsAsync<ConflictException>(() => f.Attach());
        Assert.Equal(0, f.Storage.Writes);
        Assert.Equal(0, f.Storage.Deletes);
        Assert.Empty(f.Audit.Entries);
    }

    [Fact]
    public async Task Task_evidence_is_read_only_when_project_is_no_longer_active()
    {
        var f = new Fixture();
        f.Repository.Project = f.Repository.Project with { Status = "APPROVED" };
        Assert.False(Assert.Single((await f.Evidence()).Items).CanDelete);
        await Assert.ThrowsAsync<ConflictException>(() => f.DeleteFile());
        Assert.Equal(0, f.Storage.Deletes);
    }

    [Fact]
    public async Task Failed_deletion_audit_preserves_storage_content()
    {
        var f = new Fixture();
        f.Audit.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.DeleteFile());
        Assert.Equal(0, f.Storage.Deletes);
    }

    [Fact]
    public async Task Prepared_upload_transfers_outside_transaction_then_commits_metadata_and_audit()
    {
        var f = new Fixture();
        f.Storage.ExpectTransferOutsideTransaction = true;
        var file = await f.AttachPrepared();
        Assert.Equal(9, file.SizeBytes);
        Assert.Equal(1, f.Storage.Writes);
        Assert.Equal(0, f.Storage.Deletes);
        Assert.Equal("PROJECT_FILE_UPLOADED", Assert.Single(f.Audit.Entries).Action);
    }

    [Fact]
    public async Task Prepared_upload_rechecks_state_after_transfer_and_cleans_up_confirmed_rollback()
    {
        var f = new Fixture();
        f.Storage.ExpectTransferOutsideTransaction = true;
        f.Storage.OnWrite = () => f.Repository.Parent = f.Repository.Parent with { Status = "DONE" };
        await Assert.ThrowsAsync<ConflictException>(() => f.AttachPrepared());
        Assert.Equal(1, f.Storage.Writes);
        Assert.Equal(1, f.Storage.Deletes);
        Assert.Empty(f.Audit.Entries);
    }

    [Fact]
    public async Task Prepared_upload_recovers_lost_commit_ack_without_duplicate_file_even_when_parent_is_locked()
    {
        var f = new Fixture();
        f.Storage.ExpectTransferOutsideTransaction = true;
        var first = await f.AttachPrepared();
        f.Repository.Parent = f.Repository.Parent with { Status = "DONE" };
        var retry = await f.AttachPrepared();
        Assert.Equal(first.Id, retry.Id);
        Assert.False(retry.CanDelete);
        Assert.Equal(1, f.Storage.Writes);
        Assert.Single(f.Audit.Entries);
    }

    [Fact]
    public async Task Prepared_upload_reuses_verified_object_after_worker_restart()
    {
        var f = new Fixture();
        f.Storage.ExistingBytes = "%PDF-test"u8.ToArray();
        var file = await f.AttachPrepared();
        Assert.Equal(9, file.SizeBytes);
        Assert.Equal(0, f.Storage.Writes);
        Assert.Single(f.Audit.Entries);
    }

    [Fact]
    public async Task Prepared_upload_never_overwrites_or_deletes_an_unexpected_existing_object()
    {
        var f = new Fixture();
        f.Storage.ExistingBytes = "unexpected"u8.ToArray();
        await Assert.ThrowsAsync<ConflictException>(() => f.AttachPrepared());
        Assert.Equal(0, f.Storage.Writes);
        Assert.Equal(0, f.Storage.Deletes);
        Assert.Empty(f.Audit.Entries);
    }

    [Fact]
    public async Task Prepared_upload_rechecks_persisted_actor_at_commit()
    {
        var f = new Fixture();
        f.Storage.ExpectTransferOutsideTransaction = true;
        f.Storage.OnWrite = () => f.Accounts.Actor = f.Accounts.Actor with { IsActive = false };
        await Assert.ThrowsAsync<ForbiddenException>(() => f.AttachPrepared());
        Assert.Equal(1, f.Storage.Deletes);
        Assert.Empty(f.Audit.Entries);
    }

    [Fact]
    public async Task Prepared_cleanup_retains_a_confirmed_file_reference_even_after_actor_is_revoked()
    {
        var f = new Fixture();
        f.Storage.ExpectTransferOutsideTransaction = true;
        await f.AttachPrepared();
        f.Accounts.Actor = f.Accounts.Actor with { IsActive = false };
        await f.CleanupPrepared();
        Assert.Equal(0, f.Storage.Deletes);
    }

    [Fact]
    public async Task Prepared_cleanup_removes_unreferenced_object_from_a_canceled_session()
    {
        var f = new Fixture();
        await f.CleanupPrepared();
        Assert.Equal(1, f.Storage.Deletes);
    }

    [Fact]
    public async Task Project_download_selects_streaming_provider_and_preserves_trusted_length()
    {
        var f = new Fixture();
        var result = await f.Download();
        await using var content = result.Content;
        Assert.Equal(4, result.Length);
        Assert.Equal(1, f.Storage.SequentialReads);
        Assert.Equal("evidence.txt", result.FileName);
    }

    private sealed class Fixture
    {
        public Repository Repository { get; } = new();
        public Accounts Accounts { get; } = new();
        public Storage Storage { get; }
        public Audit Audit { get; }
        private readonly DeliverableWorkflow workflow;
        public Fixture()
        {
            Storage = new(Repository);
            Audit = new(Repository);
            workflow = new(Repository, Storage, new Actor(), Accounts, new Access(), Audit, TimeProvider.System,
                NullLogger<DeliverableWorkflow>.Instance, new RecordingPublisher(),
                new AIPMS.Infrastructure.Services.Files.BasicSignatureAndArchiveScanner());
        }
        public async Task<DeliverableVersionDto> Submit()
        {
            using var content = new MemoryStream("test"u8.ToArray());
            return await new SubmitDeliverableVersionCommandHandler(workflow).Handle(new(1, 2, " Evidence ",
                new("evidence.txt", "text/plain", 4, content)), default);
        }
        public Task<bool> DeleteFile() => workflow.DeleteFileAsync(8, default);
        public Task<PagedResult<ProjectFileDto>> Evidence() => workflow.TaskEvidenceAsync(5, 1, 20, default);
        public async Task<ProjectFileDto> Attach()
        {
            using var content = new MemoryStream("test"u8.ToArray());
            return await workflow.AttachAsync("TASK", 5, new("evidence.txt", "text/plain", 4, content), default);
        }
        public async Task<ProjectFileDto> AttachPrepared()
        {
            using var content = new MemoryStream("%PDF-test"u8.ToArray());
            var metadata = await ProjectFileUploadValidator.ValidateAsync(content, "evidence.pdf", "application/pdf", content.Length, default);
            return await workflow.AttachPreparedForActorAsync(10, "TASK", 5, new string('a', 32), metadata, content, default);
        }
        public Task CleanupPrepared() => workflow.CleanupPreparedForActorAsync(10, "TASK", 5, new string('a', 32), default);
        public Task<FileDownload> Download() => workflow.DownloadAsync(8, default);
    }

    private sealed class Actor : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => 10;
        public string? Email => null;
        public string? FullName => null;
        public IReadOnlyCollection<string> Roles => [AppRoles.Admin];
    }
    private sealed class Access : IProjectAccessService
    {
        public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
    private sealed class Audit(Repository repository) : IAuditTrail
    {
        public bool Fail { get; set; }
        public List<AuditEntry> Entries { get; } = [];
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Assert.True(repository.InTransaction);
            if (Fail) throw new InvalidOperationException("Audit failed");
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
    private sealed class Storage(Repository repository) : IStreamingFileStorage
    {
        public bool ExpectTransferOutsideTransaction { get; set; }
        public Action? OnWrite { get; set; }
        public byte[]? ExistingBytes { get; set; }
        public bool FailCreate { get; set; }
        public int Writes { get; private set; }
        public int Deletes { get; private set; }
        public int SequentialReads { get; private set; }
        public Task WriteAsync(string key, Stream content, CancellationToken ct)
        {
            Assert.Equal(!ExpectTransferOutsideTransaction, repository.InTransaction);
            if (!ExpectTransferOutsideTransaction) Assert.True(repository.Locked);
            if (FailCreate) throw new IOException("Existing object cannot be overwritten");
            Writes++;
            OnWrite?.Invoke();
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string key, CancellationToken ct)
        {
            Assert.False(repository.InTransaction);
            Deletes++;
            return Task.CompletedTask;
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => ExistingBytes is null
            ? throw new FileNotFoundException() : Task.FromResult<Stream>(new MemoryStream(ExistingBytes));
        public Task<Stream> OpenSequentialReadAsync(string key, CancellationToken ct)
        {
            SequentialReads++;
            return Task.FromResult<Stream>(new MemoryStream("test"u8.ToArray()));
        }
    }
    private sealed class Accounts : ISupervisorProfileRepository
    {
        public SupervisorAccount Actor { get; set; } = new(10, 1, true, true, [AppRoles.Student]);
        public Task<SupervisorAccount?> GetAccountAsync(long userId, CancellationToken ct) => Task.FromResult<SupervisorAccount?>(Actor);
        public Task<PagedResult<SupervisorProfileModel>> SearchAsync(SupervisorSearch search, CancellationToken ct) => throw new NotSupportedException();
        public Task<SupervisorProfileModel?> GetAsync(long profileId, CancellationToken ct,
            long? authorizedOrganizationId = null, long? authorizedDepartmentId = null) => throw new NotSupportedException();
        public Task<SupervisorProfileModel?> GetByUserAsync(long userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SupervisorProfileModel> UpsertAsync(long userId, string? bio, bool available, DateTime now, CancellationToken ct) => throw new NotSupportedException();
        public Task<SupervisorProfileModel> ReplaceExpertiseAsync(long profileId, IReadOnlyList<SupervisorExpertiseModel> expertise, DateTime now, CancellationToken ct) => throw new NotSupportedException();
        public Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Repository : IDeliverableRepository
    {
        public bool InTransaction { get; private set; }
        public bool Locked { get; private set; }
        public Action? OnBegin { get; set; }
        public bool HasWindow { get; set; } = true;
        public DeliverableDto Item { get; set; } = new(1, 20, null, "Report", null, null, null, "REJECTED", 10, 2);
        public DeliverableProject Project { get; set; } = new(20, 30, "ACTIVE", true, false, null);
        public FileParent Parent { get; set; } = new("TASK", 5, 20, "TODO", 10);
        public StoredProjectFile File { get; set; } = new(new(8, "TASK", 5, "evidence.txt", "text/plain", 4, "hash", 10, DateTime.UtcNow), "private-object", 20, 1);
        public int FileDeletes { get; private set; }
        public StoredProjectFile? PreparedFile { get; private set; }
        public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct, Func<Task>? onRollback = null)
        {
            InTransaction = true;
            try { OnBegin?.Invoke(); return await action(ct); }
            catch
            {
                InTransaction = false;
                if (onRollback is not null) await onRollback();
                throw;
            }
            finally { InTransaction = false; }
        }
        public Task LockProjectAsync(long projectId, CancellationToken ct)
        {
            Assert.True(InTransaction);
            Locked = true;
            return Task.CompletedTask;
        }
        public Task<DeliverableProject?> GetProjectAsync(long projectId, long actorId, CancellationToken ct) => Task.FromResult<DeliverableProject?>(Project);
        public Task<bool> HasExecutionWindowAsync(long semesterId, DateTime now, CancellationToken ct) => Task.FromResult(HasWindow);
        public Task<DeliverableDto?> GetAsync(long id, CancellationToken ct) => Task.FromResult<DeliverableDto?>(Item);
        public Task<DeliverableVersionDto> SubmitAsync(long id, int number, long actorId, string? note, string storageKey, ValidatedUpload file, DateTime now, CancellationToken ct)
        {
            Assert.True(InTransaction);
            Assert.True(Locked);
            return Task.FromResult(new DeliverableVersionDto(2, id, number, actorId, note, "SUBMITTED", now, []));
        }
        public Task<bool> MilestoneBelongsAsync(long milestoneId, long projectId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PagedResult<DeliverableDto>> SearchAsync(DeliverableSearch search, CancellationToken ct) => throw new NotSupportedException();
        public Task<DeliverableDto> SaveAsync(long? id, long projectId, SaveDeliverableRequest data, long actorId, DateTime now, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(long id, CancellationToken ct) => throw new NotSupportedException();
        public Task<DeliverableVersionDto?> GetVersionAsync(long id, CancellationToken ct) => throw new NotSupportedException();
        public Task<PagedResult<DeliverableVersionDto>> VersionsAsync(long deliverableId, int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<DeliverableFeedbackDto> ReviewAsync(long versionId, long assignmentId, long actorId, string decision, string feedback, DateTime now, CancellationToken ct) => throw new NotSupportedException();
        public Task<PagedResult<DeliverableFeedbackDto>> FeedbackAsync(long versionId, int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<FileParent?> GetParentAsync(string type, long id, CancellationToken ct) => Task.FromResult<FileParent?>(Parent);
        public Task<StoredProjectFile?> GetFileAsync(long id, CancellationToken ct) => Task.FromResult<StoredProjectFile?>(File);
        public Task<StoredProjectFile?> FindFileByStorageKeyAsync(string storageKey, CancellationToken ct) => Task.FromResult(PreparedFile);
        public Task<PagedResult<ProjectFileDto>> FilesAsync(FileSearch search, CancellationToken ct) => Task.FromResult(new PagedResult<ProjectFileDto>([File.Dto], 1, 20, 1));
        public Task<ProjectFileDto> AttachAsync(FileParent parent, string key, UploadMetadata file, long actorId, DateTime now, CancellationToken ct)
        {
            Assert.True(InTransaction);
            Assert.True(Locked);
            var dto = new ProjectFileDto(90, parent.Type, parent.Id, file.FileName, file.ContentType, file.Length, file.Sha256, actorId, now);
            PreparedFile = new(dto, key, parent.ProjectId, 1);
            return Task.FromResult(dto);
        }
        public Task DeleteFileAsync(long id, CancellationToken ct)
        {
            Assert.True(InTransaction);
            Assert.True(Locked);
            FileDeletes++;
            return Task.CompletedTask;
        }
    }
}
