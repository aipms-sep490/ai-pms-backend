using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Email;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.DTOs;
using AIPMS.Application.Features.Auth.Models;
using AIPMS.Infrastructure.Email;
using AIPMS.Infrastructure.Identity;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Services.Auditing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests;

public sealed class PasswordRecoveryTests(PasswordRecoveryTests.Factory factory) : IClassFixture<PasswordRecoveryTests.Factory>
{
    private const string Root = "/api/v1/auth/";
    private const string Old = "RecoveryOld@12345";
    private const string New = "RecoveryNew@12345";

    [Fact]
    public async Task Recovery_lifecycle_revokes_sessions_and_consumes_link_once()
    {
        var account = await factory.Seed();
        using var client = factory.CreateClient();
        var login = await Login(client, account.Email, Old);
        var accepted = await client.PostAsJsonAsync(Root + "forgot-password", new { account.Email });
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await factory.Process();
        var token = factory.Mail.Tokens[account.Email];
        await factory.WithDb(async db =>
        {
            var queue = await db.PasswordRecoveryRequests.SingleAsync();
            Assert.Equal("SENT", queue.Status); Assert.Null(queue.ProtectedPayload);
            var reset = await db.PasswordResetTokens.SingleAsync(x => x.Id == queue.ResetTokenId);
            Assert.Equal(new OpaqueTokenService().Hash(token), reset.TokenHash);
        });
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Root + "reset-password", new { token, newPassword = New })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Root + "me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "refresh", new { refreshToken = login.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "login", new { account.Email, password = Old })).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        await Login(client, account.Email, New);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "reset-password", new { token, newPassword = Old })).StatusCode);
        await factory.WithDb(async db => Assert.True(await db.AuditLogs.AnyAsync(x => x.ActorUserId == account.Id && x.Action == "AUTH_PASSWORD_RESET_COMPLETED")));
    }

    [Theory]
    [InlineData("missing")][InlineData("inactive")][InlineData("active")]
    public async Task Forgot_has_same_accepted_contract_without_synchronous_SMTP(string scenario)
    {
        var account = await factory.Seed();
        if (scenario == "inactive") await factory.WithDb(db => db.Users.Where(x => x.Id == account.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "SUSPENDED")));
        var email = scenario == "missing" ? "absent@example.test" : account.Email;
        using var client = factory.CreateClient();
        factory.Mail.Fail = true;
        try
        {
            var response = await client.PostAsJsonAsync(Root + "forgot-password", new { email });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("Request accepted. If an eligible account exists, check your email for password reset instructions.", (await response.Content.ReadFromJsonAsync<MessageResponse>())!.Message);
            Assert.Empty(factory.Mail.Tokens);
            await factory.Process();
            await factory.WithDb(async db => Assert.Equal(scenario == "active" ? "RETRY" : "SKIPPED", (await db.PasswordRecoveryRequests.SingleAsync()).Status));
        }
        finally { factory.Mail.Fail = false; }
    }

    [Fact]
    public async Task Disabled_or_unready_returns_503_for_any_email_without_queueing()
    {
        var account = await factory.Seed();
        var options = factory.Services.GetRequiredService<IOptions<PasswordRecoverySettings>>().Value;
        using var client = factory.CreateClient();
        foreach (var disabled in new[] { true, false })
        {
            options.Enabled = !disabled; options.IsReady = disabled;
            try
            {
                foreach (var email in new[] { account.Email, "absent@example.test" })
                    Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(Root + "forgot-password", new { email })).StatusCode);
            }
            finally { options.Enabled = true; options.IsReady = true; }
        }
        await factory.WithDb(async db => Assert.Empty(await db.PasswordRecoveryRequests.ToArrayAsync()));
    }

    [Theory]
    [InlineData("enqueue")][InlineData("reset")][InlineData("change")]
    public async Task Audit_failure_rolls_back_credentials_tokens_sessions_and_queue(string operation)
    {
        var account = await factory.Seed(); using var client = factory.CreateClient();
        var login = await Login(client, account.Email, Old);
        string? token = null;
        if (operation == "reset") { await client.PostAsJsonAsync(Root + "forgot-password", new { account.Email }); await factory.Process(); token = factory.Mail.Tokens[account.Email]; }
        factory.FailAudit = true;
        try
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
            var response = operation switch
            {
                "enqueue" => await client.PostAsJsonAsync(Root + "forgot-password", new { account.Email }),
                "reset" => await client.PostAsJsonAsync(Root + "reset-password", new { token, newPassword = New }),
                _ => await client.PostAsJsonAsync(Root + "change-password", new { currentPassword = Old, newPassword = New })
            };
            Assert.Equal(operation == "enqueue" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally { factory.FailAudit = false; }
        await factory.WithDb(async db =>
        {
            var user = await db.Users.SingleAsync(x => x.Id == account.Id);
            Assert.Equal(account.PasswordHash, user.PasswordHash); Assert.Null(user.PasswordChangedAt);
            Assert.All(await db.RefreshTokens.Where(x => x.UserId == account.Id).ToArrayAsync(), x => Assert.Null(x.RevokedAt));
            Assert.All(await db.PasswordResetTokens.Where(x => x.UserId == account.Id).ToArrayAsync(), x => Assert.Null(x.UsedAt));
            if (operation == "enqueue") Assert.Empty(await db.PasswordRecoveryRequests.ToArrayAsync());
        });
    }

    [Fact]
    public async Task Concurrent_reset_consumes_one_token_and_writes_one_audit()
    {
        var account = await factory.Seed(); using var client = factory.CreateClient();
        await client.PostAsJsonAsync(Root + "forgot-password", new { account.Email }); await factory.Process();
        var token = factory.Mail.Tokens[account.Email];
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => client.PostAsJsonAsync(Root + "reset-password", new { token, newPassword = New + i })));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, r => r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Conflict);
        await factory.WithDb(async db => Assert.Single(await db.AuditLogs.Where(x => x.ActorUserId == account.Id && x.Action == "AUTH_PASSWORD_RESET_COMPLETED").ToArrayAsync()));
    }

    [Fact]
    public async Task Change_revokes_sessions_and_pending_and_delivered_recovery()
    {
        var account = await factory.Seed(); using var client = factory.CreateClient();
        var login = await Login(client, account.Email, Old);
        await client.PostAsJsonAsync(Root + "forgot-password", new { account.Email }); await factory.Process();
        var oldToken = factory.Mail.Tokens[account.Email];
        await client.PostAsJsonAsync(Root + "forgot-password", new { account.Email });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Root + "change-password", new { currentPassword = Old, newPassword = New })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Root + "me")).StatusCode);
        await factory.Process();
        Assert.Equal(oldToken, factory.Mail.Tokens[account.Email]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "reset-password", new { token = oldToken, newPassword = Old })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "refresh", new { refreshToken = login.RefreshToken })).StatusCode);
        await factory.WithDb(async db => Assert.All(await db.PasswordRecoveryRequests.ToArrayAsync(), x => Assert.Equal("SUPERSEDED", x.Status)));
    }

    [Fact]
    public async Task Retry_keeps_same_encrypted_token_and_new_request_invalidates_old_link()
    {
        var account = await factory.Seed(); using var client = factory.CreateClient();
        await client.PostAsJsonAsync(Root + "forgot-password", new { account.Email });
        factory.Mail.Fail = true;
        try { await factory.Process(); } finally { factory.Mail.Fail = false; }
        var firstToken = factory.Mail.Tokens[account.Email];
        await factory.WithDb(async db =>
        {
            var row = await db.PasswordRecoveryRequests.SingleAsync();
            Assert.Equal("RETRY", row.Status); Assert.DoesNotContain(firstToken, row.ProtectedPayload!); Assert.DoesNotContain(account.Email, row.ProtectedPayload!);
            row.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        });
        await factory.Process(); Assert.Equal(firstToken, factory.Mail.Tokens[account.Email]);
        await client.PostAsJsonAsync(Root + "forgot-password", new { account.Email });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root + "reset-password", new { token = firstToken, newPassword = New })).StatusCode);
        await factory.Process(); Assert.NotEqual(firstToken, factory.Mail.Tokens[account.Email]);
    }

    [Fact]
    public async Task Expired_or_disabled_account_never_receives_a_new_token()
    {
        var account = await factory.Seed(); using var client = factory.CreateClient();
        await client.PostAsJsonAsync(Root + "forgot-password", new { account.Email });
        await factory.WithDb(async db =>
        {
            var row = await db.PasswordRecoveryRequests.SingleAsync(); row.CreatedAt = DateTime.UtcNow.AddHours(-2); row.ExpiresAt = DateTime.UtcNow.AddHours(-1); await db.SaveChangesAsync();
        });
        await factory.Process(); Assert.Empty(factory.Mail.Tokens);
        await client.PostAsJsonAsync(Root + "forgot-password", new { account.Email });
        await factory.WithDb(db => db.Users.Where(x => x.Id == account.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "SUSPENDED")));
        await factory.Process(); Assert.Empty(factory.Mail.Tokens);
    }

    [Fact]
    public async Task Two_workers_claim_once_and_expired_lease_can_be_recovered()
    {
        var account = await factory.Seed(); using var client = factory.CreateClient();
        await client.PostAsJsonAsync(Root + "forgot-password", new { account.Email });
        await Task.WhenAll(factory.Process(), factory.Process());
        Assert.Equal(1, factory.Mail.SendCount);
        await client.PostAsJsonAsync(Root + "forgot-password", new { account.Email });
        await factory.WithDb(db => db.PasswordRecoveryRequests.Where(x => x.Status == "PENDING").ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "SENDING").SetProperty(x => x.LeaseToken, Guid.NewGuid()).SetProperty(x => x.LeaseUntil, DateTime.UtcNow.AddSeconds(-1))));
        await factory.Process(); Assert.Equal(2, factory.Mail.SendCount);
    }

    [Fact]
    public async Task Concurrent_changes_reject_stale_hash_and_stale_login_refresh_cannot_create_sessions()
    {
        var account = await factory.Seed(); using var client = factory.CreateClient();
        await Login(client, account.Email, Old);
        async Task<Exception?> Change(string password)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IAuthRepository>();
            return await Record.ExceptionAsync(() => repo.UpdatePasswordAsync(account.Id, password, DateTime.UtcNow, expectedPasswordHash: account.PasswordHash));
        }
        var outcomes = await Task.WhenAll(Change("writer-one"), Change("writer-two"));
        Assert.Single(outcomes, x => x is null); Assert.Single(outcomes, x => x is UnauthorizedException);
        await using var scope = factory.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAuthRepository>();
        var refresh = new RefreshTokenData(new OpaqueTokenService().Generate().Hash, Guid.NewGuid(), DateTime.UtcNow.AddDays(1), null, null);
        await Assert.ThrowsAsync<UnauthorizedException>(() => repo.CompleteSuccessfulLoginAsync(account.Id, DateTime.UtcNow, refresh, expectedPasswordHash: account.PasswordHash));
        await factory.WithDb(async db =>
        {
            var oldSession = await db.RefreshTokens.SingleAsync(x => x.UserId == account.Id);
            await Assert.ThrowsAsync<UnauthorizedException>(() => repo.RotateRefreshTokenAsync(oldSession.Id, refresh, DateTime.UtcNow, expectedPasswordHash: account.PasswordHash));
            Assert.NotNull(oldSession.RevokedAt);
        });
    }

    [Fact]
    public async Task Migration_reruns_and_Swagger_preserves_FE_routes_and_errors()
    {
        var account = await factory.Seed(); using var client = factory.CreateClient();
        await client.PostAsJsonAsync(Root + "forgot-password", new { account.Email });
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(Path.Combine(directory.FullName, "db/schema.sql"))) directory = directory.Parent;
        var sql = await System.IO.File.ReadAllTextAsync(Path.Combine(directory!.FullName, "db/changes/20261001_add_password_recovery_queue.sql"));
        await factory.WithDb(async db => { await db.Database.ExecuteSqlRawAsync(sql); await db.Database.ExecuteSqlRawAsync(sql); Assert.Single(await db.PasswordRecoveryRequests.ToArrayAsync()); });
        var swagger = await client.GetStringAsync("/swagger/v1/swagger.json");
        using var document = System.Text.Json.JsonDocument.Parse(swagger);
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty(Root + "forgot-password").GetProperty("post").GetProperty("responses").TryGetProperty("503", out _));
        Assert.True(paths.TryGetProperty(Root + "reset-password", out _)); Assert.True(paths.TryGetProperty(Root + "change-password", out _));
    }

    private static async Task<LoginResponse> Login(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync(Root + "login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    [Fact]
    public async Task A_late_worker_cannot_overwrite_new_lease_or_regenerate_used_token()
    {
        var account = await factory.Seed(); using var client = factory.CreateClient();
        await client.PostAsJsonAsync(Root + "forgot-password", new { account.Email });
        var replacementLease = Guid.NewGuid();
        factory.Mail.OnSend = async () => await factory.WithDb(db => db.PasswordRecoveryRequests.Where(x => x.Status == "SENDING")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseToken, replacementLease).SetProperty(x => x.LeaseUntil, DateTime.UtcNow.AddMinutes(2))));
        try { await factory.Process(); } finally { factory.Mail.OnSend = null; }
        var token = factory.Mail.Tokens[account.Email];
        await factory.WithDb(async db =>
        {
            var row = await db.PasswordRecoveryRequests.SingleAsync();
            Assert.Equal("SENDING", row.Status); Assert.Equal(replacementLease, row.LeaseToken);
            await db.PasswordResetTokens.Where(x => x.Id == row.ResetTokenId).ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, DateTime.UtcNow));
            row.LeaseUntil = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        });
        await factory.Process(); Assert.Equal(1, factory.Mail.SendCount);
        await factory.WithDb(async db =>
        {
            Assert.Single(await db.PasswordResetTokens.Where(x => x.UserId == account.Id).ToArrayAsync());
            var row = await db.PasswordRecoveryRequests.SingleAsync();
            Assert.Equal("SUPERSEDED", row.Status); Assert.Null(row.ProtectedPayload);
        });
    }

    [Fact]
    public async Task Restart_uses_persistent_keyring_and_permanent_failure_erases_payload()
    {
        var account = await factory.Seed(); using var client = factory.CreateClient();
        await client.PostAsJsonAsync(Root + "forgot-password", new { account.Email });
        factory.Mail.Fail = true;
        try
        {
            await factory.Process();
            var firstToken = factory.Mail.Tokens[account.Email];
            for (var attempt = 2; attempt <= 5; attempt++)
            {
                await factory.WithDb(db => db.PasswordRecoveryRequests.Where(x => x.Status == "RETRY")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt, DateTime.UtcNow.AddSeconds(-1))));
                await using var scope = factory.Services.CreateAsyncScope();
                var services = scope.ServiceProvider;
                var settings = services.GetRequiredService<IOptions<PasswordRecoverySettings>>();
                var newProvider = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(new DirectoryInfo(settings.Value.KeyRingPath),
                    b => Microsoft.AspNetCore.DataProtection.DataProtectionBuilderExtensions.SetApplicationName(b, "AI-PMS.PasswordRecovery"));
                var restarted = new PasswordRecoveryQueue(services.GetRequiredService<AipmsDbContext>(), newProvider, settings,
                    services.GetRequiredService<IOpaqueTokenService>(), factory.Mail, services.GetRequiredService<IAuditTrail>(),
                    TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<PasswordRecoveryQueue>.Instance);
                await restarted.ProcessOneAsync(default);
                Assert.Equal(firstToken, factory.Mail.Tokens[account.Email]);
            }
        }
        finally { factory.Mail.Fail = false; }
        await factory.WithDb(async db =>
        {
            var row = await db.PasswordRecoveryRequests.SingleAsync();
            Assert.Equal("FAILED", row.Status); Assert.Equal(5, row.AttemptCount); Assert.Null(row.ProtectedPayload); Assert.NotNull(row.CompletedAt);
        });
    }

    [Fact]
    public async Task Request_enqueued_before_password_change_but_committed_after_it_is_skipped()
    {
        var account = await factory.Seed(); using var client = factory.CreateClient();
        var requestTime = DateTime.UtcNow.AddSeconds(-1);
        var login = await Login(client, account.Email, Old);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Root + "change-password", new { currentPassword = Old, newPassword = New })).StatusCode);
        await client.PostAsJsonAsync(Root + "forgot-password", new { account.Email });
        await factory.WithDb(db => db.PasswordRecoveryRequests.ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, requestTime)));
        await factory.Process(); Assert.Empty(factory.Mail.Tokens);
        await factory.WithDb(async db => Assert.Equal("SUPERSEDED", (await db.PasswordRecoveryRequests.SingleAsync()).Status));
    }

    public sealed class Factory : AipmsWebApplicationFactory, IAsyncLifetime
    {
        private readonly IsolatedSqlDatabase database = new();
        private readonly string keyPath = Path.Combine(Path.GetTempPath(), "aipms-recovery-keys-" + Guid.NewGuid().ToString("N"));
        public FakeNotifier Mail { get; } = new();
        public bool FailAudit { get; set; }
        public async Task InitializeAsync() => await database.StartAsync(Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION"));
        async Task IAsyncLifetime.DisposeAsync() { await DisposeAsync(); await database.DisposeAsync(); if (Directory.Exists(keyPath)) Directory.Delete(keyPath, true); }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder); builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                ["PasswordRecovery:Enabled"] = "true", ["PasswordRecovery:LookupKey"] = "integration-test-only-key-not-used-outside-tests",
                ["PasswordRecovery:KeyRingPath"] = keyPath, ["Email:PasswordResetUrl"] = "https://test.example/reset-password",
                ["Email:Host"] = "smtp.test.example", ["Email:Port"] = "587", ["Email:EnableSsl"] = "true",
                ["Email:SenderAddress"] = "test@example.test", ["Email:Username"] = "test", ["Email:Password"] = "test"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IAccessTokenAccountValidator>(); services.AddScoped<IAccessTokenAccountValidator, AccessTokenAccountValidator>();
                services.RemoveAll<IPasswordResetNotifier>(); services.AddSingleton<IPasswordResetNotifier>(Mail);
                services.RemoveAll<IAuditTrail>(); services.AddScoped<DatabaseAuditTrail>();
                services.AddScoped<IAuditTrail>(sp => new AuditProxy(this, sp.GetRequiredService<DatabaseAuditTrail>()));
                services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
                services.Configure<RateLimiterOptions>(o => o.AddPolicy("authentication", _ => RateLimitPartition.GetNoLimiter("test")));
            });
        }
        public async Task WithDb(Func<AipmsDbContext, Task> action)
        { await using var scope = Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<AipmsDbContext>()); }
        public async Task Process()
        { await using var scope = Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<IPasswordRecoveryQueue>().ProcessOneAsync(default); }
        public async Task<User> Seed()
        {
            Mail.Tokens.Clear(); Mail.SendCount = 0;
            User user = null!;
            await WithDb(async db =>
            {
                await db.PasswordRecoveryRequests.ExecuteDeleteAsync();
                using var scope = Services.CreateScope();
                user = new User { Email = Guid.NewGuid() + "@example.test", FullName = "Recovery Test", PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHashingService>().Hash(Old), Status = "ACTIVE", AcademicProfileStatus = "PENDING", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
                db.Users.Add(user); await db.SaveChangesAsync();
            });
            return user;
        }
    }
    public sealed class FakeNotifier : IPasswordResetNotifier
    {
        public ConcurrentDictionary<string, string> Tokens { get; } = new();
        public bool Fail { get; set; }
        public Func<Task>? OnSend { get; set; }
        public int SendCount;
        public async Task SendAsync(string recipientEmail, string recipientName, string rawResetToken, DateTime expiresAtUtc, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref SendCount); Tokens[recipientEmail] = rawResetToken;
            if (Fail) throw new ServiceUnavailableException("Fake provider unavailable");
            if (OnSend is not null) await OnSend();
        }
    }
    private sealed class AuditProxy(Factory factory, IAuditTrail inner) : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => factory.FailAudit
            ? throw new InvalidOperationException("Test audit unavailable") : inner.RecordAsync(entry, cancellationToken);
    }
}
