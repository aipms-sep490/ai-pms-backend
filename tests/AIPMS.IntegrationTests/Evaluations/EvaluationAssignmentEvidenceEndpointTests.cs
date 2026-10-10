using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Models;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using FileEntity = AIPMS.Infrastructure.Persistence.Generated.Models.File;
using FinalSubmissionEntity = AIPMS.Infrastructure.Persistence.Models.FinalSubmission;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Evaluations;

[Collection("ProjectDbTests")]
public sealed class EvaluationAssignmentEvidenceEndpointTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private sealed class MemoryFileStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> _files = new();

        public Task WriteAsync(string key, Stream content, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            _files[key] = ms.ToArray();
            return Task.CompletedTask;
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        {
            if (!_files.TryGetValue(key, out var bytes))
                throw new FileNotFoundException("Key not found", key);
            return Task.FromResult<Stream>(new MemoryStream(bytes));
        }

        public Task DeleteAsync(string key, CancellationToken ct)
        {
            _files.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class EvaluationClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(EvaluationDraftDatabaseFixture.Now);
    }

    private sealed class EvidenceTestFactory(EvaluationDraftDatabaseFixture database, MemoryFileStorage storage) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new EvaluationClock());
                services.RemoveAll<IFileStorage>();
                services.AddSingleton<IFileStorage>(storage);
            });
        }
    }

    private async Task<(EvaluationAssignmentDto Assignment, EvaluationScenario Scenario, long MajorId)> SeedAssignmentAsync(
        EvidenceTestFactory factory,
        string scope = "COMMON",
        long? majorId = null,
        long? studentId = null,
        string status = "ACTIVE")
    {
        var s = await database.Seed(lockedSubmission: true);
        await using var db = database.CreateContext();
        var project = await db.Projects.Include(p => p.Team).Include(p => p.ProjectMajors).SingleAsync(p => p.Id == s.ProjectId);
        var defaultMajor = project.ProjectMajors.Single().MajorId;
        var student = await db.Users.SingleAsync(u => u.Id == s.Scope.Users.Student);
        student.MajorId = defaultMajor;
        student.AcademicProfileStatus = "VERIFIED";

        if (!await db.TeamMembers.AnyAsync(m => m.TeamId == project.TeamId && m.UserId == student.Id))
        {
            db.TeamMembers.Add(new()
            {
                TeamId = project.TeamId,
                AcademicSemesterId = s.Scope.SemesterId,
                UserId = student.Id,
                IsLeader = true
            });
        }

        if (!await db.Set<ProjectRegistrationSnapshot>().AnyAsync(x => x.ProjectId == project.Id))
        {
            db.Set<ProjectRegistrationSnapshot>().Add(new()
            {
                ProjectId = project.Id,
                ProjectPeriodId = s.PeriodId,
                LeadDepartmentId = s.Scope.Users.DepartmentId,
                SubmittedBy = student.Id,
                SubmittedAt = EvaluationDraftDatabaseFixture.Now.AddDays(-1),
                SnapshotJson = JsonSerializer.Serialize(new RegistrationEvidence(
                    new TeamAcademicScopeDto("SINGLE_MAJOR", defaultMajor, s.Scope.Users.DepartmentId, [new MajorRequirementDto(defaultMajor, 1, 5, "Engineering")], Guid.NewGuid()),
                    new(1, 5, 1, "test"), 1, EvaluationDraftDatabaseFixture.Now.AddDays(-2), EvaluationDraftDatabaseFixture.Now.AddDays(2),
                    [new(student.Id, student.FullName, defaultMajor, true)], [s.Scope.Users.DepartmentId],
                    MajorDepartmentIds: new Dictionary<long, long> { [defaultMajor] = s.Scope.Users.DepartmentId }))
            });
        }

        await db.SaveChangesAsync();

        var staffClient = factory.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        var effectiveMajor = majorId ?? defaultMajor;
        var scheme = await SetupPublishedSchemeAsync(staffClient, s, effectiveMajor);

        var targetMajorId = scope == "COMMON" ? null : (long?)effectiveMajor;
        var targetStudentId = scope == "INDIVIDUAL" ? (long?)(studentId ?? student.Id) : null;

        var comp = scheme.Components.First(c => c.Scope == scope);

        var assignResponse = await staffClient.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER", scope, targetMajorId, targetStudentId, comp.Id));
        Assert.True(assignResponse.IsSuccessStatusCode, await assignResponse.Content.ReadAsStringAsync());

        var assignment = (await assignResponse.Content.ReadFromJsonAsync<EvaluationAssignmentDto>())!;

        if (status == "REVOKED")
        {
            var revokeResponse = await staffClient.PostAsJsonAsync(
                $"/api/v1/evaluation-assignments/{assignment.Id}/revoke",
                new RevokeEvaluatorRequest(assignment.ConcurrencyToken, "Testing revocation"));
            Assert.True(revokeResponse.IsSuccessStatusCode, await revokeResponse.Content.ReadAsStringAsync());
            assignment = (await revokeResponse.Content.ReadFromJsonAsync<EvaluationAssignmentDto>())!;
        }

        return (assignment, s, defaultMajor);
    }

    private static async Task<EvaluationSchemeDto> SetupPublishedSchemeAsync(HttpClient staff, EvaluationScenario s, long major)
    {
        var request = new SaveEvaluationSchemeRequest(s.ProjectId, s.PeriodId, "CIB Evidence Scheme", 5,
            [
                new("Common", "COMMON", null, s.RubricId, 50, 40, 1),
                new("Major Specific", "MAJOR_SPECIFIC", major, s.RubricId, 50, 30, 1),
                new("Individual", "INDIVIDUAL", major, s.RubricId, 0, 30, 1)
            ]);

        var draftResponse = await staff.PostAsJsonAsync("/api/v1/evaluation-schemes", request);
        Assert.True(draftResponse.IsSuccessStatusCode, await draftResponse.Content.ReadAsStringAsync());
        var draft = (await draftResponse.Content.ReadFromJsonAsync<EvaluationSchemeDto>())!;

        var publishResponse = await staff.PostAsJsonAsync(
            $"/api/v1/evaluation-schemes/{draft.Id}/publish",
            new SchemeTokenRequest(draft.ConcurrencyToken));
        Assert.True(publishResponse.IsSuccessStatusCode, await publishResponse.Content.ReadAsStringAsync());
        return (await publishResponse.Content.ReadFromJsonAsync<EvaluationSchemeDto>())!;
    }

    [Fact]
    public async Task ValidAssignment_ReturnsExistingMetadataAndItems()
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, _) = await SeedAssignmentAsync(factory);
        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var evidence = await response.Content.ReadFromJsonAsync<EvaluationAssignmentEvidenceDto>();
        Assert.NotNull(evidence);
        Assert.Equal(assignment.Id, evidence.AssignmentId);
        Assert.Equal(s.ProjectId, evidence.ProjectId);
        Assert.Equal("COMMON", evidence.Scope);
        Assert.True(evidence.IsReadOnly);
        Assert.Equal(1, evidence.ItemCount);
        Assert.Single(evidence.Items);

        var item = evidence.Items[0];
        Assert.Equal("Final report", item.Title);
        Assert.Equal("report.txt", item.FileName);
        Assert.Equal("DELIVERABLE", item.SourceType);
        Assert.Equal($"/api/v1/evaluation-assignments/{assignment.Id}/evidence/files/{item.FileId}", item.DownloadUrl);
    }

    [Fact]
    public async Task WrongEvaluator_IsDenied()
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, _) = await SeedAssignmentAsync(factory);
        // NewLecturer is in the same department but is NOT the assigned evaluator
        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.NewLecturer);

        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RevokedAssignment_FollowsCurrentAccessRule()
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, _) = await SeedAssignmentAsync(factory, status: "REVOKED");
        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CrossProjectEvidence_ExcludedAndDenied()
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, _) = await SeedAssignmentAsync(factory);
        await using var db = database.CreateContext();

        // Seed a separate project with its own team, deliverable and file
        var foreignTeam = new Team
        {
            Code = Guid.NewGuid().ToString("N")[..8],
            Name = "Foreign Team",
            Status = "LOCKED",
            AcademicSemesterId = s.Scope.SemesterId,
            CreatedBy = s.Scope.Users.Student
        };
        db.Teams.Add(foreignTeam);
        await db.SaveChangesAsync();

        var foreignProject = new Project
        {
            Code = "PRJ_FOREIGN_" + Guid.NewGuid().ToString("N")[..8],
            Title = "Foreign Project",
            Status = "ACTIVE",
            TeamId = foreignTeam.Id,
            RowVersion = new byte[] { 1 },
            CreatedBy = s.Scope.Users.Student
        };
        db.Projects.Add(foreignProject);
        await db.SaveChangesAsync();

        // Insert placeholder file so foreignFile does not share ID 1 with the frozen submission item
        db.Files.Add(new FileEntity
        {
            UploadedBy = s.Scope.Users.Student,
            OriginalFileName = "local-placeholder.pdf",
            StoragePath = "local-placeholder",
            MimeType = "application/pdf",
            FileSizeBytes = 50,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var foreignFile = new FileEntity
        {
            UploadedBy = s.Scope.Users.Student,
            OriginalFileName = "foreign.pdf",
            StoragePath = "foreign-storage-key",
            MimeType = "application/pdf",
            FileSizeBytes = 100,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Files.Add(foreignFile);
        await db.SaveChangesAsync();

        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        // Evidence query should only include files from assignment's project
        var evidenceResponse = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence");
        Assert.Equal(HttpStatusCode.OK, evidenceResponse.StatusCode);
        var evidence = await evidenceResponse.Content.ReadFromJsonAsync<EvaluationAssignmentEvidenceDto>();
        Assert.NotNull(evidence);
        Assert.DoesNotContain(evidence.Items, i => i.FileId == foreignFile.Id);

        // Attempting to download the foreign project file through this assignment must be denied/not found
        var downloadResponse = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence/files/{foreignFile.Id}");
        Assert.Equal(HttpStatusCode.NotFound, downloadResponse.StatusCode);
    }

    [Fact]
    public async Task MajorSpecific_CannotSeeAnotherMajorEvidence()
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, majorA) = await SeedAssignmentAsync(factory, scope: "MAJOR_SPECIFIC");

        await using var db = database.CreateContext();

        var majorB = new Major
        {
            DepartmentId = s.Scope.Users.DepartmentId,
            Code = "MAJOR_B_" + Guid.NewGuid().ToString("N")[..8],
            Name = "Major B",
            IsActive = true
        };
        db.Majors.Add(majorB);
        await db.SaveChangesAsync();

        var studentB = new User
        {
            Email = "studentB_" + Guid.NewGuid().ToString("N")[..6] + "@example.test",
            FullName = "Student B",
            PasswordHash = "unused",
            Status = "ACTIVE",
            DepartmentId = s.Scope.Users.DepartmentId,
            MajorId = majorB.Id,
            AcademicProfileStatus = "VERIFIED"
        };
        db.Users.Add(studentB);
        await db.SaveChangesAsync();

        // Update frozen snapshot to include studentB with majorB
        var snapshot = await db.Set<ProjectRegistrationSnapshot>().FirstAsync(x => x.ProjectId == s.ProjectId);
        var evidenceObj = JsonSerializer.Deserialize<RegistrationEvidence>(snapshot.SnapshotJson)!;
        var updatedMembers = evidenceObj.Members.ToList();
        updatedMembers.Add(new RegisteredMemberDto(studentB.Id, studentB.FullName, majorB.Id, false));
        snapshot.SnapshotJson = JsonSerializer.Serialize(new RegistrationEvidence(
            evidenceObj.Scope,
            evidenceObj.Policy,
            evidenceObj.OrganizationId,
            evidenceObj.WindowStartAt,
            evidenceObj.WindowEndAt,
            updatedMembers,
            evidenceObj.DepartmentIds,
            MajorDepartmentIds: new Dictionary<long, long> { [majorA] = s.Scope.Users.DepartmentId, [majorB.Id] = s.Scope.Users.DepartmentId }));

        // Add a second deliverable version to final submission tied to Major B via studentB
        var final = await db.Set<FinalSubmissionEntity>().Include(f => f.Items).FirstAsync(f => f.ProjectId == s.ProjectId);
        var delB = new Deliverable { ProjectId = s.ProjectId, Title = "Major B Deliverable", Status = "OPEN", CreatedBy = studentB.Id };
        db.Deliverables.Add(delB);
        await db.SaveChangesAsync();

        var verB = new DeliverableVersion
        {
            DeliverableId = delB.Id,
            VersionNumber = 1,
            Status = "SUBMITTED",
            SubmittedBy = studentB.Id,
            SubmittedAt = DateTime.UtcNow
        };
        db.DeliverableVersions.Add(verB);
        await db.SaveChangesAsync();

        var itemB = new FinalSubmissionItem
        {
            SubmissionId = final.Id,
            DeliverableId = delB.Id,
            DeliverableVersionId = verB.Id,
            Title = "Major B Deliverable",
            VersionNumber = 1,
            StatusAtSubmission = "SUBMITTED",
            WasRequired = true,
            // Use typed FinalSnapshotFile — no TargetStudentId/TargetMajorId in production record (CASE B)
            FilesJson = JsonSerializer.Serialize(new[]
            {
                new FinalSnapshotFile(new(902L, "DELIVERABLE_VERSION", verB.Id, "major_b.txt", "text/plain", 10L, new string('b', 64), studentB.Id, DateTime.UtcNow), "storage-b")
            })
        };
        final.Items.Add(itemB);
        await db.SaveChangesAsync();

        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var evidence = await response.Content.ReadFromJsonAsync<EvaluationAssignmentEvidenceDto>();
        Assert.NotNull(evidence);

        // CASE B: MAJOR_SPECIFIC scope returns 0 items because item-level majorId cannot be proven
        // from typed FinalSnapshotFile/ProjectFileDto (no TargetMajorId field exists).
        // Both majorA and majorB items are excluded — fail closed.
        Assert.Empty(evidence.Items);
        Assert.Equal(0, evidence.ItemCount);

        // All evidence files are outside this assignment's scoped items → Forbidden
        var downloadResponseA = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence/files/1");
        Assert.Equal(HttpStatusCode.Forbidden, downloadResponseA.StatusCode);

        var downloadResponseB = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence/files/902");
        Assert.Equal(HttpStatusCode.Forbidden, downloadResponseB.StatusCode);
    }


    [Fact]
    public async Task Individual_CannotSeeAnotherStudentEvidence()
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, major) = await SeedAssignmentAsync(factory, scope: "INDIVIDUAL");
        await using var db = database.CreateContext();

        // Create student 2 in the same department
        var student2 = new User
        {
            Email = "student2_" + Guid.NewGuid().ToString("N")[..6] + "@example.test",
            FullName = "Student Two",
            PasswordHash = "unused",
            Status = "ACTIVE",
            DepartmentId = s.Scope.Users.DepartmentId,
            MajorId = major,
            AcademicProfileStatus = "VERIFIED"
        };
        db.Users.Add(student2);
        await db.SaveChangesAsync();

        var del2 = new Deliverable { ProjectId = s.ProjectId, Title = "Student 2 Specific Task", Status = "OPEN", CreatedBy = student2.Id };
        db.Deliverables.Add(del2);
        await db.SaveChangesAsync();

        var ver2 = new DeliverableVersion
        {
            DeliverableId = del2.Id,
            VersionNumber = 1,
            Status = "SUBMITTED",
            SubmittedBy = student2.Id,
            SubmittedAt = DateTime.UtcNow
        };
        db.DeliverableVersions.Add(ver2);
        await db.SaveChangesAsync();

        // Add student2 to frozen registration snapshot
        var snapshot = await db.Set<ProjectRegistrationSnapshot>().FirstAsync(x => x.ProjectId == s.ProjectId);
        var evidenceObj = JsonSerializer.Deserialize<RegistrationEvidence>(snapshot.SnapshotJson)!;
        var members = evidenceObj.Members.ToList();
        members.Add(new RegisteredMemberDto(student2.Id, student2.FullName, major, false));
        snapshot.SnapshotJson = JsonSerializer.Serialize(new RegistrationEvidence(
            evidenceObj.Scope,
            evidenceObj.Policy,
            evidenceObj.OrganizationId,
            evidenceObj.WindowStartAt,
            evidenceObj.WindowEndAt,
            members,
            evidenceObj.DepartmentIds,
            MajorDepartmentIds: evidenceObj.MajorDepartmentIds));

        // Add an item for student 2 to final submission
        var final = await db.Set<FinalSubmissionEntity>().Include(f => f.Items).FirstAsync(f => f.ProjectId == s.ProjectId);
        var item2 = new FinalSubmissionItem
        {
            SubmissionId = final.Id,
            DeliverableId = del2.Id,
            DeliverableVersionId = ver2.Id,
            Title = "Student 2 Specific Task",
            VersionNumber = 1,
            StatusAtSubmission = "SUBMITTED",
            WasRequired = true,
            // Use typed FinalSnapshotFile — no TargetStudentId/TargetMajorId in production record (CASE B)
            FilesJson = JsonSerializer.Serialize(new[]
            {
                new FinalSnapshotFile(new(802L, "DELIVERABLE_VERSION", ver2.Id, "student2.txt", "text/plain", 15L, new string('c', 64), student2.Id, DateTime.UtcNow), "storage-student2")
            })
        };
        final.Items.Add(item2);
        await db.SaveChangesAsync();

        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var evidence = await response.Content.ReadFromJsonAsync<EvaluationAssignmentEvidenceDto>();
        Assert.NotNull(evidence);

        // CASE B: INDIVIDUAL scope returns 0 items because item-level studentId cannot be proven
        // from typed FinalSnapshotFile/ProjectFileDto (no TargetStudentId field exists).
        // Both student1 and student2 items are excluded — fail closed.
        Assert.Empty(evidence.Items);
        Assert.Equal(0, evidence.ItemCount);

        // All evidence files are outside this assignment's scoped items → Forbidden
        var downloadResponseS1 = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence/files/1");
        Assert.Equal(HttpStatusCode.Forbidden, downloadResponseS1.StatusCode);

        var downloadResponseS2 = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence/files/802");
        Assert.Equal(HttpStatusCode.Forbidden, downloadResponseS2.StatusCode);
    }


    [Fact]
    public async Task FrozenFinalPackageVersion_IsUsed()
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, _) = await SeedAssignmentAsync(factory);
        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var evidence = await response.Content.ReadFromJsonAsync<EvaluationAssignmentEvidenceDto>();
        Assert.NotNull(evidence);
        Assert.NotNull(evidence.FinalSubmissionId);
        Assert.Single(evidence.Items);
        Assert.Contains("Version 1", evidence.Items[0].Description);
    }

    [Fact]
    public async Task LaterUpload_DoesNotReplaceFrozenItem()
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, _) = await SeedAssignmentAsync(factory);
        await using var db = database.CreateContext();

        // Simulate student uploading Version 2 after final submission has been locked
        var final = await db.Set<FinalSubmissionEntity>().Include(f => f.Items).FirstAsync(f => f.ProjectId == s.ProjectId);
        var frozenDeliverableId = final.Items.First().DeliverableId;

        var v2 = new DeliverableVersion
        {
            DeliverableId = frozenDeliverableId,
            VersionNumber = 2,
            Status = "SUBMITTED",
            SubmittedBy = s.Scope.Users.Student,
            SubmittedAt = DateTime.UtcNow
        };
        db.DeliverableVersions.Add(v2);
        await db.SaveChangesAsync();

        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var evidence = await response.Content.ReadFromJsonAsync<EvaluationAssignmentEvidenceDto>();
        Assert.NotNull(evidence);

        // The item must still be the frozen Version 1, not Version 2
        Assert.Single(evidence.Items);
        Assert.Contains("Version 1", evidence.Items[0].Description);
        Assert.DoesNotContain("Version 2", evidence.Items[0].Description);
    }

    [Fact]
    public async Task ValidProtectedFileAccess_Succeeds()
    {
        var storage = new MemoryFileStorage();
        // Seed content for the frozen object ("fixture-object" is seeded in EvaluationDraftDatabaseFixture)
        var content = "This is verified frozen evidence content."u8.ToArray();
        await storage.WriteAsync("fixture-object", new MemoryStream(content), CancellationToken.None);

        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, _) = await SeedAssignmentAsync(factory);
        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        // FileId 1 is the file in the frozen submission
        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence/files/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var downloadedBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(content, downloadedBytes);
    }

    [Fact]
    public async Task WrongAssignmentOrFileScope_Denied()
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, _) = await SeedAssignmentAsync(factory);
        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        // Non-existent file ID -> 404 Not Found
        var notFoundResponse = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence/files/999999");
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);

        // Non-existent assignment ID -> 404 Not Found
        var wrongAssignmentResponse = await client.GetAsync("/api/v1/evaluation-assignments/999999/evidence/files/1");
        Assert.Equal(HttpStatusCode.NotFound, wrongAssignmentResponse.StatusCode);
    }

    [Fact]
    public async Task ExistingMetadataFields_RemainBackwardCompatible()
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, _) = await SeedAssignmentAsync(factory);
        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rawJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        // Verify all 9 original properties exist and have expected types
        Assert.True(root.TryGetProperty("assignmentId", out var pAssignmentId) && pAssignmentId.GetInt64() == assignment.Id);
        Assert.True(root.TryGetProperty("projectId", out var pProjectId) && pProjectId.GetInt64() == s.ProjectId);
        Assert.True(root.TryGetProperty("scope", out var pScope) && pScope.GetString() == "COMMON");
        Assert.True(root.TryGetProperty("majorId", out _));
        Assert.True(root.TryGetProperty("studentId", out _));
        Assert.True(root.TryGetProperty("finalSubmissionId", out var pFinalId) && pFinalId.GetInt64() > 0);
        Assert.True(root.TryGetProperty("submittedAt", out var pSubmittedAt) && pSubmittedAt.GetString() != null);
        Assert.True(root.TryGetProperty("itemCount", out var pItemCount) && pItemCount.GetInt32() == 1);
        Assert.True(root.TryGetProperty("isReadOnly", out var pIsReadOnly) && pIsReadOnly.GetBoolean());

        // And verify new items property exists
        Assert.True(root.TryGetProperty("items", out var pItems) && pItems.GetArrayLength() == 1);
    }

    [Fact]
    public async Task UnknownScope_ExposesZeroItems_AndDownloadIsForbidden()
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, _) = await SeedAssignmentAsync(factory);

        // Mutate assignment scope to UNKNOWN directly in the database
        await using (var db = database.CreateContext())
        {
            var assign = await db.Set<EvaluationAssignment>().SingleAsync(a => a.Id == assignment.Id);
            assign.Scope = "UNKNOWN";
            assign.ComponentId = null;
            assign.MajorId = null;
            assign.StudentId = null;
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        // Evidence projection for UNKNOWN scope fails closed: 0 items
        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var evidence = await response.Content.ReadFromJsonAsync<EvaluationAssignmentEvidenceDto>();
        Assert.NotNull(evidence);
        Assert.Equal("UNKNOWN", evidence.Scope);
        Assert.Equal(0, evidence.ItemCount);
        Assert.Empty(evidence.Items);

        // Download must be rejected with 403 Forbidden for UNKNOWN scope
        var downloadResponse = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence/files/1");
        Assert.Equal(HttpStatusCode.Forbidden, downloadResponse.StatusCode);
    }

    [Fact]
    public async Task ProjectEvidenceAddedAfterLock_IsExcludedFromItemsAndDownloadReturnsNotFound()
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, _) = await SeedAssignmentAsync(factory);

        // Seed a standalone file and project evidence record in the DB after submission lock
        var postLockFileId = 0L;
        await using (var db = database.CreateContext())
        {
            // Insert placeholder file so postLockFile does not share ID 1 with the frozen submission item
            db.Files.Add(new FileEntity
            {
                UploadedBy = s.Scope.Users.Student,
                OriginalFileName = "local-placeholder.pdf",
                StoragePath = "local-placeholder",
                MimeType = "application/pdf",
                FileSizeBytes = 50,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var postLockFile = new FileEntity
            {
                UploadedBy = s.Scope.Users.Student,
                OriginalFileName = "post_lock_evidence.pdf",
                StoragePath = "post_lock_storage",
                MimeType = "application/pdf",
                FileSizeBytes = 100,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Files.Add(postLockFile);
            await db.SaveChangesAsync();
            postLockFileId = postLockFile.Id;

            db.Set<ProjectEvidence>().Add(new ProjectEvidence
            {
                ProjectId = s.ProjectId,
                SourceType = "FILE",
                SourceId = postLockFileId,
                FileId = postLockFileId,
                SubmittedBy = s.Scope.Users.Student,
                SubmittedAt = DateTime.UtcNow,
                VerificationStatus = "PENDING"
            });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        // Evidence endpoint must only contain items from locked FinalSubmission package
        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var evidence = await response.Content.ReadFromJsonAsync<EvaluationAssignmentEvidenceDto>();
        Assert.NotNull(evidence);
        Assert.DoesNotContain(evidence.Items, i => i.FileId == postLockFileId);

        // Attempting to download file outside locked final package must return 404 Not Found
        var downloadResponse = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence/files/{postLockFileId}");
        Assert.Equal(HttpStatusCode.NotFound, downloadResponse.StatusCode);
    }

    [Fact]
    public async Task FrozenProvenance_MajorAttributionImmuneToLiveUserMajorChange()
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, majorA) = await SeedAssignmentAsync(factory, scope: "MAJOR_SPECIFIC");

        // Mutate live student user MajorId in Users table to null
        await using (var db = database.CreateContext())
        {
            var student = await db.Users.SingleAsync(u => u.Id == s.Scope.Users.Student);
            student.MajorId = null;
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        // CASE B: Live Users.MajorId is irrelevant — MAJOR_SPECIFIC fails closed (0 items)
        // because item-level majorId cannot be proven from typed FinalSnapshotFile/ProjectFileDto.
        // This also confirms live user profile mutations cannot influence evidence scope.
        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var evidence = await response.Content.ReadFromJsonAsync<EvaluationAssignmentEvidenceDto>();
        Assert.NotNull(evidence);
        Assert.Empty(evidence.Items);
        Assert.Equal(0, evidence.ItemCount);
    }

    [Fact]
    public async Task FreeTextMetadata_DoesNotGrantIndividualEvidenceAccess()
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, _) = await SeedAssignmentAsync(factory, scope: "INDIVIDUAL");

        // Seed a deliverable with target-looking free text in Title, Description, and FinalSubmission.Notes
        await using (var db = database.CreateContext())
        {
            var final = await db.Set<FinalSubmissionEntity>().Include(f => f.Items).FirstAsync(f => f.ProjectId == s.ProjectId);
            final.Notes = $"{{\"targetStudentId\": {s.Scope.Users.Student}, \"targets\": {{ \"1\": {{ \"studentId\": {s.Scope.Users.Student} }} }} }}";

            var del = new Deliverable
            {
                ProjectId = s.ProjectId,
                Title = $"[student_id: {s.Scope.Users.Student}] Final FreeText Deliverable",
                Description = $"{{\"targetStudentId\": {s.Scope.Users.Student}, \"student_id\": {s.Scope.Users.Student}}}",
                Status = "OPEN",
                CreatedBy = s.Scope.Users.Student
            };
            db.Deliverables.Add(del);
            await db.SaveChangesAsync();

            var ver = new DeliverableVersion
            {
                DeliverableId = del.Id,
                VersionNumber = 1,
                Status = "SUBMITTED",
                SubmittedBy = s.Scope.Users.Student,
                SubmittedAt = DateTime.UtcNow
            };
            db.DeliverableVersions.Add(ver);
            await db.SaveChangesAsync();

            var item = new FinalSubmissionItem
            {
                SubmissionId = final.Id,
                DeliverableId = del.Id,
                DeliverableVersionId = ver.Id,
                Title = del.Title,
                VersionNumber = 1,
                StatusAtSubmission = "SUBMITTED",
                WasRequired = true,
                FilesJson = JsonSerializer.Serialize(new[]
                {
                    new FinalSnapshotFile(new(777L, "DELIVERABLE_VERSION", ver.Id, "freetext.txt", "text/plain", 20L, new string('f', 64), s.Scope.Users.Student, DateTime.UtcNow), "storage-freetext")
                })
            };
            final.Items.Add(item);
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        // Free text in Title, Description, or Notes must NEVER grant evidence authorization -> fails closed (0 items)
        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var evidence = await response.Content.ReadFromJsonAsync<EvaluationAssignmentEvidenceDto>();
        Assert.NotNull(evidence);
        Assert.Equal(0, evidence.ItemCount);
        Assert.Empty(evidence.Items);

        // Download must be rejected with 403 Forbidden
        var dlResponse = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence/files/777");
        Assert.Equal(HttpStatusCode.Forbidden, dlResponse.StatusCode);
    }

    [Fact]
    public async Task DeliverableCreatedBy_DoesNotGrantIndividualEvidenceAccess()
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignmentStudent1, s, _) = await SeedAssignmentAsync(factory, scope: "INDIVIDUAL");

        // The deliverable has CreatedBy = student1 and file has UploadedBy = student1,
        // but NO structured frozen target metadata exists in the typed payload.
        await using (var db = database.CreateContext())
        {
            var final = await db.Set<FinalSubmissionEntity>().Include(f => f.Items).FirstAsync(f => f.ProjectId == s.ProjectId);
            var item = final.Items.First();
            item.FilesJson = JsonSerializer.Serialize(new[]
            {
                new FinalSnapshotFile(new(1, "DELIVERABLE_VERSION", item.DeliverableVersionId, "report.txt", "text/plain", 12, new string('a', 64), s.Scope.Users.Student, DateTime.UtcNow), "fixture-object")
            });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        // Deliverable.CreatedBy alone MUST NOT grant INDIVIDUAL evidence access -> fails closed (0 items)
        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignmentStudent1.Id}/evidence");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var evidence = await response.Content.ReadFromJsonAsync<EvaluationAssignmentEvidenceDto>();
        Assert.NotNull(evidence);
        Assert.Equal(0, evidence.ItemCount);
        Assert.Empty(evidence.Items);

        // Download must be rejected with 403 Forbidden because file is outside this assignment's proven scope
        var dlResponse = await client.GetAsync($"/api/v1/evaluation-assignments/{assignmentStudent1.Id}/evidence/files/1");
        Assert.Equal(HttpStatusCode.Forbidden, dlResponse.StatusCode);
    }

    [Fact]
    public async Task MissingStructuredFrozenProvenance_FailsClosed()
    {
        var storage = new MemoryFileStorage();
        using var factory = new EvidenceTestFactory(database, storage);
        var (assignment, s, _) = await SeedAssignmentAsync(factory, scope: "INDIVIDUAL");

        await using (var db = database.CreateContext())
        {
            var final = await db.Set<FinalSubmissionEntity>().Include(f => f.Items).FirstAsync(f => f.ProjectId == s.ProjectId);
            var del = new Deliverable { ProjectId = s.ProjectId, Title = "Unproven Deliverable", Status = "OPEN", CreatedBy = s.Scope.Users.Student };
            db.Deliverables.Add(del);
            await db.SaveChangesAsync();

            var ver = new DeliverableVersion
            {
                DeliverableId = del.Id,
                VersionNumber = 1,
                Status = "SUBMITTED",
                SubmittedBy = s.Scope.Users.Student,
                SubmittedAt = DateTime.UtcNow
            };
            db.DeliverableVersions.Add(ver);
            await db.SaveChangesAsync();

            var item = new FinalSubmissionItem
            {
                SubmissionId = final.Id,
                DeliverableId = del.Id,
                DeliverableVersionId = ver.Id,
                Title = "Unproven Deliverable",
                VersionNumber = 1,
                StatusAtSubmission = "SUBMITTED",
                WasRequired = true,
                FilesJson = JsonSerializer.Serialize(new[]
                {
                    new FinalSnapshotFile(new(9991L, "DELIVERABLE_VERSION", ver.Id, "unproven.txt", "text/plain", 10L, new string('u', 64), s.Scope.Users.Student, DateTime.UtcNow), "storage-unproven")
                })
            };
            final.Items.Add(item);
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        // Missing structured frozen provenance fails closed: 0 items returned
        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var evidence = await response.Content.ReadFromJsonAsync<EvaluationAssignmentEvidenceDto>();
        Assert.NotNull(evidence);
        Assert.Empty(evidence.Items);
        Assert.Equal(0, evidence.ItemCount);

        // Download is denied with 403 Forbidden
        var dlResponse = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence/files/9991");
        Assert.Equal(HttpStatusCode.Forbidden, dlResponse.StatusCode);
    }
}
