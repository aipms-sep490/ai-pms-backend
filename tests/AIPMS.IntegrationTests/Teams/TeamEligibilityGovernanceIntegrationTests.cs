using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Domain.Projects;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Project = AIPMS.Infrastructure.Persistence.Generated.Models.Project;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Teams;

public sealed class TeamEligibilityGovernanceIntegrationTests(TeamDatabaseFixture database) : IClassFixture<TeamDatabaseFixture>
{
    private static async Task<T> BodyAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<long> SeedReviewerAsync(TeamScenario scenario)
    {
        await using var context = database.CreateContext();
        var departmentId = await context.Majors.Where(m => m.Id == scenario.SeMajorId)
            .Select(m => m.DepartmentId).SingleAsync();
        var role = await context.Roles.SingleOrDefaultAsync(r => r.Code == "DEPARTMENT_STAFF")
            ?? new Role { Code = "DEPARTMENT_STAFF", Name = "Department Staff", IsSystemRole = true };
        var reviewer = new User
        {
            Email = $"reviewer-{Guid.NewGuid():N}@example.test", FullName = "Reviewer",
            PasswordHash = "unused-test-hash", Status = "ACTIVE", DepartmentId = departmentId,
            UserRoleUsers = new List<UserRole> { new() { Role = role } }
        };
        context.Users.Add(reviewer);
        await context.SaveChangesAsync();
        return reviewer.Id;
    }

    // ==========================================
    // CHECK WORKFLOW (14 - 19) & STATUS MATRIX (20 - 23)
    // ==========================================

    [Fact]
    public async Task Test14_15_ExplicitCheck_PassAndFail_CreatesSnapshotAndDeterministicIssues()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 2, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientMember = app.CreateAuthenticatedClient(s.Students[1]);

        // 1. Create team (single member -> FAIL because minMembers = 2)
        var createRes = await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TCHK1", name = "Check Team 1", description = "Test" });
        var team = await BodyAsync<TeamDto>(createRes);

        // Explicit Check -> FAIL (Too few members)
        var checkFailRes = await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);
        var checkFail = await BodyAsync<TeamEligibilityCheckDto>(checkFailRes);

        Assert.Equal("FAIL", checkFail.Result);
        Assert.Equal("CURRENT", checkFail.Freshness);
        Assert.Equal("FORMATION", checkFail.RoundType);
        Assert.NotEmpty(checkFail.Issues);
        Assert.Contains(checkFail.Issues, i => i.RuleCode == "TOO_FEW_MEMBERS");

        // Status matrix: FORMING + FAIL => FORMING
        var teamAfterFail = await BodyAsync<TeamDto>(await clientLeader.GetAsync($"/api/v1/teams/{team.Id}"));
        Assert.Equal("FORMING", teamAfterFail.Status);

        // 2. Add second member -> PASS
        var inviteRes = await clientLeader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = s.Students[1], message = "Join" });
        var invite = await BodyAsync<TeamInvitationDto>(inviteRes);
        await clientMember.PostAsync($"/api/v1/teams/invitations/{invite.Id}/accept", null);

        // Explicit Check -> PASS
        var checkPassRes = await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);
        var checkPass = await BodyAsync<TeamEligibilityCheckDto>(checkPassRes);

        Assert.Equal("PASS", checkPass.Result);
        Assert.Equal("CURRENT", checkPass.Freshness);
        Assert.Empty(checkPass.Issues);

        // Status matrix: FORMING + PASS => ELIGIBLE
        var teamAfterPass = await BodyAsync<TeamDto>(await clientLeader.GetAsync($"/api/v1/teams/{team.Id}"));
        Assert.Equal("ELIGIBLE", teamAfterPass.Status);
    }

    [Fact]
    public async Task Test16_17_Dedup_SameEvaluationKeyCreatesNoDuplicateSnapshot()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TDEDUP", name = "Dedup Team", description = "Test" }));

        // First Check
        var check1 = await BodyAsync<TeamEligibilityCheckDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));

        // Second Check with same facts
        var check2 = await BodyAsync<TeamEligibilityCheckDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));

        // Assert same CheckId (deduplicated)
        Assert.Equal(check1.CheckId, check2.CheckId);

        // Verify database has exactly 1 snapshot
        await using var ctx = database.CreateContext();
        var snapshotCount = await ctx.TeamEligibilityChecks.CountAsync(c => c.TeamId == team.Id);
        Assert.Equal(1, snapshotCount);
    }

    [Fact]
    public async Task Test18_19_RefreshDelegatesSameWorkflow_AndLockedTeamRemainsLocked()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TREFR", name = "Refresh Team", description = "Test" }));

        // Check -> ELIGIBLE
        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        // Lock -> LOCKED
        var lockedTeam = await BodyAsync<TeamDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/lock", null));
        Assert.Equal("LOCKED", lockedTeam.Status);

        // Refresh on LOCKED team -> remains LOCKED
        var refreshedTeam = await BodyAsync<TeamDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/refresh", null));
        Assert.Equal("LOCKED", refreshedTeam.Status);
    }

    // ==========================================
    // STATUS / ROSTER GUARD (20 - 24)
    // ==========================================

    [Fact]
    public async Task Test20_23_StatusTransitions_FormingEligibleLocked()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TSTAT", name = "Status Team", description = "Test" }));

        // 20. FORMING + PASS -> ELIGIBLE
        var checkPass = await BodyAsync<TeamEligibilityCheckDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        Assert.Equal("PASS", checkPass.Result);
        var teamAfterCheck = await BodyAsync<TeamDto>(await clientLeader.GetAsync($"/api/v1/teams/{team.Id}"));
        Assert.Equal("ELIGIBLE", teamAfterCheck.Status);

        // 22. LOCKED + PASS -> LOCKED
        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/lock", null);
        var teamLocked = await BodyAsync<TeamDto>(await clientLeader.GetAsync($"/api/v1/teams/{team.Id}"));
        Assert.Equal("LOCKED", teamLocked.Status);

        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);
        var teamStillLocked = await BodyAsync<TeamDto>(await clientLeader.GetAsync($"/api/v1/teams/{team.Id}"));
        Assert.Equal("LOCKED", teamStillLocked.Status);
    }

    [Fact]
    public async Task Test24_RosterMutation_Denied_WhenAnotherProjectLocksRoster()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TROST", name = "Roster Team", description = "Test" }));

        // Attach a project in SUBMITTED status to the team
        await using (var ctx = database.CreateContext())
        {
            var project = new Project
            {
                TeamId = team.Id,
                Code = "PRJ-LOCK",
                Title = "Locking Project",
                Status = "SUBMITTED",
                RegisteredAt = DateTime.UtcNow,
                CreatedBy = s.Students[0],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            ctx.Projects.Add(project);
            await ctx.SaveChangesAsync();
        }

        // Try mutating roster (e.g. invite another member) -> Conflict 409
        var inviteRes = await clientLeader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = s.Students[1], message = "Join" });
        Assert.Equal(HttpStatusCode.Conflict, inviteRes.StatusCode);
    }

    // ==========================================
    // GET ELIGIBILITY & HISTORY (44)
    // ==========================================

    [Fact]
    public async Task Test44_GetEligibilityAndHistory_ZeroDbMutationsAndZeroAudits()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TGET", name = "Get Team", description = "Test" }));

        // Run Check once to have 1 snapshot
        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        await using (var ctx = database.CreateContext())
        {
            var checkCountBefore = await ctx.TeamEligibilityChecks.CountAsync(c => c.TeamId == team.Id);
            var auditCountBefore = await ctx.AuditLogs.CountAsync();

            // GET /eligibility
            var getRes = await clientLeader.GetAsync($"/api/v1/teams/{team.Id}/eligibility");
            Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);

            // GET /eligibility/history
            var histRes = await clientLeader.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
            Assert.Equal(HttpStatusCode.OK, histRes.StatusCode);
            var history = await BodyAsync<IReadOnlyList<TeamEligibilityCheckDto>>(histRes);
            Assert.Single(history);

            var checkCountAfter = await ctx.TeamEligibilityChecks.CountAsync(c => c.TeamId == team.Id);
            var auditCountAfter = await ctx.AuditLogs.CountAsync();

            // Zero mutations, zero audits
            Assert.Equal(checkCountBefore, checkCountAfter);
            Assert.Equal(auditCountBefore, auditCountAfter);
        }
    }

    // ==========================================
    // LOCK WORKFLOW & DENIAL AUDIT (45, 48)
    // ==========================================

    [Fact]
    public async Task Test45_48_LockDenial_EmitsExactlyOneDenialAudit_AndSurvivesRollback()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 2, maxMembers: 3); // Needs 2 members
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);

        // Create team with 1 member
        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TLOCKDENY", name = "Lock Deny Team", description = "Test" }));

        // Explicit Check -> produces FAIL snapshot
        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        // Attempt Lock on FAIL snapshot -> 409 Conflict
        var lockRes = await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/lock", null);
        Assert.Equal(HttpStatusCode.Conflict, lockRes.StatusCode);

        // Verify denial audit was written
        await using var ctx = database.CreateContext();
        var audits = await ctx.AuditLogs
            .Where(a => a.Action == "TEAM_ELIGIBILITY_LOCK_DENIED" && a.EntityId == team.Id.ToString())
            .ToListAsync();
        Assert.Single(audits);
    }

    // ==========================================
    // REVISION FLOW (30, 31, 32)
    // ==========================================

    [Fact]
    public async Task Test30_RevisionNoOtherLockingProject_TransitionsLockedToForming()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientAdmin = app.CreateAuthenticatedClient(s.Students[0], roles: ["ADMIN"]);
        using var clientReviewer = app.CreateAuthenticatedClient(await SeedReviewerAsync(s), roles: ["DEPARTMENT_STAFF"]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TREV1", name = "Revision Team 1", description = "Test" }));

        // Check & Lock team
        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);
        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/lock", null);

        var teamLocked = await BodyAsync<TeamDto>(await clientLeader.GetAsync($"/api/v1/teams/{team.Id}"));
        Assert.Equal("LOCKED", teamLocked.Status);

        // Create project in UNDER_REVIEW status
        long projectId;
        string concurrencyToken;
        await using (var ctx = database.CreateContext())
        {
            var project = new Project
            {
                TeamId = team.Id,
                Code = "PRJ-REV1",
                ProjectMajors = new List<ProjectMajor> { new() { MajorId = s.SeMajorId } },
                Title = "Revision Project",
                Status = "UNDER_REVIEW",
                RegisteredAt = DateTime.UtcNow,
                CreatedBy = s.Students[0],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            ctx.Projects.Add(project);
            await ctx.SaveChangesAsync();
            projectId = project.Id;
            concurrencyToken = Convert.ToBase64String(project.RowVersion);
            var department = await ctx.Majors.Where(m => m.Id == s.SeMajorId).Select(m => m.DepartmentId).SingleAsync();
            await AcademicSnapshotFixture.AddAsync(ctx, project.Id, s.PeriodId, department, s.Students[0], DateTime.UtcNow);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await clientAdmin.PostAsJsonAsync($"/api/v1/projects/{projectId}/revision",
            new { concurrencyToken, reason = "Admin cannot replace academic review." })).StatusCode);
        var revRes = await clientReviewer.PostAsJsonAsync($"/api/v1/projects/{projectId}/revision",
            new { concurrencyToken, reason = "Please revise problem statement." });
        Assert.Equal(HttpStatusCode.OK, revRes.StatusCode);

        // Verify: Team LOCKED -> FORMING in same transaction
        var teamAfterRevision = await BodyAsync<TeamDto>(await clientLeader.GetAsync($"/api/v1/teams/{team.Id}"));
        Assert.Equal("FORMING", teamAfterRevision.Status);
    }

    [Fact]
    public async Task Test31_RevisionWithAnotherLockingProject_TeamRemainsLocked()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientAdmin = app.CreateAuthenticatedClient(s.Students[0], roles: ["ADMIN"]);
        using var clientReviewer = app.CreateAuthenticatedClient(await SeedReviewerAsync(s), roles: ["DEPARTMENT_STAFF"]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TREV2", name = "Revision Team 2", description = "Test" }));

        // Check & Lock team
        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);
        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/lock", null);

        long projectId1;
        string concurrencyToken1;
        await using (var ctx = database.CreateContext())
        {
            // Project 1: to be revised
            var project1 = new Project
            {
                TeamId = team.Id,
                Code = "PRJ-REV2A",
                ProjectMajors = new List<ProjectMajor> { new() { MajorId = s.SeMajorId } },
                Title = "Revision Project 1",
                Status = "UNDER_REVIEW",
                RegisteredAt = DateTime.UtcNow,
                CreatedBy = s.Students[0],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            // Project 2: another project that locks the roster (COMPLETED locks roster according to TeamRules.ProjectLocksRoster)
            var project2 = new Project
            {
                TeamId = team.Id,
                Code = "PRJ-REV2B",
                Title = "Other Locking Project",
                Status = "COMPLETED",
                RegisteredAt = DateTime.UtcNow,
                CreatedBy = s.Students[0],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            ctx.Projects.AddRange(project1, project2);
            await ctx.SaveChangesAsync();
            projectId1 = project1.Id;
            concurrencyToken1 = Convert.ToBase64String(project1.RowVersion);
            var department = await ctx.Majors.Where(m => m.Id == s.SeMajorId).Select(m => m.DepartmentId).SingleAsync();
            await AcademicSnapshotFixture.AddAsync(ctx, project1.Id, s.PeriodId, department, s.Students[0], DateTime.UtcNow);
        }

        // Request revision on Project 1
        Assert.Equal(HttpStatusCode.Forbidden, (await clientAdmin.PostAsJsonAsync($"/api/v1/projects/{projectId1}/revision",
            new { concurrencyToken = concurrencyToken1, reason = "Admin cannot replace academic review." })).StatusCode);
        var revRes = await clientReviewer.PostAsJsonAsync($"/api/v1/projects/{projectId1}/revision",
            new { concurrencyToken = concurrencyToken1, reason = "Needs revision" });
        Assert.Equal(HttpStatusCode.OK, revRes.StatusCode);

        // Team must REMAIN LOCKED because Project 2 still locks roster
        var teamAfterRevision = await BodyAsync<TeamDto>(await clientLeader.GetAsync($"/api/v1/teams/{team.Id}"));
        Assert.Equal("LOCKED", teamAfterRevision.Status);
    }

    [Fact]
    public async Task Test32_OldRevisionSnapshot_CannotSatisfyNewRevisionRound()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TREV3", name = "Revision Team 3", description = "Test" }));

        // Create project in REVISION_REQUIRED with history round 1
        long projectId;
        long historyId1;
        long historyId2;
        await using (var ctx = database.CreateContext())
        {
            var project = new Project
            {
                TeamId = team.Id,
                Code = "PRJ-REV3",
                Title = "Multi-Revision Project",
                Status = "REVISION_REQUIRED",
                RegisteredAt = DateTime.UtcNow,
                CreatedBy = s.Students[0],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            ctx.Projects.Add(project);
            await ctx.SaveChangesAsync();
            projectId = project.Id;

            var h1 = new ProjectStatusHistory
            {
                ProjectId = projectId,
                OldStatus = "SUBMITTED",
                NewStatus = "REVISION_REQUIRED",
                ChangedBy = s.Students[0],
                Reason = "Round 1",
                ChangedAt = DateTime.UtcNow.AddMinutes(-30)
            };
            ctx.ProjectStatusHistories.Add(h1);
            await ctx.SaveChangesAsync();
            historyId1 = h1.Id;
        }

        // Run Check for Round 1 -> creates snapshot for historyId1
        var checkRound1 = await BodyAsync<TeamEligibilityCheckDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        Assert.Equal("REVISION", checkRound1.RoundType);
        Assert.Equal(historyId1, checkRound1.RevisionHistoryId);
        Assert.Equal("CURRENT", checkRound1.Freshness);

        // Advance to revision Round 2 (add new REVISION_REQUIRED history)
        await using (var ctx = database.CreateContext())
        {
            var h2 = new ProjectStatusHistory
            {
                ProjectId = projectId,
                OldStatus = "SUBMITTED",
                NewStatus = "REVISION_REQUIRED",
                ChangedBy = s.Students[0],
                Reason = "Round 2",
                ChangedAt = DateTime.UtcNow
            };
            ctx.ProjectStatusHistories.Add(h2);
            await ctx.SaveChangesAsync();
            historyId2 = h2.Id;
        }

        // Query history: Round 1 snapshot is now STALE against Round 2
        var histRes = await clientLeader.GetAsync($"/api/v1/teams/{team.Id}/eligibility/history");
        var history = await BodyAsync<IReadOnlyList<TeamEligibilityCheckDto>>(histRes);
        var snapshot1 = history.First(h => h.RevisionHistoryId == historyId1);
        Assert.Equal("STALE", snapshot1.Freshness);
    }

    // ==========================================
    // CONCURRENCY & RACE SCENARIOS
    // ==========================================

    [Fact]
    public async Task Test_ConcurrentSameCheck_DeduplicatesSnapshot_AndEmitsSingleAudit()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientMember = app.CreateAuthenticatedClient(s.Students[1]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TCC1", name = "Concurrent Check Team", description = "Test" }));

        // Add member so both leader and member are active team members
        var invite = await BodyAsync<TeamInvitationDto>(await clientLeader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = s.Students[1], message = "Join" }));
        await clientMember.PostAsync($"/api/v1/teams/invitations/{invite.Id}/accept", null);

        // Start two Check requests concurrently against same team and facts
        var t1 = clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);
        var t2 = clientMember.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);
        var responses = await Task.WhenAll(t1, t2);

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);

        var check1 = await BodyAsync<TeamEligibilityCheckDto>(responses[0]);
        var check2 = await BodyAsync<TeamEligibilityCheckDto>(responses[1]);

        Assert.Equal(check1.CheckId, check2.CheckId);

        await using var ctx = database.CreateContext();
        var snapshotCount = await ctx.TeamEligibilityChecks.CountAsync(c => c.TeamId == team.Id);
        Assert.Equal(1, snapshotCount);

        var auditCount = await ctx.AuditLogs
            .CountAsync(a => a.Action == "TEAM_ELIGIBILITY_CHECKED" && a.EntityId == check1.CheckId.ToString());
        Assert.Equal(1, auditCount);
    }

    [Fact]
    public async Task Test_ConcurrentLock_IsIdempotent_AndLeavesTeamLocked()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TCLK1", name = "Concurrent Lock Team", description = "Test" }));

        // Establish CURRENT PASS snapshot
        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        // Concurrently invoke Lock
        var t1 = clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/lock", null);
        var t2 = clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/lock", null);
        var responses = await Task.WhenAll(t1, t2);

        Assert.True(responses[0].StatusCode == HttpStatusCode.OK || responses[1].StatusCode == HttpStatusCode.OK);

        await using var ctx = database.CreateContext();
        var finalTeam = await ctx.Teams.SingleAsync(t => t.Id == team.Id);
        Assert.Equal("LOCKED", finalTeam.Status);

        var lockAudits = await ctx.AuditLogs
            .Where(a => a.Action == "TEAM_ELIGIBILITY_LOCKED" && a.EntityId == team.Id.ToString())
            .ToListAsync();
        Assert.True(lockAudits.Count is 1 or 2);
    }

    [Fact]
    public async Task Test_CheckRacingWithRosterMutation_ProducesCoherentSnapshot()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 1, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientMember = app.CreateAuthenticatedClient(s.Students[1]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TRACE1", name = "Race Team", description = "Test" }));

        var invite = await BodyAsync<TeamInvitationDto>(await clientLeader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = s.Students[1], message = "Join" }));

        // Race check vs invitation accept
        var checkTask = clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);
        var acceptTask = clientMember.PostAsync($"/api/v1/teams/invitations/{invite.Id}/accept", null);
        await Task.WhenAll(checkTask, acceptTask);

        await using var ctx = database.CreateContext();
        var snapshot = await ctx.TeamEligibilityChecks
            .Include(c => c.TeamEligibilityIssues)
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync(c => c.TeamId == team.Id);

        Assert.NotNull(snapshot);
        Assert.Equal("FORMATION", snapshot.RoundType);
        Assert.False(string.IsNullOrEmpty(snapshot.Fingerprint));
        Assert.False(string.IsNullOrEmpty(snapshot.EvaluationKey));
    }

    [Fact]
    public async Task Test_ResubmitRacingWithEligibilityInputMutation_DeniesStaleSubmission()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 2, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientMember = app.CreateAuthenticatedClient(s.Students[1]);

        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TRESRACE", name = "Resubmit Race Team", description = "Test" }));

        var invite = await BodyAsync<TeamInvitationDto>(await clientLeader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = s.Students[1], message = "Join" }));
        await clientMember.PostAsync($"/api/v1/teams/invitations/{invite.Id}/accept", null);

        long projectId;
        string token;
        long revHistoryId;
        await using (var ctx = database.CreateContext())
        {
            var project = new Project
            {
                TeamId = team.Id,
                Code = "PRJ-RR1",
                Title = "Resubmit Race Project",
                Status = "REVISION_REQUIRED",
                ProposalSource = "STUDENT_PROPOSAL",
                ProblemStatement = "Problem",
                Objectives = "Objectives",
                ExpectedOutput = "Output",
                RegisteredAt = DateTime.UtcNow,
                CreatedBy = s.Students[0],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            ctx.Projects.Add(project);
            await ctx.SaveChangesAsync();
            projectId = project.Id;
            token = Convert.ToBase64String(project.RowVersion);

            var h = new ProjectStatusHistory
            {
                ProjectId = projectId,
                OldStatus = "SUBMITTED",
                NewStatus = "REVISION_REQUIRED",
                ChangedBy = s.Students[0],
                Reason = "Revision required",
                ChangedAt = DateTime.UtcNow
            };
            ctx.ProjectStatusHistories.Add(h);
            await ctx.SaveChangesAsync();
            revHistoryId = h.Id;
        }

        // Run Check -> creates REVISION snapshot with CURRENT PASS
        var checkDto = await BodyAsync<TeamEligibilityCheckDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        Assert.Equal("PASS", checkDto.Result);
        Assert.Equal(revHistoryId, checkDto.RevisionHistoryId);

        // Mutate member to INACTIVE
        await using (var ctx = database.CreateContext())
        {
            await ctx.Users.Where(u => u.Id == s.Students[1]).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "INACTIVE"));
        }

        // Resubmit project -> guard must detect STALE / invalid profile and deny with Conflict
        var resubmitRes = await clientLeader.PostAsJsonAsync($"/api/v1/projects/{projectId}/resubmit", new { concurrencyToken = token });
        Assert.Equal(HttpStatusCode.Conflict, resubmitRes.StatusCode);

        // Project must remain REVISION_REQUIRED
        await using (var ctx = database.CreateContext())
        {
            var p = await ctx.Projects.SingleAsync(x => x.Id == projectId);
            Assert.Equal("REVISION_REQUIRED", p.Status);

            var denialAudits = await ctx.AuditLogs
                .Where(a => a.Action == "PROJECT_RESUBMISSION_GUARD_DENIED" && a.EntityId == projectId.ToString())
                .ToListAsync();
            Assert.Single(denialAudits);
        }
    }

    [Fact]
    public async Task Test_StaleSubmitAndResubmitDenial_EmitsExactlyOneAudit_AndSurvivesRollback()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, minMembers: 2, maxMembers: 3);
        using var clientLeader = app.CreateAuthenticatedClient(s.Students[0]);
        using var clientMember = app.CreateAuthenticatedClient(s.Students[1]);

        // --- Part A: Stale Submit Denial ---
        var team = await BodyAsync<TeamDto>(await clientLeader.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "TAUDIT1", name = "Audit Team 1", description = "Test" }));

        var invite = await BodyAsync<TeamInvitationDto>(await clientLeader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = s.Students[1], message = "Join" }));
        await clientMember.PostAsync($"/api/v1/teams/invitations/{invite.Id}/accept", null);

        // Create project draft
        var draftRes = await clientLeader.PostAsJsonAsync("/api/v1/projects", new
        {
            title = "Audit Submit Project",
            description = "Desc",
            objectives = "Objectives",
            problemStatement = "Problem",
            expectedOutput = "Output",
            requiredMajorIds = new[] { s.SeMajorId },
            domain = "Education",
            technologies = new[] { ".NET" },
            keywords = new[] { "Test" }
        });
        var draft = await BodyAsync<ProjectDto>(draftRes);

        // Check -> creates INITIAL PASS snapshot
        await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null);

        // Mutate member to INACTIVE -> makes snapshot STALE
        await using (var ctx = database.CreateContext())
        {
            await ctx.Users.Where(u => u.Id == s.Students[1]).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "INACTIVE"));
        }

        // Submit -> fails with 409 Conflict
        var submitRes = await clientLeader.PostAsJsonAsync($"/api/v1/projects/{draft.Id}/submit", new { concurrencyToken = draft.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, submitRes.StatusCode);

        await using (var ctx = database.CreateContext())
        {
            // Business changes rolled back: remains DRAFT, no status history
            var proj = await ctx.Projects.SingleAsync(p => p.Id == draft.Id);
            Assert.Equal("DRAFT", proj.Status);
            Assert.False(await ctx.ProjectStatusHistories.AnyAsync(h => h.ProjectId == draft.Id));

            // Exactly one denial audit emitted and survived rollback
            var submitDenialAudits = await ctx.AuditLogs
                .Where(a => a.Action == "PROJECT_SUBMISSION_GUARD_DENIED" && a.EntityId == draft.Id.ToString())
                .ToListAsync();
            Assert.Single(submitDenialAudits);
        }

        // --- Part B: Stale Resubmit Denial ---
        // Restore member to ACTIVE
        await using (var ctx = database.CreateContext())
        {
            await ctx.Users.Where(u => u.Id == s.Students[1]).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "ACTIVE"));
        }

        // Advance project to REVISION_REQUIRED
        long revHistoryId;
        string resubmitToken;
        await using (var ctx = database.CreateContext())
        {
            await ctx.Projects.Where(p => p.Id == draft.Id).ExecuteUpdateAsync(p => p.SetProperty(x => x.Status, "REVISION_REQUIRED"));
            var h = new ProjectStatusHistory
            {
                ProjectId = draft.Id,
                OldStatus = "SUBMITTED",
                NewStatus = "REVISION_REQUIRED",
                ChangedBy = s.Students[0],
                Reason = "Revision",
                ChangedAt = DateTime.UtcNow
            };
            ctx.ProjectStatusHistories.Add(h);
            await ctx.SaveChangesAsync();
            revHistoryId = h.Id;

            var p = await ctx.Projects.SingleAsync(x => x.Id == draft.Id);
            resubmitToken = Convert.ToBase64String(p.RowVersion);
        }

        // Check -> creates REVISION PASS snapshot
        var revCheck = await BodyAsync<TeamEligibilityCheckDto>(await clientLeader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        Assert.Equal("REVISION", revCheck.RoundType);
        Assert.Equal(revHistoryId, revCheck.RevisionHistoryId);

        // Mutate member to INACTIVE again
        await using (var ctx = database.CreateContext())
        {
            await ctx.Users.Where(u => u.Id == s.Students[1]).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "INACTIVE"));
        }

        // Resubmit -> fails with 409 Conflict
        var resubmitRes = await clientLeader.PostAsJsonAsync($"/api/v1/projects/{draft.Id}/resubmit", new { concurrencyToken = resubmitToken });
        Assert.Equal(HttpStatusCode.Conflict, resubmitRes.StatusCode);

        await using (var ctx = database.CreateContext())
        {
            // Business changes rolled back: remains REVISION_REQUIRED
            var proj = await ctx.Projects.SingleAsync(p => p.Id == draft.Id);
            Assert.Equal("REVISION_REQUIRED", proj.Status);

            // Exactly one denial audit emitted and survived rollback
            var resubmitDenialAudits = await ctx.AuditLogs
                .Where(a => a.Action == "PROJECT_RESUBMISSION_GUARD_DENIED" && a.EntityId == draft.Id.ToString())
                .ToListAsync();
            Assert.Single(resubmitDenialAudits);
        }
    }
}
