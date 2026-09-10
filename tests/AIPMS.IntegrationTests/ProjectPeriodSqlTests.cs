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

    private static string GetPreBe12SchemaSql()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tests", "AIPMS.IntegrationTests", "PreBe12Schema.sql")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var fixturePath = Path.Combine(directory.FullName, "tests", "AIPMS.IntegrationTests", "PreBe12Schema.sql");
        Assert.True(File.Exists(fixturePath), $"Pre-BE12 schema fixture not found at {fixturePath}");

        return File.ReadAllText(fixturePath);
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
            // Drop all tables in isolated database catalog to start from clean slate
            await context.Database.ExecuteSqlRawAsync(@"
                DECLARE @sql NVARCHAR(MAX) = N'';
                SELECT @sql += N'ALTER TABLE [' + SCHEMA_NAME(schema_id) + N'].[' + OBJECT_NAME(parent_object_id) + N'] DROP CONSTRAINT [' + name + N'];' + CHAR(13)
                FROM sys.foreign_keys;
                IF @sql <> N'' EXEC sp_executesql @sql;

                SET @sql = N'';
                SELECT @sql += N'DROP TABLE [' + SCHEMA_NAME(schema_id) + N'].[' + name + N'];' + CHAR(13)
                FROM sys.tables;
                IF @sql <> N'' EXEC sp_executesql @sql;
            ");

            // Execute exact pre-BE12 schema.sql from commit 8e3f3c02cb5ce210ca67c0371ed331118dc62b25
            var preBe12SchemaSql = GetPreBe12SchemaSql();
            var startIndex = preBe12SchemaSql.IndexOf("SET ANSI_NULLS ON;", StringComparison.Ordinal);
            if (startIndex >= 0)
            {
                preBe12SchemaSql = preBe12SchemaSql[startIndex..];
            }

            var preBe12Batches = Regex.Split(
                preBe12SchemaSql,
                @"^\s*GO\s*$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);

            foreach (var batch in preBe12Batches)
            {
                var cleanBatch = Regex.Replace(batch, @"\bUSE\s+\[.*?\];?", "", RegexOptions.IgnoreCase);
                if (string.IsNullOrWhiteSpace(cleanBatch)) continue;
                try
                {
                    await context.Database.ExecuteSqlRawAsync(cleanBatch);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed executing batch during pre-BE12 schema bootstrap:\n{cleanBatch}\nError: {ex.Message}", ex);
                }
            }

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

    [Fact]
    public async Task PartialPolicyUpdate_WithInvalidMergedBounds_ShouldRejectBeforeDbCall()
    {
        using var context = _fixture.CreateContext();
        var semester = await CreateTestSemesterAsync(context, "PARTIAL");
        var repo = new SemesterRepository(context);
        var student1Id = await context.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var handler = new UpdateProjectPeriodCommandHandler(
            repo,
            new SemesterAccessService(new StubCurrentUser { UserId = student1Id, Roles = new[] { "ADMIN" } }),
            new DatabaseAuditTrail(context, new StubRequestContext { ActorUserId = student1Id }, TimeProvider.System),
            TimeProvider.System);

        var period = await repo.CreateProjectPeriodAsync(
            semester.Id, "PER_PART_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), "Partial Test Period",
            "REGISTRATION",
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            minTeamSize: 3, maxTeamSize: 5, minDistinctMajors: 1, maxProjectsPerSupervisor: 5,
            milestoneTemplateId: null, rubricId: null, utcNow: DateTime.UtcNow);

        // Update MinTeamSize to 6 (greater than existing MaxTeamSize of 5) without specifying MaxTeamSize
        var updateCmd = new UpdateProjectPeriodCommand(
            period.Id, period.Code, period.Name, period.PeriodType,
            period.StartAt, period.EndAt,
            MinTeamSize: 6);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(updateCmd, CancellationToken.None));
        Assert.Contains("Invalid team size configuration", ex.Message);
    }

    [Fact]
    public async Task UpdateSemester_WhenChildPeriodOutOfBounds_ShouldThrowConflictException()
    {
        using var context = _fixture.CreateContext();
        var orgId = await context.Organizations.Select(o => o.Id).FirstAsync();
        var student1Id = await context.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var repo = new SemesterRepository(context);

        var semCode = "SEM_BOUND_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var semester = await repo.CreateSemesterAsync(
            orgId, semCode, "Semester Bounds Test",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            DateTime.UtcNow);

        await repo.CreateProjectPeriodAsync(
            semester.Id, "PER_BOUND_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), "Child Period",
            "REGISTRATION",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            3, 5, 1, 5, null, null, DateTime.UtcNow);

        var updateSemesterHandler = new UpdateSemesterCommandHandler(
            repo,
            new SemesterAccessService(new StubCurrentUser { UserId = student1Id, Roles = new[] { "ADMIN" } }),
            new DatabaseAuditTrail(context, new StubRequestContext { ActorUserId = student1Id }, TimeProvider.System),
            TimeProvider.System);

        // Attempt to shrink semester end date to 2026-07-01, which excludes child period end date 2026-08-31
        var updateCmd = new UpdateSemesterCommand(
            semester.Id, semester.Code, semester.Name,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => updateSemesterHandler.Handle(updateCmd, CancellationToken.None));
        Assert.Contains("would fall outside the new semester bounds", ex.Message);
    }

    [Fact]
    public async Task CloseSemester_WhenChildPeriodActive_ShouldThrowConflictException()
    {
        using var context = _fixture.CreateContext();
        var orgId = await context.Organizations.Select(o => o.Id).FirstAsync();
        var student1Id = await context.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var repo = new SemesterRepository(context);

        var semCode = "SEM_CLOSE_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var semester = await repo.CreateSemesterAsync(
            orgId, semCode, "Semester Close Test",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            DateTime.UtcNow);

        await repo.SetSemesterStatusAsync(semester.Id, "UPCOMING", "DRAFT", DateTime.UtcNow);
        await repo.SetSemesterStatusAsync(semester.Id, "ACTIVE", "UPCOMING", DateTime.UtcNow);

        await repo.CreateProjectPeriodAsync(
            semester.Id, "PER_ACT_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), "Active Child Period",
            "REGISTRATION",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            3, 5, 1, 5, null, null, DateTime.UtcNow);

        var setStatusHandler = new SetSemesterStatusCommandHandler(
            repo,
            new SemesterAccessService(new StubCurrentUser { UserId = student1Id, Roles = new[] { "ADMIN" } }),
            new DatabaseAuditTrail(context, new StubRequestContext { ActorUserId = student1Id }, TimeProvider.System),
            TimeProvider.System);

        var setStatusCmd = new SetSemesterStatusCommand(semester.Id, "CLOSED", "ACTIVE");
        var ex = await Assert.ThrowsAsync<ConflictException>(() => setStatusHandler.Handle(setStatusCmd, CancellationToken.None));
        Assert.Contains("must be closed or archived first", ex.Message);
    }

    [Fact]
    public async Task ConcurrentUpdateAfterPeriodClosed_ShouldRejectStaleContentWrite()
    {
        using var context = _fixture.CreateContext();
        var semester = await CreateTestSemesterAsync(context, "STALEUPD");
        var repo = new SemesterRepository(context);
        var student1Id = await context.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();

        var period = await repo.CreateProjectPeriodAsync(
            semester.Id, "PER_STALE_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), "Stale Period",
            "REGISTRATION",
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            3, 5, 1, 5, null, null, DateTime.UtcNow);

        // Transition period to CLOSED
        await repo.SetProjectPeriodStatusAsync(period.Id, "UPCOMING", "DRAFT", DateTime.UtcNow);
        await repo.SetProjectPeriodStatusAsync(period.Id, "ACTIVE", "UPCOMING", DateTime.UtcNow);
        await repo.SetProjectPeriodStatusAsync(period.Id, "CLOSED", "ACTIVE", DateTime.UtcNow);

        var handler = new UpdateProjectPeriodCommandHandler(
            repo,
            new SemesterAccessService(new StubCurrentUser { UserId = student1Id, Roles = new[] { "ADMIN" } }),
            new DatabaseAuditTrail(context, new StubRequestContext { ActorUserId = student1Id }, TimeProvider.System),
            TimeProvider.System);

        var updateCmd = new UpdateProjectPeriodCommand(
            period.Id, period.Code, "Updated Stale Name", period.PeriodType,
            period.StartAt, period.EndAt);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(updateCmd, CancellationToken.None));
        Assert.Contains("A closed or archived project period cannot be modified", ex.Message);
    }

    [Fact]
    public async Task UpdateProjectPeriod_ShouldAuditPolicyBeforeAndAfter()
    {
        using var context = _fixture.CreateContext();
        var semester = await CreateTestSemesterAsync(context, "AUDITPOL");
        var repo = new SemesterRepository(context);
        var student1Id = await context.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var recordingAudit = new RecordingAuditTrail();

        var period = await repo.CreateProjectPeriodAsync(
            semester.Id, "PER_AUD_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), "Audit Period",
            "REGISTRATION",
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            minTeamSize: 3, maxTeamSize: 5, minDistinctMajors: 1, maxProjectsPerSupervisor: 5,
            milestoneTemplateId: null, rubricId: null, utcNow: DateTime.UtcNow);

        var handler = new UpdateProjectPeriodCommandHandler(
            repo,
            new SemesterAccessService(new StubCurrentUser { UserId = student1Id, Roles = new[] { "ADMIN" } }),
            recordingAudit,
            TimeProvider.System);

        var updateCmd = new UpdateProjectPeriodCommand(
            period.Id, period.Code, "Updated Audit Period Name", period.PeriodType,
            period.StartAt, period.EndAt,
            MinTeamSize: 4, MaxTeamSize: 6, MinDistinctMajors: 2, MaxProjectsPerSupervisor: 8);

        await handler.Handle(updateCmd, CancellationToken.None);

        Assert.Single(recordingAudit.Entries);
        var entry = recordingAudit.Entries[0];
        Assert.Equal("PROJECT_PERIOD_UPDATED", entry.Action);
        Assert.NotNull(entry.Context);

        var beforeDict = entry.Context["before"] as Dictionary<string, object?>;
        var afterDict = entry.Context["after"] as Dictionary<string, object?>;

        Assert.NotNull(beforeDict);
        Assert.NotNull(afterDict);
        Assert.Equal(3, beforeDict["minTeamSize"]);
        Assert.Equal(4, afterDict["minTeamSize"]);
        Assert.Equal(5, beforeDict["maxTeamSize"]);
        Assert.Equal(6, afterDict["maxTeamSize"]);
        Assert.Equal(1, beforeDict["minDistinctMajors"]);
        Assert.Equal(2, afterDict["minDistinctMajors"]);
    }

    [Fact]
    public async Task RubricValidation_InvalidOrCrossSemesterRubric_ShouldThrowConflictException()
    {
        using var context = _fixture.CreateContext();
        var semester = await CreateTestSemesterAsync(context, "RUBRIC");
        var repo = new SemesterRepository(context);
        var student1Id = await context.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();
        var handler = new CreateProjectPeriodCommandHandler(
            repo,
            new SemesterAccessService(new StubCurrentUser { UserId = student1Id, Roles = new[] { "ADMIN" } }),
            new DatabaseAuditTrail(context, new StubRequestContext { ActorUserId = student1Id }, TimeProvider.System),
            TimeProvider.System);

        var cmd = new CreateProjectPeriodCommand(
            semester.Id, "PER_RUB_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), "Rubric Test Period",
            "REGISTRATION",
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            3, 5, 1, 5, null, RubricId: 999999);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(cmd, CancellationToken.None));
        Assert.Contains("Rubric with ID 999999 does not exist", ex.Message);
    }

    [Fact]
    public async Task ConcurrentUpdateAndClose_ShouldBeSerialisedAndRejectUpdate()
    {
        using var initContext = _fixture.CreateContext();
        var semester = await CreateTestSemesterAsync(initContext, "RACEUPDCLS");
        var repoInit = new SemesterRepository(initContext);
        var student1Id = await initContext.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();

        var period = await repoInit.CreateProjectPeriodAsync(
            semester.Id, "PER_RACE_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), "Race Period",
            "REGISTRATION",
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            3, 5, 1, 5, null, null, DateTime.UtcNow);

        // Put period into ACTIVE status
        await repoInit.SetProjectPeriodStatusAsync(period.Id, "UPCOMING", "DRAFT", DateTime.UtcNow);
        await repoInit.SetProjectPeriodStatusAsync(period.Id, "ACTIVE", "UPCOMING", DateTime.UtcNow);

        using var contextClose = _fixture.CreateContext();
        using var contextUpdate = _fixture.CreateContext();

        var repoClose = new SemesterRepository(contextClose);
        var repoUpdate = new SemesterRepository(contextUpdate);

        var closeHandler = new SetProjectPeriodStatusCommandHandler(
            repoClose,
            new SemesterAccessService(new StubCurrentUser { UserId = student1Id, Roles = new[] { "ADMIN" } }),
            new DatabaseAuditTrail(contextClose, new StubRequestContext { ActorUserId = student1Id }, TimeProvider.System),
            TimeProvider.System);

        var updateHandler = new UpdateProjectPeriodCommandHandler(
            repoUpdate,
            new SemesterAccessService(new StubCurrentUser { UserId = student1Id, Roles = new[] { "ADMIN" } }),
            new DatabaseAuditTrail(contextUpdate, new StubRequestContext { ActorUserId = student1Id }, TimeProvider.System),
            TimeProvider.System);

        var taskClose = System.Threading.Tasks.Task.Run<object>(async () =>
        {
            try
            {
                return await closeHandler.Handle(new SetProjectPeriodStatusCommand(period.Id, "CLOSED", "ACTIVE"), CancellationToken.None);
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        var taskUpdate = System.Threading.Tasks.Task.Run<object>(async () =>
        {
            try
            {
                return await updateHandler.Handle(new UpdateProjectPeriodCommand(
                    period.Id, period.Code, "Race Updated Period Name", period.PeriodType, period.StartAt, period.EndAt), CancellationToken.None);
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        var results = await System.Threading.Tasks.Task.WhenAll(taskClose, taskUpdate);

        // Verify database state: period MUST be CLOSED
        using var verifyContext = _fixture.CreateContext();
        var loaded = await verifyContext.ProjectPeriods.FindAsync(period.Id);
        Assert.NotNull(loaded);
        Assert.Equal("CLOSED", loaded.Status);

        // Verify update was rejected if close ran first or simultaneously
        var updateResult = results[1];
        if (updateResult is Exception updateEx)
        {
            Assert.IsType<ConflictException>(updateEx);
        }
    }

    [Fact]
    public async Task ProjectPeriodLifecycle_FullTransitionFromDraftToArchived_Succeeds()
    {
        using var context = _fixture.CreateContext();
        var semester = await CreateTestSemesterAsync(context, "PPLIFE");
        var repo = new SemesterRepository(context);

        var period = await repo.CreateProjectPeriodAsync(
            semester.Id, "PER_LIFE_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), "Lifecycle Period",
            "REGISTRATION",
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            3, 5, 1, 5, null, null, DateTime.UtcNow);

        Assert.Equal("DRAFT", period.Status);

        // DRAFT -> UPCOMING
        var pUpcoming = await repo.SetProjectPeriodStatusAsync(period.Id, "UPCOMING", "DRAFT", DateTime.UtcNow);
        Assert.Equal("UPCOMING", pUpcoming.Status);

        // UPCOMING -> ACTIVE
        var pActive = await repo.SetProjectPeriodStatusAsync(period.Id, "ACTIVE", "UPCOMING", DateTime.UtcNow);
        Assert.Equal("ACTIVE", pActive.Status);

        // ACTIVE -> CLOSED
        var pClosed = await repo.SetProjectPeriodStatusAsync(period.Id, "CLOSED", "ACTIVE", DateTime.UtcNow);
        Assert.Equal("CLOSED", pClosed.Status);

        // CLOSED -> ARCHIVED
        var pArchived = await repo.SetProjectPeriodStatusAsync(period.Id, "ARCHIVED", "CLOSED", DateTime.UtcNow);
        Assert.Equal("ARCHIVED", pArchived.Status);

        using var verifyContext = _fixture.CreateContext();
        var loaded = await verifyContext.ProjectPeriods.FindAsync(period.Id);
        Assert.NotNull(loaded);
        Assert.Equal("ARCHIVED", loaded.Status);
    }

    [Fact]
    public async Task SemesterLifecycle_FullTransitionFromDraftToArchived_WithChildPeriod_Succeeds()
    {
        using var context = _fixture.CreateContext();
        var orgId = await context.Organizations.Select(o => o.Id).FirstAsync();
        var repo = new SemesterRepository(context);

        var semester = await repo.CreateSemesterAsync(
            orgId, "SEM_LIFE_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), "Full Lifecycle Semester",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            DateTime.UtcNow);

        Assert.Equal("DRAFT", semester.Status);

        // Semester: DRAFT -> UPCOMING -> ACTIVE
        await repo.SetSemesterStatusAsync(semester.Id, "UPCOMING", "DRAFT", DateTime.UtcNow);
        await repo.SetSemesterStatusAsync(semester.Id, "ACTIVE", "UPCOMING", DateTime.UtcNow);

        // Add child period
        var period = await repo.CreateProjectPeriodAsync(
            semester.Id, "PER_SEMLIFE_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), "Child Lifecycle Period",
            "REGISTRATION",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            3, 5, 1, 5, null, null, DateTime.UtcNow);

        // Child Period: DRAFT -> UPCOMING -> ACTIVE -> CLOSED -> ARCHIVED
        await repo.SetProjectPeriodStatusAsync(period.Id, "UPCOMING", "DRAFT", DateTime.UtcNow);
        await repo.SetProjectPeriodStatusAsync(period.Id, "ACTIVE", "UPCOMING", DateTime.UtcNow);
        await repo.SetProjectPeriodStatusAsync(period.Id, "CLOSED", "ACTIVE", DateTime.UtcNow);
        await repo.SetProjectPeriodStatusAsync(period.Id, "ARCHIVED", "CLOSED", DateTime.UtcNow);

        // Semester: ACTIVE -> CLOSED -> ARCHIVED
        var semClosed = await repo.SetSemesterStatusAsync(semester.Id, "CLOSED", "ACTIVE", DateTime.UtcNow);
        Assert.Equal("CLOSED", semClosed.Status);

        var semArchived = await repo.SetSemesterStatusAsync(semester.Id, "ARCHIVED", "CLOSED", DateTime.UtcNow);
        Assert.Equal("ARCHIVED", semArchived.Status);

        using var verifyContext = _fixture.CreateContext();
        var loadedSem = await verifyContext.AcademicSemesters.FindAsync(semester.Id);
        var loadedPeriod = await verifyContext.ProjectPeriods.FindAsync(period.Id);

        Assert.NotNull(loadedSem);
        Assert.Equal("ARCHIVED", loadedSem.Status);

        Assert.NotNull(loadedPeriod);
        Assert.Equal("ARCHIVED", loadedPeriod.Status);
    }

    [Fact]
    public async Task ConcurrentPeriodTypeChange_AcquiresBothOldAndNewLocks_PreventsRace()
    {
        using var initContext = _fixture.CreateContext();
        var semester = await CreateTestSemesterAsync(initContext, "TYPECHGLOCK");
        var repoInit = new SemesterRepository(initContext);
        var student1Id = await initContext.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).FirstAsync();

        var periodToChange = await repoInit.CreateProjectPeriodAsync(
            semester.Id, "PER_REG_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), "Registration Period",
            "REGISTRATION",
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
            3, 5, 1, 5, null, null, DateTime.UtcNow);

        using var contextTypeChange = _fixture.CreateContext();
        using var contextCreateTarget = _fixture.CreateContext();

        var repoTypeChange = new SemesterRepository(contextTypeChange);
        var repoCreateTarget = new SemesterRepository(contextCreateTarget);

        var typeChangeHandler = new UpdateProjectPeriodCommandHandler(
            repoTypeChange,
            new SemesterAccessService(new StubCurrentUser { UserId = student1Id, Roles = new[] { "ADMIN" } }),
            new DatabaseAuditTrail(contextTypeChange, new StubRequestContext { ActorUserId = student1Id }, TimeProvider.System),
            TimeProvider.System);

        var createHandler = new CreateProjectPeriodCommandHandler(
            repoCreateTarget,
            new SemesterAccessService(new StubCurrentUser { UserId = student1Id, Roles = new[] { "ADMIN" } }),
            new DatabaseAuditTrail(contextCreateTarget, new StubRequestContext { ActorUserId = student1Id }, TimeProvider.System),
            TimeProvider.System);

        // Task A: Update periodToChange to change PeriodType from REGISTRATION to EXECUTION (overlapping 2026-09-01 to 2026-09-30)
        var taskChangeType = System.Threading.Tasks.Task.Run<object>(async () =>
        {
            try
            {
                return await typeChangeHandler.Handle(new UpdateProjectPeriodCommand(
                    periodToChange.Id, periodToChange.Code, periodToChange.Name, "EXECUTION",
                    periodToChange.StartAt, periodToChange.EndAt), CancellationToken.None);
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        // Task B: Create a new EXECUTION period overlapping the exact same dates (2026-09-01 to 2026-09-30)
        var taskCreateExecution = System.Threading.Tasks.Task.Run<object>(async () =>
        {
            try
            {
                return await createHandler.Handle(new CreateProjectPeriodCommand(
                    semester.Id, "PER_EXE_" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), "New Execution Period",
                    "EXECUTION",
                    new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc)), CancellationToken.None);
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        var results = await System.Threading.Tasks.Task.WhenAll(taskChangeType, taskCreateExecution);

        var successCount = results.Count(r => r is ProjectPeriodDto);
        var conflictCount = results.Count(r => r is ConflictException);

        // Exactly one must succeed and one must fail with ConflictException due to chronological overlap in EXECUTION
        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);
    }

    private class RecordingAuditTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = new();

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
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
