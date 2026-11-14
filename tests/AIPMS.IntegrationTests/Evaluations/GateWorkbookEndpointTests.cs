using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AIPMS.Application.Features.ReviewGates;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class GateWorkbookEndpointTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task Department_staff_cannot_read_another_departments_gate_template()
    {
        using var app = new EvaluationFactory(database);
        var scenario = await database.Seed(false);
        await using var db = database.CreateContext();
        var major = await db.ProjectMajors.Where(x => x.ProjectId == scenario.ProjectId)
            .Select(x => x.Major.Code).SingleAsync();
        using var staff = app.CreateAuthenticatedClient(scenario.Scope.Users.Staff);
        var created = await Body<ReviewGateTemplateDto>(await staff.PostAsJsonAsync("/api/v1/academic/gate-templates",
            new SaveReviewGateTemplateRequest(scenario.PeriodId, "PRIVATE_GATE", "Department only", "SINGLE_MAJOR",
                major, "1", null,
                [new ReviewGateDefinitionInput("R1", 1, "Gate 1", "Review", true, true, true,
                    null, null, "TWO_REVIEWERS", "COMMON",
                    [new ReviewGateRequirementInput("REPORT", "Báo cáo", "Nộp báo cáo", "FILE", "COMMON", null)], 100m)])));
        var other = new AIPMS.Infrastructure.Persistence.Generated.Models.Department
        {
            OrganizationId = scenario.Scope.OrganizationId, Code = $"OTHER_{Guid.NewGuid():N}",
            Name = "Other department", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Departments.Add(other);
        await db.SaveChangesAsync();
        var user = await db.Users.SingleAsync(x => x.Id == scenario.Scope.Users.Staff);
        user.DepartmentId = other.Id;
        await db.SaveChangesAsync();

        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.GetAsync($"/api/v1/academic/gate-templates/{created.Id}")).StatusCode);
        var listed = await Body<ReviewGateTemplateDto[]>(await staff.GetAsync("/api/v1/academic/gate-templates"));
        Assert.DoesNotContain(listed, x => x.Id == created.Id);
    }

    [Fact]
    public async Task Department_can_preview_commit_and_reexport_gate_workbook_while_student_cannot()
    {
        using var app = new EvaluationFactory(database);
        var scenario = await database.Seed(false);
        await using var db = database.CreateContext();
        var major = await db.ProjectMajors.Where(x => x.ProjectId == scenario.ProjectId)
            .Select(x => x.Major.Code).SingleAsync();
        using var staff = app.CreateAuthenticatedClient(scenario.Scope.Users.Staff);
        using var student = app.CreateAuthenticatedClient(scenario.Scope.Users.Student);
        var created = await Body<ReviewGateTemplateDto>(await staff.PostAsJsonAsync("/api/v1/academic/gate-templates",
            new SaveReviewGateTemplateRequest(scenario.PeriodId, "GATE_XLSX", "Mẫu tiến trình", "SINGLE_MAJOR",
                major, "1", null,
                [new ReviewGateDefinitionInput("R1", 1, "Gate 1", "Review evidence", true, true, true,
                    null, null, "TWO_REVIEWERS", "COMMON",
                    [new ReviewGateRequirementInput("REPORT", "Báo cáo", "Nộp báo cáo", "FILE", "COMMON", null)], 100m)])));
        var url = $"/api/v1/academic/gate-templates/{created.Id}/workbook";
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/v1/academic/gate-templates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/v1/academic/gate-templates/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(url)).StatusCode);
        var download = await staff.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        var bytes = await download.Content.ReadAsByteArrayAsync();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        workbook.Worksheet("Thông tin").Cell("B4").Value = "Mẫu Gate sau nhập Excel";
        workbook.Worksheet("Yêu cầu").Cell("D2").Value = "Hướng dẫn cập nhật";
        using var output = new MemoryStream(); workbook.SaveAs(output);
        async Task<HttpResponseMessage> Preview(byte[] content)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(content);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            form.Add(file, "file", "gate.xlsx");
            return await staff.PostAsync(url + "/preview", form);
        }
        var preview = await Body<GateWorkbookPreview>(await Preview(output.ToArray()));
        Assert.Empty(preview.Issues);
        Assert.Equal("Mẫu Gate sau nhập Excel", preview.Draft!.Name);
        var saved = await Body<ReviewGateTemplateDto>(await staff.PutAsJsonAsync($"/api/v1/academic/gate-templates/{created.Id}", preview.Draft));
        Assert.Equal("Mẫu Gate sau nhập Excel", saved.Name);
        Assert.Equal("Hướng dẫn cập nhật", Assert.Single(Assert.Single(saved.Gates).Requirements).Description);
        Assert.Equal(HttpStatusCode.Conflict, (await Preview(output.ToArray())).StatusCode);
        using var latest = new XLWorkbook(new MemoryStream(await (await staff.GetAsync(url)).Content.ReadAsByteArrayAsync()));
        Assert.Equal(saved.Name, latest.Worksheet("Thông tin").Cell("B4").GetString());
    }
}
