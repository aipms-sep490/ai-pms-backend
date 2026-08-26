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
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using Testcontainers.MsSql;
using Xunit;

using Task = System.Threading.Tasks.Task;
using File = System.IO.File;

namespace AIPMS.IntegrationTests;

public class DbFixture : IAsyncLifetime
{
    private MsSqlContainer? _msSqlContainer;
    private DbContextOptions<AipmsDbContext> _options = null!;
    public string ConnectionString { get; private set; } = null!;

    public AipmsDbContext CreateContext()
    {
        return new AipmsDbContext(_options);
    }

    public async Task InitializeAsync()
    {
        var isCI = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

        if (isCI)
        {
            var testConnectionString = Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION");
            if (string.IsNullOrWhiteSpace(testConnectionString))
            {
                throw new InvalidOperationException("CI environment detected but AIPMS_TEST_SQL_CONNECTION is missing.");
            }
            ConnectionString = testConnectionString;
        }
        else
        {
            // Local runs must ALWAYS use Testcontainers SQL Server
            _msSqlContainer = new MsSqlBuilder()
                .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                .Build();
            await _msSqlContainer.StartAsync();
            ConnectionString = _msSqlContainer.GetConnectionString();
        }

        var builder = new SqlConnectionStringBuilder(ConnectionString);
        builder.InitialCatalog = "master";
        var masterConnectionString = builder.ConnectionString;

        // SQL Server Readiness check / Retry logic
        var retries = 10;
        var connected = false;
        while (retries > 0 && !connected)
        {
            try
            {
                await using var testConnection = new SqlConnection(masterConnectionString);
                await testConnection.OpenAsync();
                connected = true;
            }
            catch (SqlException)
            {
                retries--;
                if (retries == 0) throw;
                await Task.Delay(2000);
            }
        }

        await using (var master = new SqlConnection(masterConnectionString))
        {
            await master.OpenAsync();
            await using var cmd = master.CreateCommand();
            cmd.CommandText = """
                IF DB_ID(N'AI_PMS') IS NULL
                BEGIN
                    CREATE DATABASE [AI_PMS];
                END
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        // Target InitialCatalog AI_PMS
        builder.InitialCatalog = "AI_PMS";
        ConnectionString = builder.ConnectionString;

        _options = new DbContextOptionsBuilder<AipmsDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        var schemaPath = Path.Combine(AppContext.BaseDirectory, "../../../../../db/schema.sql");
        if (!File.Exists(schemaPath))
        {
            schemaPath = Path.Combine(AppContext.BaseDirectory, "../../../../db/schema.sql");
        }
        var schemaSql = await File.ReadAllTextAsync(schemaPath);
        var batches = Regex.Split(schemaSql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }

        using (var seedContext = CreateContext())
        {
            await SeedMinimumTestDataAsync(seedContext);
        }
    }

    public async Task DisposeAsync()
    {
        if (_msSqlContainer is not null)
        {
            await _msSqlContainer.DisposeAsync();
        }
    }

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
}
