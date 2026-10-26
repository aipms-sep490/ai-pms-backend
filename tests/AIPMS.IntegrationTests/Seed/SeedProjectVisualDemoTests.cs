using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIPMS.IntegrationTests.Seed;

public sealed class SeedProjectVisualDemoTests(SupervisorDatabaseFixture database) : IClassFixture<SupervisorDatabaseFixture>
{
    private static string GetSeedScriptSql()
    {
        var path = Path.Combine(SqlSchemaBootstrap.Root, "db", "seed_project_visual_demo.sql");
        return System.IO.File.ReadAllText(path);
    }

    [Fact]
    public async Task Seed_WhenProjectCodeDoesNotExist_FailsFastWithRollback_AndDoesNotMutateData()
    {
        // On a fresh database where project 'SEP490-FA26-AIPMS' does not exist
        var ex = await Assert.ThrowsAsync<SqlException>(async () =>
        {
            await SqlSchemaBootstrap.ExecuteAsync(database.ConnectionString, GetSeedScriptSql());
        });

        Assert.Contains("[SECURITY & ISOLATION CHECK]", ex.Message);

        // Verify zero milestones or deliverables were created
        await using var db = database.CreateContext();
        var milestonesCount = await db.Milestones.CountAsync();
        Assert.Equal(0, milestonesCount);
    }

    [Fact]
    public async Task Seed_Rerun_PreservesUserCustomizationsAndWorkingAgreement()
    {
        var s = await database.SeedAsync();
        long projectId;
        long leaderUserId;
        const string targetProjectCode = "SEP490-FA26-AIPMS";

        await using (var db = database.CreateContext())
        {
            var org = await db.Organizations.FirstAsync();
            var dept = await db.Departments.FirstAsync();

            var semester = new AcademicSemester
            {
                OrganizationId = org.Id,
                Code = "FA26-" + Guid.NewGuid().ToString("N")[..6],
                Name = "Fall 2026",
                Status = "ACTIVE",
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(4))
            };
            db.AcademicSemesters.Add(semester);
            await db.SaveChangesAsync();

            var studentRole = await db.Roles.SingleAsync(r => r.Code == "STUDENT");

            var leader = new User
            {
                DepartmentId = dept.Id,
                Email = "khangnmde180901@fpt.edu.vn",
                FullName = "khangnmde180901",
                PasswordHash = "unused",
                Status = "ACTIVE",
                UserRoleUsers = [new() { RoleId = studentRole.Id }]
            };
            db.Users.Add(leader);
            await db.SaveChangesAsync();
            leaderUserId = leader.Id;

            var team = new Team
            {
                Code = "TEAM-" + Guid.NewGuid().ToString("N")[..6],
                Name = "AIPMS Capstone Team",
                AcademicSemesterId = semester.Id,
                CreatedBy = leader.Id,
                Status = "ELIGIBLE",
                TeamMembers = [new() { AcademicSemesterId = semester.Id, UserId = leader.Id, IsLeader = true, MembershipGeneration = Guid.NewGuid() }]
            };
            db.Teams.Add(team);

            var project = new Project
            {
                Code = targetProjectCode,
                Title = "AI Capstone Project",
                Status = "APPROVED",
                CreatedBy = leader.Id,
                Team = team
            };
            db.Projects.Add(project);

            var period = new ProjectPeriod
            {
                AcademicSemesterId = semester.Id,
                Code = "SEP490-FA26-EXEC",
                Name = "Execution Period",
                PeriodType = "PROJECT_EXECUTION",
                Status = "ACTIVE",
                StartAt = DateTime.UtcNow.AddDays(-10),
                EndAt = DateTime.UtcNow.AddDays(80)
            };
            db.ProjectPeriods.Add(period);

            db.SupervisorAssignments.Add(new SupervisorAssignment
            {
                Project = project,
                SupervisorProfileId = s.ProfileId,
                IsPrimary = true,
                AssignmentType = "PRIMARY",
                AssignedAt = DateTime.UtcNow,
                SupervisorRequest = new SupervisorRequest
                {
                    Project = project,
                    SupervisorProfileId = s.ProfileId,
                    Status = "ACCEPTED",
                    AssignmentType = "PRIMARY",
                    RequestedAt = DateTime.UtcNow,
                    RespondedAt = DateTime.UtcNow
                }
            });

            await db.SaveChangesAsync();
            projectId = project.Id;
        }

        // 1. First Run: Seeds visual demo data
        await SqlSchemaBootstrap.ExecuteAsync(database.ConnectionString, GetSeedScriptSql());

        await using (var db = database.CreateContext())
        {
            var milestones = await db.Milestones.Where(m => m.ProjectId == projectId).ToListAsync();
            Assert.NotEmpty(milestones);

            var user = await db.Users.FindAsync(leaderUserId);
            Assert.NotNull(user);
            Assert.Equal("Nguyễn Minh Khang", user.FullName); // Seeded default Vietnamese name

            var agreement = await db.Set<WorkingAgreement>().SingleOrDefaultAsync(a => a.ProjectId == projectId);
            Assert.NotNull(agreement);
            Assert.Equal(1, agreement.Version);
        }

        // 2. User edits their name and milestone title
        const string customEditedName = "Nguyễn Minh Khang (Custom Leader Edit)";
        const string customMilestoneTitle = "Giai đoạn 1: Khảo sát (Custom Title Edit)";

        await using (var db = database.CreateContext())
        {
            var user = await db.Users.FindAsync(leaderUserId);
            user!.FullName = customEditedName;

            var m1 = await db.Milestones.Where(m => m.ProjectId == projectId && m.SortOrder == 1).SingleAsync();
            m1.Title = customMilestoneTitle;

            await db.SaveChangesAsync();
        }

        // 3. Rerun Seed: Must preserve modified data without overwriting
        await SqlSchemaBootstrap.ExecuteAsync(database.ConnectionString, GetSeedScriptSql());

        await using (var db = database.CreateContext())
        {
            var userAfter = await db.Users.FindAsync(leaderUserId);
            Assert.NotNull(userAfter);
            Assert.Equal(customEditedName, userAfter.FullName); // Custom name preserved!

            var m1After = await db.Milestones.Where(m => m.ProjectId == projectId && m.SortOrder == 1).SingleAsync();
            Assert.Equal(customMilestoneTitle, m1After.Title); // Custom title preserved!

            var agreements = await db.Set<WorkingAgreement>().Where(a => a.ProjectId == projectId).ToListAsync();
            Assert.Single(agreements);
            Assert.Equal(1, agreements[0].Version); // No duplicate versions created!
        }
    }
}
