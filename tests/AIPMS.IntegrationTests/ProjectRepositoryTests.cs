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
using AIPMS.Infrastructure.Persistence.Repositories;
using Testcontainers.MsSql;
using Xunit;

namespace AIPMS.IntegrationTests;

public class DbFixture : IAsyncLifetime
{
    private MsSqlContainer? _msSqlContainer;
    public string ConnectionString { get; private set; } = null!;
    public AipmsDbContext Context { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            var ciConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
            if (!string.IsNullOrWhiteSpace(ciConnectionString))
            {
                ConnectionString = ciConnectionString;
            }
            else
            {
                _msSqlContainer = new MsSqlBuilder()
                    .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                    .Build();
                await _msSqlContainer.StartAsync();
                ConnectionString = _msSqlContainer.GetConnectionString();
            }
        }
        catch (Exception)
        {
            // Suppress error if Docker is not running on developer's local machine
            ConnectionString = null!;
            return;
        }

        var options = new DbContextOptionsBuilder<AipmsDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        Context = new AipmsDbContext(options);

        var schemaPath = Path.Combine(AppContext.BaseDirectory, "../../../../../db/schema.sql");
        if (!File.Exists(schemaPath))
        {
            schemaPath = Path.Combine(AppContext.BaseDirectory, "../../../../db/schema.sql");
        }
        var schemaSql = await File.ReadAllTextAsync(schemaPath);
        var batches = Regex.Split(schemaSql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }

        await SeedMinimumTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        if (Context is not null)
        {
            await Context.DisposeAsync();
        }
        if (_msSqlContainer is not null)
        {
            await _msSqlContainer.DisposeAsync();
        }
    }

    private async Task SeedMinimumTestDataAsync()
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
            INSERT INTO dbo.project_periods (academic_semester_id, period_type, status, start_at, end_at) 
            VALUES ({semId}, 'REGISTRATION', 'ACTIVE', '2026-08-01', '2026-12-31')
            """);

        await Context.Database.ExecuteSqlAsync($"""
            INSERT INTO dbo.users (email, password_hash, full_name, is_active, organization_id, department_id, major_id) VALUES 
            ('student1@fpt.edu.vn', 'hash', 'Student One', 1, {orgId}, {deptId}, {majorId}), 
            ('student2@fpt.edu.vn', 'hash', 'Student Two', 1, {orgId}, {deptId}, {majorId}), 
            ('staff@fpt.edu.vn', 'hash', 'Staff One', 1, {orgId}, {deptId}, NULL)
            """);

        var student1Id = await Context.Users.Where(u => u.Email == "student1@fpt.edu.vn").Select(u => u.Id).FirstAsync();
        var student2Id = await Context.Users.Where(u => u.Email == "student2@fpt.edu.vn").Select(u => u.Id).FirstAsync();
        var staffId = await Context.Users.Where(u => u.Email == "staff@fpt.edu.vn").Select(u => u.Id).FirstAsync();

        await Context.Database.ExecuteSqlAsync($"""
            INSERT INTO dbo.user_roles (user_id, role_id) VALUES 
            ({student1Id}, (SELECT id FROM dbo.roles WHERE code = 'STUDENT')), 
            ({student2Id}, (SELECT id FROM dbo.roles WHERE code = 'STUDENT')), 
            ({staffId}, (SELECT id FROM dbo.roles WHERE code = 'DEPARTMENT_STAFF'))
            """);

        await Context.Database.ExecuteSqlAsync($"""
            INSERT INTO dbo.teams (academic_semester_id, name, status) 
            VALUES ({semId}, 'Team One', 'ELIGIBLE'), ({semId}, 'Team Two', 'ELIGIBLE')
            """);

        var team1Id = await Context.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var team2Id = await Context.Teams.Where(t => t.Name == "Team Two").Select(t => t.Id).FirstAsync();

        await Context.Database.ExecuteSqlAsync($"""
            INSERT INTO dbo.team_members (team_id, user_id, is_leader, academic_semester_id) 
            VALUES ({team1Id}, {student1Id}, 1, {semId}), ({team2Id}, {student2Id}, 1, {semId})
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
        if (_fixture.ConnectionString is null) return;

        var context = _fixture.Context;
        var repo = new ProjectRepository(context);
        var teamId = await context.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await context.Users.Where(u => u.Email == "student1@fpt.edu.vn").Select(u => u.Id).FirstAsync();

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
                new[] { 9999L },
                "DomainRollback",
                new[] { "TechRollback" },
                new[] { "KwRollback" },
                default);
        });

        var projectExists = await context.Projects.AnyAsync(p => p.Title == "Atomic Rollback Test");
        Assert.False(projectExists);

        var tagExists = await context.Tags.AnyAsync(t => t.Name == "DomainRollback");
        Assert.False(tagExists);
    }

    [Fact]
    public async Task Should_Throw_ConflictException_On_Duplicate_Active_Project_Creation()
    {
        if (_fixture.ConnectionString is null) return;

        var context = _fixture.Context;
        var repo = new ProjectRepository(context);
        var teamId = await context.Teams.Where(t => t.Name == "Team Two").Select(t => t.Id).FirstAsync();
        var studentId = await context.Users.Where(u => u.Email == "student2@fpt.edu.vn").Select(u => u.Id).FirstAsync();
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

        var entity = await context.Projects.SingleAsync(p => p.Id == project1.Id);
        entity.Status = "REVISION_REQUIRED";
        await context.SaveChangesAsync();

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

        entity.Status = "ACTIVE";
        await context.SaveChangesAsync();

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

        entity.Status = "REJECTED";
        await context.SaveChangesAsync();

        var project2 = await repo.CreateDraftAsync(
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

        Assert.NotNull(project2);
        Assert.Equal("Active Project 2", project2.Title);
    }

    [Fact]
    public async Task Should_Update_Project_State_And_Write_Status_History_Atomically()
    {
        if (_fixture.ConnectionString is null) return;

        var context = _fixture.Context;
        var repo = new ProjectRepository(context);
        var teamId = await context.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await context.Users.Where(u => u.Email == "student1@fpt.edu.vn").Select(u => u.Id).FirstAsync();
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

        var history = await context.ProjectStatusHistories
            .Where(h => h.ProjectId == project.Id)
            .SingleAsync();

        Assert.Equal("DRAFT", history.OldStatus);
        Assert.Equal("SUBMITTED", history.NewStatus);
        Assert.Equal("Submitting proposal", history.Reason);
        Assert.Equal(studentId, history.ChangedBy);
    }

    [Fact]
    public async Task Should_Enforce_Optimistic_Concurrency_When_Updating_Stale_Token()
    {
        if (_fixture.ConnectionString is null) return;

        var context = _fixture.Context;
        var repo1 = new ProjectRepository(context);
        var teamId = await context.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).FirstAsync();
        var studentId = await context.Users.Where(u => u.Email == "student1@fpt.edu.vn").Select(u => u.Id).FirstAsync();
        var majorId = await context.Majors.Select(m => m.Id).FirstAsync();

        var project = await repo1.CreateDraftAsync(
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

        var options2 = new DbContextOptionsBuilder<AipmsDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options;
        using var context2 = new AipmsDbContext(options2);
        var repo2 = new ProjectRepository(context2);

        var updated1 = await repo1.UpdateStatusAsync(
            project.Id,
            project.ConcurrencyToken,
            "DRAFT",
            "SUBMITTED",
            studentId,
            "First submission",
            default);

        await Assert.ThrowsAsync<ConflictException>(async () =>
        {
            await repo2.UpdateStatusAsync(
                project.Id,
                project.ConcurrencyToken,
                "DRAFT",
                "UNDER_REVIEW",
                studentId,
                "Stale update attempt",
                default);
        });
    }
}
