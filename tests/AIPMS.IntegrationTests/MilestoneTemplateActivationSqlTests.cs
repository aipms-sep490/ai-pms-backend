using AIPMS.Application.Abstractions.Projects;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests;

public sealed class MilestoneTemplateActivationSqlTests(SupervisorDatabaseFixture database)
    : IClassFixture<SupervisorDatabaseFixture>
{
    [Fact]
    public async Task Pinned_template_version_is_applied_once_and_retry_is_idempotent()
    {
        var accounts = await database.SeedAsync();
        long projectId;
        long templateVersionId;
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
                Code = Guid.NewGuid().ToString("N"), Title = "Pinned activation", Status = "APPROVED", CreatedBy = accounts.Student,
                RegisteredAt = DateTime.UtcNow, Team = new Team
                {
                    AcademicSemester = semester, Code = Guid.NewGuid().ToString("N"), Name = "Activation team",
                    Status = "ELIGIBLE", CreatedBy = accounts.Student,
                    TeamMembers = [new TeamMember { UserId = accounts.Student, IsLeader = true, JoinedAt = DateTime.UtcNow }]
                }
            };
            db.MilestoneTemplateVersions.Add(version);
            await db.SaveChangesAsync();
            period.MilestoneTemplateId = template.Id;
            period.MilestoneTemplateVersionId = version.Id;
            db.ProjectPeriods.Add(period);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            projectId = project.Id;
            templateVersionId = version.Id;
        }

        using var app = new SupervisorFactory(database);
        using (var scope = app.Services.CreateScope())
        {
            var activation = scope.ServiceProvider.GetRequiredService<IProjectActivationService>();
            var now = DateTime.UtcNow;
            await activation.ApplyMilestoneTemplateAsync(projectId, accounts.Admin, now);
            await activation.ApplyMilestoneTemplateAsync(projectId, accounts.Admin, now.AddMinutes(1));
        }

        await using var verify = database.CreateContext();
        Assert.Equal(1, await verify.ProjectMilestoneTemplateApplications.CountAsync(a => a.ProjectId == projectId));
        Assert.Equal(1, await verify.Milestones.CountAsync(m => m.ProjectId == projectId));
        Assert.Equal(templateVersionId, await verify.ProjectMilestoneTemplateApplications
            .Where(a => a.ProjectId == projectId).Select(a => a.MilestoneTemplateVersionId).SingleAsync());
    }
}
