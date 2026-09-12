using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Topics;

public sealed class TopicDatabaseFixture : IAsyncLifetime
{
    private readonly SupervisorDatabaseFixture database = new();
    public static readonly DateTime Now = new(2026, 9, 13, 8, 0, 0, DateTimeKind.Utc);
    public string ConnectionString => database.ConnectionString;
    public AipmsDbContext CreateContext() => database.CreateContext();

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        try { await Migrate(); }
        catch { await database.DisposeAsync(); throw; }
    }
    public Task DisposeAsync() => database.DisposeAsync();

    public async Task Migrate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "db", "changes"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var sql = await File.ReadAllTextAsync(Path.Combine(dir.FullName, "db", "changes", "20260913_add_topic_catalog.sql"));
        await using var db = CreateContext();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    public async Task<TopicScenario> Seed()
    {
        var users = await database.SeedAsync();
        await using var db = CreateContext();
        var department = (await db.Departments.FindAsync(users.DepartmentId))!;
        var otherDepartment = (await db.Users.FindAsync(users.OutsideStaff))!.DepartmentId!.Value;
        var major = new M.Major { Code = "SE", Name = "Software", DepartmentId = department.Id, IsActive = true };
        var otherMajor = new M.Major { Code = "BUS", Name = "Business", DepartmentId = otherDepartment, IsActive = true };
        var semester = new M.AcademicSemester { OrganizationId = department.OrganizationId, Code = Guid.NewGuid().ToString("N"),
            Name = "Topic semester", Status = "ACTIVE", StartDate = DateOnly.FromDateTime(Now.AddDays(-10)), EndDate = DateOnly.FromDateTime(Now.AddDays(90)) };
        var period = new M.ProjectPeriod { AcademicSemester = semester, Code = "REG", Name = "Registration", PeriodType = "REGISTRATION",
            Status = "ACTIVE", StartAt = Now.AddDays(-1), EndAt = Now.AddDays(15), MinTeamSize = 2, MaxTeamSize = 5, MinDistinctMajors = 1 };
        db.AddRange(major, otherMajor, period);
        var student = (await db.Users.FindAsync(users.Student))!; student.Major = major;
        var studentRole = await db.Roles.SingleAsync(r => r.Code == "STUDENT");
        var otherStudent = new M.User { Email = Guid.NewGuid() + "@example.test", FullName = "Business student", Status = "ACTIVE", PasswordHash = "unused",
            DepartmentId = otherDepartment, Major = otherMajor, UserRoleUsers = [new() { RoleId = studentRole.Id }] };
        db.Users.Add(otherStudent);
        await db.SaveChangesAsync();
        return new(users, department.OrganizationId, semester.Id, period.Id, major.Id, otherMajor.Id, otherDepartment, otherStudent.Id);
    }
}

public sealed record TopicScenario(SupervisorScenario Users, long Organization, long Semester, long Period,
    long Major, long OtherMajor, long OtherDepartment, long OtherStudent);
