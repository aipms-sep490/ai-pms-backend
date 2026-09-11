using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Notifications.DTOs;
using AIPMS.Infrastructure.Identity;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Notifications;

public sealed class NotificationInboxEndpointTests(SupervisorDatabaseFixture database)
    : IClassFixture<SupervisorDatabaseFixture>
{
    private static readonly DateTime Now = new(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);
    private const string Url = "/api/v1/notifications";

    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<Scenario> Seed()
    {
        var accounts = await database.SeedAsync();
        await using var db = database.CreateContext();
        M.Notification Notification(string type, string title, DateTime created, params long[] recipients) => new()
        {
            CreatedBy = accounts.Admin, NotificationType = type, Title = title, Content = title + " content",
            RelatedEntityType = "PROJECT", RelatedEntityId = 123, CreatedAt = created, UpdatedAt = created,
            NotificationRecipients = recipients.Select(id => new M.NotificationRecipient
            {
                UserId = id, CreatedAt = created, UpdatedAt = created, DeliveredAt = created
            }).ToList()
        };
        var read = Notification("PROJECT_APPROVED", "Old read", Now.AddDays(-2), accounts.Student);
        read.NotificationRecipients.Single().IsRead = true;
        read.NotificationRecipients.Single().ReadAt = Now.AddDays(-1);
        read.NotificationRecipients.Single().UpdatedAt = Now.AddDays(-1);
        var shared = Notification("TEAM_INVITATION", "Shared", Now.AddHours(-1), accounts.Student, accounts.Lecturer);
        var latest = Notification("TEAM_INVITATION", "Latest", Now.AddHours(-1), accounts.Student);
        var foreign = Notification("PRIVATE", "Other account only", Now, accounts.Lecturer);
        // Explicit insertion order verifies pagination tie-breaking for equal timestamps.
        foreach (var notification in new[] { read, shared, latest, foreign })
        {
            db.Notifications.Add(notification);
            await db.SaveChangesAsync();
        }
        return new(accounts.Student, accounts.Lecturer, accounts.Admin, read.Id, shared.Id, latest.Id, foreign.Id);
    }

    [Fact]
    public async Task Inbox_filters_and_pages_only_recipient_rows_with_stable_order_and_utc_dates()
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var user = app.CreateAuthenticatedClient(s.User);
        var response = await user.GetAsync(Url + "?pageSize=1");
        Assert.True(response.Headers.CacheControl?.NoStore);
        var first = await Body<PagedResult<NotificationDto>>(response);
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        var newest = Assert.Single(first.Items);
        Assert.Equal(s.Latest, newest.Id);
        Assert.Equal(DateTimeKind.Utc, newest.CreatedAt.Kind);
        Assert.False(newest.IsRead);
        Assert.Null(newest.ReadAt);
        Assert.Equal("PROJECT", newest.RelatedEntityType);
        Assert.Equal(123, newest.RelatedEntityId);
        var second = await Body<PagedResult<NotificationDto>>(await user.GetAsync(Url + "?pageSize=1&page=2"));
        Assert.Equal(s.Shared, Assert.Single(second.Items).Id);
        var read = await Body<PagedResult<NotificationDto>>(await user.GetAsync(Url + "?isRead=true"));
        Assert.Equal(s.Read, Assert.Single(read.Items).Id);
        Assert.Equal(DateTimeKind.Utc, read.Items[0].ReadAt!.Value.Kind);
        var unread = await Body<PagedResult<NotificationDto>>(await user.GetAsync(Url + "?isRead=false&notificationType=%20TEAM_INVITATION%20"));
        Assert.Equal(2, unread.TotalCount);
        Assert.All(unread.Items, n => Assert.False(n.IsRead));
        var unknown = await Body<PagedResult<NotificationDto>>(await user.GetAsync(Url + "?notificationType=UNKNOWN"));
        Assert.Empty(unknown.Items);
        Assert.Equal(0, unknown.TotalCount);
        Assert.Empty((await Body<PagedResult<NotificationDto>>(await user.GetAsync(Url + "?page=9"))).Items);
        Assert.Equal(2, (await Body<UnreadNotificationCountDto>(await user.GetAsync(Url + "/unread-count"))).Count);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("userId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recipient", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mark_one_and_all_are_idempotent_and_shared_recipients_remain_independent()
    {
        var s = await Seed();
        var clock = new Clock();
        using var app = new Factory(database, clock);
        using var user = app.CreateAuthenticatedClient(s.User);
        Assert.Equal(HttpStatusCode.NoContent, (await user.PatchAsync($"{Url}/{s.Shared}/read", null)).StatusCode);
        clock.Now = Now.AddHours(2);
        Assert.Equal(HttpStatusCode.NoContent, (await user.PatchAsync($"{Url}/{s.Shared}/read", null)).StatusCode);
        Assert.Equal(1, (await Body<UnreadNotificationCountDto>(await user.GetAsync(Url + "/unread-count"))).Count);
        Assert.Equal(HttpStatusCode.NoContent, (await user.PostAsync(Url + "/read-all", null)).StatusCode);
        clock.Now = Now.AddHours(3);
        Assert.Equal(HttpStatusCode.NoContent, (await user.PostAsync(Url + "/read-all", null)).StatusCode);
        Assert.Equal(0, (await Body<UnreadNotificationCountDto>(await user.GetAsync(Url + "/unread-count"))).Count);
        await using var db = database.CreateContext();
        var rows = await db.NotificationRecipients.Where(r => r.UserId == s.User).ToListAsync();
        Assert.Equal(Now, rows.Single(r => r.NotificationId == s.Shared).ReadAt);
        Assert.Equal(Now, rows.Single(r => r.NotificationId == s.Shared).UpdatedAt);
        Assert.Equal(Now.AddHours(2), rows.Single(r => r.NotificationId == s.Latest).ReadAt);
        Assert.Equal(Now.AddDays(-1), rows.Single(r => r.NotificationId == s.Read).ReadAt);
        Assert.All(await db.NotificationRecipients.Where(r => r.UserId == s.Other).ToListAsync(), r =>
        {
            Assert.False(r.IsRead);
            Assert.Null(r.ReadAt);
        });
    }

    [Fact]
    public async Task Concurrent_mark_one_and_all_preserve_ownership_and_first_read_time()
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var user = app.CreateAuthenticatedClient(s.User);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => i % 2 == 0
            ? user.PatchAsync($"{Url}/{s.Shared}/read", null)
            : user.PostAsync(Url + "/read-all", null)));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        await using var db = database.CreateContext();
        Assert.All(await db.NotificationRecipients.Where(r => r.UserId == s.User && r.NotificationId != s.Read).ToListAsync(), r =>
        {
            Assert.True(r.IsRead);
            Assert.Equal(Now, r.ReadAt);
        });
        Assert.Equal(Now.AddDays(-1), (await db.NotificationRecipients.SingleAsync(r => r.UserId == s.User && r.NotificationId == s.Read)).ReadAt);
        Assert.Equal(2, await db.NotificationRecipients.CountAsync(r => r.UserId == s.Other && !r.IsRead));
    }

    [Fact]
    public async Task Foreign_and_missing_notifications_are_indistinguishable_even_for_admin_creator()
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var user = app.CreateAuthenticatedClient(s.User);
        using var admin = app.CreateAuthenticatedClient(s.Admin, roles: AppRoles.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await user.PatchAsync($"{Url}/{s.Foreign}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.PatchAsync($"{Url}/{long.MaxValue}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PatchAsync($"{Url}/{s.Shared}/read", null)).StatusCode);
        Assert.Empty((await Body<PagedResult<NotificationDto>>(await admin.GetAsync(Url + $"?userId={s.User}"))).Items);
        Assert.Equal(0, (await Body<UnreadNotificationCountDto>(await admin.GetAsync(Url + "/unread-count"))).Count);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync(Url + "/read-all", null)).StatusCode);
        Assert.Equal(2, (await Body<UnreadNotificationCountDto>(await user.GetAsync(Url + "/unread-count"))).Count);
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/unread-count")]
    [InlineData("PATCH", "/1/read")]
    [InlineData("POST", "/read-all")]
    public async Task All_endpoints_require_authentication_and_active_account(string method, string path)
    {
        var s = await Seed();
        await using (var db = database.CreateContext())
        {
            (await db.Users.FindAsync(s.User))!.Status = "INACTIVE";
            await db.SaveChangesAsync();
        }
        using var app = new Factory(database);
        using var anonymous = app.CreateClient();
        using var inactive = app.CreateAuthenticatedClient(s.User);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(new(new HttpMethod(method), Url + path))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await inactive.SendAsync(new(new HttpMethod(method), Url + path))).StatusCode);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=2147483647")]
    [InlineData("?pageSize=101")]
    [InlineData("?pageSize=0")]
    [InlineData("?isRead=invalid")]
    [InlineData("?notificationType=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Invalid_filters_return_problem_details(string query)
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var user = app.CreateAuthenticatedClient(s.User);
        var response = await user.GetAsync(Url + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Invalid_notification_id_is_rejected()
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var user = app.CreateAuthenticatedClient(s.User);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PatchAsync(Url + "/0/read", null)).StatusCode);
    }

    private sealed record Scenario(long User, long Other, long Admin, long Read, long Shared, long Latest, long Foreign);
    private sealed class Clock : TimeProvider
    {
        public DateTime Now { get; set; } = NotificationInboxEndpointTests.Now;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
    private sealed class Factory(SupervisorDatabaseFixture database, Clock? clock = null) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock ?? new Clock());
                services.RemoveAll<IAccessTokenAccountValidator>();
                services.AddScoped<IAccessTokenAccountValidator, AccessTokenAccountValidator>();
            });
        }
    }
}
