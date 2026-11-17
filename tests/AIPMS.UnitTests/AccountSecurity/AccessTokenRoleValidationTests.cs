using AIPMS.Application.Common.Security;
using AIPMS.Infrastructure.Identity;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.UnitTests.AccountSecurity;

public sealed class AccessTokenRoleValidationTests
{
    [Fact]
    public async Task Revoked_Role_Invalidates_Existing_Access_Token()
    {
        await using var db = NewDatabase();
        var version = new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);
        db.Users.Add(new User { Id = 1, Email = "admin@example.test", FullName = "Admin",
            PasswordHash = "hash", Status = "ACTIVE", PasswordChangedAt = version });
        db.Roles.Add(new Role { Id = 2, Code = AppRoles.Admin, Name = "Administrator" });
        db.UserRoles.Add(new UserRole { UserId = 1, RoleId = 2, AssignedAt = version });
        await db.SaveChangesAsync();

        var validator = new AccessTokenAccountValidator(db);
        Assert.True(await validator.IsValidAsync(1, version, [AppRoles.Admin]));

        db.UserRoles.Remove(Assert.Single(db.UserRoles));
        await db.SaveChangesAsync();
        Assert.False(await validator.IsValidAsync(1, version, [AppRoles.Admin]));
        Assert.True(await validator.IsValidAsync(1, version, []));
    }

    [Fact]
    public async Task Account_And_Password_Checks_Still_Apply()
    {
        await using var db = NewDatabase();
        var version = new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);
        db.Users.Add(new User { Id = 1, Email = "student@example.test", FullName = "Student",
            PasswordHash = "hash", Status = "ACTIVE", PasswordChangedAt = version });
        await db.SaveChangesAsync();

        var validator = new AccessTokenAccountValidator(db);
        Assert.False(await validator.IsValidAsync(1, version.AddDays(-1), []));
        Assert.False(await validator.IsValidAsync(1, version, [AppRoles.Admin]));
        db.Users.Single().Status = "INACTIVE";
        await db.SaveChangesAsync();
        Assert.False(await validator.IsValidAsync(1, version, []));
    }

    private static AipmsDbContext NewDatabase() =>
        new(new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
