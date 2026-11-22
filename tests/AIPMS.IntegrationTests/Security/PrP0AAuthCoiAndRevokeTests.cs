using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services.Projects;
using AIPMS.IntegrationTests.Evaluations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Security;

public sealed class PrP0AAuthCoiAndRevokeTests(ReviewGateBehaviorFixture fixture) : IClassFixture<ReviewGateBehaviorFixture>
{
    private sealed class StorageStub : IFileStorage
    {
        public Dictionary<string, byte[]> Objects { get; } = [];
        public Task WriteAsync(string storageKey, Stream content, CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            Objects[storageKey] = ms.ToArray();
            return Task.CompletedTask;
        }
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Objects.TryGetValue(storageKey, out var bytes)
                ? Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
                : throw new FileNotFoundException();
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            Objects.Remove(storageKey);
            return Task.CompletedTask;
        }
    }

    private sealed class SecurityFactory(ReviewGateBehaviorFixture db, StorageStub storage) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = db.ConnectionString
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFileStorage>();
                services.AddSingleton<IFileStorage>(storage);
            });
        }
    }

    private async Task<(M.Project Project, ProjectGateInstance Gate, ProjectGateRequirement CommonReq, ProjectGateRequirement ItReq, ProjectGateRequirement MktReq, M.File ItTaskFile, M.File MktTaskFile, M.File CommonReportFile, long StudentId, long MktMentorId, long PendingLecturerId)>
        SeedInterdisciplinaryScopeAsync(StorageStub storage)
    {
        var s = fixture.Scenario;
        await using var db = fixture.CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var lecturerRole = await db.Roles.SingleAsync(r => r.Code == AppRoles.Lecturer);

        var student = new M.User
        {
            Email = $"stu-p0a-{suffix}@example.test",
            FullName = $"Student {suffix}",
            Status = "ACTIVE",
            AcademicProfileStatus = "VERIFIED",
            PasswordHash = "unused",
            DepartmentId = s.ItDeptId,
            MajorId = s.SeMajorId,
            UserRoleUsers = [new() { RoleId = s.StudentRoleId }]
        };
        var mktMentor = new M.User
        {
            Email = $"mkt-mentor-{suffix}@example.test",
            FullName = $"MKT Mentor {suffix}",
            Status = "ACTIVE",
            AcademicProfileStatus = "VERIFIED",
            PasswordHash = "unused",
            DepartmentId = s.BusDeptId,
            UserRoleUsers = [new() { RoleId = lecturerRole.Id }]
        };
        var pendingLecturer = new M.User
        {
            Email = $"pending-lec-{suffix}@example.test",
            FullName = $"Pending Lecturer {suffix}",
            Status = "ACTIVE",
            AcademicProfileStatus = "VERIFIED",
            PasswordHash = "unused",
            DepartmentId = s.ItDeptId,
            UserRoleUsers = [new() { RoleId = lecturerRole.Id }]
        };
        var mktMentorProfile = new M.SupervisorProfile { User = mktMentor, IsAvailable = true, MaxActiveProjects = 3 };
        var pendingProfile = new M.SupervisorProfile { User = pendingLecturer, IsAvailable = true, MaxActiveProjects = 3 };
        db.AddRange(student, mktMentor, pendingLecturer, mktMentorProfile, pendingProfile);
        await db.SaveChangesAsync();

        var team = new M.Team
        {
            AcademicSemesterId = s.SemesterId,
            Code = $"TM-P0A-{suffix}",
            Name = $"Team {suffix}",
            Status = "LOCKED",
            CreatedBy = student.Id,
            TeamMembers = [new() { AcademicSemesterId = s.SemesterId, UserId = student.Id, IsLeader = true }]
        };
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        var project = new M.Project
        {
            TeamId = team.Id,
            Code = $"PRJ-P0A-{suffix}",
            Title = $"Interdisciplinary {suffix}",
            Status = "ACTIVE",
            CreatedBy = student.Id,
            RowVersion = [1],
            ProjectMajors =
            [
                new() { MajorId = s.SeMajorId },
                new() { MajorId = s.MktMajorId }
            ]
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var snapshotPayload = JsonSerializer.Serialize(new
        {
            DepartmentIds = new[] { s.ItDeptId, s.BusDeptId },
            Scope = new
            {
                ProjectMode = "INTERDISCIPLINARY",
                LeadDepartmentId = s.ItDeptId,
                Requirements = new[]
                {
                    new { MajorId = s.SeMajorId, DepartmentId = s.ItDeptId, MinMembers = 1 },
                    new { MajorId = s.MktMajorId, DepartmentId = s.BusDeptId, MinMembers = 1 }
                }
            },
            MajorDepartmentIds = new Dictionary<long, long>
            {
                [s.SeMajorId] = s.ItDeptId,
                [s.MktMajorId] = s.BusDeptId
            },
            Members = new[] { new { UserId = student.Id, MajorId = s.SeMajorId, IsLeader = true } }
        });
        db.Set<ProjectRegistrationSnapshot>().Add(new ProjectRegistrationSnapshot
        {
            ProjectId = project.Id,
            ProjectPeriodId = s.PeriodId,
            LeadDepartmentId = s.ItDeptId,
            SnapshotJson = snapshotPayload,
            SubmittedBy = student.Id,
            SubmittedAt = DateTime.UtcNow
        });

        // Primary supervisor = Lecturer1; Discipline mentor (MKT) = mktMentor; Pending request = pendingLecturer
        var primaryReq = new M.SupervisorRequest
        {
            ProjectId = project.Id, SupervisorProfileId = s.Lecturer1ProfileId, RequestedBy = student.Id,
            Status = "ACCEPTED", AssignmentType = "PRIMARY", RequestedAt = DateTime.UtcNow, RespondedAt = DateTime.UtcNow
        };
        var mentorReq = new M.SupervisorRequest
        {
            ProjectId = project.Id, SupervisorProfileId = mktMentorProfile.Id, RequestedBy = student.Id,
            Status = "ACCEPTED", AssignmentType = "DISCIPLINE_MENTOR", MajorId = s.MktMajorId,
            RequestedAt = DateTime.UtcNow, RespondedAt = DateTime.UtcNow
        };
        var pendingReq = new M.SupervisorRequest
        {
            ProjectId = project.Id, SupervisorProfileId = pendingProfile.Id, RequestedBy = student.Id,
            Status = "PENDING", AssignmentType = "DISCIPLINE_MENTOR", MajorId = s.SeMajorId,
            RequestedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(5)
        };
        db.AddRange(primaryReq, mentorReq, pendingReq);
        await db.SaveChangesAsync();

        db.SupervisorAssignments.AddRange(
            new M.SupervisorAssignment
            {
                ProjectId = project.Id, SupervisorProfileId = s.Lecturer1ProfileId, SupervisorRequestId = primaryReq.Id,
                IsPrimary = true, AssignmentType = "PRIMARY", AssignedAt = DateTime.UtcNow.AddHours(-1)
            },
            new M.SupervisorAssignment
            {
                ProjectId = project.Id, SupervisorProfileId = mktMentorProfile.Id, SupervisorRequestId = mentorReq.Id,
                IsPrimary = false, AssignmentType = "DISCIPLINE_MENTOR", MajorId = s.MktMajorId, AssignedAt = DateTime.UtcNow.AddHours(-1)
            });

        // Files attached to IT task, MKT task, and COMMON report
        var milestone = new M.Milestone
        {
            ProjectId = project.Id, Title = "M1", Status = "IN_PROGRESS", CreatedBy = student.Id,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Milestones.Add(milestone);
        await db.SaveChangesAsync();

        var itTask = new M.Task
        {
            MilestoneId = milestone.Id, Title = "IT Backend Task", Status = "IN_PROGRESS", Priority = "HIGH",
            CreatedBy = student.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, ConcurrencyToken = Guid.NewGuid()
        };
        var mktTask = new M.Task
        {
            MilestoneId = milestone.Id, Title = "MKT Campaign Task", Status = "IN_PROGRESS", Priority = "HIGH",
            CreatedBy = student.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, ConcurrencyToken = Guid.NewGuid()
        };
        var report = new M.ProgressReport
        {
            ProjectId = project.Id, ReportType = "WEEKLY", Status = "DRAFT", PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
            PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow), Summary = "Common weekly", SubmittedBy = student.Id,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.AddRange(itTask, mktTask, report);
        await db.SaveChangesAsync();

        db.Set<M.TaskDiscipline>().AddRange(
            new M.TaskDiscipline { TaskId = itTask.Id, MajorId = s.SeMajorId, Role = "PRIMARY", CreatedBy = student.Id, CreatedAt = DateTime.UtcNow },
            new M.TaskDiscipline { TaskId = mktTask.Id, MajorId = s.MktMajorId, Role = "PRIMARY", CreatedBy = student.Id, CreatedAt = DateTime.UtcNow });

        var itKey = Guid.NewGuid().ToString("N");
        var mktKey = Guid.NewGuid().ToString("N");
        var commonKey = Guid.NewGuid().ToString("N");
        storage.Objects[itKey] = "%PDF-1.7\nIT-SECRET"u8.ToArray();
        storage.Objects[mktKey] = "%PDF-1.7\nMKT-DATA"u8.ToArray();
        storage.Objects[commonKey] = "%PDF-1.7\nCOMMON-DATA"u8.ToArray();

        var itFile = new M.File
        {
            TaskId = itTask.Id, UploadedBy = student.Id, OriginalFileName = "it-architecture.pdf",
            StoredFileName = itKey, StoragePath = itKey, MimeType = "application/pdf", FileSizeBytes = storage.Objects[itKey].Length,
            ChecksumSha256 = new string('a', 64), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        var mktFile = new M.File
        {
            TaskId = mktTask.Id, UploadedBy = student.Id, OriginalFileName = "mkt-plan.pdf",
            StoredFileName = mktKey, StoragePath = mktKey, MimeType = "application/pdf", FileSizeBytes = storage.Objects[mktKey].Length,
            ChecksumSha256 = new string('b', 64), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        var commonFile = new M.File
        {
            ProgressReportId = report.Id, UploadedBy = student.Id, OriginalFileName = "common-weekly.pdf",
            StoredFileName = commonKey, StoragePath = commonKey, MimeType = "application/pdf", FileSizeBytes = storage.Objects[commonKey].Length,
            ChecksumSha256 = new string('c', 64), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Files.AddRange(itFile, mktFile, commonFile);

        // Gate with COMMON, IT MAJOR_SPECIFIC, and MKT MAJOR_SPECIFIC requirements + submitted evidence
        var itGateKey = Guid.NewGuid().ToString("N");
        var mktGateKey = Guid.NewGuid().ToString("N");
        storage.Objects[itGateKey] = "%PDF-1.7\nIT-GATE"u8.ToArray();
        storage.Objects[mktGateKey] = "%PDF-1.7\nMKT-GATE"u8.ToArray();

        var commonReq = new ProjectGateRequirement
        {
            Code = "G1-COM", Title = "Project Charter", Type = "DOCUMENT", Scope = "COMMON", Required = true, VerificationStatus = "SUBMITTED",
            Evidence = [new GateEvidence { ProjectId = project.Id, Version = 1, ArtifactName = "charter.pdf", ContentType = "application/pdf", StorageKey = commonKey, SubmittedBy = student.Id, SubmittedAt = DateTime.UtcNow, Status = "SUBMITTED" }]
        };
        var itReq = new ProjectGateRequirement
        {
            Code = "G1-IT", Title = "IT SRS Secret", Type = "EXTERNAL_LINK", Scope = "MAJOR_SPECIFIC", MajorId = s.SeMajorId, Required = true, VerificationStatus = "SUBMITTED",
            Evidence = [new GateEvidence { ProjectId = project.Id, Version = 1, ArtifactName = "srs.pdf", ContentType = "application/pdf", StorageKey = itGateKey, LinkUrl = "https://it.example.invalid/secret-srs", SubmittedBy = student.Id, SubmittedAt = DateTime.UtcNow, Status = "SUBMITTED" }]
        };
        var mktReq = new ProjectGateRequirement
        {
            Code = "G1-MKT", Title = "MKT Brief", Type = "DOCUMENT", Scope = "MAJOR_SPECIFIC", MajorId = s.MktMajorId, Required = true, VerificationStatus = "SUBMITTED",
            Evidence = [new GateEvidence { ProjectId = project.Id, Version = 1, ArtifactName = "mkt-brief.pdf", ContentType = "application/pdf", StorageKey = mktGateKey, SubmittedBy = student.Id, SubmittedAt = DateTime.UtcNow, Status = "SUBMITTED" }]
        };
        var gate = new ProjectGateInstance
        {
            ProjectId = project.Id, TemplateId = s.DefaultTemplateId, TemplateVersion = 1,
            Code = "G1", Sequence = 1, Title = "Gate 1", Objective = "G1", Status = "IN_REVIEW",
            ReviewEligibility = "READY", Required = true, BlockNextGate = true, BlocksDefense = false, Weight = 100m,
            CreatedAt = DateTime.UtcNow, ConcurrencyToken = Guid.NewGuid(),
            Requirements = [commonReq, itReq, mktReq]
        };
        db.ProjectGateInstances.Add(gate);
        await db.SaveChangesAsync();

        return (project, gate, commonReq, itReq, mktReq, itFile, mktFile, commonFile, student.Id, mktMentor.Id, pendingLecturer.Id);
    }

    [Fact]
    public async Task TEST_P0_02_Mentor_and_pending_supervisor_cannot_access_out_of_scope_files_or_gate_timeline()
    {
        var storage = new StorageStub();
        var seeded = await SeedInterdisciplinaryScopeAsync(storage);
        using var app = new SecurityFactory(fixture, storage);
        using var mktMentor = app.CreateAuthenticatedClient(seeded.MktMentorId, roles: [AppRoles.Lecturer]);
        using var primary = app.CreateAuthenticatedClient(fixture.Scenario.Lecturer1Id, roles: [AppRoles.Lecturer]);
        using var pending = app.CreateAuthenticatedClient(seeded.PendingLecturerId, roles: [AppRoles.Lecturer]);

        // 1. Pending SupervisorRequest MUST be blocked 403 on files list & file download.
        var pendingFiles = await pending.GetAsync($"/api/v1/projects/{seeded.Project.Id}/files");
        Assert.Equal(HttpStatusCode.Forbidden, pendingFiles.StatusCode);
        var pendingDownload = await pending.GetAsync($"/api/v1/files/{seeded.CommonReportFile.Id}/download");
        Assert.Equal(HttpStatusCode.Forbidden, pendingDownload.StatusCode);

        // 2. MKT Mentor lists files: sees MKT + COMMON, NEVER sees IT task file.
        var mentorFilesRes = await mktMentor.GetAsync($"/api/v1/projects/{seeded.Project.Id}/files");
        Assert.Equal(HttpStatusCode.OK, mentorFilesRes.StatusCode);
        var mentorFiles = (await mentorFilesRes.Content.ReadFromJsonAsync<PagedResult<ProjectFileDto>>())!;
        Assert.Equal(2, mentorFiles.TotalCount);
        Assert.DoesNotContain(mentorFiles.Items, f => f.Id == seeded.ItTaskFile.Id);
        Assert.Contains(mentorFiles.Items, f => f.Id == seeded.MktTaskFile.Id);
        Assert.Contains(mentorFiles.Items, f => f.Id == seeded.CommonReportFile.Id);

        // 3. MKT Mentor direct download of IT file -> 403 SCOPE_ACCESS_DENIED; MKT file -> 200 OK.
        var itDownload = await mktMentor.GetAsync($"/api/v1/files/{seeded.ItTaskFile.Id}/download");
        Assert.Equal(HttpStatusCode.Forbidden, itDownload.StatusCode);
        Assert.Contains(WorkflowErrorCodes.ScopeAccessDenied, await itDownload.Content.ReadAsStringAsync());

        var mktDownload = await mktMentor.GetAsync($"/api/v1/files/{seeded.MktTaskFile.Id}/download");
        Assert.Equal(HttpStatusCode.OK, mktDownload.StatusCode);

        // 4. MKT Mentor reads Gate Timeline: IT requirement metadata & link URL are completely excluded.
        var timelineRes = await mktMentor.GetAsync($"/api/v1/projects/{seeded.Project.Id}/review-gates/timeline");
        Assert.Equal(HttpStatusCode.OK, timelineRes.StatusCode);
        var rawTimeline = await timelineRes.Content.ReadAsStringAsync();
        Assert.DoesNotContain("IT SRS Secret", rawTimeline);
        Assert.DoesNotContain("secret-srs", rawTimeline);
        Assert.Contains("MKT Brief", rawTimeline);
        Assert.Contains("Project Charter", rawTimeline);

        // 5. MKT Mentor direct download of IT gate evidence -> 403 SCOPE_ACCESS_DENIED; MKT gate evidence -> 200 OK.
        var itGateDownload = await mktMentor.GetAsync($"/api/v1/projects/{seeded.Project.Id}/review-gates/G1/requirements/{seeded.ItReq.Id}/evidence/1/download");
        Assert.Equal(HttpStatusCode.Forbidden, itGateDownload.StatusCode);
        Assert.Contains(WorkflowErrorCodes.ScopeAccessDenied, await itGateDownload.Content.ReadAsStringAsync());

        var mktGateDownload = await mktMentor.GetAsync($"/api/v1/projects/{seeded.Project.Id}/review-gates/G1/requirements/{seeded.MktReq.Id}/evidence/1/download");
        Assert.Equal(HttpStatusCode.OK, mktGateDownload.StatusCode);

        // 6. Positive: Primary Supervisor sees ALL 3 files and ALL 3 gate requirements.
        var primaryFiles = (await (await primary.GetAsync($"/api/v1/projects/{seeded.Project.Id}/files"))
            .Content.ReadFromJsonAsync<PagedResult<ProjectFileDto>>())!;
        Assert.Equal(3, primaryFiles.TotalCount);
        var primaryTimeline = await (await primary.GetAsync($"/api/v1/projects/{seeded.Project.Id}/review-gates/timeline")).Content.ReadAsStringAsync();
        Assert.Contains("IT SRS Secret", primaryTimeline);
        Assert.Contains("MKT Brief", primaryTimeline);
    }

    [Fact]
    public async Task TEST_P0_01_Former_primary_and_bidirectional_gate_reviewer_return_409_EVALUATOR_ROLE_CONFLICT()
    {
        var storage = new StorageStub();
        var seeded = await SeedInterdisciplinaryScopeAsync(storage);
        var s = fixture.Scenario;

        // End Lecturer1's PRIMARY assignment so Lecturer1 is now a FORMER Primary Supervisor.
        await using (var db = fixture.CreateContext())
        {
            var primaryAssignment = await db.SupervisorAssignments.SingleAsync(a => a.ProjectId == seeded.Project.Id && a.IsPrimary);
            primaryAssignment.EndedAt = DateTime.UtcNow.AddMinutes(-5);
            primaryAssignment.EndReason = "Mid-semester replacement";
            await db.SaveChangesAsync();

            // 1. Extended D17: Former Primary CANNOT become Evaluator (409 EVALUATOR_ROLE_CONFLICT).
            await using var tx = await db.Database.BeginTransactionAsync();
            var evalEx = await Assert.ThrowsAsync<ConflictException>(() =>
                AcademicAssignmentGuard.EnsureEvaluatorAsync(db, seeded.Project.Id, s.Lecturer1Id, "LECTURER", default));
            Assert.Equal(WorkflowErrorCodes.EvaluatorRoleConflict, evalEx.Code);
            await tx.RollbackAsync();
        }

        // 2. Extended D17: Former Primary CANNOT be assigned as Gate Reviewer (409 EVALUATOR_ROLE_CONFLICT).
        using var app = new SecurityFactory(fixture, storage);
        using var staff = app.CreateAuthenticatedClient(s.StaffId, roles: [AppRoles.DepartmentStaff]);
        var assignGateRes = await staff.PostAsJsonAsync("/api/v1/review-gates/assignments",
            new CreateReviewGateAssignmentRequest(seeded.Project.Id.ToString(), "G1", s.Lecturer1Id, "COMMON", null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, assignGateRes.StatusCode);
        Assert.Contains(WorkflowErrorCodes.EvaluatorRoleConflict, await assignGateRes.Content.ReadAsStringAsync());

        // 3. Bidirectional D17: Active Gate Reviewer (Lecturer2) CANNOT become Primary Supervisor (409 EVALUATOR_ROLE_CONFLICT).
        var validAssignRes = await staff.PostAsJsonAsync("/api/v1/review-gates/assignments",
            new CreateReviewGateAssignmentRequest(seeded.Project.Id.ToString(), "G1", s.Lecturer2Id, "COMMON", null, null, null));
        Assert.Equal(HttpStatusCode.OK, validAssignRes.StatusCode);

        await using (var db = fixture.CreateContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var primaryEx = await Assert.ThrowsAsync<ConflictException>(() =>
                AcademicAssignmentGuard.EnsurePrimaryAsync(db, seeded.Project.Id, s.Lecturer2ProfileId, default));
            Assert.Equal(WorkflowErrorCodes.EvaluatorRoleConflict, primaryEx.Code);
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task TEST_P0_04_Expired_and_revoked_gate_assignments_return_403_ASSIGNMENT_REVOKED()
    {
        var storage = new StorageStub();
        var seeded = await SeedInterdisciplinaryScopeAsync(storage);
        var s = fixture.Scenario;
        var now = DateTime.UtcNow;

        long expiredAssignId;
        string expiredToken;
        await using (var db = fixture.CreateContext())
        {
            var expired = new GateReviewerAssignment
            {
                GateInstanceId = seeded.Gate.Id,
                ReviewerId = s.Lecturer2Id,
                Scope = "COMMON",
                Status = "PENDING_REVIEW",
                ValidFrom = now.AddDays(-5),
                ValidUntil = now.AddMinutes(-1),
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAt = now.AddDays(-5)
            };
            db.GateReviewerAssignments.Add(expired);
            await db.SaveChangesAsync();
            expiredAssignId = expired.Id;
            expiredToken = expired.ConcurrencyToken.ToString("N");
        }

        using var app = new SecurityFactory(fixture, storage);
        using var reviewer = app.CreateAuthenticatedClient(s.Lecturer2Id, roles: [AppRoles.Lecturer]);

        // 1. Expired GateReviewerAssignment -> VerifyEvidence returns 403 ASSIGNMENT_REVOKED.
        var verifyRes = await reviewer.PostAsJsonAsync($"/api/v1/review-gates/assignments/{expiredAssignId}/evidence/{seeded.CommonReq.Id}/verify",
            new VerifyReviewGateEvidenceRequest(seeded.CommonReq.Id.ToString(), "ACCEPTED", "Too late", expiredToken));
        Assert.Equal(HttpStatusCode.Forbidden, verifyRes.StatusCode);
        Assert.Contains(WorkflowErrorCodes.AssignmentRevoked, await verifyRes.Content.ReadAsStringAsync());

        // 2. Expired GateReviewerAssignment -> Decide returns 403 ASSIGNMENT_REVOKED.
        var decideRes = await reviewer.PostAsJsonAsync($"/api/v1/review-gates/assignments/{expiredAssignId}/decision",
            new SubmitReviewGateDecisionRequest("PASSED", "Too late", expiredToken));
        Assert.Equal(HttpStatusCode.Forbidden, decideRes.StatusCode);
        Assert.Contains(WorkflowErrorCodes.AssignmentRevoked, await decideRes.Content.ReadAsStringAsync());

        // 3. Expired GateReviewerAssignment -> DownloadEvidence returns 403 ASSIGNMENT_REVOKED.
        var downloadRes = await reviewer.GetAsync($"/api/v1/projects/{seeded.Project.Id}/review-gates/G1/requirements/{seeded.CommonReq.Id}/evidence/1/download");
        Assert.Equal(HttpStatusCode.Forbidden, downloadRes.StatusCode);
        Assert.Contains(WorkflowErrorCodes.AssignmentRevoked, await downloadRes.Content.ReadAsStringAsync());

        // 4. Positive: extending ValidUntil into the future allows VerifyEvidence & DownloadEvidence immediately.
        await using (var db = fixture.CreateContext())
        {
            var row = await db.GateReviewerAssignments.SingleAsync(x => x.Id == expiredAssignId);
            row.ValidUntil = DateTime.UtcNow.AddHours(2);
            await db.SaveChangesAsync();
        }
        var okDownload = await reviewer.GetAsync($"/api/v1/projects/{seeded.Project.Id}/review-gates/G1/requirements/{seeded.CommonReq.Id}/evidence/1/download");
        Assert.Equal(HttpStatusCode.OK, okDownload.StatusCode);
        var okVerify = await reviewer.PostAsJsonAsync($"/api/v1/review-gates/assignments/{expiredAssignId}/evidence/{seeded.CommonReq.Id}/verify",
            new VerifyReviewGateEvidenceRequest(seeded.CommonReq.Id.ToString(), "ACCEPTED", "Within window", expiredToken));
        Assert.Equal(HttpStatusCode.OK, okVerify.StatusCode);

        // 5. Framework 401 returns structured additive RFC ProblemDetails with code + correlationId + message.
        using var anonymous = app.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-anon-401");
        var unauthRes = await anonymous.GetAsync($"/api/v1/projects/{seeded.Project.Id}/files");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);
        using var unauthJson = JsonDocument.Parse(await unauthRes.Content.ReadAsStringAsync());
        Assert.Equal(WorkflowErrorCodes.Unauthorized, unauthJson.RootElement.GetProperty("code").GetString());
        Assert.Equal("corr-anon-401", unauthJson.RootElement.GetProperty("correlationId").GetString());
        Assert.True(unauthJson.RootElement.TryGetProperty("message", out _));
        Assert.False(unauthJson.RootElement.TryGetProperty("fieldErrors", out _));
    }
}
