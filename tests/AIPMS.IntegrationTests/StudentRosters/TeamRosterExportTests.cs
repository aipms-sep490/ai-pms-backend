using System.Net;
using System.Text.Json;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.IntegrationTests.Teams;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.StudentRosters;

public sealed class TeamRosterExportTests(TeamDatabaseFixture database) : IClassFixture<TeamDatabaseFixture>
{
    private async Task<long[]> SeedTeams(TeamScenario s)
    {
        await using var db = database.CreateContext();
        var teams = new[] { "SE_02", "SE_01" }.Select(code => new Team {
            AcademicSemesterId = s.SemesterId, Code = code, Name = code, Status = "FORMING", CreatedBy = s.Students[0]
        }).ToArray();
        db.Teams.AddRange(teams);
        await db.SaveChangesAsync();
        string[] names = ["Nguyễn Văn An", "Trần Minh Bình", "Lê Hoàng Chi", "Phạm Gia Dũng", "Võ Thanh Hà", "Đặng Minh Khang"];
        for (var i = 0; i < 6; i++)
        {
            var user = await db.Users.FindAsync(s.Students[i]);
            user!.FullName = names[i];
            user.StudentCode = $"00{s.Students[i]:D4}";
            user.Phone = $"090000000{i}";
            user.Email = $"sample-student-{i}-{s.SemesterId}@example.test";
            user.CurriculumCode = i % 2 == 0 ? "BIT_SE_18D_Java" : "BIT_SE_18D_.NET";
            db.TeamMembers.Add(new TeamMember { TeamId = teams[i < 3 ? 0 : 1].Id,
                AcademicSemesterId = s.SemesterId, UserId = user.Id, IsLeader = i is 1 or 4, JoinedAt = TeamDatabaseFixture.Now.AddDays(-1),
                LeftAt = i == 5 ? TeamDatabaseFixture.Now : null });
        }
        await db.SaveChangesAsync();
        return teams.Select(t => t.Id).ToArray();
    }

    [Fact]
    public async Task Export_filters_current_members_orders_leader_first_and_audits()
    {
        var s = await database.SeedAsync();
        var admin = await CurriculumImportTests.SeedAdmin(database, s);
        var teams = await SeedTeams(s);
        using var app = new TeamTestFactory(database, s);
        using var client = app.CreateAuthenticatedClient(admin, roles: ["ADMIN"]);
        var response = await client.GetAsync($"/api/v1/teams/export?semesterId={s.SemesterId}&format=xlsx");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Contains($"team-roster-{s.SemesterId}.xlsx", response.Content.Headers.ContentDisposition!.ToString());
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var book = new XLWorkbook(new MemoryStream(bytes));
        var sheet = book.Worksheet(1);
        Assert.Equal(6, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal("SE_01", sheet.Cell("B2").GetString());
        Assert.Equal($"00{s.Students[4]:D4}", sheet.Cell("C2").GetString());
        Assert.Equal($"00{s.Students[3]:D4}", sheet.Cell("C3").GetString());
        Assert.Equal($"00{s.Students[1]:D4}", sheet.Cell("C4").GetString());
        Assert.Equal("0900000004", sheet.Cell("F2").GetString());
        Assert.All(sheet.CellsUsed(), c => Assert.False(c.HasFormula));
        await using var db = database.CreateContext();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.ActorUserId == admin && a.Action == "TEAM_ROSTER_EXPORTED"));
        var filtered = await client.GetAsync($"/api/v1/teams/export?semesterId={s.SemesterId}&majorId={s.IsMajorId}&teamId={teams[1]}");
        using var filteredBook = new XLWorkbook(new MemoryStream(await filtered.Content.ReadAsByteArrayAsync()));
        Assert.Equal(2, filteredBook.Worksheet(1).LastRowUsed()!.RowNumber());
        Assert.Equal($"00{s.Students[4]:D4}", filteredBook.Worksheet(1).Cell("C2").GetString());
        var samplePath = Environment.GetEnvironmentVariable("AIPMS_ROSTER_SAMPLE_PATH");
        if (!string.IsNullOrEmpty(samplePath)) await System.IO.File.WriteAllBytesAsync(samplePath, bytes);
    }

    [Fact]
    public async Task Export_denies_wrong_role_revoked_admin_and_foreign_filters()
    {
        var s = await database.SeedAsync();
        var other = await database.SeedAsync();
        var admin = await CurriculumImportTests.SeedAdmin(database, s);
        var teams = await SeedTeams(other);
        using var app = new TeamTestFactory(database, s);
        using var client = app.CreateAuthenticatedClient(admin, roles: ["ADMIN"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/teams/export")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/teams/export?semesterId={s.SemesterId}&format=csv")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/teams/export?semesterId={s.SemesterId}&teamId={teams[0]}")).StatusCode);
        await using var db = database.CreateContext();
        var department = (await db.Majors.FindAsync(s.SeMajorId))!.DepartmentId;
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/teams/export?semesterId={s.SemesterId}&departmentId={department}&majorId={other.SeMajorId}")).StatusCode);
        using var student = app.CreateAuthenticatedClient(s.Students[0]);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/v1/teams/export?semesterId={s.SemesterId}")).StatusCode);
        using var spoofed = app.CreateAuthenticatedClient(s.Students[0], roles: ["ADMIN"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await spoofed.GetAsync($"/api/v1/teams/export?semesterId={s.SemesterId}")).StatusCode);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/teams/export?semesterId={s.SemesterId}")).StatusCode);
        var user = await db.Users.FindAsync(admin);
        user!.Status = "INACTIVE";
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/teams/export?semesterId={s.SemesterId}")).StatusCode);
    }

    [Fact]
    public async Task Export_rejects_10001_rows_but_allows_exactly_10000()
    {
        var s = await database.SeedAsync();
        var admin = await CurriculumImportTests.SeedAdmin(database, s);
        await using var db = database.CreateContext();
        var team = new Team { AcademicSemesterId = s.SemesterId, Code = "LIMIT", Name = "Limit test", Status = "FORMING", CreatedBy = s.Students[0] };
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        var suffix = $"limit-{Guid.NewGuid():N}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @inserted TABLE (id BIGINT);
            INSERT INTO dbo.users(email,full_name,password_hash,status)
            OUTPUT inserted.id INTO @inserted
            SELECT TOP (10001) CONCAT({suffix},'-',ROW_NUMBER() OVER(ORDER BY a.object_id,b.object_id),'@example.test'),
                N'Limit fixture','unused','ACTIVE'
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            INSERT INTO dbo.team_members(team_id,academic_semester_id,user_id,is_leader)
            SELECT {team.Id},{s.SemesterId},id,0 FROM @inserted;
            """);
        using var app = new TeamTestFactory(database, s);
        using var client = app.CreateAuthenticatedClient(admin, roles: ["ADMIN"]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync($"/api/v1/teams/export?semesterId={s.SemesterId}")).StatusCode);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE TOP (1) dbo.team_members SET left_at=SYSUTCDATETIME() WHERE team_id={team.Id}");
        var response = await client.GetAsync($"/api/v1/teams/export?semesterId={s.SemesterId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var book = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        Assert.Equal(10001, book.Worksheet(1).LastRowUsed()!.RowNumber());
    }

    [Fact]
    public async Task Audit_failure_prevents_download_and_routes_appear_in_openapi()
    {
        var s = await database.SeedAsync();
        var admin = await CurriculumImportTests.SeedAdmin(database, s);
        using var app = new TeamTestFactory(database, s, failAuditAction: "TEAM_ROSTER_EXPORTED");
        using var client = app.CreateAuthenticatedClient(admin, roles: ["ADMIN"]);
        var response = await client.GetAsync($"/api/v1/teams/export?semesterId={s.SemesterId}");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var swagger = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        using var doc = JsonDocument.Parse(await swagger.Content.ReadAsStringAsync());
        foreach (var path in new[] { "/api/v1/teams/export", "/api/v1/users/curriculum-import/preview", "/api/v1/users/curriculum-import/commit" })
            Assert.True(doc.RootElement.GetProperty("paths").TryGetProperty(path, out _));
        var export = doc.RootElement.GetProperty("paths").GetProperty("/api/v1/teams/export").GetProperty("get");
        Assert.Equal("binary", export.GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet").GetProperty("schema").GetProperty("format").GetString());
        Assert.True(export.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "semesterId").GetProperty("required").GetBoolean());
    }
}

