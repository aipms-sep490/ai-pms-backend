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

        // Add a second deliverable version to final submission tied to Major B
        var final = await db.Set<FinalSubmissionEntity>().Include(f => f.Items).FirstAsync(f => f.ProjectId == s.ProjectId);
        var delB = new Deliverable { ProjectId = s.ProjectId, Title = "Major B Deliverable", Status = "OPEN", CreatedBy = s.Scope.Users.Student };
        db.Deliverables.Add(delB);
        await db.SaveChangesAsync();

        var verB = new DeliverableVersion
        {
            DeliverableId = delB.Id,
            VersionNumber = 1,
            Status = "SUBMITTED",
            SubmittedBy = s.Scope.Users.Student,
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
            FilesJson = JsonSerializer.Serialize(new[]
            {
                new FinalSnapshotFile(new(902, "DELIVERABLE_VERSION", verB.Id, "major_b.txt", "text/plain", 10, new string('b', 64), s.Scope.Users.Student, DateTime.UtcNow), "storage-b")
            })
        };
        final.Items.Add(itemB);

        // Add project evidence mappings: Deliverable 1 -> Major A, Deliverable B -> Major B
        var existingItem = final.Items.First(i => i.DeliverableId != delB.Id);
        db.Set<ProjectEvidence>().AddRange(
            new ProjectEvidence { ProjectId = s.ProjectId, SourceType = "DELIVERABLE", SourceId = existingItem.DeliverableId, DeliverableId = existingItem.DeliverableId, MajorId = majorA, VerificationStatus = "PENDING", SubmittedBy = s.Scope.Users.Student, SubmittedAt = DateTime.UtcNow },
            new ProjectEvidence { ProjectId = s.ProjectId, SourceType = "DELIVERABLE", SourceId = delB.Id, DeliverableId = delB.Id, MajorId = majorB.Id, VerificationStatus = "PENDING", SubmittedBy = s.Scope.Users.Student, SubmittedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var evidence = await response.Content.ReadFromJsonAsync<EvaluationAssignmentEvidenceDto>();
        Assert.NotNull(evidence);
        Assert.Single(evidence.Items);
        Assert.Equal(majorA, evidence.Items[0].MajorId);
        Assert.DoesNotContain(evidence.Items, i => i.MajorId == majorB.Id);

        // Attempting to download Major B file using Major A's assignment is forbidden
        var downloadResponse = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence/files/902");
        Assert.Equal(HttpStatusCode.Forbidden, downloadResponse.StatusCode);
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
            FilesJson = JsonSerializer.Serialize(new[]
            {
                new FinalSnapshotFile(new(802, "DELIVERABLE_VERSION", ver2.Id, "student2.txt", "text/plain", 15, new string('c', 64), student2.Id, DateTime.UtcNow), "storage-student2")
            })
        };
        final.Items.Add(item2);
        await db.SaveChangesAsync();

        using var client = factory.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        var response = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var evidence = await response.Content.ReadFromJsonAsync<EvaluationAssignmentEvidenceDto>();
        Assert.NotNull(evidence);
        Assert.All(evidence.Items, item => Assert.Equal(s.Scope.Users.Student, item.StudentId));
        Assert.DoesNotContain(evidence.Items, i => i.StudentId == student2.Id);

        // Attempting to download Student 2's file using Student 1's assignment is forbidden
        var downloadResponse = await client.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence/files/802");
        Assert.Equal(HttpStatusCode.Forbidden, downloadResponse.StatusCode);
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
}
