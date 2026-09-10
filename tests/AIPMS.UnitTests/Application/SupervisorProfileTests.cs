using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.Commands;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Application.Features.Supervisors.Queries;
using AIPMS.Application.Features.Supervisors.Services;
using AIPMS.Application.Features.Supervisors.Validators;

namespace AIPMS.UnitTests.Application;

public sealed class SupervisorProfileTests
{
    [Fact]
    public async Task Candidate_handler_applies_policy_and_normalizes_filters()
    {
        var accounts = new StubRepository();
        accounts.Accounts[1] = new(1, 10, true, true, [AppRoles.Student]);
        var candidates = new CandidateRepositoryStub();
        var handler = new GetSupervisorCandidatesQueryHandler(candidates,
            new SupervisorAccessService(new Actor(1), accounts), new ProjectAccessStub(true), TimeProvider.System);
        var result = await handler.Handle(new(7, " Lecturer ", " AI ", 2, 5), default);
        Assert.Equal(new SupervisorCandidateSearch(7, 3, candidates.Project.DepartmentIds, 5,
            "Lecturer", "AI", 2, 5), candidates.Search);
        var candidate = Assert.Single(result.Items);
        Assert.Equal(1, candidate.RemainingSlots);
        Assert.Equal(4, candidate.SelectionPeriodId);
        Assert.Equal(2, result.Page);
        Assert.Equal(12, result.TotalCount);
    }

    [Fact]
    public async Task Candidate_handler_does_not_load_project_or_workload_when_access_is_denied()
    {
        var accounts = new StubRepository();
        accounts.Accounts[1] = new(1, 10, true, true, [AppRoles.Student]);
        var candidates = new CandidateRepositoryStub();
        var handler = new GetSupervisorCandidatesQueryHandler(candidates,
            new SupervisorAccessService(new Actor(1), accounts), new ProjectAccessStub(false), TimeProvider.System);
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new(7), default));
        Assert.False(candidates.ProjectRead);
        Assert.Null(candidates.Search);
    }

    private sealed class ProjectAccessStub(bool allowed) : IProjectAccessService
    {
        public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(allowed);
    }

    private sealed class CandidateRepositoryStub : ISupervisorCandidateRepository
    {
        public SupervisorCandidateProject Project { get; } = new(7, 3, "APPROVED", true, false, [10]);
        public bool ProjectRead { get; private set; }
        public SupervisorCandidateSearch? Search { get; private set; }
        public Task<SupervisorCandidateProject?> GetProjectAsync(long projectId, DateTime now, CancellationToken ct)
        {
            ProjectRead = true;
            return Task.FromResult<SupervisorCandidateProject?>(Project);
        }
        public Task<IReadOnlyList<SupervisorSelectionPolicy>> GetSelectionPoliciesAsync(long semesterId, DateTime now, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SupervisorSelectionPolicy>>([new(4, 5)]);
        public Task<PagedResult<SupervisorCandidateModel>> SearchAsync(SupervisorCandidateSearch search, CancellationToken ct)
        {
            Search = search;
            return Task.FromResult(new PagedResult<SupervisorCandidateModel>(
                [new(new(2, 9, "Lecturer", 10, "IT", null, true, [new("AI", null)]), 3, 2, 1)],
                search.Page, search.PageSize, 12));
        }
    }

    [Theory]
    [InlineData(AppRoles.Admin, 99, true)]
    [InlineData(AppRoles.DepartmentStaff, 10, true)]
    [InlineData(AppRoles.DepartmentStaff, 20, false)]
    [InlineData(AppRoles.Student, 10, false)]
    [InlineData(AppRoles.Lecturer, 10, false)]
    public async Task Edit_enforces_persisted_role_and_department(string role, long department, bool allowed)
    {
        var repository = new StubRepository();
        repository.Accounts[1] = new(1, department, true, true, [role]);
        repository.Accounts[2] = new(2, 10, true, true, [AppRoles.Lecturer]);
        var service = new SupervisorAccessService(new Actor(1), repository);
        if (allowed) Assert.Equal(1, await service.EnsureCanEditAsync(2, default));
        else await Assert.ThrowsAsync<ForbiddenException>(() => service.EnsureCanEditAsync(2, default));
    }

    [Fact]
    public async Task Lecturer_can_edit_self_but_inactive_account_cannot()
    {
        var repository = new StubRepository();
        repository.Accounts[1] = new(1, 10, true, true, [AppRoles.Lecturer]);
        var service = new SupervisorAccessService(new Actor(1), repository);
        Assert.Equal(1, await service.EnsureCanEditAsync(1, default));
        repository.Accounts[1] = repository.Accounts[1] with { IsActive = false };
        await Assert.ThrowsAsync<ForbiddenException>(() => service.EnsureCanEditAsync(1, default));
    }

    [Fact]
    public async Task Anonymous_cannot_read()
    {
        var service = new SupervisorAccessService(new Actor(null), new StubRepository());
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.EnsureCanReadAsync(default));
    }

    [Theory]
    [InlineData(false, true, AppRoles.Lecturer)]
    [InlineData(true, false, AppRoles.Lecturer)]
    [InlineData(true, true, AppRoles.Student)]
    public async Task Admin_cannot_provision_invalid_supervisor(bool active, bool scope, string role)
    {
        var repository = new StubRepository();
        repository.Accounts[1] = new(1, null, true, false, [AppRoles.Admin]);
        repository.Accounts[2] = new(2, 10, active, scope, [role]);
        var service = new SupervisorAccessService(new Actor(1), repository);
        await Assert.ThrowsAsync<ConflictException>(() => service.EnsureCanEditAsync(2, default));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 101)]
    [InlineData(1_000_001, 20)]
    public void Invalid_pagination_is_rejected(int page, int size) => Assert.False(
        new GetSupervisorsQueryValidator().Validate(new GetSupervisorsQuery(Page: page, PageSize: size)).IsValid);

    [Fact]
    public void Expertise_validation_handles_null_duplicate_and_length()
    {
        var validator = new ReplaceSupervisorExpertiseCommandValidator();
        Assert.False(validator.Validate(new ReplaceSupervisorExpertiseCommand(1, null!)).IsValid);
        Assert.False(validator.Validate(new ReplaceSupervisorExpertiseCommand(1, [null!])).IsValid);
        Assert.False(validator.Validate(new ReplaceSupervisorExpertiseCommand(1, [new("AI", null), new(" ai ", null)])).IsValid);
        Assert.False(validator.Validate(new ReplaceSupervisorExpertiseCommand(1, [new(" ", null)])).IsValid);
        Assert.False(validator.Validate(new ReplaceSupervisorExpertiseCommand(1, [new(new string('a', 256), null)])).IsValid);
        Assert.False(validator.Validate(new ReplaceSupervisorExpertiseCommand(1, [new("AI", new string('a', 51))])).IsValid);
        Assert.True(validator.Validate(new ReplaceSupervisorExpertiseCommand(1, [])).IsValid);
        Assert.True(validator.Validate(new ReplaceSupervisorExpertiseCommand(1, [new("AI", "Advanced")])).IsValid);
        Assert.False(new UpdateSupervisorProfileCommandValidator().Validate(new UpdateSupervisorProfileCommand(1, new string('x', 4001), true)).IsValid);
    }

    [Fact]
    public async Task Profile_handler_normalizes_and_records_before_after_in_transaction()
    {
        var repository = new StubRepository();
        repository.Accounts[1] = new(1, 10, true, true, [AppRoles.Lecturer]);
        var audit = new AuditSpy(repository);
        var handler = new UpdateSupervisorProfileCommandHandler(repository,
            new SupervisorAccessService(new Actor(1), repository), audit, TimeProvider.System);
        var result = await handler.Handle(new(1, "  Bio  ", false), default);
        Assert.Equal("Bio", result.Bio);
        Assert.False(result.IsAvailable);
        Assert.Equal("SUPERVISOR_PROFILE_CREATED", audit.Entry!.Action);
        Assert.Null(audit.Entry.Context["before"]);
        Assert.NotNull(audit.Entry.Context["after"]);
        await handler.Handle(new(1, "  ", true), default);
        Assert.Equal("SUPERVISOR_PROFILE_UPDATED", audit.Entry.Action);
        Assert.NotNull(audit.Entry.Context["before"]);
        Assert.Null(repository.Profile!.Bio);
    }

    private sealed class Actor(long? id) : ICurrentUser
    {
        public bool IsAuthenticated => id.HasValue;
        public long? UserId => id;
        public string? Email => null;
        public string? FullName => null;
        public IReadOnlyCollection<string> Roles => []; // Permissions are resolved from DB, not these claims.
    }

    private sealed class AuditSpy(StubRepository repository) : IAuditTrail
    {
        public AuditEntry? Entry { get; private set; }
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Assert.True(repository.InTransaction);
            Entry = entry;
            return Task.CompletedTask;
        }
    }

    private sealed class StubRepository : ISupervisorProfileRepository
    {
        public Dictionary<long, SupervisorAccount> Accounts { get; } = new();
        public SupervisorProfileModel? Profile { get; set; }
        public bool InTransaction { get; private set; }
        public Task<SupervisorAccount?> GetAccountAsync(long id, CancellationToken ct) => Task.FromResult(Accounts.GetValueOrDefault(id));
        public Task<SupervisorProfileModel?> GetAsync(long id, CancellationToken ct) => Task.FromResult(Profile);
        public Task<SupervisorProfileModel?> GetByUserAsync(long id, CancellationToken ct) => Task.FromResult(Profile);
        public Task<PagedResult<SupervisorProfileModel>> SearchAsync(SupervisorSearch search, CancellationToken ct) =>
            Task.FromResult(new PagedResult<SupervisorProfileModel>(Profile is null ? [] : [Profile], search.Page, search.PageSize, Profile is null ? 0 : 1));
        public Task<SupervisorProfileModel> UpsertAsync(long id, string? bio, bool available, DateTime now, CancellationToken ct)
        {
            Assert.True(InTransaction);
            Profile = new(1, id, "Lecturer", 10, "IT", bio, available, []);
            return Task.FromResult(Profile);
        }
        public Task<SupervisorProfileModel> ReplaceExpertiseAsync(long id, IReadOnlyList<SupervisorExpertiseModel> items, DateTime now, CancellationToken ct) =>
            throw new NotSupportedException();
        public async Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct)
        {
            InTransaction = true;
            try { return await action(); }
            finally { InTransaction = false; }
        }
    }
}
