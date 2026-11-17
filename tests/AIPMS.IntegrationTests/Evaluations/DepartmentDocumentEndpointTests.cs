using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AIPMS.Application.Features.DepartmentDocuments;
using AIPMS.Application.Features.Files;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class DepartmentDocumentEndpointTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task Staff_publishes_versioned_template_and_student_reads_only_published_file()
    {
        using var app = new EvaluationFactory(database);
        var scenario = await database.Seed(false);
        await using var db = database.CreateContext();
        var majorId = await db.ProjectMajors.Where(x => x.ProjectId == scenario.ProjectId).Select(x => x.MajorId).SingleAsync();
        using var staff = app.CreateAuthenticatedClient(scenario.Scope.Users.Staff);
        using var otherDepartmentStaff = app.CreateAuthenticatedClient(scenario.Scope.Users.OutsideStaff);
        using var student = app.CreateAuthenticatedClient(scenario.Scope.Users.Student);
        var root = "/api/v1/academic/department-documents";

        async Task<HttpResponseMessage> Upload(string title)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(majorId.ToString()), "majorId");
            form.Add(new StringContent(scenario.PeriodId.ToString()), "projectPeriodId");
            form.Add(new StringContent("REPORT_TEMPLATE"), "code");
            form.Add(new StringContent(title), "title");
            var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4\nDepartment template"));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(file, "file", "report.pdf");
            return await staff.PostAsync(root, form);
        }

        var first = await Body<DepartmentDocumentDto>(await Upload("Report template v1"));
        Assert.Equal(1, first.Version);
        Assert.Equal("DRAFT", first.Status);
        var listUrl = $"{root}?majorId={majorId}&projectPeriodId={scenario.PeriodId}";
        Assert.Empty(await Body<DepartmentDocumentDto[]>(await student.GetAsync(listUrl)));
        Assert.Equal(HttpStatusCode.Forbidden, (await otherDepartmentStaff.GetAsync(listUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherDepartmentStaff.GetAsync($"{root}/{first.Id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"{root}/{first.Id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync($"{root}/{first.Id}/publish",
            new ChangeDepartmentDocumentStatusRequest(first.ConcurrencyToken))).StatusCode);

        var published = await Body<DepartmentDocumentDto>(await staff.PostAsJsonAsync($"{root}/{first.Id}/publish",
            new ChangeDepartmentDocumentStatusRequest(first.ConcurrencyToken)));
        Assert.Equal("PUBLISHED", published.Status);
        Assert.Single(await Body<DepartmentDocumentDto[]>(await student.GetAsync(listUrl)));
        var download = await student.GetAsync($"{root}/{first.Id}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(await download.Content.ReadAsByteArrayAsync()));

        var second = await Body<DepartmentDocumentDto>(await Upload("Report template v2"));
        Assert.Equal(2, second.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"{root}/{first.Id}/retire",
            new ChangeDepartmentDocumentStatusRequest(first.ConcurrencyToken))).StatusCode);
        var latest = await Body<DepartmentDocumentDto>(await staff.PostAsJsonAsync($"{root}/{second.Id}/publish",
            new ChangeDepartmentDocumentStatusRequest(second.ConcurrencyToken)));
        Assert.Equal("PUBLISHED", latest.Status);
        var visible = await Body<DepartmentDocumentDto[]>(await student.GetAsync(listUrl));
        Assert.Single(visible);
        Assert.Equal(second.Id, visible[0].Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"{root}/{first.Id}/download")).StatusCode);
    }

    [Fact]
    public async Task Office_preview_requires_authentication_and_returns_readonly_grid()
    {
        using var app = new EvaluationFactory(database);
        var scenario = await database.Seed(false);
        using var anonymous = app.CreateClient();
        using var student = app.CreateAuthenticatedClient(scenario.Scope.Users.Student);
        using var book = new XLWorkbook();
        book.Worksheets.Add("Gate").Cell("A1").Value = "Requirement";
        using var bytes = new MemoryStream();
        book.SaveAs(bytes);
        MultipartFormDataContent Form()
        {
            var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes.ToArray());
            file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            content.Add(file, "file", "gate.xlsx");
            return content;
        }
        using (var unauthenticatedForm = Form())
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/v1/files/office-preview", unauthenticatedForm)).StatusCode);
        using var authenticatedForm = Form();
        var preview = await Body<OfficePreviewDto>(await student.PostAsync("/api/v1/files/office-preview", authenticatedForm));
        Assert.Equal("XLSX_GRID", preview.Kind);
        Assert.Equal("Requirement", Assert.Single(preview.Sheets).Rows[0][0]);
    }
}
