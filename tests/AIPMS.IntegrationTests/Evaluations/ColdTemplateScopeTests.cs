using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Cold;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class ColdTemplateScopeTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static DateTime Now => EvaluationDraftDatabaseFixture.Now;

    [Fact]
    public async Task CreateTemplate_CommonScope_NullMajor_Returns201Created()
    {
        var s = await database.Seed(lockedSubmission: false);
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);

        var request = new CreateColdTemplateRequest(
            MajorId: null,
            ProjectPeriodId: s.PeriodId,
            Name: "Interdisciplinary Common Cold Report",
            Reports: [new("COMMON_DOC", "Joint Project Summary", Required: true)],
            Scope: "COMMON");

        var response = await staff.PostAsJsonAsync("/api/v1/cold-report-templates", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ColdTemplateDto>();
        Assert.NotNull(dto);
        Assert.Null(dto.MajorId);
        Assert.Equal("COMMON", dto.Scope);
        Assert.Equal("Interdisciplinary Common Cold Report", dto.Name);
        Assert.Equal(1, dto.Version);
        var report = Assert.Single(dto.Reports);
        Assert.Equal("COMMON_DOC", report.Code);
    }

    [Fact]
    public async Task CreateTemplate_CommonScope_WithMajorId_Returns422CommonHasMajor()
    {
        var s = await database.Seed(lockedSubmission: false);
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);

        await using var db = database.CreateContext();
        var major = await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync();

        var request = new CreateColdTemplateRequest(
            MajorId: major,
            ProjectPeriodId: s.PeriodId,
            Name: "Invalid Common Report With Major",
            Reports: [new("REPORT", "Report 1", Required: true)],
            Scope: "COMMON");

        var response = await staff.PostAsJsonAsync("/api/v1/cold-report-templates", request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.True(problem.Extensions.TryGetValue("code", out var code));
        Assert.Equal("COLD_TEMPLATE_COMMON_HAS_MAJOR", code?.ToString());
    }

    [Fact]
    public async Task CreateTemplate_MajorSpecificScope_NullMajor_Returns422MajorRequired()
    {
        var s = await database.Seed(lockedSubmission: false);
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);

        var request = new CreateColdTemplateRequest(
            MajorId: null,
            ProjectPeriodId: s.PeriodId,
            Name: "Invalid Major Specific Without Major",
            Reports: [new("REPORT", "Report 1", Required: true)],
            Scope: "MAJOR_SPECIFIC");

        var response = await staff.PostAsJsonAsync("/api/v1/cold-report-templates", request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.True(problem.Extensions.TryGetValue("code", out var code));
        Assert.Equal("COLD_TEMPLATE_MAJOR_REQUIRED", code?.ToString());
    }

    [Fact]
    public async Task GetTemplates_LegacyTemplateWithNullScope_ReturnsCorrectlyWithoutError()
    {
        var s = await database.Seed(lockedSubmission: false);
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);

        await using var db = database.CreateContext();
        var major = await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync();

        var legacyName = $"Legacy Template {Guid.NewGuid():N}";
        await SqlSchemaBootstrap.ExecuteAsync(database.ConnectionString, $$"""
            INSERT INTO dbo.cold_report_templates (major_id, project_period_id, name, version, reports_json, created_by, created_at, scope)
            VALUES ({{major}}, {{s.PeriodId}}, N'{{legacyName}}', 99, N'[{"Code":"LEGACY_DOC","Name":"Legacy Document","Required":true}]', {{s.Scope.Users.Staff}}, SYSUTCDATETIME(), NULL);
            """);

        var response = await staff.GetAsync($"/api/v1/cold-report-templates?majorId={major}&projectPeriodId={s.PeriodId}&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<ColdTemplateDto>>();
        Assert.NotNull(result);
        var legacy = Assert.Single(result.Items, t => t.Name == legacyName);
        Assert.Equal(major, legacy.MajorId);
        Assert.Null(legacy.Scope);
        Assert.Equal(99, legacy.Version);
        var report = Assert.Single(legacy.Reports);
        Assert.Equal("LEGACY_DOC", report.Code);
    }

    [Fact]
    public async Task Migration_ColdTemplateCommonScope_IsIdempotentWhenRunTwice()
    {
        var migrationSql = await File.ReadAllTextAsync(
            Path.Combine(SqlSchemaBootstrap.Root, "db", "changes", "20261030_cold_template_common_scope.sql"));

        // Execute 1st run
        await SqlSchemaBootstrap.ExecuteAsync(database.ConnectionString, migrationSql);

        // Execute 2nd run
        await SqlSchemaBootstrap.ExecuteAsync(database.ConnectionString, migrationSql);

        // Verify column and check constraints exist
        await using var db = database.CreateContext();
        var hasScopeCol = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS Value
            FROM sys.columns c
            JOIN sys.tables t ON c.object_id = t.object_id
            WHERE t.name = 'cold_report_templates' AND c.name = 'scope'
            """).SingleAsync();
        Assert.Equal(1, hasScopeCol);
    }
}
