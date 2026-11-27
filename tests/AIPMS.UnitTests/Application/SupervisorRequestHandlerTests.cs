using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.Commands;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Application.Features.Supervisors.Services;

namespace AIPMS.UnitTests.Application;

public sealed class SupervisorRequestHandlerTests
{
    [Fact]
    public async Task Accept_replay_returns_original_decision_without_loading_eligibility_or_writing()
    {
        var requests = new RequestRepository();
        var handler = new AcceptSupervisorRequestCommandHandler(Workflow(requests));
        var result = await handler.Handle(new(1, "Changed replay message"), default);
        Assert.Equal("ACCEPTED", result.Status);
        Assert.Equal("Original response", result.ResponseMessage);
        Assert.Equal(99, result.AssignmentId);
        Assert.Equal(1, requests.Locks);
    }

    [Theory]
    [InlineData("ACCEPTED", null)]
    [InlineData("REJECTED", null)]
    [InlineData("CANCELLED", null)]
    public async Task Accept_never_repairs_inconsistent_or_overwrites_other_final_decisions(string status, long? assignmentId)
    {
        var requests = new RequestRepository();
        requests.Request = requests.Request with { Status = status, AssignmentId = assignmentId };
        var handler = new AcceptSupervisorRequestCommandHandler(Workflow(requests));
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new(1, null), default));
    }

    [Fact]
    public async Task Even_a_replay_requires_the_persisted_recipient()
    {
        var handler = new AcceptSupervisorRequestCommandHandler(Workflow(new RequestRepository(), userId: 30));
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new(1, null), default));
    }

    [Fact]
    public async Task Admin_role_is_not_permission_to_send_for_a_team()
    {
        var handler = new SendSupervisorRequestCommandHandler(Workflow(new RequestRepository(), role: AppRoles.Admin));
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new(10, 5, null), default));
    }

    [Fact]
    public async Task Accept_rejects_lecturer_already_holding_conflicting_role_on_same_project()
    {
        var requests = new RequestRepository
        {
            Request = new(1, 10, 5, 20, 2, "PENDING", "Invite mentor", null, DateTime.UnixEpoch, null, null, "DISCIPLINE_MENTOR", 7),
            HasActiveConflictingRole = true,
        };
        var handler = new AcceptSupervisorRequestCommandHandler(Workflow(requests, withEligibility: true));
        var error = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new(1, null), default));
        Assert.Equal(WorkflowErrorCodes.SupervisorRoleConflict, error.Code);
    }

    private static SupervisorRequestWorkflow Workflow(RequestRepository requests, long userId = 20, string role = AppRoles.Lecturer, bool withEligibility = false)
    {
        var profiles = new ProfileRepository(role, withEligibility);
        return new(requests, new CandidateRepository(withEligibility), profiles,
            new SupervisorAccessService(new Actor(userId), profiles), new ProjectAccess(), new Audit(), TimeProvider.System,
            new RecordingPublisher(() => Assert.Fail("Replays and rejected operations must not publish notifications.")));
    }

    private sealed class Actor(long userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => userId;
        public string? Email => null;
        public string? FullName => null;
        public IReadOnlyCollection<string> Roles => [AppRoles.Admin];
    }

    private sealed class ProfileRepository(string role, bool withEligibility = false) : ISupervisorProfileRepository
    {
        public Task<SupervisorAccount?> GetAccountAsync(long userId, CancellationToken ct) =>
            Task.FromResult<SupervisorAccount?>(new(userId, 1, true, true, [role]));
        public Task<SupervisorProfileModel?> GetAsync(long id, CancellationToken ct,
            long? authorizedOrganizationId = null, long? authorizedDepartmentId = null) => withEligibility
                ? Task.FromResult<SupervisorProfileModel?>(new(id, 20, "Lecturer", 1, "SE", "Bio", true, [new("SE", "EXPERT")]))
                : throw new NotSupportedException();
        public Task<SupervisorProfileModel?> GetByUserAsync(long id, CancellationToken ct) => throw new NotSupportedException();
        public Task<PagedResult<SupervisorProfileModel>> SearchAsync(SupervisorSearch search, CancellationToken ct) => throw new NotSupportedException();
        public Task<SupervisorProfileModel> UpsertAsync(long id, string? bio, bool available, DateTime now, CancellationToken ct) => throw new NotSupportedException();
        public Task<SupervisorProfileModel> ReplaceExpertiseAsync(long id, IReadOnlyList<SupervisorExpertiseModel> expertise, DateTime now, CancellationToken ct) => throw new NotSupportedException();
        public Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class CandidateRepository(bool withEligibility = false) : ISupervisorCandidateRepository
    {
        public Task<SupervisorCandidateProject?> GetProjectAsync(long id, DateTime now, CancellationToken ct) => withEligibility
            ? Task.FromResult<SupervisorCandidateProject?>(new(id, 1, "DRAFT", true, true, [1], [7], [new(7, 1, "SE", "Software Engineering")], []))
            : throw new NotSupportedException();
        public Task<IReadOnlyList<SupervisorSelectionPolicy>> GetSelectionPoliciesAsync(long id, DateTime now, CancellationToken ct, bool execution = false) => withEligibility
            ? Task.FromResult<IReadOnlyList<SupervisorSelectionPolicy>>([new(1, 5)])
            : throw new NotSupportedException();
        public Task<PagedResult<SupervisorCandidateModel>> SearchAsync(SupervisorCandidateSearch search, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class ProjectAccess : IProjectAccessService
    {
        public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Audit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RequestRepository : ISupervisorRequestRepository
    {
        public SupervisorRequestModel Request { get; set; } = new(1, 10, 5, 20, 2, "ACCEPTED",
            "Original request", "Original response", DateTime.UnixEpoch, DateTime.UnixEpoch, 99);
        public bool HasActiveConflictingRole { get; set; }
        public int Locks { get; private set; }
        private bool inTransaction;
        public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
        {
            inTransaction = true;
            try { return await action(ct); }
            finally { inTransaction = false; }
        }
        public Task LockRequestAsync(long id, CancellationToken ct)
        {
            Assert.True(inTransaction);
            Locks++;
            return Task.CompletedTask;
        }
        public Task<SupervisorRequestModel?> GetAsync(long id, CancellationToken ct) => Task.FromResult<SupervisorRequestModel?>(Request);
        public Task LockSupervisorAndProjectAsync(long profileId, long projectId, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> IsTeamLeaderAsync(long projectId, long userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PagedResult<SupervisorRequestModel>> SearchAsync(SupervisorRequestSearch search, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> HasPendingAsync(long projectId, long profileId, CancellationToken ct, string assignmentType = "PRIMARY", long? majorId = null) => throw new NotSupportedException();
        public Task<bool> HasActiveConflictingRoleAssignmentAsync(long projectId, long profileId, string assignmentType, CancellationToken ct) => Task.FromResult(HasActiveConflictingRole);
        public Task<SupervisorWorkload> GetWorkloadAsync(long profileId, long semesterId, CancellationToken ct, long? excludeProjectId = null) => Task.FromResult(new SupervisorWorkload(5, 0, 0));
        public Task<SupervisorRequestModel> CreateAsync(long projectId, long profileId, long actorId, string? message, DateTime now, CancellationToken ct) => throw new NotSupportedException();
        public Task<SupervisorRequestModel> CreateAsync(long projectId, long profileId, long actorId, string? message, DateTime now, CancellationToken ct, string assignmentType, long? majorId) => throw new NotSupportedException();
        public Task<SupervisorRequestModel> RespondAsync(long requestId, string status, string? message, DateTime now, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> AssignAndActivateAsync(SupervisorRequestModel request, long actorId, DateTime now, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SupervisorRequestModel>> GetOtherPendingAsync(long projectId, long requestId, CancellationToken ct) => throw new NotSupportedException();
    }
}
