using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Semesters.Commands;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Application.Features.Semesters.Models;
using AIPMS.Application.Features.Semesters.Services;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;
using File = System.IO.File;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests;

[Collection("ProjectDbTests")]
public class ProjectPeriodSqlTests
{
    private readonly DbFixture _fixture;

    public ProjectPeriodSqlTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<AcademicSemesterModel> CreateTestSemesterAsync(AipmsDbContext context, string prefix = "TEST")
    {
        var orgId = await context.Organizations.Select(o => o.Id).FirstAsync();
        var repo = new SemesterRepository(context);
        var code = $"{prefix}_{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        return await repo.CreateSemesterAsync(
            orgId, code, $"{prefix} Test Semester",
            new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31),
            DateTime.UtcNow);
    }

    private async Task<(AcademicSemesterModel Semester, ProjectPeriodModel Period, long StudentId)> SetupActiveProjectTestAsync(AipmsDbContext context, string prefix)
    {
        var semester = await CreateTestSemesterAsync(context, prefix);
        var student1Id = await context.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var repo = new SemesterRepository(context);
        var code = $"PER_{prefix}_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();

        var period = await repo.CreateProjectPeriodAsync(
            semester.Id, code, $"{prefix} Test Period", "EXECUTION",
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            minTeamSize: 3, maxTeamSize: 5, minDistinctMajors: 1, maxProjectsPerSupervisor: 5,
            milestoneTemplateId: null, rubricId: null, utcNow: DateTime.UtcNow);

        var team = new Team
        {
            AcademicSemesterId = semester.Id,
            Code = $"TM_{prefix}_" + Guid.NewGuid().ToString("N")[..4],
            Name = $"{prefix} Team",
            Status = "ELIGIBLE",
            CreatedBy = student1Id
        };
        context.Teams.Add(team);
        await context.SaveChangesAsync();

        var project = new Project
        {
            TeamId = team.Id,
            Code = $"PRJ_{prefix}_" + Guid.NewGuid().ToString("N")[..4],
            Title = $"{prefix} Active Test Project",
            Status = "ACTIVE",
            CreatedBy = student1Id
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        return (semester, period, student1Id);
    }

    [Fact]
    public async Task MigrationScript_AddProjectPeriodPolicyFields_ShouldBeIdempotentAndExecutable()
    {
        using var context = _fixture.CreateContext();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "db", "schema.sql")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var migrationScriptPath = Path.Combine(directory.FullName, "db", "changes", "20260909_add_project_period_policy_fields.sql");
        Assert.True(File.Exists(migrationScriptPath), $"Migration script not found at {migrationScriptPath}");

        var migrationSql = await File.ReadAllTextAsync(migrationScriptPath);

        var batches = Regex.Split(
            migrationSql,
            @"^\s*GO\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        foreach (var batch in batches)
        {
            var cleanBatch = Regex.Replace(batch, @"^\s*USE\s+\[.*?\];?\s*$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (string.IsNullOrWhiteSpace(cleanBatch)) continue;
            await context.Database.ExecuteSqlRawAsync(cleanBatch);
        }

        var columnCount = await context.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*) AS Value
            FROM sys.columns
            WHERE object_id = OBJECT_ID('dbo.project_periods')
              AND name IN ('min_team_size', 'max_team_size', 'min_distinct_majors', 'max_projects_per_supervisor', 'milestone_template_id', 'rubric_id')
            """)
            .SingleAsync();

        Assert.Equal(6, columnCount);
    }

    [Fact]
    public async Task MigrateOldSchemaToBe12_ShouldPreserveExistingDataAndAddNewPolicyFields()
    {
        await using var isolatedDb = new IsolatedSqlDatabase();
        var sourceConnection = Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION");
        await isolatedDb.StartAsync(sourceConnection);

        var options = new DbContextOptionsBuilder<AipmsDbContext>()
            .UseSqlServer(isolatedDb.ConnectionString)
            .Options;

        using (var context = new AipmsDbContext(options))
        {
            await context.Database.ExecuteSqlAsync(
                $"INSERT INTO dbo.organizations (code, name, is_active) VALUES ('ORG_OLD', 'Old Org', 1)");
            var orgId = await context.Organizations.Select(o => o.Id).FirstAsync();

            await context.Database.ExecuteSqlAsync(
                $"INSERT INTO dbo.academic_semesters (organization_id, code, name, start_date, end_date, status) VALUES ({orgId}, 'SEM_OLD', 'Old Semester', '2026-01-01', '2026-12-31', 'ACTIVE')");
            var semId = await context.AcademicSemesters.Select(s => s.Id).FirstAsync();

            await context.Database.ExecuteSqlAsync($"""
                INSERT INTO dbo.project_periods (academic_semester_id, code, name, period_type, start_at, end_at, status)
                VALUES ({semId}, 'PRE_BE12', 'Pre BE-12 Old Schema Period', 'REGISTRATION', '2026-09-01', '2026-09-15', 'ACTIVE')
                """);
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "db", "schema.sql")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var migrationScriptPath = Path.Combine(directory.FullName, "db", "changes", "20260909_add_project_period_policy_fields.sql");
        var migrationSql = await File.ReadAllTextAsync(migrationScriptPath);

        var batches = Regex.Split(
            migrationSql,
            @"^\s*GO\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        using (var context = new AipmsDbContext(options))
        {
            foreach (var batch in batches)
            {
                var cleanBatch = Regex.Replace(batch, @"^\s*USE\s+\[.*?\];?\s*$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                if (string.IsNullOrWhiteSpace(cleanBatch)) continue;
                await context.Database.ExecuteSqlRawAsync(cleanBatch);
            }

            var oldRecord = await context.ProjectPeriods
                .SingleOrDefaultAsync(p => p.Code == "PRE_BE12");

            Assert.NotNull(oldRecord);
            Assert.Equal("Pre BE-12 Old Schema Period", oldRecord.Name);
            Assert.Equal("REGISTRATION", oldRecord.PeriodType);
            Assert.Equal("ACTIVE", oldRecord.Status);

            Assert.Equal(3, oldRecord.MinTeamSize);
            Assert.Equal(5, oldRecord.MaxTeamSize);
            Assert.Equal(1, oldRecord.MinDistinctMajors);

            foreach (var batch in batches)
            {
                var cleanBatch = Regex.Replace(batch, @"^\s*USE\s+\[.*?\];?\s*$", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                if (string.IsNullOrWhiteSpace(cleanBatch)) continue;
                await context.Database.ExecuteSqlRawAsync(cleanBatch);
            }

            var secondCheck = await context.ProjectPeriods
                .SingleOrDefaultAsync(p => p.Code == "PRE_BE12");

            Assert.NotNull(secondCheck);
            Assert.Equal("PRE_BE12", secondCheck.Code);
        }
    }

    [Fact]
    public async Task FullSchema_ShouldBootstrapCleanDatabase()
    {
        await using var isolatedDb = new IsolatedSqlDatabase();
        var sourceConnection = Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION");
        await isolatedDb.StartAsync(sourceConnection);

        var options = new DbContextOptionsBuilder<AipmsDbContext>()
            .UseSqlServer(isolatedDb.ConnectionString)
            .Options;

        await using var context = new AipmsDbContext(options);

        var projectPeriodsTableExists = await context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM sys.tables WHERE name = 'project_periods'")
            .SingleAsync();
        Assert.Equal(1, projectPeriodsTableExists);

        var rubricsTableExists = await context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM sys.tables WHERE name = 'rubrics'")
            .SingleAsync();
        Assert.Equal(1, rubricsTableExists);

        var fkExists = await context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM sys.foreign_keys WHERE name = 'fk_project_periods_rubric'")
            .SingleAsync();
        Assert.Equal(1, fkExists);

        var policyColumnsCount = await context.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*) AS Value
            FROM sys.columns
            WHERE object_id = OBJECT_ID('dbo.project_periods')
              AND name IN ('min_team_size', 'max_team_size', 'min_distinct_majors', 'max_projects_per_supervisor', 'milestone_template_id', 'rubric_id')
            """)
            .SingleAsync();
        Assert.Equal(6, policyColumnsCount);
    }

    [Fact]
    public async Task CreateAndGetProjectPeriod_ShouldPersistPolicyFieldsInSqlDatabase()
    {
        using var context = _fixture.CreateContext();
        var semester = await CreateTestSemesterAsync(context, "POL");
        var repo = new SemesterRepository(context);

        var code = "PER_POL_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var startAt = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);
        var endAt = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);
        var utcNow = DateTime.UtcNow;

        var created = await repo.CreateProjectPeriodAsync(
            semesterId: semester.Id,
            code: code,
            name: "Policy Field Test Period",
            periodType: "REGISTRATION",
            startAt: startAt,
            endAt: endAt,
            minTeamSize: 4,
            maxTeamSize: 6,
            minDistinctMajors: 2,
            maxProjectsPerSupervisor: 8,
            milestoneTemplateId: null,
            rubricId: null,
            utcNow: utcNow,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(created);
        Assert.Equal(4, created.MinTeamSize);
        Assert.Equal(6, created.MaxTeamSize);
        Assert.Equal(2, created.MinDistinctMajors);
        Assert.Equal(8, created.MaxProjectsPerSupervisor);

        using var verifyContext = _fixture.CreateContext();
        var verifyRepo = new SemesterRepository(verifyContext);
        var loaded = await verifyRepo.GetProjectPeriodAsync(created.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(code, loaded.Code);
        Assert.Equal(4, loaded.MinTeamSize);
        Assert.Equal(6, loaded.MaxTeamSize);
        Assert.Equal(2, loaded.MinDistinctMajors);
        Assert.Equal(8, loaded.MaxProjectsPerSupervisor);
    }

    [Fact]
    public async Task UpdateProjectPeriod_ShouldUpdatePolicyFieldsInSqlDatabase()
    {
        using var context = _fixture.CreateContext();
        var semester = await CreateTestSemesterAsync(context, "UPD");
        var repo = new SemesterRepository(context);

        var code = "PER_UPD_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var utcNow = DateTime.UtcNow;

        var created = await repo.CreateProjectPeriodAsync(
            semesterId: semester.Id,
            code: code,
            name: "Pre-Update Period",
            periodType: "EXECUTION",
            startAt: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            endAt: new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc),
            minTeamSize: 3,
            maxTeamSize: 5,
            minDistinctMajors: 1,
            maxProjectsPerSupervisor: 5,
            milestoneTemplateId: null,
            rubricId: null,
            utcNow: utcNow,
            cancellationToken: CancellationToken.None);

        var updated = await repo.UpdateProjectPeriodAsync(
            periodId: created.Id,
            code: code,
            name: "Updated Period",
            periodType: "EXECUTION",
            startAt: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            endAt: new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc),
            minTeamSize: 5,
            maxTeamSize: 7,
            minDistinctMajors: 3,
            maxProjectsPerSupervisor: 10,
            milestoneTemplateId: null,
            rubricId: null,
            utcNow: utcNow,
            cancellationToken: CancellationToken.None);

        Assert.Equal(5, updated.MinTeamSize);
        Assert.Equal(7, updated.MaxTeamSize);
        Assert.Equal(3, updated.MinDistinctMajors);
        Assert.Equal(10, updated.MaxProjectsPerSupervisor);

        using var verifyContext = _fixture.CreateContext();
        var verifyRepo = new SemesterRepository(verifyContext);
        var loaded = await verifyRepo.GetProjectPeriodAsync(created.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("Updated Period", loaded.Name);
        Assert.Equal(5, loaded.MinTeamSize);
        Assert.Equal(7, loaded.MaxTeamSize);
        Assert.Equal(3, loaded.MinDistinctMajors);
        Assert.Equal(10, loaded.MaxProjectsPerSupervisor);
    }

    [Fact]
    public async Task ConcurrentOverlappingProjectPeriods_OnlyOneRequestSucceeds()
    {
        using var initContext = _fixture.CreateContext();
        var semester = await CreateTestSemesterAsync(initContext, "RACE");

        var periodType = "REGISTRATION";
        var startAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var endAt = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);

        var codeA = "RACE_A_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var codeB = "RACE_B_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();

        using var contextA = _fixture.CreateContext();
        using var contextB = _fixture.CreateContext();

        var repoA = new SemesterRepository(contextA);
        var repoB = new SemesterRepository(contextB);

        var taskA = System.Threading.Tasks.Task.Run<object>(async () =>
        {
            try
            {
                return await repoA.CreateProjectPeriodAsync(
                    semester.Id, codeA, "Race Period A", periodType, startAt, endAt, 3, 5, 1, 5, null, null, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        var taskB = System.Threading.Tasks.Task.Run<object>(async () =>
        {
            try
            {
                return await repoB.CreateProjectPeriodAsync(
                    semester.Id, codeB, "Race Period B", periodType, startAt, endAt, 3, 5, 1, 5, null, null, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        var results = await System.Threading.Tasks.Task.WhenAll(taskA, taskB);

        var successCount = results.Count(r => r is ProjectPeriodModel);
        var conflictCount = results.Count(r => r is ConflictException);

        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);

        using var verifyContext = _fixture.CreateContext();
        var periods = await verifyContext.ProjectPeriods
            .Where(p => p.AcademicSemesterId == semester.Id && (p.Code == codeA || p.Code == codeB))
            .ToListAsync();

        Assert.Single(periods);
    }

    [Fact]
    public async Task ConcurrentStatusTransition_OnlyOneSucceeds()
    {
        using var initContext = _fixture.CreateContext();
        var semester = await CreateTestSemesterAsync(initContext, "STATRACE");
        var repoInit = new SemesterRepository(initContext);

        var period = await repoInit.CreateProjectPeriodAsync(
            semester.Id, "STAT_A_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), "Status Race Period",
            "REGISTRATION",
            new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 20, 0, 0, 0, DateTimeKind.Utc),
            3, 5, 1, 5, null, null, DateTime.UtcNow);

        using var contextA = _fixture.CreateContext();
        using var contextB = _fixture.CreateContext();

        var repoA = new SemesterRepository(contextA);
        var repoB = new SemesterRepository(contextB);

        var taskA = System.Threading.Tasks.Task.Run<object>(async () =>
        {
            try
            {
                return await repoA.SetProjectPeriodStatusAsync(period.Id, "ACTIVE", "DRAFT", DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        var taskB = System.Threading.Tasks.Task.Run<object>(async () =>
        {
            try
            {
                return await repoB.SetProjectPeriodStatusAsync(period.Id, "ACTIVE", "DRAFT", DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        var results = await System.Threading.Tasks.Task.WhenAll(taskA, taskB);

        var successCount = results.Count(r => r is ProjectPeriodModel);
        var conflictCount = results.Count(r => r is ConflictException);

        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);

        using var verifyContext = _fixture.CreateContext();
        var loaded = await verifyContext.ProjectPeriods.FindAsync(period.Id);
        Assert.NotNull(loaded);
        Assert.Equal("ACTIVE", loaded.Status);
    }

    [Fact]
    public async Task ActiveProject_UnsafeMinTeamIncrease_Rejects()
    {
        using var context = _fixture.CreateContext();
        var (semester, period, studentId) = await SetupActiveProjectTestAsync(context, "MININC");
        var repo = new SemesterRepository(context);
        var handler = new UpdateProjectPeriodCommandHandler(
            repo,
            new SemesterAccessService(new StubCurrentUser { UserId = studentId, Roles = new[] { "ADMIN" } }),
            new DatabaseAuditTrail(context, new StubRequestContext { ActorUserId = studentId }, TimeProvider.System),
            TimeProvider.System);

        var updateCmd = new UpdateProjectPeriodCommand(
            period.Id, period.Code, period.Name, period.PeriodType,
            period.StartAt, period.EndAt,
            MinTeamSize: 4, MaxTeamSize: 5);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(updateCmd, CancellationToken.None));
    }

    [Fact]
    public async Task ActiveProject_UnsafeMaxTeamDecrease_Rejects()
    {
        using var context = _fixture.CreateContext();
        var (semester, period, studentId) = await SetupActiveProjectTestAsync(context, "MAXDEC");
        var repo = new SemesterRepository(context);
        var handler = new UpdateProjectPeriodCommandHandler(
            repo,
            new SemesterAccessService(new StubCurrentUser { UserId = studentId, Roles = new[] { "ADMIN" } }),
            new DatabaseAuditTrail(context, new StubRequestContext { ActorUserId = studentId }, TimeProvider.System),
            TimeProvider.System);

        var updateCmd = new UpdateProjectPeriodCommand(
            period.Id, period.Code, period.Name, period.PeriodType,
            period.StartAt, period.EndAt,
            MinTeamSize: 3, MaxTeamSize: 3);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(updateCmd, CancellationToken.None));
    }

    [Fact]
    public async Task ActiveProject_UnsafeDistinctMajorIncrease_Rejects()
    {
        using var context = _fixture.CreateContext();
        var (semester, period, studentId) = await SetupActiveProjectTestAsync(context, "MAJINC");
        var repo = new SemesterRepository(context);
        var handler = new UpdateProjectPeriodCommandHandler(
            repo,
            new SemesterAccessService(new StubCurrentUser { UserId = studentId, Roles = new[] { "ADMIN" } }),
            new DatabaseAuditTrail(context, new StubRequestContext { ActorUserId = studentId }, TimeProvider.System),
            TimeProvider.System);

        var updateCmd = new UpdateProjectPeriodCommand(
            period.Id, period.Code, period.Name, period.PeriodType,
            period.StartAt, period.EndAt,
            MinTeamSize: 3, MaxTeamSize: 5, MinDistinctMajors: 2);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(updateCmd, CancellationToken.None));
    }

    [Fact]
    public async Task ActiveProject_UnsafeDeadlineChange_Rejects()
    {
        using var context = _fixture.CreateContext();
        var (semester, period, studentId) = await SetupActiveProjectTestAsync(context, "PASTEND");
        var repo = new SemesterRepository(context);
        var handler = new UpdateProjectPeriodCommandHandler(
            repo,
            new SemesterAccessService(new StubCurrentUser { UserId = studentId, Roles = new[] { "ADMIN" } }),
            new DatabaseAuditTrail(context, new StubRequestContext { ActorUserId = studentId }, TimeProvider.System),
            TimeProvider.System);

        var pastEndAt = DateTime.UtcNow.AddDays(-10);
        var updateCmd = new UpdateProjectPeriodCommand(
            period.Id, period.Code, period.Name, period.PeriodType,
            period.StartAt, pastEndAt,
            MinTeamSize: 3, MaxTeamSize: 5, MinDistinctMajors: 1);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(updateCmd, CancellationToken.None));
    }

    [Fact]
    public async Task ActiveProject_UnsafePeriodTypeChange_Rejects()
    {
        using var context = _fixture.CreateContext();
        var (semester, period, studentId) = await SetupActiveProjectTestAsync(context, "TYPECHG");
        var repo = new SemesterRepository(context);
        var handler = new UpdateProjectPeriodCommandHandler(
            repo,
            new SemesterAccessService(new StubCurrentUser { UserId = studentId, Roles = new[] { "ADMIN" } }),
            new DatabaseAuditTrail(context, new StubRequestContext { ActorUserId = studentId }, TimeProvider.System),
            TimeProvider.System);

        var updateCmd = new UpdateProjectPeriodCommand(
            period.Id, period.Code, period.Name, "REGISTRATION",
            period.StartAt, period.EndAt,
            MinTeamSize: 3, MaxTeamSize: 5, MinDistinctMajors: 1);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(updateCmd, CancellationToken.None));
    }

    [Fact]
    public async Task ActiveProject_SafePolicyUpdate_Allows()
    {
        using var context = _fixture.CreateContext();
        var (semester, period, studentId) = await SetupActiveProjectTestAsync(context, "SAFEUPD");
        var repo = new SemesterRepository(context);
        var handler = new UpdateProjectPeriodCommandHandler(
            repo,
            new SemesterAccessService(new StubCurrentUser { UserId = studentId, Roles = new[] { "ADMIN" } }),
            new DatabaseAuditTrail(context, new StubRequestContext { ActorUserId = studentId }, TimeProvider.System),
            TimeProvider.System);

        var updateCmd = new UpdateProjectPeriodCommand(
            period.Id, period.Code, "Safe Updated Name", period.PeriodType,
            period.StartAt, period.EndAt,
            MinTeamSize: 2, MaxTeamSize: 6, MinDistinctMajors: 1, MaxProjectsPerSupervisor: 10);

        var updated = await handler.Handle(updateCmd, CancellationToken.None);

        Assert.Equal("Safe Updated Name", updated.Name);
        Assert.Equal(2, updated.MinTeamSize);
        Assert.Equal(6, updated.MaxTeamSize);
        Assert.Equal(10, updated.MaxProjectsPerSupervisor);
    }

    [Fact]
    public async Task SetSemesterStatus_WithStaleExpectedStatus_ShouldThrowConflictException()
    {
        using var context = _fixture.CreateContext();
        var orgId = await context.Organizations.Select(o => o.Id).FirstAsync();
        var repo = new SemesterRepository(context);

        var code = "SEM_STALE_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var created = await repo.CreateSemesterAsync(
            orgId, code, "Stale Semester",
            new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
            DateTime.UtcNow);

        await Assert.ThrowsAsync<ConflictException>(async () =>
        {
            await repo.SetSemesterStatusAsync(
                created.Id, "UPCOMING", expectedStatus: "ACTIVE", DateTime.UtcNow);
        });

        using var verifyContext = _fixture.CreateContext();
        var loaded = await verifyContext.AcademicSemesters.FindAsync(created.Id);
        Assert.NotNull(loaded);
        Assert.Equal("DRAFT", loaded.Status);
    }

    [Fact]
    public async Task SetSemesterStatus_WithMatchingExpectedStatus_ShouldUpdateAtomically()
    {
        using var context = _fixture.CreateContext();
        var orgId = await context.Organizations.Select(o => o.Id).FirstAsync();
        var repo = new SemesterRepository(context);

        var code = "SEM_SUCC_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var created = await repo.CreateSemesterAsync(
            orgId, code, "Atomic Semester Transition",
            new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31),
            DateTime.UtcNow);

        var updated = await repo.SetSemesterStatusAsync(
            created.Id, "UPCOMING", expectedStatus: "DRAFT", DateTime.UtcNow);

        Assert.Equal("UPCOMING", updated.Status);

        using var verifyContext = _fixture.CreateContext();
        var loaded = await verifyContext.AcademicSemesters.FindAsync(created.Id);
        Assert.NotNull(loaded);
        Assert.Equal("UPCOMING", loaded.Status);
    }

    [Fact]
    public async Task SetProjectPeriodStatus_WithMismatchedExpectedStatus_ShouldThrowConflictException()
    {
        using var context = _fixture.CreateContext();
        var semester = await CreateTestSemesterAsync(context, "CONC");
        var repo = new SemesterRepository(context);

        var code = "PER_CONC_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var created = await repo.CreateProjectPeriodAsync(
            semesterId: semester.Id,
            code: code,
            name: "Concurrency Period Test",
            periodType: "FINAL_SUBMISSION",
            startAt: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            endAt: new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc),
            minTeamSize: 3,
            maxTeamSize: 5,
            minDistinctMajors: 1,
            maxProjectsPerSupervisor: 5,
            milestoneTemplateId: null,
            rubricId: null,
            utcNow: DateTime.UtcNow,
            cancellationToken: CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(async () =>
        {
            await repo.SetProjectPeriodStatusAsync(
                periodId: created.Id,
                status: "UPCOMING",
                expectedStatus: "ACTIVE",
                utcNow: DateTime.UtcNow,
                cancellationToken: CancellationToken.None);
        });

        using var verifyContext = _fixture.CreateContext();
        var verifyRepo = new SemesterRepository(verifyContext);
        var loaded = await verifyRepo.GetProjectPeriodAsync(created.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("DRAFT", loaded.Status);
    }

    [Fact]
    public async Task SetProjectPeriodStatus_WithMatchingExpectedStatus_ShouldUpdateStatusAtomically()
    {
        using var context = _fixture.CreateContext();
        var semester = await CreateTestSemesterAsync(context, "SUCC");
        var repo = new SemesterRepository(context);

        var code = "PER_SUCC_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var created = await repo.CreateProjectPeriodAsync(
            semesterId: semester.Id,
            code: code,
            name: "Successful Status Transition Test",
            periodType: "FINAL_SUBMISSION",
            startAt: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            endAt: new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc),
            minTeamSize: 3,
            maxTeamSize: 5,
            minDistinctMajors: 1,
            maxProjectsPerSupervisor: 5,
            milestoneTemplateId: null,
            rubricId: null,
            utcNow: DateTime.UtcNow,
            cancellationToken: CancellationToken.None);

        var updated = await repo.SetProjectPeriodStatusAsync(
            periodId: created.Id,
            status: "UPCOMING",
            expectedStatus: "DRAFT",
            utcNow: DateTime.UtcNow,
            cancellationToken: CancellationToken.None);

        Assert.Equal("UPCOMING", updated.Status);
    }

    [Fact]
    public async Task SemesterMutation_WhenAuditFails_RollsBackInSqlDatabase()
    {
        using var context = _fixture.CreateContext();
        var orgId = await context.Organizations.Select(o => o.Id).FirstAsync();
        var student1Id = await context.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var repo = new SemesterRepository(context);
        var handler = new CreateSemesterCommandHandler(
            repo,
            new SemesterAccessService(new StubCurrentUser { UserId = student1Id, Roles = new[] { "ADMIN" } }),
            new FailingAuditTrail(),
            TimeProvider.System);

        var code = "SEM_ROLL_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await handler.Handle(
                new CreateSemesterCommand(orgId, code, "Rollback Test Semester", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31)),
                CancellationToken.None);
        });

        using var verifyContext = _fixture.CreateContext();
        var exists = await verifyContext.AcademicSemesters.AnyAsync(s => s.Code == code);
        Assert.False(exists, "Semester must not exist in SQL database after audit trail failure.");
    }

    [Fact]
    public async Task ProjectPeriodMutation_WhenAuditFails_RollsBackInSqlDatabase()
    {
        using var context = _fixture.CreateContext();
        var semester = await CreateTestSemesterAsync(context, "ROLL");
        var student1Id = await context.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var repo = new SemesterRepository(context);
        var handler = new CreateProjectPeriodCommandHandler(
            repo,
            new SemesterAccessService(new StubCurrentUser { UserId = student1Id, Roles = new[] { "ADMIN" } }),
            new FailingAuditTrail(),
            TimeProvider.System);

        var code = "PER_ROLL_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await handler.Handle(
                new CreateProjectPeriodCommand(
                    semester.Id, code, "Rollback Test Period", "REGISTRATION",
                    new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
                    3, 5, 1, 5, null, null),
                CancellationToken.None);
        });

        using var verifyContext = _fixture.CreateContext();
        var exists = await verifyContext.ProjectPeriods.AnyAsync(p => p.Code == code);
        Assert.False(exists, "Project Period must not exist in SQL database after audit trail failure.");
    }

    private class FailingAuditTrail : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated Audit Log Failure");
        }
    }

    private class StubCurrentUser : AIPMS.Application.Abstractions.Security.ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId { get; init; }
        public string? Email => "admin@example.com";
        public string? FullName => "Admin User";
        public IReadOnlyCollection<string> Roles { get; init; } = new[] { "ADMIN" };
    }

    private class StubRequestContext : AIPMS.Application.Abstractions.Security.IRequestContext
    {
        public long? ActorUserId { get; init; }
        public string UserAgent => "TestAgent";
        public string IpAddress => "127.0.0.1";
        public Guid CorrelationId => Guid.NewGuid();
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
        public string? Email => "test@example.com";
        public string? FullName => "Test User";
        public long? DepartmentId => null;
        public long? MajorId => null;
    }
}
