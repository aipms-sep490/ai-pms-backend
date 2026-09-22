using AIPMS.Application.Abstractions.Projects;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.MilestoneTemplates.Abstractions;
using AIPMS.Application.Features.MilestoneTemplates.DTOs;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests;

public sealed class MilestoneTemplateActivationSqlTests(SupervisorDatabaseFixture database)
    : IClassFixture<SupervisorDatabaseFixture>
{
    [Fact]
    public async Task Concurrent_activation_and_retries_apply_pinned_version_once()
    {
        var s = await SeedAsync();
        using var app = new SupervisorFactory(database);
        async Task Activate()
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IProjectActivationService>()
                .ApplyMilestoneTemplateAsync(s.ProjectId, s.Admin, DateTime.UtcNow);
        }
        await Task.WhenAll(Activate(), Activate());
        await Activate();
        await using var db = database.CreateContext();
        Assert.Single(await db.Milestones.Where(m => m.ProjectId == s.ProjectId).ToListAsync());
        Assert.Equal(s.VersionId, (await db.ProjectMilestoneTemplateApplications.SingleAsync(a => a.ProjectId == s.ProjectId)).MilestoneTemplateVersionId);
        Assert.True((await db.Projects.FindAsync(s.ProjectId))!.MilestonesInitialized);
    }

    [Fact]
    public async Task No_template_activation_remains_initialized_after_a_template_is_assigned()
    {
        var s = await SeedAsync(withTemplate: false);
        using var app = new SupervisorFactory(database);
        using var scope = app.Services.CreateScope();
        var activation = scope.ServiceProvider.GetRequiredService<IProjectActivationService>();
        await activation.ApplyMilestoneTemplateAsync(s.ProjectId, s.Admin, DateTime.UtcNow);
        var repo = scope.ServiceProvider.GetRequiredService<IMilestoneTemplateRepository>();
        await repo.AssignAsync(s.PeriodId, s.TemplateId, s.VersionId, s.Admin, DateTime.UtcNow, default);
        await activation.ApplyMilestoneTemplateAsync(s.ProjectId, s.Admin, DateTime.UtcNow);
        await using var db = database.CreateContext();
        Assert.False(await db.Milestones.AnyAsync(m => m.ProjectId == s.ProjectId));
        Assert.True((await db.Projects.FindAsync(s.ProjectId))!.MilestonesInitialized);
    }

    [Fact]
    public async Task Explicit_version_pin_is_immutable_and_new_versions_do_not_change_it()
    {
        var s = await SeedAsync(withTemplate: false);
        using var app = new SupervisorFactory(database);
        using var scope = app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IMilestoneTemplateRepository>();
        var v2 = await repo.CreateVersionAsync(s.TemplateId, s.Admin, DateTime.UtcNow, default);
        await repo.AddItemAsync(v2.Id, new("Different", null, 0, 14, 1), s.Admin, DateTime.UtcNow, default);
        await repo.PublishVersionAsync(v2.Id, s.Admin, DateTime.UtcNow, default);
        await repo.AssignAsync(s.PeriodId, s.TemplateId, s.VersionId, s.Admin, DateTime.UtcNow, default);
        await Assert.ThrowsAsync<ConflictException>(() => repo.UpdateAsync(s.TemplateId, "Changed", null, s.Admin, DateTime.UtcNow, default));
        await Assert.ThrowsAsync<ConflictException>(() => repo.AddItemAsync(s.VersionId, new("Forbidden", null, 0, 2, 2), s.Admin, DateTime.UtcNow, default));
        await scope.ServiceProvider.GetRequiredService<IProjectActivationService>().ApplyMilestoneTemplateAsync(s.ProjectId, s.Admin, DateTime.UtcNow);
        await using var db = database.CreateContext();
        Assert.Equal("Design", (await db.Milestones.SingleAsync(m => m.ProjectId == s.ProjectId)).Title);
        Assert.Equal(s.VersionId, (await db.ProjectPeriods.FindAsync(s.PeriodId))!.MilestoneTemplateVersionId);
    }

    [Fact]
    public async Task Concurrent_version_creation_allocates_distinct_numbers()
    {
        var s = await SeedAsync();
        using var app = new SupervisorFactory(database);
        async Task<MilestoneTemplateVersionDto> Create()
        {
            using var scope = app.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IMilestoneTemplateRepository>()
                .CreateVersionAsync(s.TemplateId, s.Admin, DateTime.UtcNow, default);
        }
        var versions = await Task.WhenAll(Create(), Create());
        Assert.Equal(new[] { 2, 3 }, versions.Select(v => v.VersionNumber).Order().ToArray());
    }

    [Fact]
    public async Task Catalog_crud_validates_inputs_and_preserves_audit()
    {
        var s = await SeedAsync();
        using var app = new SupervisorFactory(database);
        using var scope = app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IMilestoneTemplateRepository>();
        var template = await repo.CreateAsync("Draft plan", null, s.Admin, DateTime.UtcNow, default);
        await repo.UpdateAsync(template.Id, "Updated plan", "Description", s.Admin, DateTime.UtcNow, default);
        var version = await repo.CreateVersionAsync(template.Id, s.Admin, DateTime.UtcNow, default);
        var result = await repo.AddItemAsync(version.Id, new("Draft item", null, 0, 1, 1), s.Admin, DateTime.UtcNow, default);
        await repo.UpdateItemAsync(result.Items.Single().Id, new("Changed item", null, 2, 4, 2), s.Admin, DateTime.UtcNow, default);
        await Assert.ThrowsAsync<ValidationException>(() => repo.AddItemAsync(version.Id, new(new string('a', 256), null, 0, 1, 1), s.Admin, DateTime.UtcNow, default));
        await repo.DeleteItemAsync(result.Items.Single().Id, s.Admin, default);
        await repo.DeleteAsync(template.Id, s.Admin, default);
        await using var db = database.CreateContext();
        Assert.False(await db.MilestoneTemplates.AnyAsync(t => t.Id == template.Id));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "MILESTONE_TEMPLATE_DELETED" && a.EntityId == template.Id.ToString()));
    }

    [Fact]
    public async Task Failed_audit_rolls_back_version_and_period_assignment()
    {
        var s = await SeedAsync(withTemplate: false);
        using var app = new SupervisorFactory(database, failAudit: true);
        using (var scope = app.Services.CreateScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IMilestoneTemplateRepository>()
                .CreateVersionAsync(s.TemplateId, s.Admin, DateTime.UtcNow, default));
        using (var scope = app.Services.CreateScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IMilestoneTemplateRepository>()
                .AssignAsync(s.PeriodId, s.TemplateId, s.VersionId, s.Admin, DateTime.UtcNow, default));
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.MilestoneTemplateVersions.CountAsync(v => v.MilestoneTemplateId == s.TemplateId));
        Assert.Null((await db.ProjectPeriods.FindAsync(s.PeriodId))!.MilestoneTemplateVersionId);
        Assert.Null((await db.MilestoneTemplateVersions.FindAsync(s.VersionId))!.LockedAt);
    }

    [Fact]
    public async Task Failed_activation_transaction_rolls_back_milestones_and_marker()
    {
        var s = await SeedAsync();
        using var app = new SupervisorFactory(database);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AIPMS.Infrastructure.Persistence.Generated.AipmsDbContext>();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await scope.ServiceProvider.GetRequiredService<IProjectActivationService>().ApplyMilestoneTemplateAsync(s.ProjectId, s.Admin, DateTime.UtcNow);
            await tx.RollbackAsync();
        }
        await using var verify = database.CreateContext();
        Assert.False((await verify.Projects.FindAsync(s.ProjectId))!.MilestonesInitialized);
        Assert.False(await verify.Milestones.AnyAsync(m => m.ProjectId == s.ProjectId));
        Assert.False(await verify.ProjectMilestoneTemplateApplications.AnyAsync(a => a.ProjectId == s.ProjectId));
    }

    private async Task<Scenario> SeedAsync(bool withTemplate = true, string status = "ACTIVE")
    {
        var accounts = await database.SeedAsync();
        long projectId;
        long templateVersionId;
        long periodId;
        long templateId;
        await using (var db = database.CreateContext())
        {
            var organizationId = (await db.Departments.FindAsync(accounts.DepartmentId))!.OrganizationId;
            var semester = new AcademicSemester
            {
                OrganizationId = organizationId, Code = Guid.NewGuid().ToString("N"), Name = "Execution semester",
                Status = "ACTIVE", StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90))
            };
            var template = new MilestoneTemplate
            {
                Name = "Pinned template", Status = "ACTIVE", CreatedBy = accounts.Admin,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };
            var version = new MilestoneTemplateVersion
            {
                MilestoneTemplate = template, VersionNumber = 1, Status = "PUBLISHED", CreatedBy = accounts.Admin,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                Items = [new MilestoneTemplateItem { Title = "Design", SortOrder = 1, StartOffsetDays = 0, DueOffsetDays = 7,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }]
            };
            var period = new ProjectPeriod
            {
                AcademicSemester = semester, Code = "EXEC", Name = "Execution", PeriodType = "EXECUTION", Status = "ACTIVE",
                StartAt = DateTime.UtcNow.AddDays(-1), EndAt = DateTime.UtcNow.AddDays(30), MilestoneTemplateId = template.Id
            };
            var project = new Project
            {
                Code = Guid.NewGuid().ToString("N"), Title = "Pinned activation", Status = status, CreatedBy = accounts.Student,
                RegisteredAt = DateTime.UtcNow, Team = new Team
                {
                    AcademicSemester = semester, Code = Guid.NewGuid().ToString("N"), Name = "Activation team",
                    Status = "ELIGIBLE", CreatedBy = accounts.Student,
                    TeamMembers = [new TeamMember { UserId = accounts.Student, IsLeader = true, JoinedAt = DateTime.UtcNow }]
                }
            };
            db.MilestoneTemplateVersions.Add(version);
            await db.SaveChangesAsync();
            period.MilestoneTemplateId = withTemplate ? template.Id : null;
            period.MilestoneTemplateVersionId = withTemplate ? version.Id : null;
            db.ProjectPeriods.Add(period);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            projectId = project.Id;
            templateVersionId = version.Id;
            periodId = period.Id;
            templateId = template.Id;
        }

        return new(accounts.Admin, accounts.Student, projectId, templateId, templateVersionId, periodId);
    }
    private sealed record Scenario(long Admin, long Student, long ProjectId, long TemplateId, long VersionId, long PeriodId);
}
