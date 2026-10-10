using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using AIPMS.Api.Controllers;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.Services;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.IntegrationTests.Evaluations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using File = System.IO.File;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;
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

    private static string GetContractsRootDir()
    {
        var rootDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        return Path.Combine(rootDir, "docs", "contracts", "cib-v4");
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
    public async Task OpenApi_specification_artifact_is_generated_and_covers_cib_v4_routes()
    {
        using var app = new AipmsWebApplicationFactory();
        using var client = app.CreateClient();
        var swaggerJson = await client.GetStringAsync("/swagger/v1/swagger.json");
        Assert.False(string.IsNullOrWhiteSpace(swaggerJson));

        var contractsDir = GetContractsRootDir();
        var openApiPath = Path.Combine(contractsDir, "openapi.json");
        Assert.True(File.Exists(openApiPath), $"Expected openapi.json at {openApiPath}");

        var committedBytes = await File.ReadAllBytesAsync(openApiPath);
        var committedText = await File.ReadAllTextAsync(openApiPath);

        using var doc = JsonDocument.Parse(swaggerJson);
        var runtimeFormatted = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });

        // Verify committed artifact has not drifted from runtime OpenAPI specification
        using var committedDoc = JsonDocument.Parse(committedText);
        var committedFormatted = JsonSerializer.Serialize(committedDoc.RootElement, new JsonSerializerOptions { WriteIndented = true });

        Assert.Equal(
            committedFormatted.Replace("\r\n", "\n"),
            runtimeFormatted.Replace("\r\n", "\n"));

        // Regression assertion: acceptance tests must never mutate committed artifacts on disk (zero-write path)
        var bytesAfterTest = await File.ReadAllBytesAsync(openApiPath);
        Assert.Equal(committedBytes, bytesAfterTest);

        var paths = doc.RootElement.GetProperty("paths");

        // Verify CIB v4 routes in actual OpenAPI specification
        var requiredRoutes = new[]
        {
            "/api/v1/student-qualifications/me/certificate",
            "/api/v1/tasks/project/{projectId}",
            "/api/v1/projects/{projectId}/eligible-evaluators",
            "/api/v1/projects/{projectId}/evaluation-assignments",
            "/api/v1/evaluation-assignments/my",
            "/api/v1/evaluation-assignments/{id}/revoke",
            "/api/v1/evaluation-assignments/{id}",
            "/api/v1/evaluation-assignments/{id}/evidence",
            "/api/v1/evaluation-assignments/{id}/evidence/files/{fileId}",
            "/api/v1/evaluation-assignments/{id}/evaluation",
            "/api/v1/evaluations/{id}",
            "/api/v1/evaluations/{id}/draft",
            "/api/v1/evaluations/{id}/finalize",
            "/api/v1/projects/{projectId}/final-submission",
            "/api/v1/projects/{projectId}/result",
            "/api/v1/projects/{projectId}/students/{studentId}/result"
        };

        foreach (var route in requiredRoutes)
        {
            Assert.True(paths.TryGetProperty(route, out _), $"OpenAPI spec missing required route: {route}");
        }
    }

    [Fact]
    public async Task Contract_and_scenario_manifest_files_are_consistent_and_complete()
    {
        var contractsDir = GetContractsRootDir();
        var contractPath = Path.Combine(contractsDir, "contract.json");
        var manifestPath = Path.Combine(contractsDir, "scenario-manifest.json");

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

        // Verify OpenAPI artifact distinction
        var openapiSpecPath = contractDoc.RootElement.GetProperty("metadata").GetProperty("openapiSpecificationPath").GetString();
        Assert.Equal("openapi.json", openapiSpecPath);

        // Verify documented file limits match runtime constants
        var fileLimits = contractDoc.RootElement.GetProperty("fileLimits");
        var docBusinessMax = fileLimits.GetProperty("certificateBusinessMaxBytes").GetInt64();
        var docTransportCeiling = fileLimits.GetProperty("certificateTransportCeilingBytes").GetInt64();
        Assert.Equal(UploadValidator.MaxBytes, docBusinessMax);
        Assert.Equal(22L * 1024 * 1024, docTransportCeiling);

        var scenarios = manifestDoc.RootElement.GetProperty("scenarios");
        Assert.True(scenarios.GetArrayLength() >= 10, "Expected at least 10 documented scenarios");

        var modes = new HashSet<string>();
        var tags = new HashSet<string>();
        var evidencedScenariosCount = 0;

        foreach (var sc in scenarios.EnumerateArray())
        {
            var mode = sc.GetProperty("mode").GetString()!;
            modes.Add(mode);

            if (sc.TryGetProperty("tags", out var tProp))
            {
                foreach (var tag in tProp.EnumerateArray())
                {
                    tags.Add(tag.GetString()!);
                }
            }

            if (sc.TryGetProperty("acceptanceEvidence", out var evProp) && !string.IsNullOrWhiteSpace(evProp.GetString()))
            {
                var evidenceString = evProp.GetString()!;
                var method = ResolveTestMethod(evidenceString);
                Assert.NotNull(method);
                evidencedScenariosCount++;
            }
        }

        Assert.Contains("SINGLE_MAJOR", modes);
        Assert.Contains("INTERDISCIPLINARY", modes);
        Assert.Contains("allowed", tags);
        Assert.Contains("denied", tags);
        Assert.Contains("stale/conflict", tags);
        Assert.Contains("privacy", tags);
        Assert.Contains("BLOCKED_BY_CONTRACT", tags);
        Assert.True(evidencedScenariosCount >= 5, $"Expected at least 5 scenarios with verified acceptance evidence, found {evidencedScenariosCount}");
    }

    private static MethodInfo? ResolveTestMethod(string evidenceString)
    {
        var lastDot = evidenceString.LastIndexOf('.');
        if (lastDot <= 0) return null;
        var typeName = evidenceString[..lastDot];
        var methodName = evidenceString[(lastDot + 1)..];
        var type = typeof(CibV4AcceptanceFoundationTests).Assembly.GetType(typeName);
        return type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
    }

    [Fact]
    public async Task Certificate_file_size_contract_distinguishes_transport_and_business_limits()
    {
        // 1. Business file size limit in UploadValidator
        Assert.Equal(20 * 1024 * 1024, UploadValidator.MaxBytes);

        // 2. Transport body ceiling on StudentQualificationsController.UploadCertificate
        var method = typeof(StudentQualificationsController).GetMethod(nameof(StudentQualificationsController.UploadCertificate));
        Assert.NotNull(method);
        var requestSizeLimit = method!.GetCustomAttribute<RequestSizeLimitAttribute>();
        Assert.NotNull(requestSizeLimit);
        Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata>(requestSizeLimit);
        var sizeLimitMeta = (Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata)requestSizeLimit!;
        Assert.Equal(22L * 1024 * 1024, sizeLimitMeta.MaxRequestBodySize);

        var requestFormLimits = method.GetCustomAttribute<RequestFormLimitsAttribute>();
        Assert.NotNull(requestFormLimits);
        Assert.Equal(22L * 1024 * 1024, requestFormLimits!.MultipartBodyLengthLimit);

        // 3. Verify documented contract limits match the actual code
        var contractPath = Path.Combine(GetContractsRootDir(), "contract.json");
        var contractJson = await File.ReadAllTextAsync(contractPath);
        using var contractDoc = JsonDocument.Parse(contractJson);
        var fileLimits = contractDoc.RootElement.GetProperty("fileLimits");
        Assert.Equal(UploadValidator.MaxBytes, fileLimits.GetProperty("certificateBusinessMaxBytes").GetInt64());
        Assert.Equal(sizeLimitMeta.MaxRequestBodySize, fileLimits.GetProperty("certificateTransportCeilingBytes").GetInt64());
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
    public async Task Interdisciplinary_fixture_enforces_multi_major_boundaries_and_persisted_readback()
    {
        var (s, firstMajor) = await SeedAsync();
        long secondMajor, secondDepartmentId, secondLecturerId, secondRubricId;

        // Provision second department and major on isolated DB
        await using (var db = database.CreateContext())
        {
            var suffix = Guid.NewGuid().ToString("N")[..6];
            var dept = new M.Department
            {
                Code = "B_" + suffix,
                Name = "Business " + suffix,
                OrganizationId = (await db.Departments.FindAsync(s.Scope.Users.DepartmentId))!.OrganizationId,
                IsActive = true
            };
            db.Departments.Add(dept);
            await db.SaveChangesAsync();
            secondDepartmentId = dept.Id;

            var major = new M.Major
            {
                DepartmentId = secondDepartmentId,
                Code = "BA_" + suffix,
                Name = "Business Analysis " + suffix,
                IsActive = true
            };
            db.Majors.Add(major);

            var secondRubric = new M.Rubric
            {
                Code = "R_" + suffix,
                Name = "Business assessment",
                DepartmentId = secondDepartmentId,
                AcademicSemesterId = s.Scope.SemesterId,
                CreatedBy = s.Scope.Users.OutsideStaff,
                IsActive = true,
                RubricCriteria = [new()
                {
                    Criterion = new() { Code = "C_" + suffix, Name = "Business outcome", IsActive = true },
                    WeightPercent = 100,
                    MaxScore = 10,
                    IsRequired = true
                }]
            };
            db.Rubrics.Add(secondRubric);

            var secondLecturer = new M.User
            {
                Email = $"other_lecturer_{suffix}@example.test",
                FullName = "Other Lecturer",
                PasswordHash = "unused",
                Status = "ACTIVE",
                DepartmentId = secondDepartmentId,
                UserRoleUsers = [new() { RoleId = await db.Roles.Where(r => r.Code == "LECTURER").Select(r => r.Id).SingleAsync() }]
            };
            db.Users.Add(secondLecturer);
            db.ProjectMajors.Add(new() { ProjectId = s.ProjectId, Major = major });
            await db.SaveChangesAsync();
            secondMajor = major.Id;
            secondLecturerId = secondLecturer.Id;
            secondRubricId = secondRubric.Id;

            db.Add(new RubricVersion
            {
                RubricId = secondRubric.Id,
                RootRubricId = secondRubric.Id,
                VersionNumber = 1,
                Status = "PUBLISHED",
                ConcurrencyToken = Guid.NewGuid()
            });

            // Update registration snapshot with INTERDISCIPLINARY mode and multiple department IDs
            var snapshot = await db.Set<ProjectRegistrationSnapshot>().SingleAsync(x => x.ProjectId == s.ProjectId);
            var evidence = JsonSerializer.Deserialize<RegistrationEvidence>(snapshot.SnapshotJson)!;
            snapshot.SnapshotJson = JsonSerializer.Serialize(evidence with
            {
                Scope = evidence.Scope with
                {
                    ProjectMode = "INTERDISCIPLINARY",
                    PrimaryMajorId = null,
                    Requirements = [.. evidence.Scope.Requirements, new(secondMajor, 1, 3, "Business deliverables")]
                },
                DepartmentIds = [s.Scope.Users.DepartmentId, secondDepartmentId],
                MajorDepartmentIds = new Dictionary<long, long> { [firstMajor] = s.Scope.Users.DepartmentId, [secondMajor] = secondDepartmentId }
            });
            await db.SaveChangesAsync();
        }

        using var factory = new EvaluationFactory(database);
        var adminClient = factory.CreateAuthenticatedClient(s.Scope.Users.Admin, roles: ["ADMIN"]);

        // Setup interdisciplinary scheme with MAJOR_SPECIFIC component for both majors
        var schemeRequest = new SaveEvaluationSchemeRequest(s.ProjectId, s.PeriodId, "Interdisciplinary Scheme", 5,
            [
                new("Common", "COMMON", null, s.RubricId, 50, 50, 1),
                new("Major Specific 1", "MAJOR_SPECIFIC", firstMajor, s.RubricId, 25, 50, 1),
                new("Major Specific 2", "MAJOR_SPECIFIC", secondMajor, secondRubricId, 25, 50, 1)
            ]);
        var draft = await ReadJsonAsync<EvaluationSchemeDto>(await adminClient.PostAsJsonAsync("/api/v1/evaluation-schemes", schemeRequest));
        var scheme = await ReadJsonAsync<EvaluationSchemeDto>(await adminClient.PostAsJsonAsync($"/api/v1/evaluation-schemes/{draft.Id}/publish", new SchemeTokenRequest(draft.ConcurrencyToken)));

        var majorComponent = scheme.Components.First(c => c.Scope == "MAJOR_SPECIFIC" && c.MajorId == firstMajor);

        // Admin assigns evaluator to MAJOR_SPECIFIC component
        var assignResponse = await adminClient.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER", "MAJOR_SPECIFIC", firstMajor, null, majorComponent.Id));
        var assignment = await ReadJsonAsync<EvaluationAssignmentDto>(assignResponse);

        Assert.Equal("MAJOR_SPECIFIC", assignment.Scope);
        Assert.Equal(firstMajor, assignment.MajorId);

        // Verify foreign lecturer (different department) cannot view assignment detail
        var foreignLecturerClient = factory.CreateAuthenticatedClient(secondLecturerId, roles: ["LECTURER"]);
        var foreignResponse = await foreignLecturerClient.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);

        // Readback: Direct persistent SQL readback confirms interdisciplinary assignment state
        await using (var verifyDb = database.CreateContext())
        {
            var persisted = await verifyDb.Set<EvaluationAssignment>()
                .AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == assignment.Id);

            Assert.NotNull(persisted);
            Assert.Equal("MAJOR_SPECIFIC", persisted!.Scope);
            Assert.Equal(firstMajor, persisted.MajorId);
            Assert.Equal("ACTIVE", persisted.Status);
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
