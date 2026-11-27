using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.IntegrationTests.Evaluations;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Security;

public sealed class RbacReadOnlyMatrixTests(EvaluationDraftDatabaseFixture database)
    : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static async Task<string?> ReadProblemCode(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) return c.GetString();
        if (doc.RootElement.TryGetProperty("errorCode", out var ec) && ec.ValueKind == JsonValueKind.String) return ec.GetString();
        return null;
    }

    [Fact]
    public async Task Admin_WriteEndpoints_Return409ReadOnly_WhileReadMatrix_AndUserRoleAssignment_StillWork()
    {
        var s = await database.Seed(false);
        long studentRoleId, lecturerRoleId;
        await using (var db = database.CreateContext())
        {
            studentRoleId = await db.Roles.Where(r => r.Code == AppRoles.Student).Select(r => r.Id).SingleAsync();
            lecturerRoleId = await db.Roles.Where(r => r.Code == AppRoles.Lecturer).Select(r => r.Id).SingleAsync();
        }

        using var app = new EvaluationFactory(database);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin, roles: [AppRoles.Admin]);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        // 1. Negative: All 7 write endpoints on RolesController & PermissionsController return 409 RBAC_PERMISSION_MATRIX_READ_ONLY
        var writes = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Post, "/api/v1/security/roles", new { code = "CUSTOM_ROLE", name = "Custom", description = "d" }),
            (HttpMethod.Put, $"/api/v1/security/roles/{studentRoleId}", new { code = "STUDENT", name = "Student", description = "d" }),
            (HttpMethod.Delete, $"/api/v1/security/roles/{studentRoleId}", null),
            (HttpMethod.Put, $"/api/v1/security/roles/{studentRoleId}/permissions", new { permissionIds = Array.Empty<long>() }),
            (HttpMethod.Post, "/api/v1/security/permissions", new { code = "CUSTOM_PERM", name = "Custom", description = "d" }),
            (HttpMethod.Put, "/api/v1/security/permissions/1", new { code = "CUSTOM_PERM", name = "Custom", description = "d" }),
            (HttpMethod.Delete, "/api/v1/security/permissions/1", null)
        };

        foreach (var (method, url, body) in writes)
        {
            using var req = new HttpRequestMessage(method, url)
            {
                Content = body is null ? null : JsonContent.Create(body)
            };
            var res = await admin.SendAsync(req);
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
            Assert.Equal(WorkflowErrorCodes.RbacPermissionMatrixReadOnly, await ReadProblemCode(res));
        }

        // 2. Negative: Non-Admin still receives 403 Forbidden from AccountSecurityManagement policy
        var staffPost = await staff.PostAsJsonAsync("/api/v1/security/roles", new { code = "ROGUE", name = "Rogue" });
        Assert.Equal(HttpStatusCode.Forbidden, staffPost.StatusCode);
        var staffGet = await staff.GetAsync("/api/v1/security/permissions/matrix");
        Assert.Equal(HttpStatusCode.Forbidden, staffGet.StatusCode);

        // 3. Positive: Admin reads permission matrix & roles -> 200 OK
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/security/permissions/matrix")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/security/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/security/permissions")).StatusCode);

        // 4. Positive: Admin assigns & removes one of the 4 system roles to/from a user account (QD-10)
        var assignRes = await admin.PutAsync($"/api/v1/users/{s.Scope.Users.Student}/roles/{lecturerRoleId}", null);
        Assert.True(assignRes.IsSuccessStatusCode, await assignRes.Content.ReadAsStringAsync());

        var removeRes = await admin.DeleteAsync($"/api/v1/users/{s.Scope.Users.Student}/roles/{lecturerRoleId}");
        Assert.True(removeRes.IsSuccessStatusCode, await removeRes.Content.ReadAsStringAsync());
    }
}
