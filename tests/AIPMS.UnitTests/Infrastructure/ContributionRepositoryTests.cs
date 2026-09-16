using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class ContributionRepositoryTests
{
    [Fact]
    public async Task Rebuild_snapshot_is_idempotent_for_unchanged_evidence()
    {
        var options = new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase(nameof(Rebuild_snapshot_is_idempotent_for_unchanged_evidence)).Options;
        await using var db = new AipmsDbContext(options);
        var now = new DateTime(2026, 9, 1);
        var org = new Organization { Code = "ORG", Name = "Org", IsActive = true, CreatedAt = now, UpdatedAt = now };
        var dept = new Department { Code = "DEP", Name = "Dept", IsActive = true, Organization = org, CreatedAt = now, UpdatedAt = now };
        var student = new User { Email = "s@test", FullName = "Student", PasswordHash = "x", Status = "ACTIVE", Department = dept, CreatedAt = now, UpdatedAt = now };
        var team = new Team { Code = "TEAM", Name = "Team", Status = "LOCKED", CreatedBy = 1,
            AcademicSemester = new AcademicSemester { Code = "SEM", Name = "Semester", Status = "ACTIVE", Organization = org,
                StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) },
            CreatedAt = now, UpdatedAt = now,
            TeamMembers = [new TeamMember { User = student, UserId = 1, IsLeader = true, JoinedAt = now, CreatedAt = now, UpdatedAt = now }] };
        var project = new Project { Code = "P", Title = "Project", Status = "ACTIVE", CreatedBy = 1, Team = team,
            RegisteredAt = now, CreatedAt = now, UpdatedAt = now, RowVersion = new byte[8] };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var repository = new ContributionRepository(db);
        await repository.RebuildSnapshotAsync(project.Id, new DateTime(2026, 9, 1), default);
        await repository.RebuildSnapshotAsync(project.Id, new DateTime(2026, 9, 2), default);

        Assert.Single(await db.ContributionSnapshots.ToListAsync());
    }
}
