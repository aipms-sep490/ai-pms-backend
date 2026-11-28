using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.Models;
using AIPMS.Application.Features.Cold;
using AIPMS.Application.Features.Cold.Abstractions;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Application.Features.ReviewGates.Abstractions;
using AIPMS.Application.Features.Topics.Commands;
using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services;
using AIPMS.Infrastructure.Services.Projects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.UnitTests.AI;

public sealed class PreSemesterPhase2SetupTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public void Parsers_NormalizeAndValidate_ReviewGate_TopicBatch_AndColdTemplatePlans()
    {
        using var gateDoc = JsonDocument.Parse("""
        {
          "code": "gate-se-2026!",
          "name": "Khung thẩm định SE 2026",
          "mode": "SINGLE_MAJOR",
          "majorCode": "SE",
          "summary": "G1-G2 chuẩn",
          "gates": [
            { "code": "g1", "sequence": 1, "title": "Cổng 1", "objective": "Chốt SRS", "weight": 40, "startDate": "2026-09-10", "endDate": "2026-09-20", "required": true, "blockNextGate": true, "blocksDefense": false, "reviewerPolicy": "SUPERVISOR", "reviewScope": "COMMON", "requirements": [{ "code": "r1", "title": "SRS", "type": "FILE", "scope": "COMMON", "verification": "HUMAN", "required": true }] },
            { "code": "g2", "sequence": 2, "title": "Cổng 2", "objective": "Nghiệm thu", "weight": 60, "required": true, "blocksDefense": true, "requirements": [] }
          ]
        }
        """);
        var gatePlan = AssistantSetupPlanParser.ParseReviewGateSetup(gateDoc.RootElement);
        Assert.NotNull(gatePlan);
        Assert.Equal("GATE-SE-2026_", gatePlan!.Code);
        Assert.Equal(2, gatePlan.Gates.Count);
        Assert.Single(gatePlan.Gates[1].Requirements); // auto-added default requirement

        using var topicDoc = JsonDocument.Parse("""
        {
          "summary": "2 đề tài trích xuất",
          "topics": [
            { "code": "top 01", "title": "Hệ thống quản lý học vụ AI", "domain": "EdTech", "technologies": ["React", ".NET"], "keywords": ["AI", "EdTech"], "projectMode": "SINGLE_MAJOR" }
          ]
        }
        """);
        var topicPlan = AssistantSetupPlanParser.ParseTopicBatchSetup(topicDoc.RootElement);
        Assert.NotNull(topicPlan);
        var t = Assert.Single(topicPlan!.Topics);
        Assert.Equal("TOP_01", t.Code);
        Assert.Equal("SINGLE_MAJOR", t.ProjectMode);

        using var coldDoc = JsonDocument.Parse("""
        {
          "name": "Hồ sơ chấm nguội Marketing",
          "scope": "MAJOR_SPECIFIC",
          "majorName": "Marketing",
          "reports": [
            { "code": "market research", "name": "Nghiên cứu thị trường", "required": false },
            { "code": "market research", "name": "Báo cáo chiến dịch", "required": false }
          ]
        }
        """);
        var coldPlan = AssistantSetupPlanParser.ParseColdTemplateSetup(coldDoc.RootElement);
        Assert.NotNull(coldPlan);
        Assert.Equal(2, coldPlan!.Reports.Count);
        Assert.Equal("MARKET_RESEARCH", coldPlan.Reports[0].Code);
        Assert.Equal("MARKET_RESEARCH_2", coldPlan.Reports[1].Code);
        Assert.Contains(coldPlan.Reports, r => r.Required);
    }

    [Fact]
    public async Task KnowledgeExtractPreview_ExtractsFromUploadedFile_AndEnforcesDepartmentScopeForStoredDocs()
    {
        await using var db = SeedAcademicDb();
        var storage = new InMemoryFileStorage();
        var docBytes = Encoding.UTF8.GetBytes("[Trang 1]\n# Quy trình đăng ký đồ án\nSinh viên đăng ký nhóm gồm 3-5 thành viên trước tuần 3.\n[Trang 2]\n# Điều kiện bảo vệ\nNhóm phải đạt toàn bộ Review Gates G1 đến G3.");
        await storage.WriteAsync("dept-doc-key-1", new MemoryStream(docBytes), Ct);

        db.Set<DepartmentDocumentTemplate>().Add(new DepartmentDocumentTemplate
        {
            Id = 501,
            DepartmentId = 10,
            MajorId = 100,
            Code = "REG_2026",
            Title = "Quy định đồ án khoa CNTT 2026",
            Version = 1,
            Status = "PUBLISHED",
            FileName = "quy-dinh-2026.md",
            ContentType = "text/markdown",
            SizeBytes = docBytes.Length,
            Sha256 = "abc",
            StorageKey = "dept-doc-key-1",
            UploadedBy = 1,
            UploadedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        });
        await db.SaveChangesAsync(Ct);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiAssistant:KnowledgeEnabled"] = "true"
        }).Build();

        // Same-department staff can extract directly from departmentDocumentId
        var staffSvc = new AcademicKnowledgeService(db, new TestUser(1, ["DEPARTMENT_STAFF"]), new TestAudit(),
            new LocalStubProvider(), new EmptyContextRetriever(), new FixedTestClock(), config, null, storage);
        var preview = await staffSvc.ExtractKnowledgePreviewAsync(null, 501, Ct);
        Assert.Equal("quy-dinh-2026.md", preview.SourceFileName);
        Assert.Equal("Quy định đồ án khoa CNTT 2026", preview.SuggestedTitle);
        Assert.True(preview.Chunks.Count >= 2);
        Assert.False(preview.UsedAi);

        // Other-department staff (user 3 in dept 20) is blocked from reading dept 10 document
        var otherDeptSvc = new AcademicKnowledgeService(db, new TestUser(3, ["DEPARTMENT_STAFF"]), new TestAudit(),
            new LocalStubProvider(), new EmptyContextRetriever(), new FixedTestClock(), config, null, storage);
        await Assert.ThrowsAsync<ForbiddenException>(() => otherDeptSvc.ExtractKnowledgePreviewAsync(null, 501, Ct));
    }

    [Fact]
    public async Task ApplyReviewGateSetup_EnforcesDepartmentStaffRbac_CreatesDraft_AndReplaysIdempotently()
    {
        await using var db = SeedAcademicDb();
        var audit = new TestAudit();
        var clock = new FixedTestClock();
        var fakeGates = new FakeReviewGateService();
        var key = Guid.NewGuid();
        var req = new ApplyReviewGateSetupRequest(key, 12, 1000, "GATE_SE_2026", "Cổng SE 2026", "SINGLE_MAJOR", "SE",
        [
            new("G1", 1, "Cổng 1", "Chốt đặc tả", 40m, "2026-09-10", "2026-09-20", true, true, false, "COUNCIL", "COMMON",
                [new("G1_SRS", "SRS", null, "FILE", "COMMON", null, "HUMAN", true)]),
            new("G2", 2, "Cổng 2", "Bảo vệ", 60m, "2026-10-10", "2026-11-10", true, false, true, "COUNCIL", "COMMON",
                [new("G2_FINAL", "Báo cáo cuối kỳ", null, "FILE", "COMMON", null, "HUMAN", true)])
        ]);

        // Pure ADMIN (user 2) must be rejected
        var adminSvc = new ProjectAiSetupService(db, new TestUser(2, ["ADMIN"]), new AllowGuard(), new StubTopicSender(), audit, clock, fakeGates, new FakeColdService());
        await Assert.ThrowsAsync<ForbiddenException>(() => adminSvc.ApplyReviewGateSetupAsync(req, Ct));
        Assert.Equal(0, fakeGates.SaveCount);

        // DEPARTMENT_STAFF (user 1) succeeds and replays idempotently
        var staffSvc = new ProjectAiSetupService(db, new TestUser(1, ["DEPARTMENT_STAFF"]), new AllowGuard(), new StubTopicSender(), audit, clock, fakeGates, new FakeColdService());

        // V4.0 QĐ-09: SUPERVISOR cannot be gate reviewer policy
        var badReviewerReq = req with
        {
            IdempotencyKey = Guid.NewGuid(),
            Gates =
            [
                req.Gates[0] with { ReviewerPolicy = "SUPERVISOR" },
                req.Gates[1]
            ]
        };
        await Assert.ThrowsAsync<ArgumentException>(() => staffSvc.ApplyReviewGateSetupAsync(badReviewerReq, Ct));

        // V4.0: Fixed G1-G3 maximum (4 gates rejected)
        var fourGatesReq = req with
        {
            IdempotencyKey = Guid.NewGuid(),
            Gates =
            [
                req.Gates[0] with { Weight = 20m },
                req.Gates[1] with { Code = "G2", Sequence = 2, Weight = 25m, BlocksDefense = false },
                req.Gates[1] with { Code = "G3", Sequence = 3, Weight = 30m, BlocksDefense = false },
                req.Gates[1] with { Code = "G4", Sequence = 4, Weight = 25m, BlocksDefense = true }
            ]
        };
        await Assert.ThrowsAsync<ArgumentException>(() => staffSvc.ApplyReviewGateSetupAsync(fourGatesReq, Ct));

        var first = await staffSvc.ApplyReviewGateSetupAsync(req, Ct);
        Assert.False(first.Replay);
        Assert.Equal("DRAFT", first.Status);
        Assert.Equal(2, first.GatesCount);
        Assert.Equal(1, fakeGates.SaveCount);

        var second = await staffSvc.ApplyReviewGateSetupAsync(req, Ct);
        Assert.True(second.Replay);
        Assert.Equal(first.TemplateId, second.TemplateId);
        Assert.Equal(1, fakeGates.SaveCount);
    }

    [Fact]
    public async Task ApplyTopicBatchSetup_CreatesValidDrafts_SkipsConflicts_AndReplaysIdempotently()
    {
        await using var db = SeedAcademicDb();
        var audit = new TestAudit();
        var clock = new FixedTestClock();
        var sender = new StubTopicSender(rejectCode: "DUP_CODE");
        var svc = new ProjectAiSetupService(db, new TestUser(1, ["DEPARTMENT_STAFF"]), new AllowGuard(), sender, audit, clock, new FakeReviewGateService(), new FakeColdService());

        var req = new ApplyTopicBatchSetupRequest(Guid.NewGuid(), 15, 1000, 10,
        [
            new("TOP_1", "CAP_01", "Hệ thống gợi ý lộ trình học tập", "Mô tả", "Vấn đề", "Mục tiêu", "Web", "EdTech", ["React"], ["AI"], "SINGLE_MAJOR", 100, "Kỹ thuật phần mềm", []),
            new("TOP_2", "DUP_CODE", "Đề tài bị trùng mã", null, null, null, null, "EdTech", [], [], "SINGLE_MAJOR", 100, "Kỹ thuật phần mềm", [])
        ]);

        var first = await svc.ApplyTopicBatchSetupAsync(req, Ct);
        Assert.False(first.Replay);
        Assert.Equal(1, first.CreatedCount);
        Assert.Equal(1, first.SkippedCount);
        Assert.Equal("CAP_01", first.Created[0].Code);
        Assert.Equal("DRAFT", first.Created[0].Status);
        Assert.Equal("DUP_CODE", first.Skipped[0].Code);

        var second = await svc.ApplyTopicBatchSetupAsync(req, Ct);
        Assert.True(second.Replay);
        Assert.Equal(1, sender.CreatedCount);
    }

    [Fact]
    public async Task ApplyTopicBatchSetup_RejectsArchivedPeriodUpfront_AndDoesNotPersistIdempotencyWhenZeroCreated()
    {
        await using var db = SeedAcademicDb();
        var now = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        var archivedSem = new M.AcademicSemester
        {
            Id = 50,
            OrganizationId = 1,
            Code = "F8-20261030",
            Name = "Kỳ đã lưu trữ",
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 12, 30),
            Status = "ARCHIVED",
            CreatedAt = now,
            UpdatedAt = now
        };
        var archivedPeriod = new M.ProjectPeriod
        {
            Id = 1099,
            AcademicSemesterId = 50,
            Code = "F8-REG-20261030",
            Name = "F8 Đăng ký",
            PeriodType = "REGISTRATION",
            StartAt = now.AddDays(-20),
            EndAt = now.AddDays(20),
            Status = "ARCHIVED",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.AcademicSemesters.Add(archivedSem);
        db.ProjectPeriods.Add(archivedPeriod);
        await db.SaveChangesAsync(Ct);

        var sender = new StubTopicSender(rejectCode: "ONLY_BAD");
        var svc = new ProjectAiSetupService(db, new TestUser(1, ["DEPARTMENT_STAFF"]), new AllowGuard(), sender, new TestAudit(), new FixedTestClock(), new FakeReviewGateService(), new FakeColdService());

        var archivedReq = new ApplyTopicBatchSetupRequest(Guid.NewGuid(), 16, 1099, 10,
        [
            new("TOP_1", "CAP_99", "Đề tài hợp lệ", null, null, null, null, "EdTech", [], [], "SINGLE_MAJOR", 100, "SE", [])
        ]);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.ApplyTopicBatchSetupAsync(archivedReq, Ct));
        Assert.Contains("đã lưu trữ", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, sender.CreatedCount);

        var allSkippedReq = new ApplyTopicBatchSetupRequest(Guid.NewGuid(), 17, 1000, 10,
        [
            new("TOP_1", "ONLY_BAD", "Đề tài trùng mã", null, null, null, null, "EdTech", [], [], "SINGLE_MAJOR", 100, "SE", [])
        ]);
        var zeroResult = await svc.ApplyTopicBatchSetupAsync(allSkippedReq, Ct);
        Assert.Equal(0, zeroResult.CreatedCount);
        Assert.Equal(1, zeroResult.SkippedCount);
        Assert.Empty(db.AiSetupApplications.ToList());
    }

    [Fact]
    public async Task ApplyColdTemplateSetup_NormalizesCodes_EnsuresRequired_AndReplaysIdempotently()
    {
        await using var db = SeedAcademicDb();
        var audit = new TestAudit();
        var clock = new FixedTestClock();
        var cold = new FakeColdService();
        var svc = new ProjectAiSetupService(db, new TestUser(1, ["DEPARTMENT_STAFF"]), new AllowGuard(), new StubTopicSender(), audit, clock, new FakeReviewGateService(), cold);

        var req = new ApplyColdTemplateSetupRequest(Guid.NewGuid(), 20, 1001, "MAJOR_SPECIFIC", 100, "Hồ sơ chấm nguội SE",
        [
            new("software req!", "Đặc tả phần mềm", false),
            new("software req!", "Thiết kế kiến trúc", false)
        ]);

        var first = await svc.ApplyColdTemplateSetupAsync(req, Ct);
        Assert.False(first.Replay);
        Assert.Equal(2, first.ReportsCount);
        Assert.NotNull(cold.LastRequest);
        Assert.Equal("SOFTWARE_REQ", cold.LastRequest!.Reports[0].Code);
        Assert.Equal("SOFTWARE_REQ_2", cold.LastRequest.Reports[1].Code);
        Assert.True(cold.LastRequest.Reports[0].Required);

        var second = await svc.ApplyColdTemplateSetupAsync(req, Ct);
        Assert.True(second.Replay);
        Assert.Equal(1, cold.CreateCount);
    }

    private static AipmsDbContext SeedAcademicDb()
    {
        var options = new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase("PreSemesterPhase2_" + Guid.NewGuid().ToString("N"))
            .Options;
        var db = new AipmsDbContext(options);
        var now = DateTime.UtcNow;

        var org = new M.Organization { Id = 1, Code = "FPT", Name = "FPT University", IsActive = true, CreatedAt = now, UpdatedAt = now };
        var dept1 = new M.Department { Id = 10, OrganizationId = 1, Code = "SE_DEPT", Name = "Bộ môn Kỹ thuật phần mềm", IsActive = true, CreatedAt = now, UpdatedAt = now };
        var dept2 = new M.Department { Id = 20, OrganizationId = 1, Code = "MKT_DEPT", Name = "Bộ môn Marketing", IsActive = true, CreatedAt = now, UpdatedAt = now };
        var major1 = new M.Major { Id = 100, DepartmentId = 10, Code = "SE", Name = "Kỹ thuật phần mềm", IsActive = true, CreatedAt = now, UpdatedAt = now };
        var roleStaff = new M.Role { Id = 1, Code = "DEPARTMENT_STAFF", Name = "Department Staff", CreatedAt = now };
        var roleAdmin = new M.Role { Id = 2, Code = "ADMIN", Name = "Admin", CreatedAt = now };

        var staff1 = new M.User { Id = 1, Email = "staff1@fpt.edu.vn", FullName = "Trưởng bộ môn SE", PasswordHash = "x", Status = "ACTIVE", DepartmentId = 10, CreatedAt = now, UpdatedAt = now };
        var admin = new M.User { Id = 2, Email = "admin@fpt.edu.vn", FullName = "Quản trị viên", PasswordHash = "x", Status = "ACTIVE", DepartmentId = 10, CreatedAt = now, UpdatedAt = now };
        var staff2 = new M.User { Id = 3, Email = "staff2@fpt.edu.vn", FullName = "Trưởng bộ môn MKT", PasswordHash = "x", Status = "ACTIVE", DepartmentId = 20, CreatedAt = now, UpdatedAt = now };

        db.Organizations.Add(org);
        db.Departments.AddRange(dept1, dept2);
        db.Majors.Add(major1);
        db.Roles.AddRange(roleStaff, roleAdmin);
        db.Users.AddRange(staff1, admin, staff2);
        db.UserRoles.AddRange(
            new M.UserRole { UserId = 1, RoleId = 1, AssignedAt = now },
            new M.UserRole { UserId = 2, RoleId = 2, AssignedAt = now },
            new M.UserRole { UserId = 3, RoleId = 1, AssignedAt = now });
        db.SaveChanges();
        return db;
    }

    private sealed class TestUser(long id, string[] roles) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => id;
        public string? Email => $"u{id}@test.local";
        public string? FullName => $"User {id}";
        public IReadOnlyCollection<string> Roles => roles;
    }

    private sealed class TestAudit : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) { Entries.Add(entry); return Task.CompletedTask; }
    }

    private sealed class FixedTestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class AllowGuard : IProjectExecutionGuard
    {
        public Task MustBeActiveAsync(long projectId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MustBeActiveForMilestoneAsync(long milestoneId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MustBeActiveForTaskAsync(long taskId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MustBeActiveOrReadOnlyAsync(long projectId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class LocalStubProvider : IAiTextGenerationProvider
    {
        public string ModelVersion => "GROUNDED_LOCAL_V1";
        public string ConfigurationVersion => "LOCAL";
        public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) => Task.FromResult("{}");
    }

    private sealed class EmptyContextRetriever : IAiContextRetriever
    {
        public Task<ProjectBoundedContext> RetrieveProjectContextAsync(long projectId, string query, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<ReportBoundedContext> RetrieveReportContextAsync(long projectId, long reportId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class InMemoryFileStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        public async Task WriteAsync(string key, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            files[key] = ms.ToArray();
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
            Task.FromResult<Stream>(new MemoryStream(files[key], writable: false));
        public Task DeleteAsync(string key, CancellationToken ct = default) { files.Remove(key); return Task.CompletedTask; }
    }

    private sealed class FakeReviewGateService : IReviewGateService
    {
        public int SaveCount { get; private set; }
        public Task<ReviewGateTemplateDto> SaveTemplate(SaveReviewGateTemplateRequest request, long? id, CancellationToken ct)
        {
            SaveCount++;
            return Task.FromResult(new ReviewGateTemplateDto("701", request.ProjectPeriodId, request.Code, request.Name, request.Mode, request.Major, "DRAFT", "1.0", Guid.NewGuid().ToString("N"), request.Gates.Count, []));
        }
        public Task<IReadOnlyList<ReviewGateTemplateDto>> Templates(CancellationToken ct) => throw new NotImplementedException();
        public Task<ReviewGateTemplateDto?> Template(long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<byte[]> ExportTemplateWorkbook(long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<GateWorkbookPreview> PreviewTemplateWorkbook(long id, byte[] content, CancellationToken ct) => throw new NotImplementedException();
        public Task<ReviewGateTemplateDto> PublishTemplate(long id, PublishReviewGateTemplateRequest? request, CancellationToken ct) => throw new NotImplementedException();
        public Task DeleteTemplate(long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<StudentReviewTimelineDto> Timeline(long projectId, CancellationToken ct) => throw new NotImplementedException();
        public Task<StudentReviewTimelineDto> MyTimeline(CancellationToken ct) => throw new NotImplementedException();
        public Task<StudentReviewTimelineDto> SubmitEvidence(long projectId, string gateCode, SubmitReviewGateEvidenceRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<StudentReviewTimelineDto> SubmitEvidenceFile(long projectId, string gateCode, SubmitReviewGateFileRequest request, UploadContent file, CancellationToken ct) => throw new NotImplementedException();
        public Task<StudentReviewTimelineDto> SubmitForReview(long projectId, string gateCode, SubmitReviewGateForReviewRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<SupervisorFeedbackDto> AddFeedback(long projectId, string gateCode, AddReviewGateFeedbackRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<PagedResult<AssignedReviewItemDto>> AssignedReviews(int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<AssignedReviewItemDto> VerifyEvidence(long assignmentId, VerifyReviewGateEvidenceRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<AssignedReviewItemDto> Decide(long assignmentId, SubmitReviewGateDecisionRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<AssignedReviewItemDto> Assign(CreateReviewGateAssignmentRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<AssignedReviewItemDto> Revoke(long assignmentId, RevokeReviewGateAssignmentRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<ProjectReviewMatrixRowDto> UpdateDecision(long projectId, string gateCode, UpdateReviewGateDecisionRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<OverrideReviewGateDto> OverrideDecision(long projectId, string gateCode, OverrideReviewGateRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<ReviewGateUserCapabilitiesDto> OverrideCapability(long projectId, CancellationToken ct) => throw new NotImplementedException();
        public Task<ProjectReviewMatrixRowDto> UpdateScope(long projectId, string scope, UpdateReviewGateScopeRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<ProjectReviewMatrixRowDto>> Matrix(string? mode, long? departmentId, CancellationToken ct) => throw new NotImplementedException();
        public Task<DefenseReadinessDto> DefenseReadiness(long projectId, CancellationToken ct) => throw new NotImplementedException();
        public Task<DefenseReadinessDto> DefenseReadinessByKey(string projectKey, CancellationToken ct) => throw new NotImplementedException();
        public Task<FileDownload> DownloadEvidence(long projectId, string gateCode, long requirementId, int version, CancellationToken ct) => throw new NotImplementedException();
        public Task<AIPMS.Application.Abstractions.Security.SignedPreviewTokenDto> IssueEvidencePreviewToken(long projectId, string gateCode, long requirementId, int version, CancellationToken ct) => throw new NotImplementedException();
        public Task<FileDownload> StreamEvidence(long projectId, string gateCode, long requirementId, int version, string token, CancellationToken ct) => throw new NotImplementedException();
        public Task<AIPMS.Application.Features.Files.OfficePreviewDto> SafePreviewEvidence(long projectId, string gateCode, long requirementId, int version, CancellationToken ct) => throw new NotImplementedException();
        public Task<ReviewGateArchiveManifest> ArchiveManifest(long projectId, string gateCode, CancellationToken ct) => throw new NotImplementedException();
        public Task<GateRoundConsensusDto> GetRoundScopeConsensus(long projectId, string gateCode, int? roundNumber, CancellationToken ct) => throw new NotImplementedException();
        public Task<GateRoundConsensusDto> RecordRoundScopeDecision(long projectId, string gateCode, RecordRoundScopeDecisionRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task<SlaSweepResultDto> SweepSlaNotifications(CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class FakeColdService : IColdSubmissionService
    {
        public int CreateCount { get; private set; }
        public CreateColdTemplateRequest? LastRequest { get; private set; }
        public Task<ColdTemplateDto> CreateTemplate(CreateColdTemplateRequest input, CancellationToken ct)
        {
            CreateCount++;
            LastRequest = input;
            return Task.FromResult(new ColdTemplateDto(801, input.MajorId, input.ProjectPeriodId, input.Name, 1, input.Reports, input.Scope));
        }
        public Task<PagedResult<ColdTemplateDto>> Templates(long? majorId, long periodId, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<ColdChecklistDto> Apply(long projectId, ApplyColdChecklistRequest input, CancellationToken ct) => throw new NotImplementedException();
        public Task<ColdChecklistDto> Checklist(long projectId, CancellationToken ct) => throw new NotImplementedException();
        public Task<ColdProgressDto> Progress(long projectId, CancellationToken ct) => throw new NotImplementedException();
        public Task<ColdSubmissionDto> SaveDraft(long itemId, SaveColdDraftRequest input, CancellationToken ct) => throw new NotImplementedException();
        public Task<ColdSubmissionDto> Upload(long id, string token, UploadContent content, CancellationToken ct) => throw new NotImplementedException();
        public Task<ColdSubmissionDto> RemoveFile(long id, long fileId, string token, CancellationToken ct) => throw new NotImplementedException();
        public Task<ColdSubmissionDto> Submit(long id, string token, CancellationToken ct) => throw new NotImplementedException();
        public Task<ColdChecklistDto> Lock(long projectId, string token, CancellationToken ct) => throw new NotImplementedException();
        public Task<PagedResult<ColdSubmissionDto>> History(long itemId, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<FileDownload> Download(long id, long fileId, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class StubTopicSender(string? rejectCode = null) : ISender
    {
        public int CreatedCount { get; private set; }
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is CreateTopicCommand cmd)
            {
                if (rejectCode is not null && string.Equals(cmd.Request.Code, rejectCode, StringComparison.OrdinalIgnoreCase))
                    throw new ConflictException("Mã đề tài đã tồn tại trong kỳ.");
                CreatedCount++;
                var dto = new TopicDto(900 + CreatedCount, cmd.Request.Code, "DRAFT", cmd.Request.ProjectPeriodId, 1, 1, cmd.Request.LeadDepartmentId, "SE",
                    cmd.Request.Content.Title, cmd.Request.Content.Description, cmd.Request.Content.ProblemStatement, cmd.Request.Content.Objectives,
                    cmd.Request.Content.ExpectedOutput, cmd.Request.Content.Domain, cmd.Request.Content.Technologies, cmd.Request.Content.Keywords,
                    cmd.Request.Content.ProjectMode, cmd.Request.Content.PrimaryMajorId, [], 1, 1, DateTime.UtcNow, DateTime.UtcNow, null, null, null, null, null, Guid.NewGuid(), null);
                return Task.FromResult((TResponse)(object)dto);
            }
            throw new NotSupportedException();
        }
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => Task.CompletedTask;
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => Task.FromResult<object?>(null);
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
