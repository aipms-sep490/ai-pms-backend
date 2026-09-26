using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Services.Auditing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests;

public sealed class GoogleAuthEndpointTests(GoogleAuthEndpointTests.Factory factory) : IClassFixture<GoogleAuthEndpointTests.Factory>
{
    private const string Password = "GoogleTest@123";
    private const string Origin = "http://localhost:5173";
    private const string Root = "/api/v1/auth/";

    [Fact]
    public async Task Link_login_refresh_logout_and_unlink_preserve_existing_contract()
    {
        var id = await factory.SeedAsync();
        using var client = Client(id);
        await Link(client, id);
        var list = await client.GetStringAsync(Root + "external-logins");
        Assert.Contains("GOOGLE", list); Assert.DoesNotContain("subject", list, StringComparison.OrdinalIgnoreCase);
        client.DefaultRequestHeaders.Authorization = null;
        var challenge = await Challenge(client, "LOGIN");
        var response = await client.PostAsJsonAsync(Root + "google/login", Input(challenge, id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal(id, session.User.Id); Assert.Contains("STUDENT", session.User.Roles);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "google/login", Input(challenge, id))).StatusCode);
        var refreshed = await client.PostAsJsonAsync(Root + "refresh", new { refreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var replacement = (await refreshed.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Root + "logout", new { refreshToken = replacement.RefreshToken })).StatusCode);
        using var owner = Client(id);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync(Root + "google/unlink", new { currentPassword = Password })).StatusCode);
        var again = await Challenge(client, "LOGIN");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "google/login", Input(again, id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Root + "login", new { email = Email(id), password = Password })).StatusCode);
    }

    [Theory]
    [InlineData("nonce")][InlineData("browser")][InlineData("expired")][InlineData("purpose")]
    [InlineData("unlinked")][InlineData("email")][InlineData("locked")][InlineData("inactive")]
    public async Task Login_denies_invalid_challenge_or_account(string scenario)
    {
        var id = await factory.SeedAsync();
        using var client = Client(id);
        if (scenario != "unlinked") await Link(client, id);
        var challenge = await Challenge(client, scenario == "purpose" ? "LINK" : "LOGIN");
        if (scenario is "expired" or "locked" or "inactive")
            await factory.WithDb(async db =>
            {
                if (scenario == "expired") await db.ExternalLoginChallenges.Where(x => x.Id == challenge.ChallengeId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
                if (scenario == "locked") await db.Users.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LockoutEndAt, DateTime.UtcNow.AddMinutes(30)));
                if (scenario == "inactive") await db.Users.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "SUSPENDED"));
            });
        using var other = Client();
        var target = scenario == "browser" ? other : client;
        var input = new { challengeId = challenge.ChallengeId,
            idToken = Token(id, scenario == "nonce" ? "wrong" : challenge.Nonce, scenario == "email" ? "different@gmail.com" : null) };
        var response = await target.PostAsJsonAsync(Root + "google/login", input);
        Assert.Equal(scenario == "inactive" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Link_requires_owner_password_and_matching_email_and_preserves_failed_attempts()
    {
        var id = await factory.SeedAsync();
        using var client = Client(id);
        var c = await Challenge(client, "LINK");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "google/link", new
            { challengeId = c.ChallengeId, idToken = Token(id, c.Nonce), currentPassword = "wrong" })).StatusCode);
        await factory.WithDb(async db =>
        {
            Assert.Equal(1, (await db.Users.SingleAsync(x => x.Id == id)).AccessFailedCount);
            Assert.Empty(await db.UserExternalLogins.Where(x => x.UserId == id).ToArrayAsync());
            Assert.NotNull((await db.ExternalLoginChallenges.SingleAsync(x => x.Id == c.ChallengeId)).ConsumedAt);
        });
        c = await Challenge(client, "LINK");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Root + "google/link", new
            { challengeId = c.ChallengeId, idToken = Token(id, c.Nonce, "other@gmail.com"), currentPassword = Password })).StatusCode);
        var otherId = await factory.SeedAsync();
        using var other = Client(otherId);
        other.DefaultRequestHeaders.Add("Cookie", client.DefaultRequestHeaders.Contains("Cookie") ? "" : "aipms-google-binding=missing");
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.PostAsJsonAsync(Root + "google/link", new
            { challengeId = c.ChallengeId, idToken = Token(otherId, c.Nonce), currentPassword = Password })).StatusCode);
    }

    [Fact]
    public async Task Concurrent_login_consumes_challenge_once()
    {
        var id = await factory.SeedAsync();
        using var client = Client(id); await Link(client, id);
        var c = await Challenge(client, "LOGIN");
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(Root + "google/login", Input(c, id))));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Unauthorized);
        await factory.WithDb(async db => Assert.Single(await db.RefreshTokens.Where(x => x.UserId == id).ToArrayAsync()));
    }

    [Fact]
    public async Task Concurrent_link_same_identity_is_idempotent_and_other_identity_conflicts()
    {
        var id = await factory.SeedAsync(); using var client = Client(id);
        var a = await Challenge(client, "LINK"); var b = await Challenge(client, "LINK");
        var responses = await Task.WhenAll(new[] { a, b }.Select(c => client.PostAsJsonAsync(Root + "google/link", new
            { challengeId = c.ChallengeId, idToken = Token(id, c.Nonce), currentPassword = Password })));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.NoContent, x.StatusCode));
        var c = await Challenge(client, "LINK");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Root + "google/link", new
            { challengeId = c.ChallengeId, idToken = Token(id, c.Nonce, subject: "different"), currentPassword = Password })).StatusCode);
    }

    [Theory]
    [InlineData("LINK")][InlineData("LOGIN")][InlineData("UNLINK")]
    public async Task Audit_failure_rolls_back_identity_session_and_challenge(string operation)
    {
        var id = await factory.SeedAsync(); using var client = Client(id);
        if (operation != "LINK") await Link(client, id);
        var c = operation == "UNLINK" ? null : await Challenge(client, operation);
        factory.FailAudit = true;
        try
        {
            var result = operation switch
            {
                "LINK" => await client.PostAsJsonAsync(Root + "google/link", new { challengeId = c!.ChallengeId, idToken = Token(id, c.Nonce), currentPassword = Password }),
                "LOGIN" => await client.PostAsJsonAsync(Root + "google/login", Input(c!, id)),
                _ => await client.PostAsJsonAsync(Root + "google/unlink", new { currentPassword = Password })
            };
            Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        }
        finally { factory.FailAudit = false; }
        await factory.WithDb(async db =>
        {
            Assert.Equal(operation != "LINK", await db.UserExternalLogins.AnyAsync(x => x.UserId == id));
            Assert.Empty(await db.RefreshTokens.Where(x => x.UserId == id).ToArrayAsync());
            if (c is not null) Assert.Null((await db.ExternalLoginChallenges.SingleAsync(x => x.Id == c.ChallengeId)).ConsumedAt);
        });
    }

    [Theory]
    [InlineData(null)][InlineData("https://attacker.example")]
    public async Task Rejects_missing_or_untrusted_origin(string? origin)
    {
        using var client = factory.CreateClient();
        if (origin is not null) client.DefaultRequestHeaders.Add("Origin", origin);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Root + "google/challenge", new { purpose = "LOGIN" })).StatusCode);
    }

    [Fact]
    public async Task Swagger_and_cookie_contracts_are_present()
    {
        using var client = Client();
        var swagger = await client.GetStringAsync("/swagger/v1/swagger.json");
        foreach (var suffix in new[] { "google/challenge", "google/login", "google/link", "google/unlink", "external-logins" }) Assert.Contains(Root + suffix, swagger);
        var response = await client.PostAsJsonAsync(Root + "google/challenge", new { purpose = "LOGIN" });
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie); Assert.Contains("samesite=strict", cookie);
        using var preflight = new HttpRequestMessage(HttpMethod.Options, Root + "google/login");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type");
        var cors = await client.SendAsync(preflight);
        Assert.Equal(Origin, Assert.Single(cors.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(cors.Headers.GetValues("Access-Control-Allow-Credentials")));
    }

    [Fact]
    public async Task Challenge_is_bound_to_owner_even_in_same_browser()
    {
        var a = await factory.SeedAsync(); var b = await factory.SeedAsync();
        using var client = Client(a); using var other = Client(b);
        var response = await client.PostAsJsonAsync(Root + "google/challenge", new { purpose = "LINK" });
        var c = (await response.Content.ReadFromJsonAsync<GoogleChallenge>())!;
        var cookie = response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        other.DefaultRequestHeaders.Add("Cookie", cookie);
        var denied = await other.PostAsJsonAsync(Root + "google/link", new { challengeId = c.ChallengeId, idToken = Token(b, c.Nonce), currentPassword = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Identity_cannot_be_linked_to_a_second_user_and_unlink_revokes_all_refresh_sessions()
    {
        var a = await factory.SeedAsync(); var b = await factory.SeedAsync();
        using var client = Client(a); using var other = Client(b);
        await Link(client, a);
        var c = await Challenge(other, "LINK");
        Assert.Equal(HttpStatusCode.Conflict, (await other.PostAsJsonAsync(Root + "google/link", new
        { challengeId = c.ChallengeId, idToken = Token(b, c.Nonce, subject: a.ToString()), currentPassword = Password })).StatusCode);
        var login = await client.PostAsJsonAsync(Root + "login", new { email = Email(a), password = Password });
        var session = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        await client.PostAsJsonAsync(Root + "google/unlink", new { currentPassword = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "refresh", new { refreshToken = session.RefreshToken })).StatusCode);
    }

    [Theory]
    [InlineData("BAD")][InlineData("")]
    public async Task Challenge_rejects_invalid_purpose(string purpose)
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root + "google/challenge", new { purpose })).StatusCode);
    }

    [Fact]
    public async Task Anonymous_cannot_link_and_disabled_feature_keeps_password_login()
    {
        var id = await factory.SeedAsync(); using var client = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "google/challenge", new { purpose = "LINK" })).StatusCode);
        var settings = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AIPMS.Infrastructure.Identity.Configuration.GoogleAuthSettings>>().Value;
        settings.Enabled = false;
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(Root + "google/challenge", new { purpose = "LOGIN" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Root + "login", new { email = Email(id), password = Password })).StatusCode);
        }
        finally { settings.Enabled = true; }
    }

    [Fact]
    public async Task Migration_can_run_twice_and_subject_uniqueness_is_case_sensitive()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(Path.Combine(directory.FullName, "db/schema.sql"))) directory = directory.Parent;
        var sql = await System.IO.File.ReadAllTextAsync(Path.Combine(directory!.FullName, "db/changes/20260926_add_google_external_logins.sql"));
        var a = await factory.SeedAsync(); var b = await factory.SeedAsync();
        await factory.WithDb(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(sql); await db.Database.ExecuteSqlRawAsync(sql);
            var subject = Guid.NewGuid().ToString("N");
            db.UserExternalLogins.AddRange(new UserExternalLogin { UserId = a, Subject = subject.ToLowerInvariant(), Email = Email(a), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new UserExternalLogin { UserId = b, Subject = subject.ToUpperInvariant(), Email = Email(b), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
    }

    private HttpClient Client(long? id = null)
    {
        var client = id is null ? factory.CreateClient() : factory.CreateAuthenticatedClient(id.Value);
        client.DefaultRequestHeaders.Add("Origin", Origin); return client;
    }
    private static async Task<GoogleChallenge> Challenge(HttpClient client, string purpose)
    {
        var response = await client.PostAsJsonAsync(Root + "google/challenge", new { purpose });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GoogleChallenge>())!;
    }
    private static async Task Link(HttpClient client, long id)
    {
        var c = await Challenge(client, "LINK");
        var response = await client.PostAsJsonAsync(Root + "google/link", new { challengeId = c.ChallengeId, idToken = Token(id, c.Nonce), currentPassword = Password });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
    private static object Input(GoogleChallenge c, long id) => new { challengeId = c.ChallengeId, idToken = Token(id, c.Nonce) };
    private static string Email(long id) => $"google-{id}@aipms.test";
    private static string Token(long id, string nonce, string? email = null, string? subject = null) => JsonSerializer.Serialize(new GoogleIdentity(subject ?? id.ToString(), email ?? Email(id), nonce));

    public sealed class Factory : AipmsWebApplicationFactory, IAsyncLifetime
    {
        private readonly IsolatedSqlDatabase database = new();
        public bool FailAudit { get; set; }
        public async Task InitializeAsync() => await database.StartAsync(Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION"));
        async Task IAsyncLifetime.DisposeAsync() { await DisposeAsync(); await database.DisposeAsync(); }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                ["GoogleAuth:Enabled"] = "true", ["GoogleAuth:ClientId"] = "test.apps.googleusercontent.com",
                ["GoogleAuth:AllowedOrigins:0"] = Origin, ["NotificationEmail:Enabled"] = "false", ["ScheduledNotifications:Enabled"] = "false"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGoogleIdentityVerifier>(); services.AddSingleton<IGoogleIdentityVerifier, FakeVerifier>();
                services.RemoveAll<IAuditTrail>();
                services.AddScoped<DatabaseAuditTrail>();
                services.AddScoped<IAuditTrail>(sp => new AuditProxy(this, sp.GetRequiredService<DatabaseAuditTrail>()));
                services.RemoveAll<Microsoft.Extensions.Options.IConfigureOptions<RateLimiterOptions>>();
                services.Configure<RateLimiterOptions>(o => o.AddPolicy("authentication", _ => RateLimitPartition.GetNoLimiter("test")));
            });
        }
        public async Task WithDb(Func<AipmsDbContext, Task> action)
        {
            await using var scope = Services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<AipmsDbContext>());
        }
        public async Task<long> SeedAsync()
        {
            long id = 0;
            await WithDb(async db =>
            {
                var role = await db.Roles.FirstOrDefaultAsync(x => x.Code == "STUDENT");
                if (role is null) { role = new Role { Code = "STUDENT", Name = "Student", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }; db.Roles.Add(role); await db.SaveChangesAsync(); }
                using var scope = Services.CreateScope();
                var user = new User { Email = Guid.NewGuid() + "@aipms.test", FullName = "Google Test", Status = "ACTIVE", AcademicProfileStatus = "PENDING",
                    PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHashingService>().Hash(Password), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
                db.Users.Add(user); await db.SaveChangesAsync(); id = user.Id;
                user.Email = Email(id);
                db.UserRoles.Add(new UserRole { UserId = id, RoleId = role.Id, AssignedAt = DateTime.UtcNow }); await db.SaveChangesAsync();
            });
            return id;
        }
    }
    private sealed class FakeVerifier : IGoogleIdentityVerifier
    { public Task<GoogleIdentity> VerifyAsync(string idToken, CancellationToken ct) => Task.FromResult(JsonSerializer.Deserialize<GoogleIdentity>(idToken)!); }
    private sealed class AuditProxy(Factory factory, IAuditTrail inner) : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => factory.FailAudit
            ? throw new InvalidOperationException("Test audit unavailable") : inner.RecordAsync(entry, cancellationToken);
    }
}
