using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.IntegrationTests.Evaluations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Security;

public sealed class ExecutionConcurrencyTokenTests(EvaluationDraftDatabaseFixture database)
    : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private sealed class ConcurrencyFactory(EvaluationDraftDatabaseFixture db, bool requireTokens) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = db.ConnectionString,
                ["ExecutionConcurrency:RequireTokens"] = requireTokens ? "true" : "false",
                ["ExecutionConcurrency:LogMissingTokens"] = "true"
            }));
        }
    }

    private static async Task<string?> ReadProblemCode(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) return c.GetString();
        if (doc.RootElement.TryGetProperty("errorCode", out var ec) && ec.ValueKind == JsonValueKind.String) return ec.GetString();
        return null;
    }

    [Fact]
    public async Task ExecutionMutation_MissingToken_AllowsLegacyWhenRequireTokensFalse_AndReturns409StaleWhenRequireTokensTrue()
    {
        var s = await database.Seed(false);
        long reportId;
        string validToken;

        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";

            var report = new M.ProgressReport
            {
                ProjectId = s.ProjectId,
                ReportType = "WEEKLY",
                PeriodStart = DateOnly.FromDateTime(EvaluationDraftDatabaseFixture.Now.AddDays(-7)),
                PeriodEnd = DateOnly.FromDateTime(EvaluationDraftDatabaseFixture.Now),
                Summary = "Initial draft",
                Status = "DRAFT",
                SubmittedBy = s.Scope.Users.Student,
                CreatedAt = EvaluationDraftDatabaseFixture.Now,
                UpdatedAt = EvaluationDraftDatabaseFixture.Now,
                ConcurrencyToken = Guid.NewGuid()
            };
            db.ProgressReports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;
            validToken = report.ConcurrencyToken.ToString("N");
        }

        // 1. Positive (Mobile-safe Wave 0 default RequireTokens=false): missing token still succeeds (logs warning)
        using (var legacyApp = new ConcurrencyFactory(database, requireTokens: false))
        using (var student = legacyApp.CreateAuthenticatedClient(s.Scope.Users.Student, roles: [AppRoles.Student]))
        {
            var res = await student.PutAsJsonAsync($"/api/v1/progress-reports/{reportId}",
                new UpdateProgressReportRequest("Updated without token", null, null, null, ConcurrencyToken: null));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            validToken = doc.RootElement.GetProperty("concurrencyToken").GetString()!;
        }

        // 2. Negative (RequireTokens=true): missing token -> 409 STALE_CONCURRENCY_TOKEN
        using (var strictApp = new ConcurrencyFactory(database, requireTokens: true))
        using (var student = strictApp.CreateAuthenticatedClient(s.Scope.Users.Student, roles: [AppRoles.Student]))
        {
            var missingRes = await student.PutAsJsonAsync($"/api/v1/progress-reports/{reportId}",
                new UpdateProgressReportRequest("Should reject missing token", null, null, null, ConcurrencyToken: null));
            Assert.Equal(HttpStatusCode.Conflict, missingRes.StatusCode);
            Assert.Equal(WorkflowErrorCodes.StaleConcurrencyToken, await ReadProblemCode(missingRes));

            // 3. Negative: wrong/stale token -> 409 STALE_CONCURRENCY_TOKEN
            var staleRes = await student.PutAsJsonAsync($"/api/v1/progress-reports/{reportId}",
                new UpdateProgressReportRequest("Should reject stale token", null, null, null, ConcurrencyToken: Guid.NewGuid().ToString("N")));
            Assert.Equal(HttpStatusCode.Conflict, staleRes.StatusCode);
            Assert.Equal(WorkflowErrorCodes.StaleConcurrencyToken, await ReadProblemCode(staleRes));

            // 4. Positive: fresh token succeeds -> 200 OK
            var okRes = await student.PutAsJsonAsync($"/api/v1/progress-reports/{reportId}",
                new UpdateProgressReportRequest("Updated with valid token", null, null, null, ConcurrencyToken: validToken));
            Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);
        }

        // 5. Negative: UpdateUserAccountCommand with stale concurrencyToken -> 409 STALE_CONCURRENCY_TOKEN
        using (var adminApp = new ConcurrencyFactory(database, requireTokens: false))
        using (var admin = adminApp.CreateAuthenticatedClient(s.Scope.Users.Admin, roles: [AppRoles.Admin]))
        {
            var staleUserRes = await admin.PutAsJsonAsync($"/api/v1/users/{s.Scope.Users.Student}",
                new UpdateUserAccountRequest("Renamed Student", null, null, null, null, ConcurrencyToken: "stale-token-value"));
            Assert.Equal(HttpStatusCode.Conflict, staleUserRes.StatusCode);
            Assert.Equal(WorkflowErrorCodes.StaleConcurrencyToken, await ReadProblemCode(staleUserRes));
        }
    }
}
