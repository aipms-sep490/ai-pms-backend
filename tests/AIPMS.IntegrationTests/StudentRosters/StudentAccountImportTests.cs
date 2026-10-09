using System.Net;
using System.Net.Http.Json;
using System.Text;
using AIPMS.Application.Features.StudentRosters;
using AIPMS.IntegrationTests.Teams;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.StudentRosters;

public sealed class StudentAccountImportTests(TeamDatabaseFixture database) : IClassFixture<TeamDatabaseFixture>
{
    private const string Root = "/api/v1/users/student-import/";
    private static StudentAccountImportRow Row() => new(2, Guid.NewGuid().ToString("N"), "Imported Student", $"import-{Guid.NewGuid():N}@gmail.com", "0123456789", "BIT_SE");
    private static async Task<StudentAccountImportPreview> Preview(HttpClient client, long majorId, params StudentAccountImportRow[] rows)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(majorId.ToString()), "majorId");
        var csv = "MSSV,Ho ten,Email,SDT,Khung\n" + string.Join('\n', rows.Select(r => $"{r.StudentCode},{r.FullName},{r.Email},{r.Phone},{r.CurriculumCode}"));
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "students.csv");
        var response = await client.PostAsync(Root + "preview", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StudentAccountImportPreview>())!;
    }

    [Fact]
    public async Task Preview_then_commit_creates_only_students_and_repeat_is_conflict()
    {
        var scenario = await database.SeedAsync(); var admin = await CurriculumImportTests.SeedAdmin(database, scenario);
        using var app = new TeamTestFactory(database, scenario); using var client = app.CreateAuthenticatedClient(admin, roles: ["ADMIN"]);
        var input = Row(); var preview = await Preview(client, scenario.SeMajorId, input);
        Assert.True(preview.CanCommit);
        await using var db = database.CreateContext();
        Assert.False(await db.Users.AnyAsync(x => x.Email == input.Email));
        var request = new StudentAccountImportCommit(scenario.SeMajorId, preview.Rows.Select(x => x.Account).ToArray());
        var response = await client.PostAsJsonAsync(Root + "commit", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<StudentAccountImportResult>())!.Created);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Root + "commit", request)).StatusCode);
        var user = await db.Users.Include(x => x.UserRoleUsers).ThenInclude(x => x.Role).SingleAsync(x => x.Email == input.Email);
        Assert.True(user.GoogleEnrollmentPending); Assert.Equal("ACTIVE", user.Status); Assert.Equal("PENDING", user.AcademicProfileStatus);
        Assert.Equal("STUDENT", Assert.Single(user.UserRoleUsers).Role.Code); Assert.Equal(scenario.SeMajorId, user.MajorId);
        Assert.Equal("0123456789", user.Phone); Assert.Equal("BIT_SE", user.CurriculumCode);
        Assert.False(string.IsNullOrWhiteSpace(user.PasswordHash));
        Assert.True(await db.AuditLogs.AnyAsync(x => x.ActorUserId == admin && x.Action == "STUDENT_ACCOUNTS_IMPORTED"));
        Assert.False((await Preview(client, scenario.SeMajorId, input)).CanCommit);
    }

    [Fact]
    public async Task Preview_rejects_duplicate_invalid_and_existing_identities()
    {
        var scenario = await database.SeedAsync(); var admin = await CurriculumImportTests.SeedAdmin(database, scenario);
        using var app = new TeamTestFactory(database, scenario); using var client = app.CreateAuthenticatedClient(admin, roles: ["ADMIN"]);
        var row = Row(); var preview = await Preview(client, scenario.SeMajorId, row, row with { StudentCode = row.StudentCode.ToLowerInvariant() }, Row() with { Email = "bad", StudentCode = $"S{scenario.Students[0]}" });
        Assert.False(preview.CanCommit); Assert.Contains("DUPLICATE_EMAIL", preview.Rows[0].Errors);
        Assert.Contains("DUPLICATE_STUDENT_CODE", preview.Rows[1].Errors);
        Assert.Contains("EMAIL_INVALID", preview.Rows[2].Errors); Assert.Contains("STUDENT_CODE_ALREADY_EXISTS", preview.Rows[2].Errors);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Audit_failure_or_new_duplicate_rolls_back_the_whole_batch(bool failAudit)
    {
        var scenario = await database.SeedAsync(); var admin = await CurriculumImportTests.SeedAdmin(database, scenario);
        using var app = new TeamTestFactory(database, scenario, failAuditAction: failAudit ? "STUDENT_ACCOUNTS_IMPORTED" : null);
        using var client = app.CreateAuthenticatedClient(admin, roles: ["ADMIN"]);
        var first = Row(); var second = Row();
        var preview = await Preview(client, scenario.SeMajorId, first, second);
        if (!failAudit)
        {
            await using var edit = database.CreateContext(); var user = await edit.Users.FindAsync(scenario.Students[0]);
            user!.Email = second.Email; await edit.SaveChangesAsync();
        }
        var response = await client.PostAsJsonAsync(Root + "commit", new StudentAccountImportCommit(scenario.SeMajorId, preview.Rows.Select(x => x.Account).ToArray()));
        Assert.Equal(failAudit ? HttpStatusCode.InternalServerError : HttpStatusCode.Conflict, response.StatusCode);
        await using var db = database.CreateContext(); Assert.False(await db.Users.AnyAsync(x => x.Email == first.Email));
        Assert.False(await db.AuditLogs.AnyAsync(x => x.ActorUserId == admin && x.Action == "STUDENT_ACCOUNTS_IMPORTED"));
    }

    [Fact]
    public async Task Concurrent_commits_create_one_batch()
    {
        var scenario = await database.SeedAsync(); var admin = await CurriculumImportTests.SeedAdmin(database, scenario);
        using var app = new TeamTestFactory(database, scenario); using var client = app.CreateAuthenticatedClient(admin, roles: ["ADMIN"]);
        var request = new StudentAccountImportCommit(scenario.SeMajorId, [Row()]);
        var responses = await Task.WhenAll(client.PostAsJsonAsync(Root + "commit", request), client.PostAsJsonAsync(Root + "commit", request));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Authorization_and_inactive_major_are_rechecked_on_commit()
    {
        var scenario = await database.SeedAsync(); var admin = await CurriculumImportTests.SeedAdmin(database, scenario);
        using var app = new TeamTestFactory(database, scenario); var request = new StudentAccountImportCommit(scenario.SeMajorId, [Row()]);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Root + "commit", request)).StatusCode);
        using var student = app.CreateAuthenticatedClient(scenario.Students[0]); Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync(Root + "commit", request)).StatusCode);
        using var forged = app.CreateAuthenticatedClient(scenario.Students[0], roles: ["ADMIN"]); Assert.Equal(HttpStatusCode.Forbidden, (await forged.PostAsJsonAsync(Root + "commit", request)).StatusCode);
        using var client = app.CreateAuthenticatedClient(admin, roles: ["ADMIN"]);
        await using var db = database.CreateContext();
        var major = await db.Majors.FindAsync(scenario.SeMajorId); major!.IsActive = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root + "commit", request)).StatusCode);
    }
}
