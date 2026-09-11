using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.Commands;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Application.Features.Supervisors.Queries;
using AIPMS.Application.Features.Supervisors.Services;

namespace AIPMS.UnitTests.Application;

public sealed class SupervisorAssignmentHandlerTests
{
    [Fact]
    public async Task End_is_transactional_and_replay_does_not_rewrite_reason_or_time()
    {
        var fixture = new Fixture();
        var handler = new EndSupervisorAssignmentCommandHandler(fixture.Workflow);
        var result = await handler.Handle(new(1, " Finished "), default);
        Assert.NotNull(result.EndedAt);
        Assert.Equal(result, await handler.Handle(new(1, "New reason"), default));
        Assert.Equal(1, fixture.Repository.Writes);
        var audit = Assert.Single(fixture.Audit.Entries);
        Assert.Equal("SUPERVISOR_ASSIGNMENT_ENDED", audit.Action);
        Assert.Equal("SUPERVISOR_ASSIGNMENT", audit.EntityType);
    }

    [Theory]
    [InlineData(AppRoles.Student, 20, true, true)]
    [InlineData(AppRoles.Lecturer, 99, true, true)]
    [InlineData(AppRoles.DepartmentStaff, 99, true, false)]
    [InlineData(AppRoles.Lecturer, 20, false, true)]
    public async Task End_permission_is_independent_of_project_read_access(string role, long user, bool scope, bool department)
    {
        var f = new Fixture(role, user, scope);
        f.Repository.DepartmentMatches = department;
        await Assert.ThrowsAsync<ForbiddenException>(() => new EndSupervisorAssignmentCommandHandler(f.Workflow).Handle(new(1, "Done"), default));
        Assert.Equal(0, f.Repository.Writes);
        Assert.Empty(f.Audit.Entries);
    }

    [Theory]
    [InlineData("ACTIVE")]
    [InlineData("FINAL_SUBMISSION")]
    public async Task Even_admin_cannot_end_an_unfinished_project(string status)
    {
        var f = new Fixture(AppRoles.Admin);
        f.Repository.Assignment = f.Repository.Assignment with { ProjectStatus = status };
        await Assert.ThrowsAsync<ConflictException>(() => new EndSupervisorAssignmentCommandHandler(f.Workflow).Handle(new(1, "Done"), default));
        Assert.Equal(0, f.Repository.Writes);
    }

    [Fact]
    public async Task Future_assignment_time_returns_conflict_without_writing()
    {
        var f = new Fixture();
        f.Repository.Assignment = f.Repository.Assignment with { AssignedAt = DateTime.MaxValue };
        await Assert.ThrowsAsync<ConflictException>(() => new EndSupervisorAssignmentCommandHandler(f.Workflow).Handle(new(1, "Done"), default));
        Assert.Equal(0, f.Repository.Writes);
    }

    [Fact]
    public async Task Former_supervisor_can_read_own_assignment_without_project_access()
    {
        var f = new Fixture();
        f.ProjectAccess.Allowed = false;
        f.Repository.Assignment = f.Repository.Assignment with { EndedAt = DateTime.UnixEpoch };
        var result = await new GetSupervisorAssignmentQueryHandler(f.Workflow).Handle(new(1), default);
        Assert.NotNull(result.EndedAt);
        var page = await new GetSupervisorAssignmentsQueryHandler(f.Workflow).Handle(new(null, "ENDED", 2, 10), default);
        Assert.Single(page.Items);
        Assert.Equal(new SupervisorAssignmentSearch(null, 20, "ENDED", 2, 10), f.Repository.LastSearch);
        await Assert.ThrowsAsync<ForbiddenException>(() => new GetSupervisorAssignmentsQueryHandler(f.Workflow).Handle(new(10), default));
    }

    private sealed class Fixture
    {
        public Repository Repository { get; } = new();
        public ProjectAccess ProjectAccess { get; } = new();
        public Audit Audit { get; }
        public SupervisorAssignmentWorkflow Workflow { get; }
        public Fixture(string role = AppRoles.Lecturer, long user = 20, bool scope = true)
        {
            Audit = new(Repository);
            var profiles = new Profiles(new(user, 1, true, scope, [role]));
            Workflow = new(Repository, new(new Actor(user), profiles), ProjectAccess, Audit, TimeProvider.System);
        }
    }

    private sealed class Actor(long id) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => id;
        public string? Email => null;
        public string? FullName => null;
        public IReadOnlyCollection<string> Roles => [AppRoles.Admin];
    }

    private sealed class Audit(Repository repository) : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Assert.True(repository.InTransaction);
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class ProjectAccess : IProjectAccessService
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default) => Task.FromResult(Allowed);
    }

    private sealed class Repository : ISupervisorAssignmentRepository
    {
        public SupervisorAssignmentModel Assignment { get; set; } = new(1, 10, 5, 20, "Lecturer", 2, true,
            DateTime.UnixEpoch, null, "COMPLETED");
        public bool DepartmentMatches { get; set; } = true;
        public bool InTransaction { get; private set; }
        public int Writes { get; private set; }
        public SupervisorAssignmentSearch? LastSearch { get; private set; }
        public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
        {
            InTransaction = true;
            try { return await action(ct); }
            finally { InTransaction = false; }
        }
        public Task LockAsync(long id, CancellationToken ct)
        {
            Assert.True(InTransaction);
            return Task.CompletedTask;
        }
        public Task<SupervisorAssignmentModel?> GetAsync(long id, CancellationToken ct) => Task.FromResult<SupervisorAssignmentModel?>(Assignment);
        public Task<bool> ProjectExistsAsync(long id, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> IsProjectDepartmentAsync(long id, long department, CancellationToken ct) => Task.FromResult(DepartmentMatches);
        public Task<PagedResult<SupervisorAssignmentModel>> SearchAsync(SupervisorAssignmentSearch search, CancellationToken ct)
        {
            LastSearch = search;
            return Task.FromResult(new PagedResult<SupervisorAssignmentModel>([Assignment], search.Page, search.PageSize, 1));
        }
        public Task<SupervisorAssignmentModel> EndAsync(long id, DateTime now, CancellationToken ct)
        {
            Assert.True(InTransaction);
            Writes++;
            Assignment = Assignment with { EndedAt = now };
            return Task.FromResult(Assignment);
        }
    }

    private sealed class Profiles(SupervisorAccount account) : ISupervisorProfileRepository
    {
        public Task<SupervisorAccount?> GetAccountAsync(long id, CancellationToken ct) => Task.FromResult<SupervisorAccount?>(account);
        public Task<SupervisorProfileModel?> GetAsync(long id, CancellationToken ct) => throw new NotSupportedException();
        public Task<SupervisorProfileModel?> GetByUserAsync(long id, CancellationToken ct) => throw new NotSupportedException();
        public Task<PagedResult<SupervisorProfileModel>> SearchAsync(SupervisorSearch search, CancellationToken ct) => throw new NotSupportedException();
        public Task<SupervisorProfileModel> UpsertAsync(long id, string? bio, bool available, DateTime now, CancellationToken ct) => throw new NotSupportedException();
        public Task<SupervisorProfileModel> ReplaceExpertiseAsync(long id, IReadOnlyList<SupervisorExpertiseModel> expertise, DateTime now, CancellationToken ct) => throw new NotSupportedException();
        public Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct) => throw new NotSupportedException();
    }
}
