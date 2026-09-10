using AIPMS.Application.Common.Security;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Supervisors;

public sealed class SupervisorDatabaseFixture : IAsyncLifetime
{
    private readonly IsolatedSqlDatabase database = new();
    private readonly Dictionary<string, long> roles = new();
    public string ConnectionString => database.ConnectionString;
    public AipmsDbContext CreateContext() => new(new DbContextOptionsBuilder<AipmsDbContext>().UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await database.StartAsync(Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION"));
        try
        {
            await using var context = CreateContext();
            foreach (var code in new[] { AppRoles.Admin, AppRoles.DepartmentStaff, AppRoles.Lecturer, AppRoles.Student })
            {
                var role = new Role { Code = code, Name = code, IsSystemRole = true };
                context.Roles.Add(role);
                await context.SaveChangesAsync();
                roles.Add(code, role.Id);
            }
        }
        catch { await database.DisposeAsync(); throw; }
    }

    public async Task DisposeAsync() => await database.DisposeAsync();

    public async Task<SupervisorScenario> SeedAsync()
    {
        await using var context = CreateContext();
        var suffix = Guid.NewGuid().ToString("N");
        var org = new Organization { Code = suffix, Name = "University", IsActive = true };
        var department = new Department { Code = "IT", Name = "IT", IsActive = true, Organization = org };
        var otherDepartment = new Department { Code = "BUS", Name = "Business", IsActive = true, Organization = org };
        User Account(string name, string role, Department dept) => new()
        {
            Email = $"{name}-{suffix}@example.test", FullName = name, PasswordHash = "unused", Status = "ACTIVE",
            Department = dept, UserRoleUsers = [new() { RoleId = roles[role] }]
        };
        var admin = Account("Admin", AppRoles.Admin, department);
        var staff = Account("Staff", AppRoles.DepartmentStaff, department);
        var outsideStaff = Account("OtherStaff", AppRoles.DepartmentStaff, otherDepartment);
        var student = Account("Student", AppRoles.Student, department);
        var lecturer = Account("Lecturer", AppRoles.Lecturer, department);
        var newLecturer = Account("NewLecturer", AppRoles.Lecturer, department);
        var otherLecturer = Account("OtherLecturer", AppRoles.Lecturer, otherDepartment);
        context.Users.AddRange(admin, staff, outsideStaff, student, lecturer, newLecturer, otherLecturer);
        var profile = new SupervisorProfile
        {
            User = lecturer, Bio = "Original", IsAvailable = true, MaxActiveProjects = 3,
            SupervisorExpertises = [new() { ExpertiseName = "Software Engineering", ProficiencyLevel = "Advanced" }]
        };
        context.SupervisorProfiles.Add(profile);
        await context.SaveChangesAsync();
        return new(admin.Id, staff.Id, outsideStaff.Id, student.Id, lecturer.Id, newLecturer.Id,
            otherLecturer.Id, profile.Id, department.Id);
    }
}

public sealed record SupervisorScenario(long Admin, long Staff, long OutsideStaff, long Student,
    long Lecturer, long NewLecturer, long OtherLecturer, long ProfileId, long DepartmentId);
