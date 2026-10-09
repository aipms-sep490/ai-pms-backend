using System.Net;
using System.Net.Http.Json;
using System.Text;
using AIPMS.Application.Features.StudentRosters;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.IntegrationTests.Teams;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.StudentRosters;

public sealed class CurriculumImportTests(TeamDatabaseFixture database) : IClassFixture<TeamDatabaseFixture>
{
    internal static async Task<long> SeedAdmin(TeamDatabaseFixture database, TeamScenario scenario)
    {
        await using var db = database.CreateContext();
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Code == "ADMIN");
        role ??= new Role { Code = "ADMIN", Name = "Admin", IsSystemRole = true };
        var admin = new User { Email = $"admin-{Guid.NewGuid():N}@example.test", FullName = "Admin",
            PasswordHash = "unused", Status = "ACTIVE", UserRoleUsers = [new() { Role = role }] };
        db.Users.Add(admin);
        foreach (var id in scenario.Students)
            (await db.Users.SingleAsync(u => u.Id == id)).StudentCode = $"S{id}";
        await db.SaveChangesAsync();
        return admin.Id;
    }

    private static async Task<CurriculumPreviewDto> Preview(HttpClient client, string csv)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "data.csv");
        var response = await client.PostAsync("/api/v1/users/curriculum-import/preview", form);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CurriculumPreviewDto>())!;
    }

    private static CurriculumCommitRequest Request(CurriculumPreviewDto preview) => new(preview.Rows
        .Where(r => r.Status is "UPDATE" or "UNCHANGED")
        .Select(r => new CurriculumUpdate(r.UserId!.Value, r.StudentCode, r.CurriculumCode!, r.ExpectedConcurrencyToken!)).ToArray());

    [Fact]
    public async Task Preview_commit_reimport_and_profile_preserve_other_fields()
    {
        var s = await database.SeedAsync();
        var admin = await SeedAdmin(database, s);
        using var app = new TeamTestFactory(database, s);
        using var client = app.CreateAuthenticatedClient(admin, roles: ["ADMIN"]);
        var csv = $"MSSV,Khung\nS{s.Students[0]},BIT_SE_18D_Java\nS{s.Students[1]},";
        var preview = await Preview(client, csv);
        Assert.True(preview.CanCommit);
        Assert.Equal("SKIPPED", preview.Rows[1].Status);
        var response = await client.PostAsJsonAsync("/api/v1/users/curriculum-import/commit", Request(preview));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<CurriculumCommitDto>())!.Updated);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/users/curriculum-import/commit", Request(preview))).StatusCode);
        var repeated = await client.PostAsJsonAsync("/api/v1/users/curriculum-import/commit", Request(await Preview(client, csv)));
        Assert.Equal(1, (await repeated.Content.ReadFromJsonAsync<CurriculumCommitDto>())!.Unchanged);
        await using var db = database.CreateContext();
        var user = await db.Users.FindAsync(s.Students[0]);
        Assert.Equal("BIT_SE_18D_Java", user!.CurriculumCode);
        Assert.Equal("unused-test-hash", user.PasswordHash);
        Assert.Equal(s.SeMajorId, user.MajorId);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.ActorUserId == admin && a.Action == "STUDENT_CURRICULA_IMPORTED"));
        using var student = app.CreateAuthenticatedClient(s.Students[0]);
        var profile = await student.GetAsync("/api/v1/users/me/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Contains("BIT_SE_18D_Java", await profile.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Duplicate_unknown_and_invalid_rows_do_not_commit_and_roles_are_enforced()
    {
        var s = await database.SeedAsync();
        var admin = await SeedAdmin(database, s);
        using var app = new TeamTestFactory(database, s);
        using var client = app.CreateAuthenticatedClient(admin, roles: ["ADMIN"]);
        var preview = await Preview(client, $"MSSV,Khung\nS{s.Students[0]},SE\ns{s.Students[0]},IS\nUNKNOWN,SE");
        Assert.False(preview.CanCommit);
        Assert.Contains("DUPLICATE_STUDENT_CODE", preview.Rows[0].Errors);
        Assert.Contains("STUDENT_NOT_FOUND", preview.Rows[2].Errors);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/users/curriculum-import/commit", new { rows = new object[] { } })).StatusCode);
        using var student = app.CreateAuthenticatedClient(s.Students[0]);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync("/api/v1/users/curriculum-import/commit", new { rows = new object[] { } })).StatusCode);
        using var forged = app.CreateAuthenticatedClient(s.Students[0], roles: ["ADMIN"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await forged.PostAsJsonAsync("/api/v1/users/curriculum-import/commit", new { rows = new object[] { } })).StatusCode);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/users/curriculum-import/commit", new { rows = new object[] { } })).StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Audit_failure_or_stale_second_row_rolls_back_entire_batch(bool auditFailure)
    {
        var s = await database.SeedAsync();
        var admin = await SeedAdmin(database, s);
        using var app = new TeamTestFactory(database, s, failAuditAction: auditFailure ? "STUDENT_CURRICULA_IMPORTED" : null);
        using var client = app.CreateAuthenticatedClient(admin, roles: ["ADMIN"]);
        var preview = await Preview(client, $"MSSV,Khung\nS{s.Students[0]},SE\nS{s.Students[1]},IS");
        if (!auditFailure)
        {
            await using var edit = database.CreateContext();
            var user = await edit.Users.FindAsync(s.Students[1]);
            user!.Phone = "0123456789";
            await edit.SaveChangesAsync();
        }
        var response = await client.PostAsJsonAsync("/api/v1/users/curriculum-import/commit", Request(preview));
        Assert.Equal(auditFailure ? HttpStatusCode.InternalServerError : HttpStatusCode.Conflict, response.StatusCode);
        await using var db = database.CreateContext();
        Assert.All(await db.Users.Where(u => s.Students.Contains(u.Id)).ToArrayAsync(), u => Assert.Null(u.CurriculumCode));
        Assert.False(await db.AuditLogs.AnyAsync(a => a.ActorUserId == admin && a.Action == "STUDENT_CURRICULA_IMPORTED"));
    }

    [Fact]
    public async Task Concurrent_import_has_one_winner()
    {
        var s = await database.SeedAsync();
        var admin = await SeedAdmin(database, s);
        using var app = new TeamTestFactory(database, s);
        using var client = app.CreateAuthenticatedClient(admin, roles: ["ADMIN"]);
        var request = Request(await Preview(client, $"MSSV,Khung\nS{s.Students[0]},SE"));
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync("/api/v1/users/curriculum-import/commit", request)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
    }
}

