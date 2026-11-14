using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.Semesters;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class PolicyEvaluationEndpointTests
{
    [Fact]
    public async Task Period_calendar_requires_current_token_and_holiday_loader_preserves_manual_dates()
    {
        var (scenario, _) = await Seed();
        var sql = await File.ReadAllTextAsync(Path.Combine(SqlSchemaBootstrap.Root, "db", "changes",
            "20261107_add_project_period_calendars.sql"));
        await using (var db = database.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync(sql);
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(scenario.Scope.Users.Staff);
        using var student = app.CreateAuthenticatedClient(scenario.Scope.Users.Student);
        var url = $"/api/v1/project-periods/{scenario.PeriodId}/calendar";
        var initial = await Body<PeriodCalendarDto>(await staff.GetAsync(url));
        Assert.Equal(62, initial.WorkingDaysOfWeek);
        var manual = new PeriodHoliday(new DateOnly(2026, 11, 20), "Faculty closure", false);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PutAsJsonAsync(url,
            new SavePeriodCalendarRequest(62, [manual], initial.ConcurrencyToken))).StatusCode);
        var saved = await Body<PeriodCalendarDto>(await staff.PutAsJsonAsync(url,
            new SavePeriodCalendarRequest(62, [manual], initial.ConcurrencyToken)));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync(url,
            new SavePeriodCalendarRequest(62, [manual], initial.ConcurrencyToken))).StatusCode);
        var loaded = await Body<PeriodCalendarDto>(await staff.PostAsJsonAsync(url + "/load-vn-holidays",
            new LoadVietnamHolidaysRequest(2026, saved.ConcurrencyToken)));
        Assert.Contains(loaded.Holidays, h => h.Date == manual.Date && h.Name == manual.Name);
        Assert.Contains(loaded.Holidays, h => h.Date == new DateOnly(2026, 4, 30));
        Assert.NotEqual(saved.ConcurrencyToken, loaded.ConcurrencyToken);
    }
}
