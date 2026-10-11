using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class EvaluationAssignmentEvidenceEndpointTests
{
    [Theory]
    [InlineData("COMPONENT")]
    [InlineData("PACKAGE")]
    [InlineData("INACTIVE_DEPARTMENT")]
    [InlineData("MISSING_STORAGE_REFERENCE")]
    public async Task Locked_evidence_revalidates_authority_and_never_falls_back_to_live_storage(string change)
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, _) = await SeedAssignmentAsync(factory);
        await using (var db = database.CreateContext())
        {
            var row = (await db.Set<EvaluationAssignment>().FindAsync(assignment.Id))!;
            if (change == "COMPONENT")
            {
                var schemeId = await db.Set<EvaluationSchemeComponent>().Where(c => c.Id == row.ComponentId).Select(c => c.SchemeId).SingleAsync();
                row.ComponentId = await db.Set<EvaluationSchemeComponent>().Where(c => c.SchemeId == schemeId && c.Scope == "INDIVIDUAL")
                    .Select(c => c.Id).SingleAsync();
            }
            if (change == "PACKAGE") row.ScopeSnapshotJson = "{\"FinalSubmissionId\":9223372036854775807}";
            if (change == "INACTIVE_DEPARTMENT") (await db.Departments.FindAsync(s.Scope.Users.DepartmentId))!.IsActive = false;
            if (change == "MISSING_STORAGE_REFERENCE")
            {
                var final = await db.Set<FinalSubmission>().Include(f => f.Items).SingleAsync(f => f.ProjectId == s.ProjectId);
                var item = Assert.Single(final.Items);
                var file = Assert.Single(JsonSerializer.Deserialize<FinalSnapshotFile[]>(item.FilesJson)!);
                item.FilesJson = JsonSerializer.Serialize(new[] { file with { StorageKey = "" } });
            }
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence/files/1");
        Assert.Equal(change == "MISSING_STORAGE_REFERENCE" ? HttpStatusCode.Conflict : HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("MAJOR_SPECIFIC")]
    [InlineData("INDIVIDUAL")]
    public async Task Locked_targets_filter_other_major_and_student_without_using_live_profiles(string scope)
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, major) = await SeedAssignmentAsync(factory, scope: scope);
        await using (var db = database.CreateContext())
        {
            var final = await db.Set<FinalSubmission>().Include(f => f.Items).SingleAsync(f => f.ProjectId == s.ProjectId);
            var item = Assert.Single(final.Items);
            var original = Assert.Single(JsonSerializer.Deserialize<FinalSnapshotFile[]>(item.FilesJson)!);
            var own = original with { EvidenceScope = scope, TargetMajorId = major,
                TargetStudentId = scope == "INDIVIDUAL" ? s.Scope.Users.Student : null };
            var foreign = own with { Metadata = own.Metadata with { Id = 9999 },
                TargetMajorId = scope == "MAJOR_SPECIFIC" ? major + 9999 : major,
                TargetStudentId = scope == "INDIVIDUAL" ? s.Scope.Users.Student + 9999 : null };
            item.FilesJson = JsonSerializer.Serialize(new[] { own, foreign });
            (await db.Users.FindAsync(s.Scope.Users.Student))!.MajorId = null;
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var url = $"/api/v1/evaluation-assignments/{assignment.Id}/evidence";
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var evidence = (await response.Content.ReadFromJsonAsync<EvaluationAssignmentEvidenceDto>())!;
        Assert.Equal(1L, Assert.Single(evidence.Items).FileId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url + "/files/9999")).StatusCode);
    }
}
