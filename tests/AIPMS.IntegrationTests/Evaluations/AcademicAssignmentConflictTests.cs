using System.Data;
using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services.Projects;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class EvaluationDraftEndpointTests
{
    [Fact]
    public async Task D17_primary_is_excluded_before_paging_and_cannot_be_assigned()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        await using var db = database.CreateContext();
        var primary = await db.SupervisorAssignments.Where(a => a.ProjectId == s.ProjectId)
            .Select(a => a.SupervisorProfile.UserId).SingleAsync();
        var input = await ScopedRequest(staff, s, primary);
        await AssertCode(await staff.PostAsJsonAsync(AssignUrl(s.ProjectId), input), WorkflowErrorCodes.EvaluatorRoleConflict);
        var page = await Body<AIPMS.Application.Common.Models.PagedResult<EligibleEvaluatorDto>>(await staff.GetAsync(
            CandidatesUrl(s) + $"&componentId={input.ComponentId}&scope=COMMON&pageSize=1"));
        Assert.Equal(2, page.TotalCount);
        Assert.DoesNotContain(page.Items, x => x.UserId == primary);
        Assert.False(await db.Set<EvaluationAssignment>().AnyAsync(a => a.ProjectId == s.ProjectId));
        Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "EVALUATOR_ASSIGNED" && a.ActorUserId == s.Scope.Users.Staff));
    }

    [Fact]
    public async Task D17_existing_legacy_supervisor_may_finalize_without_allowing_new_legacy_assignments()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var evaluator = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var legacy = await SeedLegacySupervisorAssignment(staff, s);
        var draft = await Create(evaluator, legacy.Id);
        draft = await Body<EvaluationDraftDto>(await Save(evaluator, draft, s));
        Assert.Equal("FINALIZED", (await Body<EvaluationDraftDto>(await Finalize(evaluator, draft))).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(AssignUrl(s.ProjectId),
            await ScopedRequest(staff, s, type: "SUPERVISOR"))).StatusCode);
    }

    [Fact]
    public async Task TEST_P0_04_Revoked_and_expired_evaluation_assignments_return_403_ASSIGNMENT_REVOKED_without_writes()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var evaluator = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        var assigned = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync(AssignUrl(s.ProjectId),
            await ScopedRequest(staff, s, s.Scope.Users.Lecturer)));
        var draft = await Create(evaluator, assigned.Id);

        // 1. Expire ValidUntil before now -> Save, Finalize, Detail, Evidence all fail with 403 ASSIGNMENT_REVOKED.
        await using (var db = database.CreateContext())
        {
            var row = await db.Set<EvaluationAssignment>().SingleAsync(a => a.Id == assigned.Id);
            row.ValidFrom = EvaluationDraftDatabaseFixture.Now.AddDays(-2);
            row.ValidUntil = EvaluationDraftDatabaseFixture.Now.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        var saveExpired = await Save(evaluator, draft, s);
        Assert.Equal(HttpStatusCode.Forbidden, saveExpired.StatusCode);
        Assert.Contains(WorkflowErrorCodes.AssignmentRevoked, await saveExpired.Content.ReadAsStringAsync());

        var finalizeExpired = await Finalize(evaluator, draft);
        Assert.Equal(HttpStatusCode.Forbidden, finalizeExpired.StatusCode);
        Assert.Contains(WorkflowErrorCodes.AssignmentRevoked, await finalizeExpired.Content.ReadAsStringAsync());

        var detailExpired = await evaluator.GetAsync($"/api/v1/evaluation-assignments/{assigned.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, detailExpired.StatusCode);
        Assert.Contains(WorkflowErrorCodes.AssignmentRevoked, await detailExpired.Content.ReadAsStringAsync());

        var evidenceExpired = await evaluator.GetAsync($"/api/v1/evaluation-assignments/{assigned.Id}/evidence");
        Assert.Equal(HttpStatusCode.Forbidden, evidenceExpired.StatusCode);
        Assert.Contains(WorkflowErrorCodes.AssignmentRevoked, await evidenceExpired.Content.ReadAsStringAsync());

        // 2. Restore ValidUntil within window -> Save succeeds cleanly.
        await using (var db = database.CreateContext())
        {
            var row = await db.Set<EvaluationAssignment>().SingleAsync(a => a.Id == assigned.Id);
            row.ValidUntil = EvaluationDraftDatabaseFixture.Now.AddDays(1);
            await db.SaveChangesAsync();
        }
        draft = await Body<EvaluationDraftDto>(await Save(evaluator, draft, s));
        Assert.NotNull(draft.TotalScore);

        // 3. Revoke assignment -> Save & Finalize immediately return 403 ASSIGNMENT_REVOKED with zero new writes.
        var revokeRes = await staff.PostAsJsonAsync($"/api/v1/evaluation-assignments/{assigned.Id}/revoke",
            new RevokeEvaluatorRequest(assigned.ConcurrencyToken, "Immediate revocation test"));
        Assert.Equal(HttpStatusCode.OK, revokeRes.StatusCode);

        var saveRevoked = await Save(evaluator, draft, s);
        Assert.Equal(HttpStatusCode.Forbidden, saveRevoked.StatusCode);
        Assert.Contains(WorkflowErrorCodes.AssignmentRevoked, await saveRevoked.Content.ReadAsStringAsync());

        var finalizeRevoked = await Finalize(evaluator, draft);
        Assert.Equal(HttpStatusCode.Forbidden, finalizeRevoked.StatusCode);
        Assert.Contains(WorkflowErrorCodes.AssignmentRevoked, await finalizeRevoked.Content.ReadAsStringAsync());

        await using (var verify = database.CreateContext())
        {
            Assert.False(await verify.Set<EvaluationFinalization>().AnyAsync(f => f.EvaluationId == draft.Id));
            Assert.False(await verify.AuditLogs.AnyAsync(a => a.Action == "EVALUATION_FINALIZED" && a.EntityId == draft.Id.ToString()));
        }
    }
}

public sealed class AcademicAssignmentConflictTests(EvaluationDraftDatabaseFixture fixture) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    [Fact]
    public async Task Guard_requires_transaction_and_does_not_revoke_existing_assignments()
    {
        var s = await fixture.Seed();
        await using var db = fixture.CreateContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => AcademicAssignmentGuard.EnsureEvaluatorAsync(
            db, s.ProjectId, s.Scope.Users.Lecturer, "LECTURER", default));
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Add(Evaluator(s));
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ConflictException>(() => AcademicAssignmentGuard.EnsurePrimaryAsync(
            db, s.ProjectId, s.Scope.Users.ProfileId, default));
        Assert.Equal(AcademicAssignmentGuard.ConflictCode, error.Code);
        Assert.Equal("ACTIVE", (await db.Set<EvaluationAssignment>().SingleAsync(a => a.ProjectId == s.ProjectId)).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_primary_and_evaluator_creation_serializes_in_both_orders(bool primaryFirst)
    {
        var s = await fixture.Seed();
        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();
        await using var tx = await first.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await using var competingTx = await second.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        if (primaryFirst)
        {
            await AcademicAssignmentGuard.EnsurePrimaryAsync(first, s.ProjectId, s.Scope.Users.ProfileId, default);
            var primary = await first.SupervisorAssignments.SingleAsync(a => a.ProjectId == s.ProjectId);
            primary.SupervisorProfileId = s.Scope.Users.ProfileId;
        }
        else
        {
            await AcademicAssignmentGuard.EnsureEvaluatorAsync(first, s.ProjectId, s.Scope.Users.Lecturer, "LECTURER", default);
            first.Add(Evaluator(s));
        }
        await first.SaveChangesAsync();
        var competing = primaryFirst
            ? AcademicAssignmentGuard.EnsureEvaluatorAsync(second, s.ProjectId, s.Scope.Users.Lecturer, "LECTURER", default)
            : AcademicAssignmentGuard.EnsurePrimaryAsync(second, s.ProjectId, s.Scope.Users.ProfileId, default);
        Assert.NotSame(competing, await Task.WhenAny(competing, Task.Delay(150)));
        await tx.CommitAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => competing);
        Assert.Equal(AcademicAssignmentGuard.ConflictCode, conflict.Code);
        await competingTx.RollbackAsync();
        Assert.Equal(primaryFirst ? 0 : 1, await second.Set<EvaluationAssignment>().CountAsync(a => a.ProjectId == s.ProjectId));
    }

    [Fact]
    public async Task Mentor_and_revoked_evaluator_do_not_block_the_opposite_assignment()
    {
        var s = await fixture.Seed();
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var primary = await db.SupervisorAssignments.SingleAsync(a => a.ProjectId == s.ProjectId);
        primary.IsPrimary = false;
        primary.AssignmentType = "DISCIPLINE_MENTOR";
        primary.MajorId = await db.ProjectMajors.Where(p => p.ProjectId == s.ProjectId).Select(p => p.MajorId).SingleAsync();
        primary.SupervisorProfileId = s.Scope.Users.ProfileId;
        var assignment = Evaluator(s);
        assignment.Status = "REVOKED";
        assignment.RevokedAt = EvaluationDraftDatabaseFixture.Now;
        assignment.RevocationReason = "Fixture revocation";
        db.Add(assignment);
        await db.SaveChangesAsync();
        await AcademicAssignmentGuard.EnsureEvaluatorAsync(db, s.ProjectId, s.Scope.Users.Lecturer, "LECTURER", default);
        await AcademicAssignmentGuard.EnsurePrimaryAsync(db, s.ProjectId, s.Scope.Users.ProfileId, default);
        Assert.Equal("REVOKED", assignment.Status);
    }

    private static EvaluationAssignment Evaluator(EvaluationScenario s) => new()
    {
        ProjectId = s.ProjectId, EvaluatorId = s.Scope.Users.Lecturer, ProjectPeriodId = s.PeriodId,
        RubricId = s.RubricId, DepartmentId = s.Scope.Users.DepartmentId, EvaluationType = "LECTURER",
        AssignedBy = s.Scope.Users.Staff, AssignedAt = EvaluationDraftDatabaseFixture.Now, Status = "ACTIVE",
        ConcurrencyToken = Guid.NewGuid()
    };
}
