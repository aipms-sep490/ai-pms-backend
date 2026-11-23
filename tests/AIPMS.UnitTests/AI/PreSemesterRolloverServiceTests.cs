using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Academic;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.Cold;
using AIPMS.Application.Features.Cold.Abstractions;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Evaluations.Commands;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Application.Features.ReviewGates.Abstractions;
using AIPMS.Application.Features.Semesters;
using AIPMS.Application.Features.Semesters.Abstractions;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services.Academic;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.UnitTests.AI;

public sealed class PreSemesterRolloverServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public void ShiftWindow_AndBuildRolloverCode_ClampDatesWithinPeriod_AndDeduplicateCodes()
    {
        var pStart = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var pEnd = new DateTime(2026, 12, 15, 0, 0, 0, DateTimeKind.Utc);
        var (ns, ne) = PreSemesterRolloverService.ShiftWindow(
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc),
            123, pStart, pEnd);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), ns);
        Assert.Equal(pEnd, ne); // clamped safely inside target period

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var c1 = PreSemesterRolloverService.BuildRolloverCode("RUBRIC_VERY_LONG_CODE_NAME_FOR_SOFTWARE_ENGINEERING", "FA26", used, 50);
        var c2 = PreSemesterRolloverService.BuildRolloverCode("RUBRIC_VERY_LONG_CODE_NAME_FOR_SOFTWARE_ENGINEERING", "FA26", used, 50);
        Assert.True(c1.Length <= 50);
        Assert.True(c2.Length <= 50);
        Assert.NotEqual(c1, c2);
        Assert.EndsWith("_FA26", c1);
    }

    [Fact]
    public async Task Rollover_PreviewAndApply_ClonesDraftsWithShiftedDates_LoadsHolidays_AndReplaysIdempotently()
    {
        await using var db = SeedTwoSemestersDb();
        var sender = new StubRubricSender();
        var gates = new StubGateService();
        var cold = new StubColdService();
        var policy = new StubPolicyService();
        var calendar = new StubCalendarService();
        var audit = new TestAudit();
        var config = new ConfigurationBuilder().Build();

        var svc = new PreSemesterRolloverService(db, new TestUser(1, ["DEPARTMENT_STAFF"]),
            sender, gates, cold, policy, calendar, new LocalProvider(), audit, new FixedClock(), config);

        // 1. Readiness for target semester 202 (empty initially)
        var readiness = await svc.GetReadinessAsync(202, 10, Ct);
        Assert.Equal("FA26", readiness.SemesterCode);
        Assert.True(readiness.IsDepartmentStaff);
        Assert.False(readiness.IsAdmin);
        Assert.Contains(readiness.Items, i => i.Key == "RUBRICS" && i.Status == "MISSING" && i.AllowedByRole);
        Assert.Contains(readiness.Items, i => i.Key == "MILESTONE_TEMPLATES" && !i.AllowedByRole);

        // 2. Preview rollover 201 (SP26) -> 202 (FA26)
        var preview = await svc.PreviewRolloverAsync(new RolloverPreviewRequest(201, 202, 10, true, true, true, true, true, true), Ct);
        Assert.True(preview.ShiftDays > 100);
        Assert.Contains(preview.Items, i => i.Category == "RUBRIC" && !i.WillSkip);
        Assert.Contains(preview.Items, i => i.Category == "REVIEW_GATE" && !i.WillSkip && i.DateShifts.Count == 1);
        Assert.Contains(preview.Items, i => i.Category == "COLD_TEMPLATE" && !i.WillSkip);
        Assert.Contains(preview.Items, i => i.Category == "POLICY" && !i.WillSkip);

        // 3. Apply rollover
        var key = Guid.NewGuid();
        var first = await svc.ApplyRolloverAsync(new RolloverApplyRequest(key, 201, 202, 10, true, true, true, true, true, true), Ct);
        Assert.False(first.Replay);
        Assert.Equal(4, first.CreatedCount); // 1 rubric + 1 gate + 1 cold + 1 policy
        Assert.Equal(1, first.HolidaysLoadedCount);
        Assert.All(first.Created.Where(x => x.Category is "RUBRIC" or "REVIEW_GATE" or "POLICY"), x => Assert.Equal("DRAFT", x.Status));
        Assert.Equal(1, sender.RubricsCreated);
        Assert.Equal(1, gates.SavedCount);
        Assert.Equal(1, cold.CreatedCount);
        Assert.Equal(1, policy.PutCount);
        Assert.Equal(1, calendar.HolidaysLoaded);

        // 4. Replay idempotently
        var second = await svc.ApplyRolloverAsync(new RolloverApplyRequest(key, 201, 202, 10, true, true, true, true, true, true), Ct);
        Assert.True(second.Replay);
        Assert.Equal(1, sender.RubricsCreated);
        Assert.Equal(1, gates.SavedCount);

        // 5. AI Diff Check fallback works cleanly offline
        var upload = new AssistantUpload("ke-hoach-fa26.md", Encoding.UTF8.GetBytes("Học kỳ FA26 bắt đầu 01/09/2026, bảo vệ dự kiến 15/12/2026."));
        var diff = await svc.AiDiffCheckAsync(202, 10, upload, Ct);
        Assert.False(diff.UsedAi);
        Assert.NotEmpty(diff.Findings);
    }

    private static AipmsDbContext SeedTwoSemestersDb()
    {
        var options = new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase("PreSemesterRollover_" + Guid.NewGuid().ToString("N"))
            .Options;
        var db = new AipmsDbContext(options);
        var now = DateTime.UtcNow;

        db.Organizations.Add(new M.Organization { Id = 1, Code = "FPT", Name = "FPT University", IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.Departments.Add(new M.Department { Id = 10, OrganizationId = 1, Code = "SE", Name = "Kỹ thuật phần mềm", IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.Majors.Add(new M.Major { Id = 100, DepartmentId = 10, Code = "SE", Name = "Software Engineering", IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.Roles.Add(new M.Role { Id = 1, Code = "DEPARTMENT_STAFF", Name = "Staff", CreatedAt = now });
        db.Users.Add(new M.User { Id = 1, Email = "staff@fpt.edu.vn", FullName = "Trưởng BM", PasswordHash = "x", Status = "ACTIVE", DepartmentId = 10, CreatedAt = now, UpdatedAt = now });
        db.UserRoles.Add(new M.UserRole { UserId = 1, RoleId = 1, AssignedAt = now });

        var sem1 = new M.AcademicSemester { Id = 201, OrganizationId = 1, Code = "SP26", Name = "Spring 2026", StartDate = new DateOnly(2026, 1, 10), EndDate = new DateOnly(2026, 5, 10), Status = "CLOSED", CreatedAt = now, UpdatedAt = now };
        var sem2 = new M.AcademicSemester { Id = 202, OrganizationId = 1, Code = "FA26", Name = "Fall 2026", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 20), Status = "UPCOMING", CreatedAt = now, UpdatedAt = now };
        db.AcademicSemesters.AddRange(sem1, sem2);

        var crit = new M.EvaluationCriterion { Id = 1, Code = "C1", Name = "Thiết kế hệ thống", Description = "Kiến trúc", CreatedAt = now, UpdatedAt = now };
        db.EvaluationCriteria.Add(crit);

        var rubric = new M.Rubric
        {
            Id = 301, DepartmentId = 10, AcademicSemesterId = 201, Code = "RUB_SE_DEF", Name = "Rubric Bảo vệ SE",
            ProjectMode = "SINGLE_MAJOR", EvaluationStage = "DEFENSE", EvaluationScope = "COMMON", IsActive = true, CreatedAt = now, UpdatedAt = now,
            RubricCriteria = [new M.RubricCriterion { Id = 1, RubricId = 301, CriterionId = 1, Criterion = crit, WeightPercent = 100m, MaxScore = 10m, SortOrder = 1, IsRequired = true, CreatedAt = now }]
        };
        db.Rubrics.Add(rubric);
        db.Set<RubricVersion>().Add(new RubricVersion { RubricId = 301, RootRubricId = 301, VersionNumber = 1, Status = "PUBLISHED", ConcurrencyToken = Guid.NewGuid() });

        var p1 = new M.ProjectPeriod { Id = 401, AcademicSemesterId = 201, Code = "SP26_EVAL", Name = "Đánh giá SP26", PeriodType = "EVALUATION", Status = "CLOSED", StartAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), EndAt = new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc), RubricId = 301, CreatedAt = now, UpdatedAt = now };
        var p2 = new M.ProjectPeriod { Id = 402, AcademicSemesterId = 202, Code = "FA26_EVAL", Name = "Đánh giá FA26", PeriodType = "EVALUATION", Status = "UPCOMING", StartAt = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc), EndAt = new DateTime(2026, 12, 15, 0, 0, 0, DateTimeKind.Utc), RubricId = 301, CreatedAt = now, UpdatedAt = now };
        db.ProjectPeriods.AddRange(p1, p2);

        db.ReviewGateTemplates.Add(new ReviewGateTemplate
        {
            Id = 501, ProjectPeriodId = 401, Code = "GATE_SE", Name = "Khung cổng SE", Mode = "SINGLE_MAJOR", Major = "SE", Version = 1, Status = "PUBLISHED",
            ConcurrencyToken = Guid.NewGuid(), CreatedBy = 1, CreatedAt = now, UpdatedAt = now,
            Gates =
            [
                new ReviewGateDefinition
                {
                    Id = 1, Code = "G1", Sequence = 1, Title = "Cổng 1", Objective = "SRS", Required = true, Weight = 100m,
                    WindowStart = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), WindowEnd = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc),
                    ReviewerPolicy = "COUNCIL", ReviewScope = "COMMON",
                    Requirements = [new ReviewGateRequirement { Id = 1, Code = "R1", Title = "SRS", Type = "FILE", Scope = "COMMON", Verification = "HUMAN", Required = true }]
                }
            ]
        });

        db.Set<ColdReportTemplate>().Add(new ColdReportTemplate
        {
            Id = 601, MajorId = 100, Scope = "MAJOR_SPECIFIC", ProjectPeriodId = 401, Name = "Mẫu Cold SE", Version = 1,
            ReportsJson = JsonSerializer.Serialize(new[] { new ColdReportInput("SRS", "Đặc tả phần mềm", true) }),
            CreatedBy = 1, CreatedAt = now
        });

        db.SaveChanges();
        return db;
    }

    private sealed class TestUser(long id, string[] roles) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => id;
        public string? Email => "staff@fpt.edu.vn";
        public string? FullName => "Staff";
        public IReadOnlyCollection<string> Roles => roles;
    }

    private sealed class TestAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class LocalProvider : IAiTextGenerationProvider
    {
        public string ModelVersion => "GROUNDED_LOCAL_V1";
        public string ConfigurationVersion => "LOCAL";
        public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) => Task.FromResult("{}");
    }

    private sealed class StubRubricSender : ISender
    {
        public int RubricsCreated { get; private set; }
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is CreateRubricCommand cmd)
            {
                RubricsCreated++;
                var dto = new RubricDto(700 + RubricsCreated, cmd.Input.DepartmentId, cmd.Input.AcademicSemesterId, cmd.Input.Code, cmd.Input.Name,
                    cmd.Input.Description, "DRAFT", false, 700 + RubricsCreated, 1, Guid.NewGuid().ToString("N"), false, DateTime.UtcNow, DateTime.UtcNow, []);
                return Task.FromResult((TResponse)(object)dto);
            }
            throw new NotSupportedException();
        }
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => Task.CompletedTask;
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => Task.FromResult<object?>(null);
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubGateService : IReviewGateService
    {
        public int SavedCount { get; private set; }
        public Task<ReviewGateTemplateDto> SaveTemplate(SaveReviewGateTemplateRequest request, long? id, CancellationToken ct)
        {
            SavedCount++;
            return Task.FromResult(new ReviewGateTemplateDto("801", request.ProjectPeriodId, request.Code, request.Name, request.Mode, request.Major, "DRAFT", "1.0", Guid.NewGuid().ToString("N"), request.Gates.Count, []));
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

    private sealed class StubColdService : IColdSubmissionService
    {
        public int CreatedCount { get; private set; }
        public Task<ColdTemplateDto> CreateTemplate(CreateColdTemplateRequest input, CancellationToken ct)
        {
            CreatedCount++;
            return Task.FromResult(new ColdTemplateDto(901, input.MajorId, input.ProjectPeriodId, input.Name, 1, input.Reports, input.Scope));
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

    private sealed class StubPolicyService : IPeriodPolicyService
    {
        public int PutCount { get; private set; }
        public Task<IReadOnlyList<PeriodPolicyDto>> HistoryAsync(long periodId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PeriodPolicyDto>>([new PeriodPolicyDto(1, periodId, 1, "PUBLISHED", DateTime.UtcNow, DateTime.UtcNow.AddMonths(3), Guid.NewGuid().ToString("N"), new PeriodPolicyFields("SINGLE_MAJOR", "DEPARTMENT_TOPIC", 3, 5, 1, 5, false))]);
        public Task<PeriodPolicyDto> GetAsync(long periodId, DateTimeOffset? asOf, CancellationToken ct) => throw new NotImplementedException();
        public Task<PeriodPolicyDto> PutAsync(long periodId, UpdatePeriodPolicyRequest input, CancellationToken ct)
        {
            PutCount++;
            return Task.FromResult(new PeriodPolicyDto(2, periodId, input.ExpectedVersion + 1, "DRAFT", input.EffectiveFrom.UtcDateTime, input.EffectiveTo.UtcDateTime, Guid.NewGuid().ToString("N"), input.Policy));
        }
    }

    private sealed class StubCalendarService : IPeriodCalendarService
    {
        public int HolidaysLoaded { get; private set; }
        public Task<PeriodCalendarDto> Get(long projectPeriodId, CancellationToken ct) =>
            Task.FromResult(new PeriodCalendarDto(projectPeriodId, 62, "Mon-Fri", [], Guid.NewGuid().ToString("N")));
        public Task<PeriodCalendarDto> Save(long projectPeriodId, SavePeriodCalendarRequest input, CancellationToken ct) => throw new NotImplementedException();
        public Task<PeriodCalendarDto> LoadVietnamHolidays(long projectPeriodId, LoadVietnamHolidaysRequest input, CancellationToken ct)
        {
            HolidaysLoaded++;
            return Task.FromResult(new PeriodCalendarDto(projectPeriodId, 62, "Mon-Fri", [new PeriodHoliday(new DateOnly(input.Year, 9, 2), "Quốc khánh", true)], Guid.NewGuid().ToString("N")));
        }
    }
}
