using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Security;
using AIPMS.Infrastructure.Identity;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class ProjectAccessServicePersistenceTests
{
    private static AipmsDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new AipmsDbContext(options);
    }

    private static User CreateUser(long id, string email, long? departmentId = null) => new User
    {
        Id = id,
        Email = email,
        FullName = $"User {id}",
        PasswordHash = "hashedpassword",
        Status = "ACTIVE",
        DepartmentId = departmentId
    };

    [Fact]
    public async Task CanAccessAsync_DepartmentStaffInSameDepartment_ReturnsTrue()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());

        var staffUser = CreateUser(100, "staff@dept10.test", departmentId: 10);
        var role = new Role { Id = 2, Code = AppRoles.DepartmentStaff, Name = "Department Staff" };
        var userRole = new UserRole { Id = 1, UserId = 100, RoleId = 2 };

        var department = new Department { Id = 10, Code = "SE", Name = "Software Engineering" };
        var major = new Major { Id = 100, DepartmentId = 10, Code = "SE_MAJ", Name = "Software Engineering Major" };

        var project = new Project { Id = 1, Code = "PRJ-01", Title = "SE Project", Status = "ACTIVE", RowVersion = new byte[] { 1 } };
        var projectMajor = new ProjectMajor { Id = 1, ProjectId = 1, MajorId = 100 };

        db.Users.Add(staffUser);
        db.Roles.Add(role);
        db.UserRoles.Add(userRole);
        db.Departments.Add(department);
        db.Majors.Add(major);
        db.Projects.Add(project);
        db.ProjectMajors.Add(projectMajor);
        await db.SaveChangesAsync();

        var accessService = new ProjectAccessService(db);

        var canAccess = await accessService.CanAccessAsync(100, 1, CancellationToken.None);

        Assert.True(canAccess);
    }

    [Fact]
    public async Task CanAccessAsync_DepartmentStaffInDifferentDepartment_ReturnsFalse()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());

        var staffUser = CreateUser(101, "staff10@test.com", departmentId: 10);
        var role = new Role { Id = 2, Code = AppRoles.DepartmentStaff, Name = "Department Staff" };
        var userRole = new UserRole { Id = 2, UserId = 101, RoleId = 2 };

        var department20 = new Department { Id = 20, Code = "AI", Name = "Artificial Intelligence" };
        var major200 = new Major { Id = 200, DepartmentId = 20, Code = "AI_MAJ", Name = "AI Major" };

        var project2 = new Project { Id = 2, Code = "PRJ-02", Title = "AI Project", Status = "ACTIVE", RowVersion = new byte[] { 1 } };
        var projectMajor2 = new ProjectMajor { Id = 2, ProjectId = 2, MajorId = 200 };

        db.Users.Add(staffUser);
        db.Roles.Add(role);
        db.UserRoles.Add(userRole);
        db.Departments.Add(department20);
        db.Majors.Add(major200);
        db.Projects.Add(project2);
        db.ProjectMajors.Add(projectMajor2);
        await db.SaveChangesAsync();

        var accessService = new ProjectAccessService(db);

        var canAccess = await accessService.CanAccessAsync(101, 2, CancellationToken.None);

        Assert.False(canAccess);
    }

    [Fact]
    public async Task CanAccessAsync_AdminUser_ReturnsTrueForAnyProject()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());

        var adminUser = CreateUser(1, "admin@aipms.test");
        var adminRole = new Role { Id = 1, Code = AppRoles.Admin, Name = "Admin" };
        var userRole = new UserRole { Id = 3, UserId = 1, RoleId = 1 };
        var project = new Project { Id = 99, Code = "PRJ-99", Title = "Any Project", Status = "ACTIVE", RowVersion = new byte[] { 1 } };

        db.Users.Add(adminUser);
        db.Roles.Add(adminRole);
        db.UserRoles.Add(userRole);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var accessService = new ProjectAccessService(db);

        var canAccess = await accessService.CanAccessAsync(1, 99, CancellationToken.None);

        Assert.True(canAccess);
    }

    [Fact]
    public async Task CanAccessAsync_ActiveTeamMember_ReturnsTrue()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());

        var studentUser = CreateUser(50, "student@test.com");
        var studentRole = new Role { Id = 3, Code = AppRoles.Student, Name = "Student" };
        var userRole = new UserRole { Id = 4, UserId = 50, RoleId = 3 };

        var team = new Team { Id = 5, AcademicSemesterId = 1, Code = "TM-5", Name = "Team A", Status = "ACTIVE" };
        var teamMember = new TeamMember { Id = 1, TeamId = 5, AcademicSemesterId = 1, UserId = 50, LeftAt = null };
        var project = new Project { Id = 5, TeamId = 5, Code = "PRJ-05", Title = "Team Project", Status = "ACTIVE", RowVersion = new byte[] { 1 } };

        db.Users.Add(studentUser);
        db.Roles.Add(studentRole);
        db.UserRoles.Add(userRole);
        db.Teams.Add(team);
        db.TeamMembers.Add(teamMember);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var accessService = new ProjectAccessService(db);

        var canAccess = await accessService.CanAccessAsync(50, 5, CancellationToken.None);

        Assert.True(canAccess);
    }

    [Fact]
    public async Task CanAccessAsync_FormerTeamMember_ReturnsFalse()
    {
        using var db = CreateInMemoryDbContext(Guid.NewGuid().ToString());

        var studentUser = CreateUser(51, "former@test.com");
        var studentRole = new Role { Id = 3, Code = AppRoles.Student, Name = "Student" };
        var userRole = new UserRole { Id = 5, UserId = 51, RoleId = 3 };

        var team = new Team { Id = 6, AcademicSemesterId = 1, Code = "TM-6", Name = "Team B", Status = "ACTIVE" };
        var teamMember = new TeamMember { Id = 2, TeamId = 6, AcademicSemesterId = 1, UserId = 51, LeftAt = DateTime.UtcNow.AddDays(-5) };
        var project = new Project { Id = 6, TeamId = 6, Code = "PRJ-06", Title = "Team B Project", Status = "ACTIVE", RowVersion = new byte[] { 1 } };

        db.Users.Add(studentUser);
        db.Roles.Add(studentRole);
        db.UserRoles.Add(userRole);
        db.Teams.Add(team);
        db.TeamMembers.Add(teamMember);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var accessService = new ProjectAccessService(db);

        var canAccess = await accessService.CanAccessAsync(51, 6, CancellationToken.None);

        Assert.False(canAccess);
    }
}
