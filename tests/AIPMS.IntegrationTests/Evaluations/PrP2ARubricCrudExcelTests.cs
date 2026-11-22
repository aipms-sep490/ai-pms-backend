using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using ClosedXML.Excel;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class PrP2ARubricCrudExcelTests(EvaluationDraftDatabaseFixture database)
    : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, JsonOpts)!;
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
    public async Task Rubric_inline_clone_excel_roundtrip_and_immutability_guards_work()
    {
        var eval = await database.Seed(false);
        var s = eval.Scope;
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: ["DEPARTMENT_STAFF"]);

        var createReq = new CreateRubricRequest(
            s.Users.DepartmentId,
            s.SemesterId,
            "RUB_P2A_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            "Rubric P2A Draft",
            "Draft for clone and workbook round-trip",
            [
                new RubricCriterionInput("Integration Core", "Parent group", 100m, null, 0, false)
                {
                    Children =
                    [
                        new RubricCriterionInput("API Handoff", "Child leaf", 100m, 10m, 0, true)
                        {
                            TargetScope = "PROJECT",
                            IsIntegration = true,
                            LearningOutcome = "CLO-1",
                            EvidenceGuidance = "OpenAPI + integration report"
                        }
                    ]
                }
            ]);

        var created = await Body<RubricDto>(await staff.PostAsJsonAsync("/api/v1/rubrics", createReq));
        var parentCriterion = Assert.Single(created.Criteria);
        var originalLeafCode = Assert.Single(parentCriterion.Children).CriterionCode;

        // 1. Positive: Clone parent group creates new group + child with fresh unique CriterionCode
        var cloned = await Body<RubricDto>(await staff.PostAsJsonAsync(
            $"/api/v1/rubrics/{created.Id}/criteria/{parentCriterion.Id}/clone",
            new CloneRubricCriterionRequest(created.ConcurrencyToken)));
        Assert.Equal(2, cloned.Criteria.Count);
        Assert.Contains("(Bản sao)", cloned.Criteria[1].Name);
        var clonedLeafCode = Assert.Single(cloned.Criteria[1].Children).CriterionCode;
        Assert.NotEqual(originalLeafCode, clonedLeafCode);

        // 2. Negative: Stale concurrency token returns 409 STALE_CONCURRENCY_TOKEN
        var staleClone = await staff.PostAsJsonAsync(
            $"/api/v1/rubrics/{created.Id}/criteria/{cloned.Criteria[0].Id}/clone",
            new CloneRubricCriterionRequest(created.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Conflict, staleClone.StatusCode);
        Assert.Equal(WorkflowErrorCodes.StaleConcurrencyToken, await ReadProblemCode(staleClone));

        // 3. Export Excel workbook -> edit weights to 50/50 -> Preview -> Commit via PUT
        var exportResp = await staff.GetAsync($"/api/v1/rubrics/{created.Id}/workbook");
        Assert.Equal(HttpStatusCode.OK, exportResp.StatusCode);
        var xlsxBytes = await exportResp.Content.ReadAsByteArrayAsync();
        byte[] editedBytes;
        using (var wb = new XLWorkbook(new MemoryStream(xlsxBytes)))
        {
            var sheet = wb.Worksheet("Tiêu chí");
            // Row 2 = first root, Row 4 = second root (cloned)
            sheet.Cell("E2").Value = 50m;
            sheet.Cell("E4").Value = 50m;
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            editedBytes = ms.ToArray();
        }

        using (var form = new MultipartFormDataContent())
        {
            var fileContent = new ByteArrayContent(editedBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            form.Add(fileContent, "file", "rubric.xlsx");
            var preview = await Body<RubricWorkbookPreview>(await staff.PostAsync($"/api/v1/rubrics/{created.Id}/workbook/preview", form));
            Assert.Empty(preview.Issues);
            Assert.NotNull(preview.Draft);
            Assert.Equal(4, preview.StagedRows.Count);

            var updated = await Body<RubricDto>(await staff.PutAsJsonAsync($"/api/v1/rubrics/{created.Id}", preview.Draft));
            Assert.Equal(50m, updated.Criteria[0].WeightPercent);
            Assert.Equal(50m, updated.Criteria[1].WeightPercent);

            // Publish legacy rubric -> immutable
            var published = await Body<RubricDto>(await staff.PostAsJsonAsync(
                $"/api/v1/rubrics/{created.Id}/publish",
                new ChangeRubricStatusRequest(updated.ConcurrencyToken)));
            Assert.Equal("PUBLISHED", published.Status);

            // 4. Negative: Clone on PUBLISHED rubric returns 409 IMMUTABLE_VERSION
            var immutableClone = await staff.PostAsJsonAsync(
                $"/api/v1/rubrics/{created.Id}/criteria/{published.Criteria[0].Id}/clone",
                new CloneRubricCriterionRequest(published.ConcurrencyToken));
            Assert.Equal(HttpStatusCode.Conflict, immutableClone.StatusCode);
            Assert.Equal(WorkflowErrorCodes.ImmutableVersion, await ReadProblemCode(immutableClone));
        }
    }
}
