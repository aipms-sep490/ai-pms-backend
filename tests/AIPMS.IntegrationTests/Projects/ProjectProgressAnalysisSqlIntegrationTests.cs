using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Projects;

public sealed class ProjectProgressAnalysisSqlIntegrationTests(SupervisorDatabaseFixture database)
    : IClassFixture<SupervisorDatabaseFixture>
{
    private sealed class Factory(SupervisorDatabaseFixture database) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString
            }));
        }
    }

    private async Task<(long ProjectAId, long StudentAId, long ProjectBId, long StudentBId)> SeedTwoProjectsAsync()
    {
        var s = await database.SeedAsync();
        await using var db = database.CreateContext();
        var studentRole = await db.Roles.SingleAsync(r => r.Code == AppRoles.Student);

        var studentA = new M.User
        {
            DepartmentId = s.DepartmentId,
            Email = $"studentA-{Guid.NewGuid():N}@test.local",
            FullName = "Student A",
            PasswordHash = "unused",
            Status = "ACTIVE",
            UserRoleUsers = [new() { RoleId = studentRole.Id }]
        };

        var studentB = new M.User
        {
            DepartmentId = s.DepartmentId,
            Email = $"studentB-{Guid.NewGuid():N}@test.local",
            FullName = "Student B",
            PasswordHash = "unused",
            Status = "ACTIVE",
            UserRoleUsers = [new() { RoleId = studentRole.Id }]
        };

        var semester = new M.AcademicSemester
        {
            OrganizationId = (await db.Departments.FindAsync(s.DepartmentId))!.OrganizationId,
            Code = $"SEM-{Guid.NewGuid():N}",
            Name = "Test Semester",
            Status = "ACTIVE",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30))
        };

        var major = new M.Major
        {
            DepartmentId = s.DepartmentId,
            Code = $"MAJ-{Guid.NewGuid():N}",
            Name = "SE",
            IsActive = true
        };

        db.Users.AddRange(studentA, studentB);
        db.AcademicSemesters.Add(semester);
        db.Majors.Add(major);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;

        // Project A
        var teamA = new M.Team
        {
            Code = $"TEAM-A-{Guid.NewGuid():N}",
            Name = "Team A",
            AcademicSemesterId = semester.Id,
            CreatedBy = studentA.Id,
            Status = "ELIGIBLE",
            TeamMembers = [new() { AcademicSemesterId = semester.Id, UserId = studentA.Id, IsLeader = true, JoinedAt = now.AddDays(-10) }]
        };

        var projectA = new M.Project
        {
            Code = $"PROJ-A-{Guid.NewGuid():N}",
            Title = "Project A",
            Status = "ACTIVE",
            CreatedBy = studentA.Id,
            Team = teamA,
            ProjectMajors = [new() { MajorId = major.Id }]
        };

        // Project B
        var teamB = new M.Team
        {
            Code = $"TEAM-B-{Guid.NewGuid():N}",
            Name = "Team B",
            AcademicSemesterId = semester.Id,
            CreatedBy = studentB.Id,
            Status = "ELIGIBLE",
            TeamMembers = [new() { AcademicSemesterId = semester.Id, UserId = studentB.Id, IsLeader = true, JoinedAt = now.AddDays(-10) }]
        };

        var projectB = new M.Project
        {
            Code = $"PROJ-B-{Guid.NewGuid():N}",
            Title = "Project B",
            Status = "ACTIVE",
            CreatedBy = studentB.Id,
            Team = teamB,
            ProjectMajors = [new() { MajorId = major.Id }]
        };

        db.Projects.AddRange(projectA, projectB);
        await db.SaveChangesAsync();

        // Project A Execution Data:
        // - Milestone A1: Active with future due date
        // - Milestone A2: Cancelled with past due date (verifies Cancelled exclusion)
        // - Task A1: Active with future due date (0 overdue)
        // - ProgressReport A1: Draft with past PeriodEnd (verifies PeriodEnd != deadline)
        var milestoneA1 = new M.Milestone
        {
            ProjectId = projectA.Id,
            Title = "Milestone A1",
            Status = "COMPLETED",
            DueDate = DateOnly.FromDateTime(now.AddDays(14)),
            CreatedBy = studentA.Id
        };
        var milestoneA2 = new M.Milestone
        {
            ProjectId = projectA.Id,
            Title = "Milestone A2 Cancelled",
            Status = "CANCELLED",
            DueDate = DateOnly.FromDateTime(now.AddDays(-10)),
            CreatedBy = studentA.Id
        };
        db.Milestones.AddRange(milestoneA1, milestoneA2);
        await db.SaveChangesAsync();

        var taskA1 = new M.Task
        {
            MilestoneId = milestoneA1.Id,
            Title = "Task A1",
            Status = "TODO",
            DueAt = now.AddDays(7),
            CreatedBy = studentA.Id,
            TaskAssignees = [new() { UserId = studentA.Id, AssignedBy = studentA.Id }]
        };
        var reportA1 = new M.ProgressReport
        {
            ProjectId = projectA.Id,
            SubmittedBy = studentA.Id,
            ReportType = "WEEKLY",
            PeriodStart = DateOnly.FromDateTime(now.AddDays(-14)),
            PeriodEnd = DateOnly.FromDateTime(now.AddDays(-2)),
            Summary = "Draft Report A1",
            Status = "DRAFT"
        };
        db.Tasks.Add(taskA1);
        db.ProgressReports.Add(reportA1);

        // Project B Execution Data:
        // - Milestone B1: Past due date
        // - Task B1: Overdue
        // - Task B2: Blocked
        // - Task B3: Overdue
        var milestoneB1 = new M.Milestone
        {
            ProjectId = projectB.Id,
            Title = "Milestone B1",
            Status = "IN_PROGRESS",
            DueDate = DateOnly.FromDateTime(now.AddDays(-5)),
            CreatedBy = studentB.Id
        };
        db.Milestones.Add(milestoneB1);
        await db.SaveChangesAsync();

        var taskB1 = new M.Task
        {
            MilestoneId = milestoneB1.Id,
            Title = "Task B1 Overdue",
            Status = "IN_PROGRESS",
            DueAt = now.AddDays(-5),
            CreatedBy = studentB.Id,
            TaskAssignees = [new() { UserId = studentB.Id, AssignedBy = studentB.Id }]
        };
        var taskB2 = new M.Task
        {
            MilestoneId = milestoneB1.Id,
            Title = "Task B2 Blocked",
            Status = "BLOCKED",
            DueAt = now.AddDays(-2),
            CreatedBy = studentB.Id,
            TaskAssignees = [new() { UserId = studentB.Id, AssignedBy = studentB.Id }]
        };
        var taskB3 = new M.Task
        {
            MilestoneId = milestoneB1.Id,
            Title = "Task B3 Overdue",
            Status = "TODO",
            DueAt = now.AddDays(-1),
            CreatedBy = studentB.Id,
            TaskAssignees = [new() { UserId = studentB.Id, AssignedBy = studentB.Id }]
        };
        db.Tasks.AddRange(taskB1, taskB2, taskB3);
        await db.SaveChangesAsync();

        return (projectA.Id, studentA.Id, projectB.Id, studentB.Id);
    }

    [Fact]
    public async Task SQL_TwoProjects_AuthorizedProjectReadsOnlyOwnProgressFacts()
    {
        var (projectAId, studentAId, projectBId, _) = await SeedTwoProjectsAsync();
        using var app = new Factory(database);
        using var clientA = app.CreateAuthenticatedClient(studentAId, "studentA@test.local", "Student A", AppRoles.Student);

        var response = await clientA.GetAsync($"/api/v1/projects/{projectAId}/progress-analysis");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ProjectProgressAnalysisDto>();
        Assert.NotNull(dto);

        // Verifies real reader read ONLY Project A facts without leaking Project B
        Assert.Equal(projectAId, dto.ProjectId);
        Assert.Equal(1, dto.ProgressSummary.TotalTasks); // Only Task A1 (Project B has 3 tasks)
        Assert.Equal(0, dto.ProgressSummary.OverdueTasks); // Task A1 is not overdue (Project B's overdue tasks not leaked)
        Assert.Equal(0, dto.ProgressSummary.BlockedTasks); // Task A1 is not blocked (Project B's blocked task not leaked)
        Assert.Equal(2, dto.ProgressSummary.TotalMilestones); // Milestone A1 and A2

        // Verifies P2 #2: Cancelled milestone A2 does NOT produce milestone delay or overdue factor
        Assert.Equal(0.0, dto.FeatureSnapshot.MilestoneDelayDays);
        Assert.DoesNotContain(dto.Factors, f => f.Code == "MILESTONE_OVERDUE");

        // Verifies P2 #1: Draft report with past PeriodEnd does NOT count as missing report
        Assert.Null(dto.FeatureSnapshot.MissingReportCount);
        Assert.DoesNotContain(dto.Factors, f => f.Code == "UNSUBMITTED_PROGRESS_REPORT");

        // Overall risk is LOW and data status is SUFFICIENT based on Project A evidence alone
        Assert.Equal("LOW", dto.RiskLevel);
        Assert.Equal("SUFFICIENT", dto.DataStatus);
    }

    [Fact]
    public async Task SQL_TwoProjects_CrossProjectActorDenied()
    {
        var (projectAId, _, _, studentBId) = await SeedTwoProjectsAsync();
        using var app = new Factory(database);

        // Student B is a member of Project B, but NOT Project A
        using var clientB = app.CreateAuthenticatedClient(studentBId, "studentB@test.local", "Student B", AppRoles.Student);

        var response = await clientB.GetAsync($"/api/v1/projects/{projectAId}/progress-analysis");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SQL_Unauthenticated_Denied()
    {
        var (projectAId, _, _, _) = await SeedTwoProjectsAsync();
        using var app = new Factory(database);
        using var anonymous = app.CreateClient();

        var response = await anonymous.GetAsync($"/api/v1/projects/{projectAId}/progress-analysis");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SQL_NonExistentProject_NotFound()
    {
        var (_, studentAId, _, _) = await SeedTwoProjectsAsync();
        using var app = new Factory(database);
        using var clientA = app.CreateAuthenticatedClient(studentAId, "studentA@test.local", "Student A", AppRoles.Student);

        var response = await clientA.GetAsync("/api/v1/projects/99999999/progress-analysis");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
