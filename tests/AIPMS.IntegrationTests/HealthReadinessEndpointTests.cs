using System.Net;
using System.Text.Json;
using AIPMS.Api.Health;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AIPMS.IntegrationTests;

public sealed class HealthReadinessEndpointTests
{
    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class HealthTestFactory(
        string connectionString,
        string storageRootPath,
        string? gotenbergUrl,
        bool requireGotenberg,
        HttpStatusCode gotenbergStatus) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["FileStorage:Provider"] = "Local",
                    ["FileStorage:RootPath"] = storageRootPath,
                    ["OfficePreview:GotenbergUrl"] = gotenbergUrl,
                    ["HealthChecks:RequireGotenberg"] = requireGotenberg ? "true" : "false"
                }));
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient(GotenbergReadinessHealthCheck.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(_ => new HttpResponseMessage(gotenbergStatus)));
            });
        }
    }

    [Fact]
    public async Task GetHealthReady_WhenSqlStorageGotenbergHealthy_Returns200()
    {
        var source = Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION");
        await using var sql = new IsolatedSqlDatabase();
        await sql.StartAsync(source, bootstrap: false);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"aipms-health-ok-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            using var app = new HealthTestFactory(sql.ConnectionString, tempRoot, "http://gotenberg.local:3000", false, HttpStatusCode.OK);
            using var client = app.CreateClient();
            var res = await client.GetAsync("/health/ready");
            var body = await res.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("Healthy", doc.RootElement.GetProperty("status").GetString());
            Assert.Equal("Healthy", doc.RootElement.GetProperty("checks").GetProperty("sql").GetProperty("status").GetString());
            Assert.Equal("Healthy", doc.RootElement.GetProperty("checks").GetProperty("storage").GetProperty("status").GetString());
            Assert.Equal("Healthy", doc.RootElement.GetProperty("checks").GetProperty("gotenberg").GetProperty("status").GetString());
            Assert.DoesNotContain("Server=", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(tempRoot, body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetHealthReady_WhenGotenbergDown_Returns200Degraded_Or503WhenRequired()
    {
        var source = Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION");
        await using var sql = new IsolatedSqlDatabase();
        await sql.StartAsync(source, bootstrap: false);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"aipms-health-deg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            using (var app = new HealthTestFactory(sql.ConnectionString, tempRoot, "http://gotenberg.local:3000", false, HttpStatusCode.ServiceUnavailable))
            using (var client = app.CreateClient())
            {
                var res = await client.GetAsync("/health/ready");
                var body = await res.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                using var doc = JsonDocument.Parse(body);
                Assert.Equal("Degraded", doc.RootElement.GetProperty("status").GetString());
                Assert.Equal("Degraded", doc.RootElement.GetProperty("checks").GetProperty("gotenberg").GetProperty("status").GetString());
            }

            using (var appReq = new HealthTestFactory(sql.ConnectionString, tempRoot, "http://gotenberg.local:3000", true, HttpStatusCode.ServiceUnavailable))
            using (var clientReq = appReq.CreateClient())
            {
                var res = await clientReq.GetAsync("/health/ready");
                Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                Assert.Equal("Unhealthy", doc.RootElement.GetProperty("status").GetString());
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetHealthReady_WhenStorageUnwritable_OrSqlUnreachable_Returns503WithoutSecrets()
    {
        var source = Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION");
        await using var sql = new IsolatedSqlDatabase();
        await sql.StartAsync(source, bootstrap: false);

        // Create a file (not directory) so Directory.CreateDirectory / probe fails cleanly
        var blockerFile = Path.Combine(Path.GetTempPath(), $"aipms-health-blocker-{Guid.NewGuid():N}.tmp");
        await System.IO.File.WriteAllTextAsync(blockerFile, "block");
        try
        {
            using var appBadStorage = new HealthTestFactory(sql.ConnectionString, blockerFile, null, false, HttpStatusCode.OK);
            using var client1 = appBadStorage.CreateClient();
            var res1 = await client1.GetAsync("/health/ready");
            var body1 = await res1.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, res1.StatusCode);
            Assert.Contains("\"status\":\"Unhealthy\"", body1);
            Assert.DoesNotContain(blockerFile, body1, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Server=", body1, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Exception", body1, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (System.IO.File.Exists(blockerFile)) System.IO.File.Delete(blockerFile);
        }

        // Unreachable SQL -> 503 without leaking connection string
        var tempRoot = Path.Combine(Path.GetTempPath(), $"aipms-health-nosql-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var badSql = "Server=127.0.0.1,14999;Database=NO_SUCH_DB;User Id=fake;Password=SuperSecretPassword123!;Connect Timeout=1;TrustServerCertificate=True;";
            using var appBadSql = new HealthTestFactory(badSql, tempRoot, null, false, HttpStatusCode.OK);
            using var client2 = appBadSql.CreateClient();
            var res2 = await client2.GetAsync("/health/ready");
            var body2 = await res2.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, res2.StatusCode);
            Assert.Contains("\"status\":\"Unhealthy\"", body2);
            Assert.DoesNotContain("SuperSecretPassword123!", body2);
            Assert.DoesNotContain("NO_SUCH_DB", body2);
            Assert.DoesNotContain("SqlException", body2);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }
}
