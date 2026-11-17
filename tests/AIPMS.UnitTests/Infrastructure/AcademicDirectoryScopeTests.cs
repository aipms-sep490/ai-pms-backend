using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Application.Features.Supervisors.Queries;
using AIPMS.Application.Features.Supervisors.Services;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class AcademicDirectoryScopeTests
{
    [Fact]
    public async Task Period_And_Semester_Reads_Exclude_Foreign_Organization_Before_Count_And_Detail()
    {
        await using var db = await SeedAsync();
        var reader = new AcademicDirectoryScopeReader(new CurrentUser(10), db);
        var scope = await reader.GetAsync();
        var repository = new SemesterRepository(db);

        Assert.Equal(1, scope.OrganizationId);
        Assert.Null(scope.StaffDepartmentId);
        var semesters = await repository.GetSemestersAsync(null, null, null, 1, 20,
            authorizedOrganizationId: scope.OrganizationId);
        var periods = await repository.GetProjectPeriodsAsync(null, null, null, null, 1, 20,
            authorizedOrganizationId: scope.OrganizationId);
        Assert.Equal(1, semesters.TotalCount);
        Assert.Equal(1, periods.TotalCount);
        Assert.Null(await repository.GetSemesterAsync(2, authorizedOrganizationId: scope.OrganizationId));
        Assert.Null(await repository.GetProjectPeriodAsync(2, authorizedOrganizationId: scope.OrganizationId));
        Assert.Equal(2, (await repository.GetProjectPeriodsAsync(null, null, null, null, 1, 20)).TotalCount);
    }

    [Fact]
    public async Task Staff_Supervisor_Directory_Is_Limited_To_Own_Department()
    {
        await using var db = await SeedAsync();
        var actor = new CurrentUser(11);
        var repository = new SupervisorProfileRepository(db);
        var scopeReader = new AcademicDirectoryScopeReader(actor, db);
        var list = new GetSupervisorsQueryHandler(repository,
            new SupervisorAccessService(actor, repository), scopeReader);
        var detail = new GetSupervisorByIdQueryHandler(repository,
            new SupervisorAccessService(actor, repository), scopeReader);

        var own = await list.Handle(new GetSupervisorsQuery(), default);
        Assert.Equal(1, own.TotalCount);
        Assert.Equal(20, Assert.Single(own.Items).UserId);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            list.Handle(new GetSupervisorsQuery(DepartmentId: 2), default));
        await Assert.ThrowsAsync<NotFoundException>(() => detail.Handle(new(2), default));
    }

    [Fact]
    public async Task Student_Sees_Same_Organization_And_Admin_Sees_Global_Directory()
    {
        await using var db = await SeedAsync();
        var repository = new SupervisorProfileRepository(db);
        var studentScope = await new AcademicDirectoryScopeReader(new CurrentUser(10), db).GetAsync();
        var adminScope = await new AcademicDirectoryScopeReader(new CurrentUser(12), db).GetAsync();

        Assert.Equal(2, (await repository.SearchAsync(new SupervisorSearch(null, null, null, null, 1, 20,
            studentScope.OrganizationId), default)).TotalCount);
        Assert.Equal(3, (await repository.SearchAsync(new SupervisorSearch(null, null, null, null, 1, 20,
            adminScope.OrganizationId), default)).TotalCount);
    }

    [Fact]
    public async Task Inactive_Department_Is_Denied_While_System_Admin_Remains_Global()
    {
        await using var db = await SeedAsync();
        db.Departments.Single(department => department.Id == 1).IsActive = false;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new AcademicDirectoryScopeReader(new CurrentUser(11), db).GetAsync());
        Assert.Null((await new AcademicDirectoryScopeReader(new CurrentUser(12), db).GetAsync()).OrganizationId);
    }

    private static async Task<AipmsDbContext> SeedAsync()
    {
        var db = new AipmsDbContext(new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var org1 = new Organization { Id = 1, Code = "ORG1", Name = "One", IsActive = true };
        var org2 = new Organization { Id = 2, Code = "ORG2", Name = "Two", IsActive = true };
        var dept1 = new Department { Id = 1, Code = "D1", Name = "One A", IsActive = true, Organization = org1 };
        var dept2 = new Department { Id = 2, Code = "D2", Name = "One B", IsActive = true, Organization = org1 };
        var dept3 = new Department { Id = 3, Code = "D3", Name = "Two A", IsActive = true, Organization = org2 };
        var studentRole = new Role { Id = 1, Code = AppRoles.Student, Name = "Student" };
        var staffRole = new Role { Id = 2, Code = AppRoles.DepartmentStaff, Name = "Staff" };
        var adminRole = new Role { Id = 3, Code = AppRoles.Admin, Name = "Admin" };
        var lecturerRole = new Role { Id = 4, Code = AppRoles.Lecturer, Name = "Lecturer" };
        User Person(long id, Department? department, Role role) => new()
        {
            Id = id, Email = $"u{id}@example.test", FullName = $"User {id}", PasswordHash = "hash",
            Status = "ACTIVE", Department = department,
            UserRoleUsers = [new UserRole { Role = role }]
        };
        var lecturer1 = Person(20, dept1, lecturerRole);
        var lecturer2 = Person(21, dept2, lecturerRole);
        var lecturer3 = Person(22, dept3, lecturerRole);
        db.Users.AddRange(Person(10, dept1, studentRole), Person(11, dept1, staffRole),
            Person(12, null, adminRole), lecturer1, lecturer2, lecturer3);
        db.SupervisorProfiles.AddRange(
            new SupervisorProfile { Id = 1, User = lecturer1, IsAvailable = true },
            new SupervisorProfile { Id = 2, User = lecturer2, IsAvailable = true },
            new SupervisorProfile { Id = 3, User = lecturer3, IsAvailable = true });
        var sem1 = new AcademicSemester { Id = 1, Organization = org1, Code = "S1", Name = "Sem One",
            Status = "DRAFT", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1) };
        var sem2 = new AcademicSemester { Id = 2, Organization = org2, Code = "S2", Name = "Sem Two",
            Status = "DRAFT", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1) };
        db.AcademicSemesters.AddRange(sem1, sem2);
        db.ProjectPeriods.AddRange(
            new ProjectPeriod { Id = 1, AcademicSemester = sem1, Code = "P1", Name = "One",
                PeriodType = "REGISTRATION", Status = "DRAFT" },
            new ProjectPeriod { Id = 2, AcademicSemester = sem2, Code = "P2", Name = "Two",
                PeriodType = "REGISTRATION", Status = "DRAFT" });
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class CurrentUser(long id) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => id;
        public string? Email => null;
        public string? FullName => null;
        public IReadOnlyCollection<string> Roles => [];
    }
}
