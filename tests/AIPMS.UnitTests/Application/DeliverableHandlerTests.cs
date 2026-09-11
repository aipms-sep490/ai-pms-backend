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
    [InlineData("staleVersion")]
    [InlineData("closed")]
    [InlineData("deadline")]
    [InlineData("noPeriod")]
    public async Task Invalid_submission_cannot_write_file_or_audit(string reason)
    {
        var f = new Fixture();
        if (reason == "staleVersion") f.Repository.Item = f.Repository.Item with { LatestVersion = 3 };
        if (reason == "closed") f.Repository.Item = f.Repository.Item with { Status = "CLOSED" };
        if (reason == "deadline") f.Repository.Item = f.Repository.Item with { DueAt = DateTime.UnixEpoch };
        if (reason == "noPeriod") f.Repository.HasWindow = false;
        await Assert.ThrowsAsync<ConflictException>(() => f.Submit());
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
                NullLogger<DeliverableWorkflow>.Instance);
        }
        public async Task<DeliverableVersionDto> Submit()
        {
            using var content = new MemoryStream("test"u8.ToArray());
            return await new SubmitDeliverableVersionCommandHandler(workflow).Handle(new(1, 2, " Evidence ",
                new("evidence.txt", "text/plain", 4, content)), default);
        }
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
    private sealed class Storage(Repository repository) : IFileStorage
    {
        public bool FailCreate { get; set; }
        public int Writes { get; private set; }
        public int Deletes { get; private set; }
        public Task WriteAsync(string key, Stream content, CancellationToken ct)
        {
            Assert.True(repository.InTransaction);
            Assert.True(repository.Locked);
            if (FailCreate) throw new IOException("Existing object cannot be overwritten");
            Writes++;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string key, CancellationToken ct)
        {
            Assert.False(repository.InTransaction);
            Deletes++;
            return Task.CompletedTask;
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Accounts : ISupervisorProfileRepository
    {
        public SupervisorAccount Actor { get; set; } = new(10, 1, true, true, [AppRoles.Student]);
        public Task<SupervisorAccount?> GetAccountAsync(long userId, CancellationToken ct) => Task.FromResult<SupervisorAccount?>(Actor);
        public Task<PagedResult<SupervisorProfileModel>> SearchAsync(SupervisorSearch search, CancellationToken ct) => throw new NotSupportedException();
        public Task<SupervisorProfileModel?> GetAsync(long profileId, CancellationToken ct) => throw new NotSupportedException();
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
        public Task<DeliverableProject?> GetProjectAsync(long projectId, long actorId, CancellationToken ct) => Task.FromResult<DeliverableProject?>(new(20, 30, "ACTIVE", true, false, null));
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
        public Task<FileParent?> GetParentAsync(string type, long id, CancellationToken ct) => throw new NotSupportedException();
        public Task<StoredProjectFile?> GetFileAsync(long id, CancellationToken ct) => throw new NotSupportedException();
        public Task<PagedResult<ProjectFileDto>> FilesAsync(FileSearch search, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProjectFileDto> AttachAsync(FileParent parent, string key, ValidatedUpload file, long actorId, DateTime now, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteFileAsync(long id, CancellationToken ct) => throw new NotSupportedException();
    }
}
