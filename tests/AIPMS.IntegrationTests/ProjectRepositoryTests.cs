using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.Commands;
using AIPMS.Application.Features.Topics.Services;
using AIPMS.Infrastructure.Services.Auditing;
using AIPMS.Domain.Teams;
using Testcontainers.MsSql;
using Xunit;

using Task = System.Threading.Tasks.Task;
using File = System.IO.File;

namespace AIPMS.IntegrationTests;

public class DbFixture : IAsyncLifetime
{
    private readonly IsolatedSqlDatabase database = new();
    private DbContextOptions<AipmsDbContext> _options = null!;
    public string ConnectionString => database.ConnectionString;

    public AipmsDbContext CreateContext() => new(_options);

    public async Task InitializeAsync()
    {
        var source = Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION");
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" && string.IsNullOrWhiteSpace(source))
            throw new InvalidOperationException("CI environment detected but AIPMS_TEST_SQL_CONNECTION is missing.");
        await database.StartAsync(source);
        try
        {
            _options = new DbContextOptionsBuilder<AipmsDbContext>().UseSqlServer(ConnectionString).Options;
            await using var context = CreateContext();
            await SeedMinimumTestDataAsync(context);
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    private async Task SeedMinimumTestDataAsync(AipmsDbContext Context)
    {
        await Context.Database.ExecuteSqlRawAsync(
            "INSERT INTO dbo.roles (code, name, description, is_system_role) VALUES " +
            "('STUDENT', 'Student', 'Student account', 1), " +
            "('DEPARTMENT_STAFF', 'Department Staff', 'Staff account', 1), " +
            "('ADMIN', 'Admin', 'Admin account', 1)");

        await Context.Database.ExecuteSqlRawAsync(
            "INSERT INTO dbo.organizations (code, name, is_active) VALUES ('FPTU', 'FPT University', 1)");
        long orgId = await Context.Organizations.Select(o => o.Id).FirstAsync();

        await Context.Database.ExecuteSqlAsync(
            $"INSERT INTO dbo.departments (organization_id, code, name, is_active) VALUES ({orgId}, 'SE', 'Software Engineering', 1)");
        long deptId = await Context.Departments.Select(d => d.Id).FirstAsync();

        await Context.Database.ExecuteSqlAsync(
            $"INSERT INTO dbo.majors (department_id, code, name, is_active) VALUES ({deptId}, 'SE_MAJ', 'Software Engineering Major', 1)");
        long majorId = await Context.Majors.Select(m => m.Id).FirstAsync();

        await Context.Database.ExecuteSqlAsync($"""
            INSERT INTO dbo.academic_semesters (organization_id, code, name, start_date, end_date, status)
            VALUES ({orgId}, 'FA26', 'Fall 2026', '2026-09-01', '2026-12-31', 'ACTIVE')
            """);
        long semId = await Context.AcademicSemesters.Select(s => s.Id).FirstAsync();

        await Context.Database.ExecuteSqlAsync($"""
            INSERT INTO dbo.project_periods (academic_semester_id, code, name, period_type, start_at, end_at, status)
            VALUES ({semId}, 'REG_2026', 'Registration Period Fall 2026', 'REGISTRATION', '2026-08-01', '2026-12-31', 'ACTIVE')
            """);

        await Context.Database.ExecuteSqlAsync($"""
            INSERT INTO dbo.users (major_id, email, password_hash, full_name, status)
            VALUES ({majorId}, 'student1@aipms.test', 'HASH', 'Student One', 'ACTIVE')
            """);
        long student1Id = await Context.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();

        await Context.Database.ExecuteSqlAsync($"""
            INSERT INTO dbo.users (major_id, email, password_hash, full_name, status)
            VALUES ({majorId}, 'student2@aipms.test', 'HASH', 'Student Two', 'ACTIVE')
            """);
        long student2Id = await Context.Users.Where(u => u.Email == "student2@aipms.test").Select(u => u.Id).FirstAsync();

        await Context.Database.ExecuteSqlAsync($"""
            INSERT INTO dbo.users (department_id, email, password_hash, full_name, status)
            VALUES ({deptId}, 'staff@aipms.test', 'HASH', 'Staff One', 'ACTIVE')
            """);
        long staffId = await Context.Users.Where(u => u.Email == "staff@aipms.test").Select(u => u.Id).FirstAsync();

        await Context.Database.ExecuteSqlAsync($"""
            INSERT INTO dbo.user_roles (user_id, role_id) VALUES 
            ({student1Id}, (SELECT id FROM dbo.roles WHERE code = 'STUDENT')), 
            ({student2Id}, (SELECT id FROM dbo.roles WHERE code = 'STUDENT')), 
            ({staffId}, (SELECT id FROM dbo.roles WHERE code = 'DEPARTMENT_STAFF'))
            """);

        await Context.Database.ExecuteSqlAsync($"""
            INSERT INTO dbo.teams (academic_semester_id, code, name, status, created_by)
            VALUES ({semId}, 'TEST_TEAM_01', 'Team One', 'ELIGIBLE', {student1Id})
            """);
        long team1Id = await Context.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();

        await Context.Database.ExecuteSqlAsync($"""
            INSERT INTO dbo.teams (academic_semester_id, code, name, status, created_by)
            VALUES ({semId}, 'TEST_TEAM_02', 'Team Two', 'ELIGIBLE', {student2Id})
            """);
        long team2Id = await Context.Teams.Where(t => t.Name == "Team Two").Select(t => t.Id).FirstAsync();

        await Context.Database.ExecuteSqlAsync($"""
            INSERT INTO dbo.team_members (team_id, academic_semester_id, user_id, is_leader)
            VALUES ({team1Id}, {semId}, {student1Id}, 1)
            """);

        await Context.Database.ExecuteSqlAsync($"""
            INSERT INTO dbo.team_members (team_id, academic_semester_id, user_id, is_leader)
            VALUES ({team2Id}, {semId}, {student2Id}, 1)
            """);
    }
}

[CollectionDefinition("ProjectDbTests")]
public class ProjectDbTestCollection : ICollectionFixture<DbFixture> { }

[Collection("ProjectDbTests")]
public class ProjectRepositoryTests
{
    private readonly DbFixture _fixture;

    public ProjectRepositoryTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Should_Rollback_Entire_Aggregate_When_Persistence_Failure_Occurs()
    {
        using var context = _fixture.CreateContext();
        var repo = new ProjectRepository(context);
        var teamId = await context.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await context.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();

        await Assert.ThrowsAnyAsync<DbUpdateException>(async () =>
        {
            await repo.CreateDraftAsync(
                teamId,
                studentId,
                "Atomic Rollback Test",
                "Description",
                "Objectives",
                "Problem",
                "Output",
                new[] { 9999L }, // Invalid Major ID to trigger FK violation
                "DomainRollback",
                new[] { "TechRollback" },
                new[] { "KwRollback" },
                default);
        });

        // Use a brand new DbContext to verify rollback state in SQL Server
        using var verifyContext = _fixture.CreateContext();
        var projectExists = await verifyContext.Projects.AnyAsync(p => p.Title == "Atomic Rollback Test");
        Assert.False(projectExists);

        var majorsExist = await verifyContext.ProjectMajors.AnyAsync(pm => pm.MajorId == 9999L);
        Assert.False(majorsExist);

        var tagExists = await verifyContext.Tags.AnyAsync(t => t.Name == "DomainRollback" || t.Name == "TechRollback" || t.Name == "KwRollback");
        Assert.False(tagExists);

        var projectTagsExist = await verifyContext.ProjectTags.AnyAsync(pt => pt.Tag.NormalizedName == "DOMAINROLLBACK" || pt.Tag.NormalizedName == "TECHROLLBACK" || pt.Tag.NormalizedName == "KWROLLBACK");
        Assert.False(projectTagsExist);
    }

    [Fact]
    public async Task Should_Throw_ConflictException_On_Duplicate_Active_Project_Creation()
    {
        using var context = _fixture.CreateContext();
        var repo = new ProjectRepository(context);
        var teamId = await context.Teams.Where(t => t.Name == "Team Two").Select(t => t.Id).FirstAsync();
        var studentId = await context.Users.Where(u => u.Email == "student2@aipms.test").Select(u => u.Id).FirstAsync();
        var majorId = await context.Majors.Select(m => m.Id).FirstAsync();

        var project1 = await repo.CreateDraftAsync(
            teamId,
            studentId,
            "Active Project 1",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "SoftwareEng",
            new[] { "CSharp" },
            new[] { "Repository" },
            default);

        Assert.NotNull(project1);

        await Assert.ThrowsAsync<ConflictException>(async () =>
        {
            await repo.CreateDraftAsync(
                teamId,
                studentId,
                "Active Project 2",
                "Description",
                "Objectives",
                "Problem",
                "Output",
                new[] { majorId },
                "SoftwareEng",
                new[] { "CSharp" },
                new[] { "Repository" },
                default);
        });

        // Update Project 1 status to REVISION_REQUIRED
        using var contextUpdate1 = _fixture.CreateContext();
        var entity = await contextUpdate1.Projects.SingleAsync(p => p.Id == project1.Id);
        entity.Status = "REVISION_REQUIRED";
        await contextUpdate1.SaveChangesAsync();

        using var contextTest2 = _fixture.CreateContext();
        var repoTest2 = new ProjectRepository(contextTest2);
        await Assert.ThrowsAsync<ConflictException>(async () =>
        {
            await repoTest2.CreateDraftAsync(
                teamId,
                studentId,
                "Active Project 2",
                "Description",
                "Objectives",
                "Problem",
                "Output",
                new[] { majorId },
                "SoftwareEng",
                new[] { "CSharp" },
                new[] { "Repository" },
                default);
        });

        // Update Project 1 status to ACTIVE
        using var contextUpdate2 = _fixture.CreateContext();
        var entity2 = await contextUpdate2.Projects.SingleAsync(p => p.Id == project1.Id);
        entity2.Status = "ACTIVE";
        await contextUpdate2.SaveChangesAsync();

        using var contextTest3 = _fixture.CreateContext();
        var repoTest3 = new ProjectRepository(contextTest3);
        await Assert.ThrowsAsync<ConflictException>(async () =>
        {
            await repoTest3.CreateDraftAsync(
                teamId,
                studentId,
                "Active Project 2",
                "Description",
                "Objectives",
                "Problem",
                "Output",
                new[] { majorId },
                "SoftwareEng",
                new[] { "CSharp" },
                new[] { "Repository" },
                default);
        });

        // Cleanup
        using var cleanupContext = _fixture.CreateContext();
        var cleanupProject = await cleanupContext.Projects.SingleAsync(p => p.Id == project1.Id);
        cleanupContext.Projects.Remove(cleanupProject);
        await cleanupContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Should_Release_Active_Project_Slot_When_Rejected()
    {
        // NOTE: This test specifically verifies the database filtered-index slot release semantics
        // (uq_projects_active_team index behavior) and not the complete authorized ProjectStateMachine flow.
        using var context = _fixture.CreateContext();
        var repo = new ProjectRepository(context);
        var teamId = await context.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await context.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var majorId = await context.Majors.Select(m => m.Id).FirstAsync();

        var project1 = await repo.CreateDraftAsync(
            teamId,
            studentId,
            "Slot Release Project 1",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "ReleaseSlot",
            new[] { "Dotnet" },
            new[] { "Slot" },
            default);

        // Transition status through UpdateStatusAsync to test filtered-index slot release semantics
        using var contextTransition = _fixture.CreateContext();
        var repoTransition = new ProjectRepository(contextTransition);
        var updated1 = await repoTransition.UpdateStatusAsync(
            project1.Id,
            project1.ConcurrencyToken,
            "DRAFT",
            "REJECTED",
            studentId,
            "Rejected by admin",
            default);

        Assert.Equal("REJECTED", updated1.Status);

        // Attempt Project 2 -> Should now succeed
        using var contextCreate2 = _fixture.CreateContext();
        var repoCreate2 = new ProjectRepository(contextCreate2);
        var project2 = await repoCreate2.CreateDraftAsync(
            teamId,
            studentId,
            "Slot Release Project 2",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "ReleaseSlot",
            new[] { "Dotnet" },
            new[] { "Slot" },
            default);

        Assert.NotNull(project2);
        Assert.Equal("Slot Release Project 2", project2.Title);

        // Cleanup
        using var cleanupContext = _fixture.CreateContext();
        var histories = await cleanupContext.ProjectStatusHistories.Where(h => h.ProjectId == project1.Id).ToListAsync();
        cleanupContext.ProjectStatusHistories.RemoveRange(histories);
        var p1 = await cleanupContext.Projects.SingleAsync(p => p.Id == project1.Id);
        var p2 = await cleanupContext.Projects.SingleAsync(p => p.Id == project2.Id);
        cleanupContext.Projects.Remove(p1);
        cleanupContext.Projects.Remove(p2);
        await cleanupContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Should_Update_Project_State_And_Write_Status_History_Atomically()
    {
        using var context = _fixture.CreateContext();
        var repo = new ProjectRepository(context);
        var teamId = await context.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await context.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var majorId = await context.Majors.Select(m => m.Id).FirstAsync();

        var project = await repo.CreateDraftAsync(
            teamId,
            studentId,
            "State History Project",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "TestHistory",
            new string[] { },
            new string[] { },
            default);

        var updated = await repo.UpdateStatusAsync(
            project.Id,
            project.ConcurrencyToken,
            "DRAFT",
            "SUBMITTED",
            studentId,
            "Submitting proposal",
            default);

        Assert.Equal("SUBMITTED", updated.Status);

        using var verifyContext = _fixture.CreateContext();
        var history = await verifyContext.ProjectStatusHistories
            .Where(h => h.ProjectId == project.Id)
            .SingleAsync();

        Assert.Equal("DRAFT", history.OldStatus);
        Assert.Equal("SUBMITTED", history.NewStatus);
        Assert.Equal("Submitting proposal", history.Reason);
        Assert.Equal(studentId, history.ChangedBy);

        // Cleanup
        using var cleanupContext = _fixture.CreateContext();
        var historyEntities = await cleanupContext.ProjectStatusHistories.Where(h => h.ProjectId == project.Id).ToListAsync();
        cleanupContext.ProjectStatusHistories.RemoveRange(historyEntities);
        var pEntity = await cleanupContext.Projects.SingleAsync(p => p.Id == project.Id);
        cleanupContext.Projects.Remove(pEntity);
        await cleanupContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Should_Rollback_Status_Update_When_History_Insertion_Fails()
    {
        using var context = _fixture.CreateContext();
        var repo = new ProjectRepository(context);
        var teamId = await context.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await context.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var majorId = await context.Majors.Select(m => m.Id).FirstAsync();

        var project = await repo.CreateDraftAsync(
            teamId,
            studentId,
            "History Rollback Project",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "HistRollback",
            new string[] { },
            new string[] { },
            default);

        // Act & Assert
        // Pass actorUserId = 99999 (which does not exist in DB) to trigger FK constraint violation on project_status_history
        await Assert.ThrowsAnyAsync<DbUpdateException>(async () =>
        {
            await repo.UpdateStatusAsync(
                project.Id,
                project.ConcurrencyToken,
                "DRAFT",
                "SUBMITTED",
                99999L,
                "Should fail",
                default);
        });

        // Use a brand new DbContext to verify rollback state in SQL Server
        using var verifyContext = _fixture.CreateContext();
        var finalProject = await verifyContext.Projects.AsNoTracking().SingleAsync(p => p.Id == project.Id);
        Assert.Equal("DRAFT", finalProject.Status);

        var historyCount = await verifyContext.ProjectStatusHistories.CountAsync(h => h.ProjectId == project.Id);
        Assert.Equal(0, historyCount);

        // Cleanup
        using var cleanupContext = _fixture.CreateContext();
        var pEntity = await cleanupContext.Projects.SingleAsync(p => p.Id == project.Id);
        cleanupContext.Projects.Remove(pEntity);
        await cleanupContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Should_Enforce_Optimistic_Concurrency_EF_Level_Directly()
    {
        using var contextSetup = _fixture.CreateContext();
        var repoSetup = new ProjectRepository(contextSetup);
        var teamId = await contextSetup.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await contextSetup.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var majorId = await contextSetup.Majors.Select(m => m.Id).FirstAsync();

        var project = await repoSetup.CreateDraftAsync(
            teamId,
            studentId,
            "Tracked Concurrency Project",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "ConcurrencyDirect",
            new string[] { },
            new string[] { },
            default);

        // Load project in Context A (tracked)
        using var contextA = _fixture.CreateContext();
        var projA = await contextA.Projects.SingleAsync(p => p.Id == project.Id);

        // Load project in Context B (tracked)
        using var contextB = _fixture.CreateContext();
        var projB = await contextB.Projects.SingleAsync(p => p.Id == project.Id);

        // Context A modifies and saves
        projA.Title = "Updated by A";
        await contextA.SaveChangesAsync();

        // Context B modifies its still-tracked stale entity and saves (should throw DbUpdateConcurrencyException)
        projB.Title = "Updated by B";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
        {
            await contextB.SaveChangesAsync();
        });

        // Verify using a fresh context
        using var verifyContext = _fixture.CreateContext();
        var finalProject = await verifyContext.Projects.SingleAsync(p => p.Id == project.Id);
        Assert.Equal("Updated by A", finalProject.Title);

        // Cleanup
        verifyContext.Projects.Remove(finalProject);
        await verifyContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Should_Enforce_Optimistic_Concurrency_When_Updating_Stale_Token()
    {
        using var contextSetup = _fixture.CreateContext();
        var repoSetup = new ProjectRepository(contextSetup);
        var teamId = await contextSetup.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await contextSetup.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var majorId = await contextSetup.Majors.Select(m => m.Id).FirstAsync();

        var project = await repoSetup.CreateDraftAsync(
            teamId,
            studentId,
            "Concurrency Project",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "Concurrency",
            new string[] { },
            new string[] { },
            default);

        // Repo 1 / Context A loads project
        using var contextA = _fixture.CreateContext();
        var repo1 = new ProjectRepository(contextA);
        var projA = await repo1.GetByIdAsync(project.Id, default);

        // Repo 2 / Context B loads project
        using var contextB = _fixture.CreateContext();
        var repo2 = new ProjectRepository(contextB);
        var projB = await repo2.GetByIdAsync(project.Id, default);

        Assert.NotNull(projA);
        Assert.NotNull(projB);
        Assert.Equal(projA.ConcurrencyToken, projB.ConcurrencyToken);

        // Update first using repo 1 / Context A
        var updated1 = await repo1.UpdateStatusAsync(
            project.Id,
            projA.ConcurrencyToken,
            "DRAFT",
            "SUBMITTED",
            studentId,
            "First submission",
            default);

        // Attempting to update using repo 2 / Context B with stale token should throw ConflictException
        await Assert.ThrowsAsync<ConflictException>(async () =>
        {
            await repo2.UpdateStatusAsync(
                project.Id,
                projB.ConcurrencyToken,
                "DRAFT",
                "UNDER_REVIEW",
                studentId,
                "Stale update attempt",
                default);
        });

        // Verify using a fresh context
        using var verifyContext = _fixture.CreateContext();
        var finalProject = await verifyContext.Projects.SingleAsync(p => p.Id == project.Id);
        Assert.Equal("SUBMITTED", finalProject.Status);

        var historyList = await verifyContext.ProjectStatusHistories
            .Where(h => h.ProjectId == project.Id)
            .ToListAsync();

        // Assert exactly ONE ProjectStatusHistory remains (DRAFT -> SUBMITTED)
        Assert.Single(historyList);
        var singleHistory = historyList[0];
        Assert.Equal("DRAFT", singleHistory.OldStatus);
        Assert.Equal("SUBMITTED", singleHistory.NewStatus);
        Assert.Equal(studentId, singleHistory.ChangedBy);

        // Assert no UNDER_REVIEW history exists
        Assert.DoesNotContain(historyList, h => h.NewStatus == "UNDER_REVIEW");

        // Cleanup
        using var cleanupContext = _fixture.CreateContext();
        var histories = await cleanupContext.ProjectStatusHistories.Where(h => h.ProjectId == project.Id).ToListAsync();
        cleanupContext.ProjectStatusHistories.RemoveRange(histories);
        var pEntity = await cleanupContext.Projects.SingleAsync(p => p.Id == project.Id);
        cleanupContext.Projects.Remove(pEntity);
        await cleanupContext.SaveChangesAsync();
    }

    [Fact]
    public async Task ConcurrentSubmit_OnlyOneSucceeds()
    {
        using var contextSetup = _fixture.CreateContext();
        var repoSetup = new ProjectRepository(contextSetup);
        var teamId = await contextSetup.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await contextSetup.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var majorId = await contextSetup.Majors.Select(m => m.Id).FirstAsync();

        var project = await repoSetup.CreateDraftAsync(
            teamId,
            studentId,
            "Concurrent Submit Project",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "ConcurrentSubmit",
            new[] { "Tech" },
            new[] { "Kw" },
            default);

        var token = project.ConcurrencyToken;

        // Run two concurrent submissions with the same concurrency token
        using var context1 = _fixture.CreateContext();
        using var context2 = _fixture.CreateContext();
        var repo1 = new ProjectRepository(context1);
        var repo2 = new ProjectRepository(context2);

        var task1 = Task.Run(async () =>
        {
            try
            {
                return await repo1.UpdateStatusAsync(project.Id, token, "DRAFT", "SUBMITTED", studentId, "Submit 1", default);
            }
            catch (Exception ex)
            {
                return (object)ex;
            }
        });

        var task2 = Task.Run(async () =>
        {
            try
            {
                return await repo2.UpdateStatusAsync(project.Id, token, "DRAFT", "SUBMITTED", studentId, "Submit 2", default);
            }
            catch (Exception ex)
            {
                return (object)ex;
            }
        });

        var results = await Task.WhenAll(task1, task2);

        var successCount = results.Count(r => r is ProjectDto);
        var conflictCount = results.Count(r => r is ConflictException);

        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);

        // Verify DB final state
        using var verifyContext = _fixture.CreateContext();
        var finalProject = await verifyContext.Projects.SingleAsync(p => p.Id == project.Id);
        Assert.Equal("SUBMITTED", finalProject.Status);

        var histories = await verifyContext.ProjectStatusHistories.Where(h => h.ProjectId == project.Id).ToListAsync();
        Assert.Single(histories);
        Assert.Equal("SUBMITTED", histories[0].NewStatus);

        // Cleanup
        verifyContext.ProjectStatusHistories.RemoveRange(histories);
        verifyContext.Projects.Remove(finalProject);
        await verifyContext.SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateVsSubmit_SubmitWins_StaleUpdate409()
    {
        using var contextSetup = _fixture.CreateContext();
        var repoSetup = new ProjectRepository(contextSetup);
        var teamId = await contextSetup.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await contextSetup.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var majorId = await contextSetup.Majors.Select(m => m.Id).FirstAsync();

        var project = await repoSetup.CreateDraftAsync(
            teamId,
            studentId,
            "Update vs Submit Project",
            "Original Description",
            "Original Objectives",
            "Original Problem",
            "Original Output",
            new[] { majorId },
            "OriginalDomain",
            new[] { "Tech" },
            new[] { "Kw" },
            default);

        var token = project.ConcurrencyToken;

        // Submit wins first
        using var contextSubmit = _fixture.CreateContext();
        var repoSubmit = new ProjectRepository(contextSubmit);
        var submittedProject = await repoSubmit.UpdateStatusAsync(
            project.Id, token, "DRAFT", "SUBMITTED", studentId, "Submitting proposal", default);
        Assert.Equal("SUBMITTED", submittedProject.Status);

        // Stale update attempt using original token
        using var contextUpdate = _fixture.CreateContext();
        var repoUpdate = new ProjectRepository(contextUpdate);

        await Assert.ThrowsAsync<ConflictException>(async () =>
        {
            await repoUpdate.UpdateDraftAsync(
                project.Id,
                token,
                "Overwritten Title",
                "Overwritten Description",
                "Overwritten Objectives",
                "Overwritten Problem",
                "Overwritten Output",
                new[] { majorId },
                "OverwrittenDomain",
                new[] { "OverwrittenTech" },
                new[] { "OverwrittenKw" },
                default);
        });

        // Verify submitted data was NOT overwritten
        using var verifyContext = _fixture.CreateContext();
        var finalProject = await verifyContext.Projects.SingleAsync(p => p.Id == project.Id);
        Assert.Equal("SUBMITTED", finalProject.Status);
        Assert.Equal("Update vs Submit Project", finalProject.Title);
        Assert.Equal("Original Description", finalProject.Description);

        // Cleanup
        var histories = await verifyContext.ProjectStatusHistories.Where(h => h.ProjectId == project.Id).ToListAsync();
        verifyContext.ProjectStatusHistories.RemoveRange(histories);
        verifyContext.Projects.Remove(finalProject);
        await verifyContext.SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateDraft_AfterSubmit_Returns409()
    {
        using var contextSetup = _fixture.CreateContext();
        var repoSetup = new ProjectRepository(contextSetup);
        var teamId = await contextSetup.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await contextSetup.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var majorId = await contextSetup.Majors.Select(m => m.Id).FirstAsync();

        var project = await repoSetup.CreateDraftAsync(
            teamId,
            studentId,
            "Already Submitted Project",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "ImmutableDomain",
            new[] { "Tech" },
            new[] { "Kw" },
            default);

        // Transition to SUBMITTED
        var submitted = await repoSetup.UpdateStatusAsync(
            project.Id, project.ConcurrencyToken, "DRAFT", "SUBMITTED", studentId, "Submit", default);

        // Even with the fresh concurrency token of the submitted project, editing must be rejected
        using var contextEdit = _fixture.CreateContext();
        var repoEdit = new ProjectRepository(contextEdit);

        var ex = await Assert.ThrowsAsync<ConflictException>(async () =>
        {
            await repoEdit.UpdateDraftAsync(
                project.Id,
                submitted.ConcurrencyToken,
                "Mutated Title",
                "Mutated Description",
                "Mutated Objectives",
                "Mutated Problem",
                "Mutated Output",
                new[] { majorId },
                "MutatedDomain",
                new[] { "Tech" },
                new[] { "Kw" },
                default);
        });

        Assert.Equal("Only an editable proposal can be updated.", ex.Message);

        // Cleanup
        using var cleanupContext = _fixture.CreateContext();
        var histories = await cleanupContext.ProjectStatusHistories.Where(h => h.ProjectId == project.Id).ToListAsync();
        cleanupContext.ProjectStatusHistories.RemoveRange(histories);
        var p = await cleanupContext.Projects.SingleAsync(p => p.Id == project.Id);
        cleanupContext.Projects.Remove(p);
        await cleanupContext.SaveChangesAsync();
    }

    [Fact]
    public async Task TopicSelectionVsSubmit_SubmitWins_StaleSelectionReturns409()
    {
        using var contextSetup = _fixture.CreateContext();
        var repoSetup = new ProjectRepository(contextSetup);
        var teamId = await contextSetup.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await contextSetup.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var majorId = await contextSetup.Majors.Select(m => m.Id).FirstAsync();
        var periodId = await contextSetup.ProjectPeriods.Select(p => p.Id).FirstAsync();
        var deptId = await contextSetup.Departments.Select(d => d.Id).FirstAsync();

        // Ensure a published topic exists
        var topic = new ProjectTopic
        {
            ProjectPeriodId = periodId,
            LeadDepartmentId = deptId,
            Code = "TOPIC-CONC-" + Guid.NewGuid().ToString("N")[..8],
            Status = "PUBLISHED",
            Title = "Topic for Concurrency Test",
            TechnologiesJson = "[\"C#\"]",
            KeywordsJson = "[\"AI\"]",
            ProjectMode = "SINGLE_MAJOR",
            PrimaryMajorId = majorId,
            CreatedBy = studentId,
            UpdatedBy = studentId,
            PublishedBy = studentId,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
        contextSetup.Set<ProjectTopic>().Add(topic);
        await contextSetup.SaveChangesAsync();

        var project = await repoSetup.CreateDraftAsync(
            teamId,
            studentId,
            "Topic vs Submit Project",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "Domain",
            new[] { "Tech" },
            new[] { "Kw" },
            default);

        var token = project.ConcurrencyToken;

        // Submit wins first
        using var contextSubmit = _fixture.CreateContext();
        var repoSubmit = new ProjectRepository(contextSubmit);
        var submittedProject = await repoSubmit.UpdateStatusAsync(
            project.Id, token, "DRAFT", "SUBMITTED", studentId, "Submitting proposal", default);
        Assert.Equal("SUBMITTED", submittedProject.Status);

        // Stale topic selection attempt
        using var contextSelect = _fixture.CreateContext();
        var repoSelect = new ProjectRepository(contextSelect);

        var ex = await Assert.ThrowsAsync<ConflictException>(async () =>
        {
            await repoSelect.SelectTopicAsync(project.Id, topic.Id, token, default);
        });
        Assert.Equal("The project has been modified by another user. Please refresh and try again.", ex.Message);

        // Even with fresh token, submitted project cannot change topic
        var ex2 = await Assert.ThrowsAsync<ConflictException>(async () =>
        {
            await repoSelect.SelectTopicAsync(project.Id, topic.Id, submittedProject.ConcurrencyToken, default);
        });
        Assert.Equal("Only an editable proposal can be updated.", ex2.Message);

        // Verify submitted data unchanged
        using var verifyContext = _fixture.CreateContext();
        var finalProject = await verifyContext.Projects.SingleAsync(p => p.Id == project.Id);
        Assert.Equal("SUBMITTED", finalProject.Status);
        Assert.Null(finalProject.TopicId);

        // Cleanup
        var histories = await verifyContext.ProjectStatusHistories.Where(h => h.ProjectId == project.Id).ToListAsync();
        verifyContext.ProjectStatusHistories.RemoveRange(histories);
        verifyContext.Projects.Remove(finalProject);
        var t = await verifyContext.Set<ProjectTopic>().SingleAsync(x => x.Id == topic.Id);
        verifyContext.Set<ProjectTopic>().Remove(t);
        await verifyContext.SaveChangesAsync();
    }

    [Fact]
    public async Task ConcurrentTopicSelection_OnlyValidWritePersists()
    {
        using var contextSetup = _fixture.CreateContext();
        var repoSetup = new ProjectRepository(contextSetup);
        var teamId = await contextSetup.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await contextSetup.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var majorId = await contextSetup.Majors.Select(m => m.Id).FirstAsync();
        var periodId = await contextSetup.ProjectPeriods.Select(p => p.Id).FirstAsync();
        var deptId = await contextSetup.Departments.Select(d => d.Id).FirstAsync();

        var topic1 = new ProjectTopic
        {
            ProjectPeriodId = periodId,
            LeadDepartmentId = deptId,
            Code = "TOPIC-A-" + Guid.NewGuid().ToString("N")[..8],
            Status = "PUBLISHED",
            Title = "Topic A",
            TechnologiesJson = "[\"C#\"]",
            KeywordsJson = "[\"AI\"]",
            ProjectMode = "SINGLE_MAJOR",
            PrimaryMajorId = majorId,
            CreatedBy = studentId,
            UpdatedBy = studentId,
            PublishedBy = studentId,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
        var topic2 = new ProjectTopic
        {
            ProjectPeriodId = periodId,
            LeadDepartmentId = deptId,
            Code = "TOPIC-B-" + Guid.NewGuid().ToString("N")[..8],
            Status = "PUBLISHED",
            Title = "Topic B",
            TechnologiesJson = "[\"C#\"]",
            KeywordsJson = "[\"AI\"]",
            ProjectMode = "SINGLE_MAJOR",
            PrimaryMajorId = majorId,
            CreatedBy = studentId,
            UpdatedBy = studentId,
            PublishedBy = studentId,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
        contextSetup.Set<ProjectTopic>().AddRange(topic1, topic2);
        await contextSetup.SaveChangesAsync();

        var project = await repoSetup.CreateDraftAsync(
            teamId,
            studentId,
            "Concurrent Topic Selection",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "Domain",
            new[] { "Tech" },
            new[] { "Kw" },
            default);

        var token = project.ConcurrencyToken;

        // User 1 selects Topic 1
        using var ctx1 = _fixture.CreateContext();
        var repo1 = new ProjectRepository(ctx1);
        var updated1 = await repo1.SelectTopicAsync(project.Id, topic1.Id, token, default);
        Assert.Equal(topic1.Id, updated1.TopicId);
        Assert.Equal("PUBLISHED_TOPIC", updated1.ProposalSource);

        // User 2 tries to select Topic 2 using stale token
        using var ctx2 = _fixture.CreateContext();
        var repo2 = new ProjectRepository(ctx2);
        await Assert.ThrowsAsync<ConflictException>(async () =>
        {
            await repo2.SelectTopicAsync(project.Id, topic2.Id, token, default);
        });

        // User 2 retries with fresh token -> replaces topic selection
        var updated2 = await repo2.SelectTopicAsync(project.Id, topic2.Id, updated1.ConcurrencyToken, default);
        Assert.Equal(topic2.Id, updated2.TopicId);
        Assert.Equal("PUBLISHED_TOPIC", updated2.ProposalSource);

        // Cleanup
        using var cleanupCtx = _fixture.CreateContext();
        var p = await cleanupCtx.Projects.SingleAsync(x => x.Id == project.Id);
        cleanupCtx.Projects.Remove(p);
        var t1 = await cleanupCtx.Set<ProjectTopic>().SingleAsync(x => x.Id == topic1.Id);
        var t2 = await cleanupCtx.Set<ProjectTopic>().SingleAsync(x => x.Id == topic2.Id);
        cleanupCtx.Set<ProjectTopic>().RemoveRange(t1, t2);
        await cleanupCtx.SaveChangesAsync();
    }

    [Fact]
    public async Task SelectPublishedTopic_PersistsTopicId()
    {
        using var contextSetup = _fixture.CreateContext();
        var repoSetup = new ProjectRepository(contextSetup);
        var teamId = await contextSetup.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await contextSetup.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var majorId = await contextSetup.Majors.Select(m => m.Id).FirstAsync();
        var periodId = await contextSetup.ProjectPeriods.Select(p => p.Id).FirstAsync();
        var deptId = await contextSetup.Departments.Select(d => d.Id).FirstAsync();

        var topic = new ProjectTopic
        {
            ProjectPeriodId = periodId,
            LeadDepartmentId = deptId,
            Code = "TOPIC-PERSIST-" + Guid.NewGuid().ToString("N")[..8],
            Status = "PUBLISHED",
            Title = "Topic for Persistence",
            TechnologiesJson = "[\"C#\"]",
            KeywordsJson = "[\"AI\"]",
            ProjectMode = "SINGLE_MAJOR",
            PrimaryMajorId = majorId,
            CreatedBy = studentId,
            UpdatedBy = studentId,
            PublishedBy = studentId,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
        contextSetup.Set<ProjectTopic>().Add(topic);
        await contextSetup.SaveChangesAsync();

        var project = await repoSetup.CreateDraftAsync(
            teamId,
            studentId,
            "Persist Topic Project",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "Domain",
            new[] { "Tech" },
            new[] { "Kw" },
            default);

        var updated = await repoSetup.SelectTopicAsync(project.Id, topic.Id, project.ConcurrencyToken, default);
        Assert.Equal(topic.Id, updated.TopicId);
        Assert.Equal("PUBLISHED_TOPIC", updated.ProposalSource);

        var retrieved = await repoSetup.GetByIdAsync(project.Id, default);
        Assert.NotNull(retrieved);
        Assert.Equal(topic.Id, retrieved.TopicId);
        Assert.Equal("PUBLISHED_TOPIC", retrieved.ProposalSource);
        Assert.NotNull(retrieved.SelectedTopic);
        Assert.Equal(topic.Id, retrieved.SelectedTopic.Id);
        Assert.Equal(topic.Code, retrieved.SelectedTopic.Code);
        Assert.Equal(topic.Title, retrieved.SelectedTopic.Title);

        // Cleanup
        using var cleanupCtx = _fixture.CreateContext();
        var p = await cleanupCtx.Projects.SingleAsync(x => x.Id == project.Id);
        cleanupCtx.Projects.Remove(p);
        var t = await cleanupCtx.Set<ProjectTopic>().SingleAsync(x => x.Id == topic.Id);
        cleanupCtx.Set<ProjectTopic>().Remove(t);
        await cleanupCtx.SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateTopic_ReplacesSelection_WhenDraftEditable()
    {
        using var contextSetup = _fixture.CreateContext();
        var repoSetup = new ProjectRepository(contextSetup);
        var teamId = await contextSetup.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await contextSetup.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var majorId = await contextSetup.Majors.Select(m => m.Id).FirstAsync();
        var periodId = await contextSetup.ProjectPeriods.Select(p => p.Id).FirstAsync();
        var deptId = await contextSetup.Departments.Select(d => d.Id).FirstAsync();

        var topic1 = new ProjectTopic
        {
            ProjectPeriodId = periodId,
            LeadDepartmentId = deptId,
            Code = "TOPIC-REP1-" + Guid.NewGuid().ToString("N")[..8],
            Status = "PUBLISHED",
            Title = "Topic Replacement 1",
            TechnologiesJson = "[\"C#\"]",
            KeywordsJson = "[\"AI\"]",
            ProjectMode = "SINGLE_MAJOR",
            PrimaryMajorId = majorId,
            CreatedBy = studentId,
            UpdatedBy = studentId,
            PublishedBy = studentId,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
        var topic2 = new ProjectTopic
        {
            ProjectPeriodId = periodId,
            LeadDepartmentId = deptId,
            Code = "TOPIC-REP2-" + Guid.NewGuid().ToString("N")[..8],
            Status = "PUBLISHED",
            Title = "Topic Replacement 2",
            TechnologiesJson = "[\"C#\"]",
            KeywordsJson = "[\"AI\"]",
            ProjectMode = "SINGLE_MAJOR",
            PrimaryMajorId = majorId,
            CreatedBy = studentId,
            UpdatedBy = studentId,
            PublishedBy = studentId,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
        contextSetup.Set<ProjectTopic>().AddRange(topic1, topic2);
        await contextSetup.SaveChangesAsync();

        var project = await repoSetup.CreateDraftAsync(
            teamId,
            studentId,
            "Replace Topic Project",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "Domain",
            new[] { "Tech" },
            new[] { "Kw" },
            default);

        // Select topic 1
        var selected1 = await repoSetup.SelectTopicAsync(project.Id, topic1.Id, project.ConcurrencyToken, default);
        Assert.Equal(topic1.Id, selected1.TopicId);

        // Replace with topic 2
        var selected2 = await repoSetup.SelectTopicAsync(project.Id, topic2.Id, selected1.ConcurrencyToken, default);
        Assert.Equal(topic2.Id, selected2.TopicId);
        Assert.Equal(topic2.Code, selected2.SelectedTopic?.Code);

        // Cleanup
        using var cleanupCtx = _fixture.CreateContext();
        var p = await cleanupCtx.Projects.SingleAsync(x => x.Id == project.Id);
        cleanupCtx.Projects.Remove(p);
        var t1 = await cleanupCtx.Set<ProjectTopic>().SingleAsync(x => x.Id == topic1.Id);
        var t2 = await cleanupCtx.Set<ProjectTopic>().SingleAsync(x => x.Id == topic2.Id);
        cleanupCtx.Set<ProjectTopic>().RemoveRange(t1, t2);
        await cleanupCtx.SaveChangesAsync();
    }

    [Fact]
    public async Task SubmittedProject_TopicChange_Returns409()
    {
        using var contextSetup = _fixture.CreateContext();
        var repoSetup = new ProjectRepository(contextSetup);
        var teamId = await contextSetup.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await contextSetup.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var majorId = await contextSetup.Majors.Select(m => m.Id).FirstAsync();
        var periodId = await contextSetup.ProjectPeriods.Select(p => p.Id).FirstAsync();
        var deptId = await contextSetup.Departments.Select(d => d.Id).FirstAsync();

        var topic = new ProjectTopic
        {
            ProjectPeriodId = periodId,
            LeadDepartmentId = deptId,
            Code = "TOPIC-SUBM-" + Guid.NewGuid().ToString("N")[..8],
            Status = "PUBLISHED",
            Title = "Topic for Submitted Test",
            TechnologiesJson = "[\"C#\"]",
            KeywordsJson = "[\"AI\"]",
            ProjectMode = "SINGLE_MAJOR",
            PrimaryMajorId = majorId,
            CreatedBy = studentId,
            UpdatedBy = studentId,
            PublishedBy = studentId,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
        contextSetup.Set<ProjectTopic>().Add(topic);
        await contextSetup.SaveChangesAsync();

        var project = await repoSetup.CreateDraftAsync(
            teamId,
            studentId,
            "Submitted Project Topic Change",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "Domain",
            new[] { "Tech" },
            new[] { "Kw" },
            default);

        var submitted = await repoSetup.UpdateStatusAsync(
            project.Id, project.ConcurrencyToken, "DRAFT", "SUBMITTED", studentId, "Submit", default);

        var ex = await Assert.ThrowsAsync<ConflictException>(async () =>
        {
            await repoSetup.SelectTopicAsync(project.Id, topic.Id, submitted.ConcurrencyToken, default);
        });
        Assert.Equal("Only an editable proposal can be updated.", ex.Message);

        // Cleanup
        using var cleanupCtx = _fixture.CreateContext();
        var histories = await cleanupCtx.ProjectStatusHistories.Where(h => h.ProjectId == project.Id).ToListAsync();
        cleanupCtx.ProjectStatusHistories.RemoveRange(histories);
        var p = await cleanupCtx.Projects.SingleAsync(x => x.Id == project.Id);
        cleanupCtx.Projects.Remove(p);
        var t = await cleanupCtx.Set<ProjectTopic>().SingleAsync(x => x.Id == topic.Id);
        cleanupCtx.Set<ProjectTopic>().Remove(t);
        await cleanupCtx.SaveChangesAsync();
    }

    [Fact]
    public async Task SelectTopic_AuditFailure_RollsBackProjectMutation()
    {
        using var contextSetup = _fixture.CreateContext();
        var repoSetup = new ProjectRepository(contextSetup);
        var teamId = await contextSetup.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await contextSetup.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var majorId = await contextSetup.Majors.Select(m => m.Id).FirstAsync();
        var periodId = await contextSetup.ProjectPeriods.Select(p => p.Id).FirstAsync();
        var deptId = await contextSetup.Departments.Select(d => d.Id).FirstAsync();

        var topic = new ProjectTopic
        {
            Code = "TOPIC-AUDIT-FAIL",
            Status = "PUBLISHED",
            ProjectPeriodId = periodId,
            LeadDepartmentId = deptId,
            CreatedBy = studentId,
            UpdatedBy = studentId,
            PublishedBy = studentId,
            PublishedAt = DateTime.UtcNow,
            Title = "Topic for Audit Failure Test",
            Description = "Description",
            ProblemStatement = "Problem",
            Objectives = "Objectives",
            ExpectedOutput = "Output",
            Domain = "Domain",
            TechnologiesJson = "[\"C#\"]",
            KeywordsJson = "[\"AI\"]",
            ProjectMode = "SINGLE_MAJOR",
            PrimaryMajorId = majorId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
        contextSetup.Set<ProjectTopic>().Add(topic);
        await contextSetup.SaveChangesAsync();

        var project = await repoSetup.CreateDraftAsync(
            teamId,
            studentId,
            "Atomic Audit Rollback Proposal",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "Domain",
            new[] { "Tech" },
            new[] { "Kw" },
            default);

        var originalToken = project.ConcurrencyToken;

        // Verify initial state
        using var checkContext = _fixture.CreateContext();
        var initialDbProject = await checkContext.Projects.SingleAsync(p => p.Id == project.Id);
        Assert.Null(initialDbProject.TopicId);
        Assert.Equal("STUDENT_PROPOSAL", initialDbProject.ProposalSource);
        var initialRowVersion = Convert.ToBase64String(initialDbProject.RowVersion);

        // Execute SelectTopic + Audit in InTransactionAsync, where Audit persistence fails
        using var txContext = _fixture.CreateContext();
        var txRepo = new ProjectRepository(txContext);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await txRepo.InTransactionAsync<ProjectDto>(async ct =>
            {
                var updated = await txRepo.SelectTopicAsync(project.Id, topic.Id, originalToken, ct);

                // Simulate audit failure: e.g. throwing exception or database error
                throw new InvalidOperationException("Audit persistence failed abruptly.");
            }, default);
        });

        // Verify that database transaction rolled back completely
        using var verifyContext = _fixture.CreateContext();
        var rolledBackProject = await verifyContext.Projects.SingleAsync(p => p.Id == project.Id);
        Assert.Null(rolledBackProject.TopicId);
        Assert.Equal("STUDENT_PROPOSAL", rolledBackProject.ProposalSource);
        Assert.Equal(initialRowVersion, Convert.ToBase64String(rolledBackProject.RowVersion));

        var auditLogs = await verifyContext.AuditLogs
            .Where(a => a.EntityId == project.Id.ToString() && a.Action == "PROJECT_TOPIC_SELECTED")
            .ToListAsync();
        Assert.Empty(auditLogs);

        // Cleanup
        verifyContext.Projects.Remove(rolledBackProject);
        var t = await verifyContext.Set<ProjectTopic>().SingleAsync(x => x.Id == topic.Id);
        verifyContext.Set<ProjectTopic>().Remove(t);
        await verifyContext.SaveChangesAsync();
    }

    [Fact]
    public async Task SelectTopic_MissingOrBlankConcurrencyToken_ThrowsArgumentException()
    {
        using var contextSetup = _fixture.CreateContext();
        var repoSetup = new ProjectRepository(contextSetup);
        var teamId = await contextSetup.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await contextSetup.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var majorId = await contextSetup.Majors.Select(m => m.Id).FirstAsync();

        var project = await repoSetup.CreateDraftAsync(
            teamId,
            studentId,
            "Concurrency Token Test Proposal",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "Domain",
            new[] { "Tech" },
            new[] { "Kw" },
            default);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await repoSetup.SelectTopicAsync(project.Id, 1, "", default);
        });

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await repoSetup.SelectTopicAsync(project.Id, 1, "   ", default);
        });

        // Cleanup
        using var cleanupCtx = _fixture.CreateContext();
        var p = await cleanupCtx.Projects.SingleAsync(x => x.Id == project.Id);
        cleanupCtx.Projects.Remove(p);
        await cleanupCtx.SaveChangesAsync();
    }

    private sealed class StubCurrentUser(long? userId, IReadOnlyCollection<string> roles) : ICurrentUser
    {
        public bool IsAuthenticated => userId.HasValue;
        public long? UserId => userId;
        public string? Email => "test@aipms.test";
        public string? FullName => "Test User";
        public IReadOnlyCollection<string> Roles => roles;
    }

    private sealed class StubRequestContext : IRequestContext
    {
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "TestAgent";
        public Guid CorrelationId => Guid.NewGuid();
    }

    private sealed class StubPolicyProvider : AIPMS.Application.Features.Teams.Abstractions.ITeamFormationPolicyProvider
    {
        public Task<TeamFormationPolicy?> GetAsync(long periodId, CancellationToken cancellationToken) =>
            Task.FromResult<TeamFormationPolicy?>(new TeamFormationPolicy(1, 5, 24, "v1", 1));
    }

    [Fact]
    public async Task SelectTopic_WhenLeaderChangesAfterPreflight_RollsBackAndReturnsForbidden()
    {
        using var setupContext = _fixture.CreateContext();
        var repoSetup = new ProjectRepository(setupContext);
        var periodId = await setupContext.ProjectPeriods.Select(p => p.Id).FirstAsync();
        var deptId = await setupContext.Departments.Select(d => d.Id).FirstAsync();
        var majorId = await setupContext.Majors.Select(m => m.Id).FirstAsync();
        var semesterId = await setupContext.ProjectPeriods.Where(p => p.Id == periodId).Select(p => p.AcademicSemesterId).FirstAsync();

        // Create 2 test student users
        var userA = new User
        {
            Email = $"leader_a_{Guid.NewGuid():N}@aipms.test",
            FullName = "Leader A Test",
            PasswordHash = "HASH",
            Status = "ACTIVE",
            DepartmentId = deptId,
            MajorId = majorId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var userB = new User
        {
            Email = $"member_b_{Guid.NewGuid():N}@aipms.test",
            FullName = "Member B Test",
            PasswordHash = "HASH",
            Status = "ACTIVE",
            DepartmentId = deptId,
            MajorId = majorId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        setupContext.Users.AddRange(userA, userB);
        await setupContext.SaveChangesAsync();

        var studentRoleId = await setupContext.Roles.Where(r => r.Code == "STUDENT").Select(r => r.Id).FirstAsync();
        setupContext.UserRoles.AddRange(
            new UserRole { UserId = userA.Id, RoleId = studentRoleId },
            new UserRole { UserId = userB.Id, RoleId = studentRoleId }
        );
        await setupContext.SaveChangesAsync();

        // Create team
        var team = new Team
        {
            AcademicSemesterId = semesterId,
            Code = "TEAM-RA-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Team Race A " + Guid.NewGuid().ToString("N")[..8],
            Status = "ELIGIBLE",
            CreatedBy = userA.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        setupContext.Teams.Add(team);
        await setupContext.SaveChangesAsync();

        // Add team members: User A is leader, User B is member
        var tmA = new TeamMember
        {
            TeamId = team.Id,
            AcademicSemesterId = semesterId,
            UserId = userA.Id,
            IsLeader = true,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var tmB = new TeamMember
        {
            TeamId = team.Id,
            AcademicSemesterId = semesterId,
            UserId = userB.Id,
            IsLeader = false,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        setupContext.TeamMembers.AddRange(tmA, tmB);

        // Add academic configuration for team so academic scope is satisfied
        var teamConfig = new TeamAcademicConfiguration
        {
            TeamId = team.Id,
            ProjectMode = "SINGLE_MAJOR",
            LeadDepartmentId = deptId,
            PrimaryMajorId = majorId,
            ConcurrencyToken = Guid.NewGuid()
        };
        teamConfig.Requirements.Add(new TeamMajorRequirement
        {
            TeamId = team.Id,
            MajorId = majorId,
            MinMembers = 1,
            MaxMembers = 5,
            Responsibility = "Core"
        });
        setupContext.Set<TeamAcademicConfiguration>().Add(teamConfig);

        // Create published topic
        var topic = new ProjectTopic
        {
            ProjectPeriodId = periodId,
            LeadDepartmentId = deptId,
            Code = "TOPIC-RA-" + Guid.NewGuid().ToString("N")[..8],
            Status = "PUBLISHED",
            Title = "Race A Topic",
            TechnologiesJson = "[\"C#\"]",
            KeywordsJson = "[\"Race\"]",
            ProjectMode = "SINGLE_MAJOR",
            PrimaryMajorId = majorId,
            CreatedBy = userA.Id,
            UpdatedBy = userA.Id,
            PublishedBy = userA.Id,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
        topic.Requirements.Add(new TopicMajorRequirement
        {
            MajorId = majorId,
            DepartmentId = deptId,
            MinMembers = 1,
            MaxMembers = 5,
            Responsibility = "Developer"
        });
        setupContext.Set<ProjectTopic>().Add(topic);
        await setupContext.SaveChangesAsync();

        // Create project draft by User A (leader)
        var project = await repoSetup.CreateDraftAsync(
            team.Id,
            userA.Id,
            "Race A Proposal",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "Domain",
            new[] { "Tech" },
            new[] { "Kw" },
            default);

        var originalToken = project.ConcurrencyToken;

        // Synchronization gates
        var preflightCompletedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeTransactionTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        SelectProjectTopicCommandHandler.AfterPreflightHook = async () =>
        {
            preflightCompletedTcs.TrySetResult();
            await resumeTransactionTcs.Task;
        };

        try
        {
            // Start topic selection in background task as User A
            var selectTask = Task.Run(async () =>
            {
                using var handlerCtx = _fixture.CreateContext();
                var projectRepo = new ProjectRepository(handlerCtx);
                var teamRepo = new TeamRepository(handlerCtx);
                var topicRepo = new TopicRepository(handlerCtx);
                var currentUser = new StubCurrentUser(userA.Id, [AppRoles.Student]);
                var auditTrail = new DatabaseAuditTrail(handlerCtx, new StubRequestContext(), TimeProvider.System);
                var policy = new StubPolicyProvider();
                var topicWorkflow = new TopicWorkflow(topicRepo, policy, currentUser, auditTrail, TimeProvider.System);
                var guard = new TopicSelectionGuard(projectRepo, teamRepo, topicWorkflow, TimeProvider.System);
                var handler = new SelectProjectTopicCommandHandler(projectRepo, guard, currentUser, auditTrail);

                return await handler.Handle(new SelectProjectTopicCommand(project.Id, topic.Id, originalToken), default);
            });

            // Wait until preflight has successfully validated
            await preflightCompletedTcs.Task;

            // Concurrently transfer leadership from User A to User B using a separate DbContext
            using (var transferCtx = _fixture.CreateContext())
            {
                var transferTeamRepo = new TeamRepository(transferCtx);
                await transferTeamRepo.InTransactionAsync(async ct =>
                {
                    await transferTeamRepo.TransferLeaderAsync(team.Id, userA.Id, userB.Id, DateTime.UtcNow, ct);
                    return true;
                }, default);
            }

            // Release handler to enter transaction
            resumeTransactionTcs.TrySetResult();

            // Handler must detect that User A is no longer leader under transaction lock and throw ForbiddenException
            var ex = await Assert.ThrowsAsync<ForbiddenException>(() => selectTask);
            Assert.Contains("Only the Team Leader can select a topic for the project.", ex.Message);

            // Verify project topic was NOT persisted and audit was NOT recorded
            using var verifyCtx = _fixture.CreateContext();
            var verifiedProject = await verifyCtx.Projects.SingleAsync(p => p.Id == project.Id);
            Assert.Null(verifiedProject.TopicId);
            Assert.Equal("STUDENT_PROPOSAL", verifiedProject.ProposalSource);
            Assert.Equal(originalToken, Convert.ToBase64String(verifiedProject.RowVersion));

            var auditLogs = await verifyCtx.AuditLogs
                .Where(a => a.EntityId == project.Id.ToString() && a.Action == "PROJECT_TOPIC_SELECTED")
                .ToListAsync();
            Assert.Empty(auditLogs);
        }
        finally
        {
            SelectProjectTopicCommandHandler.AfterPreflightHook = null;

            // Cleanup
            using var cleanupCtx = _fixture.CreateContext();
            var p = await cleanupCtx.Projects.SingleOrDefaultAsync(x => x.Id == project.Id);
            if (p != null) cleanupCtx.Projects.Remove(p);
            var t = await cleanupCtx.Set<ProjectTopic>().Include(x => x.Requirements).SingleOrDefaultAsync(x => x.Id == topic.Id);
            if (t != null)
            {
                cleanupCtx.RemoveRange(t.Requirements);
                cleanupCtx.Set<ProjectTopic>().Remove(t);
            }
            var cfg = await cleanupCtx.Set<TeamAcademicConfiguration>().Include(x => x.Requirements).SingleOrDefaultAsync(x => x.TeamId == team.Id);
            if (cfg != null)
            {
                cleanupCtx.RemoveRange(cfg.Requirements);
                cleanupCtx.Set<TeamAcademicConfiguration>().Remove(cfg);
            }
            var tms = await cleanupCtx.TeamMembers.Where(x => x.TeamId == team.Id).ToListAsync();
            cleanupCtx.TeamMembers.RemoveRange(tms);
            var tm = await cleanupCtx.Teams.SingleOrDefaultAsync(x => x.Id == team.Id);
            if (tm != null) cleanupCtx.Teams.Remove(tm);
            var urus = await cleanupCtx.UserRoles.Where(x => x.UserId == userA.Id || x.UserId == userB.Id).ToListAsync();
            cleanupCtx.UserRoles.RemoveRange(urus);
            var us = await cleanupCtx.Users.Where(x => x.Id == userA.Id || x.Id == userB.Id).ToListAsync();
            cleanupCtx.Users.RemoveRange(us);
            await cleanupCtx.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SelectTopic_WhenTopicClosesAfterPreflight_DoesNotPersistSelection()
    {
        using var setupContext = _fixture.CreateContext();
        var repoSetup = new ProjectRepository(setupContext);
        var periodId = await setupContext.ProjectPeriods.Select(p => p.Id).FirstAsync();
        var deptId = await setupContext.Departments.Select(d => d.Id).FirstAsync();
        var majorId = await setupContext.Majors.Select(m => m.Id).FirstAsync();
        var semesterId = await setupContext.ProjectPeriods.Where(p => p.Id == periodId).Select(p => p.AcademicSemesterId).FirstAsync();

        // Create test leader user
        var leader = new User
        {
            Email = $"leader_b_{Guid.NewGuid():N}@aipms.test",
            FullName = "Leader B Test",
            PasswordHash = "HASH",
            Status = "ACTIVE",
            DepartmentId = deptId,
            MajorId = majorId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        setupContext.Users.Add(leader);
        await setupContext.SaveChangesAsync();

        var studentRoleId = await setupContext.Roles.Where(r => r.Code == "STUDENT").Select(r => r.Id).FirstAsync();
        setupContext.UserRoles.Add(new UserRole { UserId = leader.Id, RoleId = studentRoleId });
        await setupContext.SaveChangesAsync();

        // Create team
        var team = new Team
        {
            AcademicSemesterId = semesterId,
            Code = "TEAM-RB-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Team Race B " + Guid.NewGuid().ToString("N")[..8],
            Status = "ELIGIBLE",
            CreatedBy = leader.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        setupContext.Teams.Add(team);
        await setupContext.SaveChangesAsync();

        // Add team member as leader
        setupContext.TeamMembers.Add(new TeamMember
        {
            TeamId = team.Id,
            AcademicSemesterId = semesterId,
            UserId = leader.Id,
            IsLeader = true,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        // Add academic configuration for team
        var teamConfig = new TeamAcademicConfiguration
        {
            TeamId = team.Id,
            ProjectMode = "SINGLE_MAJOR",
            LeadDepartmentId = deptId,
            PrimaryMajorId = majorId,
            ConcurrencyToken = Guid.NewGuid()
        };
        teamConfig.Requirements.Add(new TeamMajorRequirement
        {
            TeamId = team.Id,
            MajorId = majorId,
            MinMembers = 1,
            MaxMembers = 5,
            Responsibility = "Core"
        });
        setupContext.Set<TeamAcademicConfiguration>().Add(teamConfig);

        // Create published topic
        var topic = new ProjectTopic
        {
            ProjectPeriodId = periodId,
            LeadDepartmentId = deptId,
            Code = "TOPIC-RB-" + Guid.NewGuid().ToString("N")[..8],
            Status = "PUBLISHED",
            Title = "Race B Topic",
            TechnologiesJson = "[\"C#\"]",
            KeywordsJson = "[\"Race\"]",
            ProjectMode = "SINGLE_MAJOR",
            PrimaryMajorId = majorId,
            CreatedBy = leader.Id,
            UpdatedBy = leader.Id,
            PublishedBy = leader.Id,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
        topic.Requirements.Add(new TopicMajorRequirement
        {
            MajorId = majorId,
            DepartmentId = deptId,
            MinMembers = 1,
            MaxMembers = 5,
            Responsibility = "Developer"
        });
        setupContext.Set<ProjectTopic>().Add(topic);
        await setupContext.SaveChangesAsync();

        // Create project draft
        var project = await repoSetup.CreateDraftAsync(
            team.Id,
            leader.Id,
            "Race B Proposal",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            new[] { majorId },
            "Domain",
            new[] { "Tech" },
            new[] { "Kw" },
            default);

        var originalToken = project.ConcurrencyToken;

        // Synchronization gates
        var preflightCompletedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeTransactionTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        SelectProjectTopicCommandHandler.AfterPreflightHook = async () =>
        {
            preflightCompletedTcs.TrySetResult();
            await resumeTransactionTcs.Task;
        };

        try
        {
            // Start topic selection in background task as Leader
            var selectTask = Task.Run(async () =>
            {
                using var handlerCtx = _fixture.CreateContext();
                var projectRepo = new ProjectRepository(handlerCtx);
                var teamRepo = new TeamRepository(handlerCtx);
                var topicRepo = new TopicRepository(handlerCtx);
                var currentUser = new StubCurrentUser(leader.Id, [AppRoles.Student]);
                var auditTrail = new DatabaseAuditTrail(handlerCtx, new StubRequestContext(), TimeProvider.System);
                var policy = new StubPolicyProvider();
                var topicWorkflow = new TopicWorkflow(topicRepo, policy, currentUser, auditTrail, TimeProvider.System);
                var guard = new TopicSelectionGuard(projectRepo, teamRepo, topicWorkflow, TimeProvider.System);
                var handler = new SelectProjectTopicCommandHandler(projectRepo, guard, currentUser, auditTrail);

                return await handler.Handle(new SelectProjectTopicCommand(project.Id, topic.Id, originalToken), default);
            });

            // Wait until preflight has successfully validated
            await preflightCompletedTcs.Task;

            // Concurrently close the topic using a separate DbContext
            using (var closeCtx = _fixture.CreateContext())
            {
                var t = await closeCtx.Set<ProjectTopic>().SingleAsync(x => x.Id == topic.Id);
                t.Status = "CLOSED";
                t.ClosedAt = DateTime.UtcNow;
                t.ClosedBy = leader.Id;
                t.CloseReason = "Closed by admin concurrently";
                await closeCtx.SaveChangesAsync();
            }

            // Release handler to enter transaction
            resumeTransactionTcs.TrySetResult();

            // Handler must detect that topic is no longer PUBLISHED under transaction lock and throw ConflictException
            var ex = await Assert.ThrowsAsync<ConflictException>(() => selectTask);
            Assert.Contains("Only published topics can be selected.", ex.Message);

            // Verify project topic was NOT persisted and audit was NOT recorded
            using var verifyCtx = _fixture.CreateContext();
            var verifiedProject = await verifyCtx.Projects.SingleAsync(p => p.Id == project.Id);
            Assert.Null(verifiedProject.TopicId);
            Assert.Equal("STUDENT_PROPOSAL", verifiedProject.ProposalSource);
            Assert.Equal(originalToken, Convert.ToBase64String(verifiedProject.RowVersion));

            var auditLogs = await verifyCtx.AuditLogs
                .Where(a => a.EntityId == project.Id.ToString() && a.Action == "PROJECT_TOPIC_SELECTED")
                .ToListAsync();
            Assert.Empty(auditLogs);
        }
        finally
        {
            SelectProjectTopicCommandHandler.AfterPreflightHook = null;

            // Cleanup
            using var cleanupCtx = _fixture.CreateContext();
            var p = await cleanupCtx.Projects.SingleOrDefaultAsync(x => x.Id == project.Id);
            if (p != null) cleanupCtx.Projects.Remove(p);
            var t = await cleanupCtx.Set<ProjectTopic>().Include(x => x.Requirements).SingleOrDefaultAsync(x => x.Id == topic.Id);
            if (t != null)
            {
                cleanupCtx.RemoveRange(t.Requirements);
                cleanupCtx.Set<ProjectTopic>().Remove(t);
            }
            var cfg = await cleanupCtx.Set<TeamAcademicConfiguration>().Include(x => x.Requirements).SingleOrDefaultAsync(x => x.TeamId == team.Id);
            if (cfg != null)
            {
                cleanupCtx.RemoveRange(cfg.Requirements);
                cleanupCtx.Set<TeamAcademicConfiguration>().Remove(cfg);
            }
            var tms = await cleanupCtx.TeamMembers.Where(x => x.TeamId == team.Id).ToListAsync();
            cleanupCtx.TeamMembers.RemoveRange(tms);
            var tm = await cleanupCtx.Teams.SingleOrDefaultAsync(x => x.Id == team.Id);
            if (tm != null) cleanupCtx.Teams.Remove(tm);
            var urus = await cleanupCtx.UserRoles.Where(x => x.UserId == leader.Id).ToListAsync();
            cleanupCtx.UserRoles.RemoveRange(urus);
            var u = await cleanupCtx.Users.SingleOrDefaultAsync(x => x.Id == leader.Id);
            if (u != null) cleanupCtx.Users.Remove(u);
            await cleanupCtx.SaveChangesAsync();
        }
    }
}
