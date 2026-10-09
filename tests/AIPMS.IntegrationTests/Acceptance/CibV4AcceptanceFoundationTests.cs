using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.IntegrationTests.Evaluations;
using Microsoft.EntityFrameworkCore;
using File = System.IO.File;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Acceptance;

public sealed class CibV4AcceptanceFoundationTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var content = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(content);
        return content!;
    }

    private async Task<(EvaluationScenario Scenario, long Major)> SeedAsync()
    {
        var s = await database.Seed(lockedSubmission: true);
        await using var db = database.CreateContext();
        var project = await db.Projects.Include(p => p.Team).Include(p => p.ProjectMajors).SingleAsync(p => p.Id == s.ProjectId);
        var major = project.ProjectMajors.Single().MajorId;
        var student = await db.Users.SingleAsync(u => u.Id == s.Scope.Users.Student);
        student.MajorId = major;
        student.AcademicProfileStatus = "VERIFIED";

        db.TeamMembers.Add(new()
        {
            TeamId = project.TeamId,
            AcademicSemesterId = s.Scope.SemesterId,
            UserId = student.Id,
            IsLeader = true
        });

        db.Set<ProjectRegistrationSnapshot>().Add(new()
        {
            ProjectId = project.Id,
            ProjectPeriodId = s.PeriodId,
            LeadDepartmentId = s.Scope.Users.DepartmentId,
            SubmittedBy = student.Id,
            SubmittedAt = EvaluationDraftDatabaseFixture.Now.AddDays(-1),
            SnapshotJson = JsonSerializer.Serialize(new RegistrationEvidence(
                new TeamAcademicScopeDto("SINGLE_MAJOR", major, s.Scope.Users.DepartmentId, [new MajorRequirementDto(major, 1, 5, "Engineering")], Guid.NewGuid()),
                new(1, 5, 1, "test"), 1, EvaluationDraftDatabaseFixture.Now.AddDays(-2), EvaluationDraftDatabaseFixture.Now.AddDays(2),
                [new(student.Id, student.FullName, major, true)], [s.Scope.Users.DepartmentId],
                MajorDepartmentIds: new Dictionary<long, long> { [major] = s.Scope.Users.DepartmentId }))
        });

        await db.SaveChangesAsync();
        return (s, major);
    }

    private static async Task<EvaluationSchemeDto> SetupPublishedSchemeAsync(HttpClient staff, EvaluationScenario s, long major)
    {
        var request = new SaveEvaluationSchemeRequest(s.ProjectId, s.PeriodId, "CIB Acceptance Scheme", 5,
            [new("Common", "COMMON", null, s.RubricId, 100, 70, 1), new("Individual", "INDIVIDUAL", major, s.RubricId, 0, 30, 1)]);

        var draft = await ReadJsonAsync<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes", request));
        return await ReadJsonAsync<EvaluationSchemeDto>(await staff.PostAsJsonAsync(
            $"/api/v1/evaluation-schemes/{draft.Id}/publish",
            new SchemeTokenRequest(draft.ConcurrencyToken)));
    }

    [Fact]
    public async Task Contract_and_scenario_manifest_files_are_consistent_and_complete()
    {
        var rootDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var contractPath = Path.Combine(rootDir, "docs", "contracts", "cib-v4", "contract.json");
        var manifestPath = Path.Combine(rootDir, "docs", "contracts", "cib-v4", "scenario-manifest.json");

        Assert.True(File.Exists(contractPath), $"contract.json not found at {contractPath}");
        Assert.True(File.Exists(manifestPath), $"scenario-manifest.json not found at {manifestPath}");

        var contractJson = await File.ReadAllTextAsync(contractPath);
        var manifestJson = await File.ReadAllTextAsync(manifestPath);

        using var contractDoc = JsonDocument.Parse(contractJson);
        using var manifestDoc = JsonDocument.Parse(manifestJson);

        var contractVersion = contractDoc.RootElement.GetProperty("contractVersion").GetString();
        var manifestVersion = manifestDoc.RootElement.GetProperty("contractVersion").GetString();
        Assert.Equal("1.0.0-cib.v4", contractVersion);
        Assert.Equal(contractVersion, manifestVersion);

        var scenarios = manifestDoc.RootElement.GetProperty("scenarios");
        Assert.True(scenarios.GetArrayLength() >= 10, "Expected at least 10 documented scenarios");

        var modes = new HashSet<string>();
        var tags = new HashSet<string>();
        foreach (var sc in scenarios.EnumerateArray())
        {
            modes.Add(sc.GetProperty("mode").GetString()!);
            if (sc.TryGetProperty("tags", out var tProp))
            {
                foreach (var tag in tProp.EnumerateArray())
                {
                    tags.Add(tag.GetString()!);
                }
            }
        }

        Assert.Contains("SINGLE_MAJOR", modes);
        Assert.Contains("INTERDISCIPLINARY", modes);
        Assert.Contains("allowed", tags);
        Assert.Contains("denied", tags);
        Assert.Contains("stale/conflict", tags);
        Assert.Contains("privacy", tags);
        Assert.Contains("BLOCKED_BY_CONTRACT", tags);
    }

    [Fact]
    public async Task Single_major_fixture_creates_mutates_and_reads_back_isolated_state()
    {
        var (s, major) = await SeedAsync();
        using var factory = new EvaluationFactory(database);

        var staffClient = factory.CreateAuthenticatedClient(
            s.Scope.Users.Staff,
            "staff@example.test",
            "Staff Member",
            "DEPARTMENT_STAFF");

        var scheme = await SetupPublishedSchemeAsync(staffClient, s, major);
        var commonComponent = scheme.Components.First(c => c.Scope == "COMMON");

        // 1. Mutate: Assign evaluator in isolated DB
        var assignResponse = await staffClient.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER", "COMMON", null, null, commonComponent.Id));

        var assignment = await ReadJsonAsync<EvaluationAssignmentDto>(assignResponse);
        Assert.Equal("ACTIVE", assignment.Status);
        Assert.Equal("COMMON", assignment.Scope);

        // 2. Readback: Direct persistent SQL readback from a new DbContext
        await using (var verifyDb = database.CreateContext())
        {
            var persisted = await verifyDb.Set<EvaluationAssignment>()
                .AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == assignment.Id);

            Assert.NotNull(persisted);
            Assert.Equal(s.ProjectId, persisted!.ProjectId);
            Assert.Equal(s.Scope.Users.Lecturer, persisted.EvaluatorId);
            Assert.Equal("ACTIVE", persisted.Status);
            Assert.Equal("COMMON", persisted.Scope);
        }

        // 3. Mutate: Revoke assignment with optimistic concurrency token
        var revokeResponse = await staffClient.PostAsJsonAsync(
            $"/api/v1/evaluation-assignments/{assignment.Id}/revoke",
            new RevokeEvaluatorRequest(assignment.ConcurrencyToken, "Testing isolated teardown"));

        var revoked = await ReadJsonAsync<EvaluationAssignmentDto>(revokeResponse);
        Assert.Equal("REVOKED", revoked.Status);

        // 4. Readback: Verify mutated state in database
        await using (var verifyDb = database.CreateContext())
        {
            var persistedRevoked = await verifyDb.Set<EvaluationAssignment>()
                .AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == assignment.Id);

            Assert.NotNull(persistedRevoked);
            Assert.Equal("REVOKED", persistedRevoked!.Status);
            Assert.NotNull(persistedRevoked.RevokedAt);
        }
    }

    [Fact]
    public async Task Evaluation_evidence_endpoint_returns_expected_metadata_contract()
    {
        var (s, major) = await SeedAsync();
        using var factory = new EvaluationFactory(database);

        var staffClient = factory.CreateAuthenticatedClient(
            s.Scope.Users.Staff,
            "staff@example.test",
            "Staff Member",
            "DEPARTMENT_STAFF");

        var scheme = await SetupPublishedSchemeAsync(staffClient, s, major);
        var commonComponent = scheme.Components.First(c => c.Scope == "COMMON");

        // Assign evaluator
        var assignResponse = await staffClient.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER", "COMMON", null, null, commonComponent.Id));
        var assignment = await ReadJsonAsync<EvaluationAssignmentDto>(assignResponse);

        var evaluatorClient = factory.CreateAuthenticatedClient(
            s.Scope.Users.Lecturer,
            "lecturer@example.test",
            "Assigned Evaluator",
            "LECTURER");

        // Query assignment evidence
        var evidenceResponse = await evaluatorClient.GetAsync(
            $"/api/v1/evaluation-assignments/{assignment.Id}/evidence");

        var evidence = await ReadJsonAsync<EvaluationAssignmentEvidenceDto>(evidenceResponse);
        Assert.Equal(assignment.Id, evidence.AssignmentId);
        Assert.Equal(s.ProjectId, evidence.ProjectId);
        Assert.Equal("COMMON", evidence.Scope);
        Assert.True(evidence.IsReadOnly);
        Assert.True(evidence.ItemCount >= 0);
    }
}
