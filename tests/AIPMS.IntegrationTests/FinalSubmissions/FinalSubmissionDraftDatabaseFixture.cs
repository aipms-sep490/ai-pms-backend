using System.Text.RegularExpressions;
using AIPMS.Application.Common.Security;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.FinalSubmissions;

public sealed class FinalSubmissionDraftDatabaseFixture : IAsyncLifetime
{
    private readonly SupervisorDatabaseFixture database = new();
    public string ConnectionString => database.ConnectionString;
    public static readonly DateTime Now = new(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);
    public AipmsDbContext CreateContext() => database.CreateContext();
    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        try { await Migrate(); }
        catch { await database.DisposeAsync(); throw; }
    }
    public Task DisposeAsync() => database.DisposeAsync();
    public Task Migrate() => FinalSubmissionTestMigration.Apply(ConnectionString);

    public async Task<FinalDraftScenario> Seed()
    {
        var users = await database.SeedAsync();
        await using var db = CreateContext();
        var organizationId = await db.Departments.Where(d => d.Id == users.DepartmentId).Select(d => d.OrganizationId).SingleAsync();
        var semester = new M.AcademicSemester { OrganizationId = organizationId, Code = Guid.NewGuid().ToString("N"),
            Name = "Final Semester", Status = "ACTIVE", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) };
        db.AcademicSemesters.Add(semester);
        await db.SaveChangesAsync();
        var studentRole = await db.Roles.SingleAsync(r => r.Code == AppRoles.Student);
        var member = new M.User { DepartmentId = users.DepartmentId, Email = $"{Guid.NewGuid():N}@test.local",
            FullName = "Member", PasswordHash = "unused", Status = "ACTIVE", UserRoleUsers = [new() { RoleId = studentRole.Id }] };
        var project = new M.Project { Code = Guid.NewGuid().ToString("N"), Title = "Final project", Status = "ACTIVE", CreatedBy = users.Student,
            Team = new M.Team { Code = Guid.NewGuid().ToString("N"), Name = "Final team", AcademicSemester = semester,
                CreatedBy = users.Student, Status = "LOCKED", TeamMembers = [
                    new() { UserId = users.Student, IsLeader = true, AcademicSemesterId = semester.Id, JoinedAt = Now.AddDays(-1) },
                    new() { User = member, IsLeader = false, AcademicSemesterId = semester.Id, JoinedAt = Now.AddDays(-1) }] },
            ProjectMajors = [new() { Major = new() { DepartmentId = users.DepartmentId, Code = Guid.NewGuid().ToString("N"), Name = "SE", IsActive = true } }] };
        var period = new M.ProjectPeriod { AcademicSemester = semester, Code = "FINAL", Name = "Final submission",
            PeriodType = "FINAL_SUBMISSION", Status = "ACTIVE", StartAt = Now.AddHours(-1), EndAt = Now.AddHours(1) };
        db.ProjectPeriods.Add(period);
        db.ProjectPeriods.Add(new() { AcademicSemester = semester, Code = "EXEC", Name = "Execution",
            PeriodType = "EXECUTION", Status = "ACTIVE", StartAt = Now.AddDays(-1), EndAt = Now.AddDays(1) });
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return new(users, project.Id, project.TeamId, member.Id, semester.Id, period.Id);
    }

    public async Task<long> Version(FinalDraftScenario scenario, string status = "SUBMITTED", long? deliverableId = null, bool file = true)
    {
        await using var db = CreateContext();
        var deliverable = deliverableId.HasValue ? await db.Deliverables.SingleAsync(d => d.Id == deliverableId)
            : new M.Deliverable { ProjectId = scenario.ProjectId, Title = "Report", Status = "OPEN", CreatedBy = scenario.Users.Student };
        var number = deliverableId.HasValue ? await db.DeliverableVersions.Where(v => v.DeliverableId == deliverableId).MaxAsync(v => v.VersionNumber) + 1 : 1;
        var version = new M.DeliverableVersion { Deliverable = deliverable, VersionNumber = number, Status = status,
            SubmittedBy = scenario.Users.Student, SubmittedAt = Now, CreatedAt = Now, UpdatedAt = Now };
        if (file) version.Files.Add(new() { UploadedBy = scenario.Users.Student, OriginalFileName = "report.txt",
            StoragePath = Guid.NewGuid().ToString("N"), FileSizeBytes = 4, MimeType = "text/plain", ChecksumSha256 = new string('a', 64) });
        db.DeliverableVersions.Add(version);
        await db.SaveChangesAsync();
        return version.Id;
    }
}

public sealed record FinalDraftScenario(SupervisorScenario Users, long ProjectId, long TeamId, long MemberId, long SemesterId, long PeriodId);
