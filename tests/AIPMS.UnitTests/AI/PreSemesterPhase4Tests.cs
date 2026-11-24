using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.MilestoneTemplates.Commands;
using AIPMS.Application.Features.MilestoneTemplates.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.Infrastructure.Services.AiSuite;
using AIPMS.Infrastructure.Services.Projects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.UnitTests.AI;

public sealed class PreSemesterPhase4Tests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task MilestoneTemplate_ApplyAiSetup_EnforcesDepartmentStaffRbac_BlocksPureAdmin_AndReplaysIdempotently()
    {
        await using var db = SeedDb();
        var audit = new TestAudit();
        var clock = new FixedClock();
        var sender = new StubMilestoneSender();
        var key = Guid.NewGuid();
        var req = new ApplyMilestoneTemplateSetupRequest(key, 44, null, "Mẫu 14 tuần", "Chuẩn đồ án",
        [
            new("Mốc 1: SRS", "Đặc tả", 0, 21, 0),
            new("Mốc 2: Bảo vệ", "Nghiệm thu", 22, 98, 1)
        ]);

        // Pure ADMIN without DEPARTMENT_STAFF is rejected (V4 strict Admin/Department isolation)
        var adminSvc = new ProjectAiSetupService(db, new TestUser(2, ["ADMIN"]), new AllowGuard(), sender, audit, clock);
        await Assert.ThrowsAsync<ForbiddenException>(() => adminSvc.ApplyMilestoneTemplateSetupAsync(req, Ct));
        Assert.Equal(0, sender.TemplatesCreated);

        // DEPARTMENT_STAFF succeeds and replays idempotently
        var staffSvc = new ProjectAiSetupService(db, new TestUser(1, ["DEPARTMENT_STAFF"]), new AllowGuard(), sender, audit, clock);
        var first = await staffSvc.ApplyMilestoneTemplateSetupAsync(req, Ct);
        Assert.False(first.Replay);
        Assert.Equal("DRAFT", first.Status);
        Assert.Equal(2, first.ItemsCount);
        Assert.Equal(1, sender.TemplatesCreated);
        Assert.Equal(2, sender.ItemsAdded);

        var second = await staffSvc.ApplyMilestoneTemplateSetupAsync(req, Ct);
        Assert.True(second.Replay);
        Assert.Equal(1, sender.TemplatesCreated);
    }

    [Fact]
    public async Task EvaluationSchemeSuggest_UsesOnlyPublishedRubrics_AndBalances100PercentForAllMajors()
    {
        await using var db = SeedDb();
        var clock = new FixedClock();
        var config = new ConfigurationBuilder().Build();
        var suite = new AiSuiteService(db, new TestUser(1, ["DEPARTMENT_STAFF"]), new AllowAccess(), new DummyStorage(), clock, config, new LocalProvider());

        var suggestion = await suite.SuggestEvaluationSchemeAsync(500, Ct);

        Assert.Equal(500, suggestion.ProjectId);
        Assert.NotEmpty(suggestion.Components);
        // Must ONLY reference published rubric IDs (301, 302, 303 — NEVER draft rubric 399!)
        Assert.DoesNotContain(suggestion.Components, c => c.RubricId == 399);
        Assert.Equal(100m, suggestion.Components.Sum(c => c.ProjectWeightPercent));
        // Each project major (100 = SE, 200 = MKT) must have studentWeightPercent == 100%
        Assert.Equal(100m, suggestion.Components.Where(c => c.Scope == "COMMON" || c.MajorId == 100).Sum(c => c.StudentWeightPercent));
        Assert.Equal(100m, suggestion.Components.Where(c => c.Scope == "COMMON" || c.MajorId == 200).Sum(c => c.StudentWeightPercent));
    }

    private static AipmsDbContext SeedDb()
    {
        var options = new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase("PreSemesterPhase4_" + Guid.NewGuid().ToString("N"))
            .Options;
        var db = new AipmsDbContext(options);
        var now = DateTime.UtcNow;

        db.Organizations.Add(new M.Organization { Id = 1, Code = "FPT", Name = "FPT", IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.Departments.AddRange(
            new M.Department { Id = 10, OrganizationId = 1, Code = "SE_D", Name = "SE Dept", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new M.Department { Id = 20, OrganizationId = 1, Code = "MKT_D", Name = "MKT Dept", IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.Majors.AddRange(
            new M.Major { Id = 100, DepartmentId = 10, Code = "SE", Name = "Kỹ thuật phần mềm", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new M.Major { Id = 200, DepartmentId = 20, Code = "MKT", Name = "Marketing", IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.Roles.AddRange(
            new M.Role { Id = 1, Code = "DEPARTMENT_STAFF", Name = "Staff", CreatedAt = now },
            new M.Role { Id = 2, Code = "ADMIN", Name = "Admin", CreatedAt = now });
        db.Users.AddRange(
            new M.User { Id = 1, Email = "staff@fpt.edu.vn", FullName = "Trưởng BM", PasswordHash = "x", Status = "ACTIVE", DepartmentId = 10, CreatedAt = now, UpdatedAt = now },
            new M.User { Id = 2, Email = "admin@fpt.edu.vn", FullName = "Admin", PasswordHash = "x", Status = "ACTIVE", DepartmentId = 10, CreatedAt = now, UpdatedAt = now });
        db.UserRoles.AddRange(
            new M.UserRole { UserId = 1, RoleId = 1, AssignedAt = now },
            new M.UserRole { UserId = 2, RoleId = 2, AssignedAt = now });

        var sem = new M.AcademicSemester { Id = 201, OrganizationId = 1, Code = "FA26", Name = "Fall 2026", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 20), Status = "ACTIVE", CreatedAt = now, UpdatedAt = now };
        db.AcademicSemesters.Add(sem);

        var team = new M.Team { Id = 400, AcademicSemesterId = 201, Code = "TEAM1", Name = "StudySpace", Status = "ACTIVE", CreatedBy = 1, CreatedAt = now, UpdatedAt = now };
        db.Teams.Add(team);

        var project = new M.Project { Id = 500, TeamId = 400, Code = "PRJ-LN1", Title = "StudySpace Liên ngành", Status = "FINAL_SUBMISSION", CreatedBy = 1, CreatedAt = now, UpdatedAt = now, RowVersion = new byte[8] };
        db.Projects.Add(project);
        db.ProjectMajors.AddRange(
            new M.ProjectMajor { ProjectId = 500, MajorId = 100, CreatedAt = now },
            new M.ProjectMajor { ProjectId = 500, MajorId = 200, CreatedAt = now });

        db.Rubrics.AddRange(
            new M.Rubric { Id = 301, DepartmentId = 10, AcademicSemesterId = 201, Code = "RUB_DEF_COMMON", Name = "Rubric Bảo vệ chung", ProjectMode = "INTERDISCIPLINARY", EvaluationStage = "DEFENSE", EvaluationScope = "COMMON", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new M.Rubric { Id = 302, DepartmentId = 10, AcademicSemesterId = 201, MajorId = 100, Code = "RUB_COLD_SE", Name = "Rubric Cold SE", ProjectMode = "INTERDISCIPLINARY", EvaluationStage = "COLD", EvaluationScope = "MAJOR_SPECIFIC", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new M.Rubric { Id = 303, DepartmentId = 20, AcademicSemesterId = 201, MajorId = 200, Code = "RUB_COLD_MKT", Name = "Rubric Cold MKT", ProjectMode = "INTERDISCIPLINARY", EvaluationStage = "COLD", EvaluationScope = "MAJOR_SPECIFIC", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new M.Rubric { Id = 399, DepartmentId = 10, AcademicSemesterId = 201, Code = "RUB_DRAFT_ONLY", Name = "Rubric nháp chưa duyệt", ProjectMode = "SINGLE_MAJOR", EvaluationStage = "DEFENSE", EvaluationScope = "COMMON", IsActive = false, CreatedAt = now, UpdatedAt = now });

        db.Set<RubricVersion>().AddRange(
            new RubricVersion { RubricId = 301, RootRubricId = 301, VersionNumber = 1, Status = "PUBLISHED", ConcurrencyToken = Guid.NewGuid() },
            new RubricVersion { RubricId = 302, RootRubricId = 302, VersionNumber = 1, Status = "PUBLISHED", ConcurrencyToken = Guid.NewGuid() },
            new RubricVersion { RubricId = 303, RootRubricId = 303, VersionNumber = 1, Status = "PUBLISHED", ConcurrencyToken = Guid.NewGuid() },
            new RubricVersion { RubricId = 399, RootRubricId = 399, VersionNumber = 1, Status = "DRAFT", ConcurrencyToken = Guid.NewGuid() });

        db.SaveChanges();
        return db;
    }

    private sealed class TestUser(long id, string[] roles) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => id;
        public string? Email => $"u{id}@fpt.edu.vn";
        public string? FullName => $"User {id}";
        public IReadOnlyCollection<string> Roles => roles;
    }

    private sealed class TestAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class AllowGuard : IProjectExecutionGuard
    {
        public Task MustBeActiveAsync(long projectId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MustBeActiveForMilestoneAsync(long milestoneId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MustBeActiveForTaskAsync(long taskId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MustBeActiveOrReadOnlyAsync(long projectId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class LocalProvider : IAiTextGenerationProvider
    {
        public string ModelVersion => "GROUNDED_LOCAL_V1";
        public string ConfigurationVersion => "LOCAL";
        public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) => Task.FromResult("{}");
    }

    private sealed class AllowAccess : IProjectAccessService
    {
        public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class DummyStorage : IFileStorage
    {
        public Task WriteAsync(string key, System.IO.Stream content, CancellationToken ct = default) => Task.CompletedTask;
        public Task<System.IO.Stream> OpenReadAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubMilestoneSender : ISender
    {
        public int TemplatesCreated { get; private set; }
        public int ItemsAdded { get; private set; }
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is CreateMilestoneTemplateCommand cmd)
            {
                TemplatesCreated++;
                var dto = new MilestoneTemplateDto(701, cmd.Name, cmd.Description, "ACTIVE", []);
                return Task.FromResult((TResponse)(object)dto);
            }
            if (request is CreateMilestoneTemplateVersionCommand vcmd)
            {
                var dto = new MilestoneTemplateVersionDto(801, 1, "DRAFT", []);
                return Task.FromResult((TResponse)(object)dto);
            }
            if (request is AddMilestoneTemplateItemCommand icmd)
            {
                ItemsAdded++;
                var dto = new MilestoneTemplateVersionDto(icmd.VersionId, 1, "DRAFT", []);
                return Task.FromResult((TResponse)(object)dto);
            }
            throw new NotSupportedException();
        }
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => Task.CompletedTask;
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => Task.FromResult<object?>(null);
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
