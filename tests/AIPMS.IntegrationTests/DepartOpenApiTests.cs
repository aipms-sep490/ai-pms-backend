using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AIPMS.IntegrationTests;

public sealed class DepartOpenApiTests
{
    [Fact]
    public async Task Depart_contract_documents_new_routes_tokens_and_authority()
    {
        using var app = new AipmsWebApplicationFactory();
        using var client = app.CreateClient();
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = doc.RootElement.GetProperty("paths");
        foreach (var path in new[] { "/api/v1/student-qualifications/{qualificationId}/certificate",
            "/api/v1/student-qualifications/{qualificationId}/certificate/download",
            "/api/v1/supervisor-assignments/{assignmentId}/replacement-candidates" })
        {
            var response = paths.GetProperty(path).GetProperty("get").GetProperty("responses");
            foreach (var status in new[] { "200", "400", "401", "403", "404", "409" }) Assert.True(response.TryGetProperty(status, out _));
        }
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        var upload = paths.GetProperty("/api/v1/student-qualifications/me/certificate").GetProperty("post");
        Assert.True(upload.GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out _));
        foreach (var status in new[] { "200", "400", "401", "403", "409", "413", "422" })
            Assert.True(upload.GetProperty("responses").TryGetProperty(status, out _));
        foreach (var type in new[] { "VerifyStudentQualificationRequest", "DecideStudentQualificationRequest" })
            Assert.Contains("compatibility", schemas.GetProperty(type).GetProperty("properties")
                .GetProperty("expectedConcurrencyToken").GetProperty("description").GetString());
        Assert.True(schemas.GetProperty("StudentQualificationDto").GetProperty("properties").TryGetProperty("concurrencyToken", out _));
        Assert.Contains("ADMIN_ONLY", paths.GetProperty("/api/v1/academic/project-periods").GetProperty("post").GetProperty("description").GetString());
    }

    [Theory]
    [InlineData("/api/v1/academic/semesters")]
    [InlineData("/api/v1/academic/project-periods")]
    public async Task Depart_D05_staff_cannot_mutate_structure(string route)
    {
        using var app = new AipmsWebApplicationFactory();
        using var staff = app.CreateAuthenticatedClient(10, roles: ["DEPARTMENT_STAFF"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(route, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync(route + "/1", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PatchAsJsonAsync(route + "/1/status", new { status = "CLOSED" })).StatusCode);
    }
}
