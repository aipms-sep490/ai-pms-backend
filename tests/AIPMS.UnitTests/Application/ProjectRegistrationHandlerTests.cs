using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.Commands;
using AIPMS.Application.Features.Projects.DTOs;

namespace AIPMS.UnitTests.Application;

public sealed class ProjectRegistrationHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("create")]
    [InlineData("submit")]
    [InlineData("resubmit")]
    public async Task Registration_handler_checks_guard_before_writing_or_auditing(string operation)
    {
        var repository = new StubProjectRepository { UserActiveTeamId = 1, IsLeader = true, IsTeamEligible = true };
        var guard = new StubRegistrationGuard(repository) { Reject = true };
        var audit = new RegistrationAudit(guard);
        var user = new TestCurrentUser(10, AppRoles.Student);
        var clock = new FakeTimeProvider(Now);
        var draft = Draft(operation == "resubmit" ? "REVISION_REQUIRED" : "DRAFT");
        repository.Projects[draft.Id] = draft;
        await Assert.ThrowsAsync<ConflictException>(async () =>
        {
            if (operation == "create")
                await new CreateProjectDraftCommandHandler(repository, user, audit, guard, clock)
                    .Handle(new("Title", null, "Objectives", "Problem", "Output", [301], "Domain", [".NET"], ["Project"]), default);
            else if (operation == "submit")
                await new SubmitProjectCommandHandler(repository, user, audit, clock, guard).Handle(new(draft.Id, "token"), default);
            else
                await new ResubmitProjectCommandHandler(repository, user, audit, clock, guard).Handle(new(draft.Id, "token"), default);
        });
        Assert.Equal(1, guard.Validations);
        Assert.False(guard.Committed);
        Assert.Equal(0, audit.Calls);
        Assert.Single(repository.Projects);
        Assert.Equal(draft.Status, repository.Projects[draft.Id].Status);
        Assert.Empty(repository.StatusHistories);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("submit")]
    [InlineData("resubmit")]
    public async Task Registration_handler_audits_before_transaction_commit(string operation)
    {
        var repository = new StubProjectRepository { UserActiveTeamId = 1, IsLeader = true };
        var guard = new StubRegistrationGuard(repository);
        var audit = new RegistrationAudit(guard);
        var user = new TestCurrentUser(10, AppRoles.Student);
        var clock = new FakeTimeProvider(Now);
        var draft = Draft(operation == "resubmit" ? "REVISION_REQUIRED" : "DRAFT");
        repository.Projects[draft.Id] = draft;
        if (operation == "create")
            await new CreateProjectDraftCommandHandler(repository, user, audit, guard, clock)
                .Handle(new("Title", null, "Objectives", "Problem", "Output", [301], "Domain", [".NET"], ["Project"]), default);
        else if (operation == "submit")
            await new SubmitProjectCommandHandler(repository, user, audit, clock, guard).Handle(new(draft.Id, "token"), default);
        else
            await new ResubmitProjectCommandHandler(repository, user, audit, clock, guard).Handle(new(draft.Id, "token"), default);
        Assert.Equal(1, guard.Validations);
        Assert.True(guard.Committed);
        Assert.Equal(1, audit.Calls);
    }

    private static ProjectDto Draft(string status) => new(
        50, 1, "Team", "PRJ", "Title", null, "Objectives", status,
        Now, null, null, null, 10, "Leader", Now, Now, "Problem", "Output", "token",
        [new ProjectMajorDto(1, 301, "SE", "Software Engineering")],
        [new ProjectTagDto(1, "Education", "DOMAIN"), new ProjectTagDto(2, ".NET", "TECHNOLOGY"), new ProjectTagDto(3, "Project", "KEYWORD")]);

    private sealed class RegistrationAudit(StubRegistrationGuard guard) : IAuditTrail
    {
        public int Calls { get; private set; }
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Assert.True(guard.InTransaction);
            Assert.False(guard.Committed);
            Calls++;
            return Task.CompletedTask;
        }
    }
}
