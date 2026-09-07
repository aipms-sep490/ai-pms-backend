using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Teams;

// Uses the same safe schema bootstrap as legacy tests, with a separate owned database.
public sealed class TeamDatabaseFixture : IAsyncLifetime
{
    private readonly IsolatedSqlDatabase database = new();
    public string ConnectionString => database.ConnectionString;
    public static readonly DateTime Now = new(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc);
    private long studentRoleId;

    public AipmsDbContext CreateContext() => new(new DbContextOptionsBuilder<AipmsDbContext>()
        .UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        var source = Environment.GetEnvironmentVariable("AIPMS_TEAM_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(source) && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            source = Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION");
        await database.StartAsync(source);
        try
        {
            await using var context = CreateContext();
            var role = new Role { Code = "STUDENT", Name = "Student", IsSystemRole = true };
            context.Roles.Add(role);
            await context.SaveChangesAsync();
            studentRoleId = role.Id;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    public async System.Threading.Tasks.Task<TeamScenario> SeedAsync()
    {
        await using var context = CreateContext();
        var suffix = Guid.NewGuid().ToString("N");
        var org = new Organization { Code = suffix, Name = "University", IsActive = true };
        var department = new Department { Code = "IT", Name = "IT", Organization = org, IsActive = true };
        var major1 = new Major { Code = "SE", Name = "Software Engineering", Department = department, IsActive = true };
        var major2 = new Major { Code = "IS", Name = "Information Systems", Department = department, IsActive = true };
        var semester = new AcademicSemester
        {
            Code = suffix, Name = "Semester", Organization = org, Status = "ACTIVE",
            StartDate = DateOnly.FromDateTime(Now.AddDays(-30)), EndDate = DateOnly.FromDateTime(Now.AddDays(90))
        };
        var period = new ProjectPeriod
        {
            Code = "REG", Name = "Registration", AcademicSemester = semester, Status = "ACTIVE",
            PeriodType = "REGISTRATION", StartAt = Now.AddDays(-1), EndAt = Now.AddDays(7)
        };
        context.ProjectPeriods.Add(period);
        // Four SE students support capacity/race tests; two IS students support the second single-major flow.
        var users = Enumerable.Range(0, 6).Select(index => new User
        {
            Email = $"student{index}-{suffix}@example.test", FullName = $"Student {index}",
            PasswordHash = "unused-test-hash", Status = "ACTIVE", Department = department,
            Major = index < 4 ? major1 : major2,
            UserRoleUsers = new List<UserRole> { new() { RoleId = studentRoleId } }
        }).ToList();
        context.Users.AddRange(users);
        await context.SaveChangesAsync();
        return new TeamScenario(semester.Id, period.Id, users.Select(u => u.Id).ToArray(), major1.Id, major2.Id);
    }
}

public sealed record TeamScenario(long SemesterId, long PeriodId, long[] Students, long SeMajorId, long IsMajorId);
