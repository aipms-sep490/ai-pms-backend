using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.Application.Features.Dashboards.DTOs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Dashboards;

[Collection("ProjectDbTests")]
public sealed class DashboardRepositoryTests(DbFixture fixture)
{
    [Fact]
    public async Task StudentDashboard_ReadsOnlyAssignedProjectTasksAndProgressFacts()
    {
        await using var setup = fixture.CreateContext();
        var teamId = await setup.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).SingleAsync();
        var studentId = await setup.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).SingleAsync();
        var project = new M.Project
        {
            TeamId = teamId,
            Code = "DASHBOARD_TEST_PROJECT",
            Title = "Dashboard project",
            Status = "ACTIVE",
            CreatedBy = studentId,
            RegisteredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Milestones =
            [
                new M.Milestone
                {
                    Title = "Execution",
                    Status = "IN_PROGRESS",
                    SortOrder = 1,
                    CreatedBy = studentId,
                    DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
                    Tasks =
                    [
                        new M.Task
                        {
                            Title = "Assigned task",
                            Status = "IN_PROGRESS",
                            CreatedBy = studentId,
                            DueAt = DateTime.UtcNow.AddDays(1),
                            TaskAssignees = [new M.TaskAssignee { UserId = studentId, AssignedBy = studentId, AssignedAt = DateTime.UtcNow }]
                        }
                    ]
                }
            ]
        };
        setup.Projects.Add(project);
        await setup.SaveChangesAsync();

        try
        {
            var repository = new DashboardRepository(fixture.CreateContext());
            var facts = await repository.GetStudentAsync(studentId, project.Id, DateTime.UtcNow, default);

            Assert.NotNull(facts.Project);
            Assert.Equal(project.Id, facts.Project!.Id);
            Assert.Equal(1, facts.AssignedOpenTasks);
            var task = Assert.Single(facts.Project.Facts.Tasks);
            Assert.Equal("IN_PROGRESS", task.Status);
            Assert.Single(facts.MilestoneDeadlines);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext();
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.tasks WHERE milestone_id IN (SELECT id FROM dbo.milestones WHERE project_id = {project.Id})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.milestones WHERE project_id = {project.Id}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.projects WHERE id = {project.Id}");
        }
    }

    [Fact]
    public async Task DepartmentPortfolio_UsesDepartmentScopeAndStableCreatedOrdering()
    {
        await using var setup = fixture.CreateContext();
        var teamId = await setup.Teams.Where(t => t.Name == "Team One").Select(t => t.Id).SingleAsync();
        var secondTeamId = await setup.Teams.Where(t => t.Name == "Team Two").Select(t => t.Id).SingleAsync();
        var studentId = await setup.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.Id).SingleAsync();
        var secondStudentId = await setup.Users.Where(u => u.Email == "student2@aipms.test").Select(u => u.Id).SingleAsync();
        var staffId = await setup.Users.Where(u => u.Email == "staff@aipms.test").Select(u => u.Id).SingleAsync();
        var majorId = await setup.Users.Where(u => u.Email == "student1@aipms.test").Select(u => u.MajorId).SingleAsync();
        var createdAt = DateTime.UtcNow;
        var first = new M.Project
        {
            TeamId = teamId,
            Code = "PORTFOLIO_SCOPE_A",
            Title = "Portfolio A",
            Status = "DRAFT",
            CreatedBy = studentId,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            ProjectMajors = [new M.ProjectMajor { MajorId = majorId!.Value, CreatedAt = createdAt }]
        };
        var second = new M.Project
        {
            TeamId = secondTeamId,
            Code = "PORTFOLIO_SCOPE_B",
            Title = "Portfolio B",
            Status = "DRAFT",
            CreatedBy = secondStudentId,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            ProjectMajors = [new M.ProjectMajor { MajorId = majorId.Value, CreatedAt = createdAt }]
        };
        setup.Projects.AddRange(first, second);
        await setup.SaveChangesAsync();

        try
        {
            var repository = new DashboardRepository(fixture.CreateContext());
            var facts = await repository.GetPortfolioAsync(staffId, false,
                new DashboardPortfolioFilter(null, null, majorId, "DRAFT", "PORTFOLIO_SCOPE", 1, 1),
                DateTime.UtcNow, default);

            Assert.Equal(2, facts.Projects.Count);
            Assert.Equal(second.Id, facts.Projects[0].Id);
            Assert.Equal(first.Id, facts.Projects[1].Id);
            Assert.Equal(majorId, Assert.Single(facts.Projects[0].Majors!).Id);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext();
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.project_majors WHERE project_id IN ({first.Id}, {second.Id})");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.projects WHERE id IN ({first.Id}, {second.Id})");
        }
    }
}
