using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.AI.Providers;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AiSuite;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services.AiSuite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class AiSuiteServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static AipmsDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static AipmsDbContext SeedFullScenario()
    {
        var db = CreateDb();
        db.Organizations.Add(new M.Organization { Id = 1, Code = "FPT", Name = "FPT University", IsActive = true });
        db.AcademicSemesters.Add(new M.AcademicSemester { Id = 1, OrganizationId = 1, Code = "FA26", Name = "Fall 2026", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 31), Status = "ACTIVE" });
        db.Departments.AddRange(
            new M.Department { Id = 1, OrganizationId = 1, Code = "SE", Name = "Kỹ thuật Phần mềm", IsActive = true },
            new M.Department { Id = 2, OrganizationId = 1, Code = "MKT", Name = "Marketing", IsActive = true });
        db.Majors.AddRange(
            new M.Major { Id = 1, DepartmentId = 1, Code = "SE", Name = "Software Engineering", IsActive = true },
            new M.Major { Id = 2, DepartmentId = 1, Code = "AI", Name = "Artificial Intelligence", IsActive = true });

        db.Roles.AddRange(
            new M.Role { Id = 1, Code = AppRoles.Student, Name = "Student" },
            new M.Role { Id = 2, Code = AppRoles.Lecturer, Name = "Lecturer" },
            new M.Role { Id = 3, Code = AppRoles.DepartmentStaff, Name = "Department Staff" },
            new M.Role { Id = 4, Code = AppRoles.Admin, Name = "Admin" });

        db.Users.AddRange(
            new M.User { Id = 1, Email = "leader@test.vn", FullName = "Nguyễn Minh Khang", PasswordHash = "h", Status = "ACTIVE", DepartmentId = 1, MajorId = 1 },
            new M.User { Id = 2, Email = "member@test.vn", FullName = "Trần Thu Hà", PasswordHash = "h", Status = "ACTIVE", DepartmentId = 1, MajorId = 2 },
            new M.User { Id = 3, Email = "gvhd@test.vn", FullName = "TS. Lê Quang Huy", PasswordHash = "h", Status = "ACTIVE", DepartmentId = 1 },
            new M.User { Id = 4, Email = "staff@test.vn", FullName = "Cán bộ Khoa", PasswordHash = "h", Status = "ACTIVE", DepartmentId = 1 },
            new M.User { Id = 5, Email = "eval2@test.vn", FullName = "ThS. Phạm Văn Nam", PasswordHash = "h", Status = "ACTIVE", DepartmentId = 1 },
            new M.User { Id = 6, Email = "otherstaff@test.vn", FullName = "Cán bộ Khoa khác", PasswordHash = "h", Status = "ACTIVE", DepartmentId = 2 },
            new M.User { Id = 7, Email = "admin@test.vn", FullName = "Quản trị viên", PasswordHash = "h", Status = "ACTIVE", DepartmentId = 1 },
            new M.User { Id = 8, Email = "eval3@test.vn", FullName = "TS. Hoàng Văn Bình", PasswordHash = "h", Status = "ACTIVE", DepartmentId = 1 });

        db.UserRoles.AddRange(
            new M.UserRole { UserId = 1, RoleId = 1 },
            new M.UserRole { UserId = 2, RoleId = 1 },
            new M.UserRole { UserId = 3, RoleId = 2 },
            new M.UserRole { UserId = 4, RoleId = 3 },
            new M.UserRole { UserId = 5, RoleId = 2 },
            new M.UserRole { UserId = 6, RoleId = 3 },
            new M.UserRole { UserId = 7, RoleId = 4 },
            new M.UserRole { UserId = 8, RoleId = 2 });

        db.Teams.Add(new M.Team { Id = 10, AcademicSemesterId = 1, Code = "T10", Name = "Nhóm Liên Ngành 10", Status = "ACTIVE", CreatedBy = 1 });
        db.TeamMembers.AddRange(
            new M.TeamMember { Id = 1, TeamId = 10, UserId = 1, IsLeader = true, AcademicSemesterId = 1 },
            new M.TeamMember { Id = 2, TeamId = 10, UserId = 2, IsLeader = false, AcademicSemesterId = 1 });

        db.Projects.Add(new M.Project { Id = 20, TeamId = 10, Code = "AI-2026-10", Title = "Hệ thống AI quản lý đồ án", Description = "Xây dựng nền tảng quản lý đồ án liên ngành tích hợp AI.", Status = "ACTIVE", TrackCode = "IT_MKT", RowVersion = [1] });
        db.ProjectMajors.AddRange(
            new M.ProjectMajor { ProjectId = 20, MajorId = 1 },
            new M.ProjectMajor { ProjectId = 20, MajorId = 2 });
        db.SupervisorProfiles.Add(new M.SupervisorProfile { Id = 30, UserId = 3 });
        db.SupervisorAssignments.Add(new M.SupervisorAssignment { Id = 31, ProjectId = 20, SupervisorProfileId = 30, AssignmentType = "PRIMARY", IsPrimary = true });

        db.Set<Sprint>().Add(new Sprint { Id = 60, ProjectId = 20, Name = "Sprint 3", Status = "ACTIVE", StartAt = Now.AddDays(-10), EndAt = Now.AddDays(4), CreatedBy = 1, CreatedAt = Now.AddDays(-10), UpdatedAt = Now });
        db.Milestones.Add(new M.Milestone { Id = 40, ProjectId = 20, Title = "Mốc 2: Hoàn thiện cốt lõi", Status = "IN_PROGRESS", SortOrder = 1, CreatedBy = 1 });
        db.Tasks.AddRange(
            new M.Task { Id = 41, MilestoneId = 40, SprintId = 60, Title = "Xây dựng API Video AI", Status = "DONE", Priority = "HIGH", CompletedAt = Now.AddDays(-2), CreatedBy = 1, UpdatedAt = Now.AddDays(-2) },
            new M.Task { Id = 42, MilestoneId = 40, SprintId = 60, Title = "Tích hợp thanh toán VNPay", Status = "BLOCKED", Priority = "HIGH", DueAt = Now.AddDays(-3), CreatedBy = 1, UpdatedAt = Now.AddDays(-1) },
            new M.Task { Id = 43, MilestoneId = 40, SprintId = 60, Title = "Viết tài liệu báo cáo", Status = "IN_PROGRESS", Priority = "MEDIUM", DueAt = Now.AddDays(2), CreatedBy = 1, UpdatedAt = Now });
        db.TaskAssignees.AddRange(
            new M.TaskAssignee { TaskId = 41, UserId = 1 },
            new M.TaskAssignee { TaskId = 42, UserId = 1 },
            new M.TaskAssignee { TaskId = 43, UserId = 2 });

        db.Meetings.Add(new M.Meeting { Id = 50, ProjectId = 20, Title = "Họp Sprint 3", StartAt = Now.AddDays(-2), Status = "COMPLETED", CreatedBy = 1 });
        db.MeetingTranscriptSegments.Add(new MeetingTranscriptSegment { Id = 1, MeetingId = 50, SpeakerUserId = 1, SpeakerName = "Nguyễn Minh Khang", Content = "Mình đã xong API Video AI.", SpokenAt = Now.AddDays(-2), CreatedBy = 1, CreatedAt = Now.AddDays(-2) });
        db.MeetingAiInsights.Add(new MeetingAiInsight { Id = 1, MeetingId = 50, InsightType = "FULL_MINUTES", Status = "COMPLETED", SummaryText = "Nhóm hoàn thành API Video AI và cần tháo gỡ lỗi VNPay.", PayloadJson = "{}", CreatedBy = 1, CreatedAt = Now.AddDays(-2) });

        // Gate & Rubric & Evaluation & Appeal (independent evaluator user 5 & 8, obeying D17/QĐ-06 COI)
        db.ProjectGateInstances.Add(new ProjectGateInstance
        {
            Id = 70, ProjectId = 20, TemplateId = 1, TemplateVersion = 1, Code = "G3", Sequence = 3, Title = "Review Gate 3", Objective = "Đánh giá hoàn thiện trước bảo vệ", Status = "IN_PROGRESS", CreatedAt = Now.AddDays(-5),
            Requirements = [new ProjectGateRequirement { Id = 71, GateInstanceId = 70, Code = "DOC-1", Title = "Báo cáo tổng kết PDF", Required = true, VerificationStatus = "PENDING" }],
            Assignments = [new GateReviewerAssignment { Id = 72, GateInstanceId = 70, ReviewerId = 5, Scope = "COMMON", SessionRole = "CHAIR", Status = "ACTIVE" }]
        });

        db.EvaluationCriteria.Add(new M.EvaluationCriterion { Id = 80, Code = "ARCH", Name = "Kiến trúc & Thiết kế hệ thống", Description = "Đánh giá thiết kế tổng thể" });
        db.Rubrics.AddRange(
            new M.Rubric
            {
                Id = 81, Code = "RUB-DEF", Name = "Rubric Bảo vệ Đồ án", AcademicSemesterId = 1, DepartmentId = 1, EvaluationStage = "DEFENSE", EvaluationScope = "COMMON", ProjectMode = "INTERDISCIPLINARY",
                RubricCriteria = [new M.RubricCriterion { Id = 82, RubricId = 81, CriterionId = 80, WeightPercent = 100, MaxScore = 10, SortOrder = 1, IsRequired = true }]
            },
            new M.Rubric
            {
                Id = 810, Code = "RUB-COLD", Name = "Rubric Chấm nguội Chung", AcademicSemesterId = 1, DepartmentId = 1, EvaluationStage = "COLD", EvaluationScope = "COMMON", ProjectMode = "INTERDISCIPLINARY"
            },
            new M.Rubric
            {
                Id = 811, Code = "RUB-SE", Name = "Rubric Chuyên ngành SE", AcademicSemesterId = 1, DepartmentId = 1, MajorId = 1, EvaluationStage = "DEFENSE", EvaluationScope = "MAJOR_SPECIFIC"
            },
            new M.Rubric
            {
                Id = 812, Code = "RUB-AI", Name = "Rubric Chuyên ngành AI", AcademicSemesterId = 1, DepartmentId = 1, MajorId = 2, EvaluationStage = "DEFENSE", EvaluationScope = "MAJOR_SPECIFIC"
            });
        db.Set<RubricVersion>().AddRange(
            new RubricVersion { RubricId = 81, RootRubricId = 81, VersionNumber = 1, Status = "PUBLISHED" },
            new RubricVersion { RubricId = 810, RootRubricId = 810, VersionNumber = 1, Status = "PUBLISHED" },
            new RubricVersion { RubricId = 811, RootRubricId = 811, VersionNumber = 1, Status = "PUBLISHED" },
            new RubricVersion { RubricId = 812, RootRubricId = 812, VersionNumber = 1, Status = "PUBLISHED" });
        db.Set<EvaluationAssignment>().Add(new EvaluationAssignment { Id = 83, ProjectId = 20, EvaluatorId = 5, RubricId = 81, ProjectPeriodId = 1, DepartmentId = 1, EvaluationType = "COUNCIL", Status = "ACTIVE", AssignedBy = 4, AssignedAt = Now });
        db.Evaluations.AddRange(
            new M.Evaluation { Id = 84, ProjectId = 20, EvaluatorId = 8, RubricId = 81, EvaluationType = "COUNCIL", Status = "SUBMITTED", TotalScore = 9.0m, Comments = "Tốt", EvaluationDetails = [new M.EvaluationDetail { Id = 85, EvaluationId = 84, RubricCriterionId = 82, Score = 9.0m, Comments = "Rất tốt" }] },
            new M.Evaluation { Id = 86, ProjectId = 20, EvaluatorId = 5, RubricId = 81, EvaluationType = "COUNCIL", Status = "SUBMITTED", TotalScore = 5.5m, Comments = "Còn thiếu kiểm thử", EvaluationDetails = [new M.EvaluationDetail { Id = 87, EvaluationId = 86, RubricCriterionId = 82, Score = 5.5m, Comments = "Thiếu kiểm thử" }] });

        db.Set<ResultAppealRequest>().Add(new ResultAppealRequest { Id = 90, ProjectId = 20, PolicyId = 1, TargetKey = 1, ResultVersion = 1, ComponentId = 1, Scope = "INDEPENDENT_80", ResponsibleDepartmentId = 1, Status = "PENDING", RequestedBy = 1, RequestedAt = Now, Reason = "Nhóm đã bổ sung kiểm thử nhưng phiếu chấm chưa ghi nhận." });
        db.Set<FinalSubmissionRequirement>().Add(new FinalSubmissionRequirement { ProjectId = 20, DeliverableId = 999 });

        db.SaveChanges();
        return db;
    }

    private static AiSuiteService CreateService(AipmsDbContext db, long actorId, string[] roles, bool canAccess = true)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AiAssistant:TimeoutSeconds"] = "30" })
            .Build();
        return new AiSuiteService(
            db,
            new StubCurrentUser(actorId, roles),
            new StubProjectAccess(canAccess),
            new StubFileStorage(),
            new FixedTimeProvider(Now),
            config,
            new GroundedAiTextGenerationProvider());
    }

    [Fact]
    public async Task AllEightAiModules_WorkEndToEndWithGroundedProviderAndPersistSnapshots()
    {
        using var db = SeedFullScenario();
        var studentSvc = CreateService(db, 1, [AppRoles.Student]);
        var supervisorSvc = CreateService(db, 3, [AppRoles.Lecturer]);
        var staffSvc = CreateService(db, 4, [AppRoles.DepartmentStaff]);
        var evaluatorSvc = CreateService(db, 5, [AppRoles.Lecturer]);

        // #1 Defense Mock + Voice/Text Answer Eval + Gate Script Draft
        // TK-W0-09: Grounded local provider must NEVER report fake "COMPLETED" — always "FALLBACK" + IsSimulatedFallback=true
        var mock = await studentSvc.GenerateDefenseMockAsync(20, new DefenseMockRequest("ALL", 4), Ct);
        Assert.Equal("FALLBACK", mock.Meta.Status);
        Assert.True(mock.Meta.IsSimulatedFallback);
        Assert.NotEmpty(mock.Questions);
        var cachedMock = await studentSvc.GetLatestDefenseMockAsync(20, Ct);
        Assert.NotNull(cachedMock);
        Assert.Equal(mock.Meta.SnapshotId, cachedMock!.Meta.SnapshotId);
        Assert.Equal("FALLBACK", cachedMock.Meta.Status);
        Assert.True(cachedMock.Meta.IsSimulatedFallback);

        var evalAns = await studentSvc.EvaluateDefenseAnswerAsync(20, new DefenseAnswerEvalRequest(
            mock.Questions[0].Question,
            "Hệ thống dùng Clean Architecture, optimistic concurrency token, transaction SQL Server và kiểm thử tự động 1500+ test case để đảm bảo độ tin cậy và nhất quán dữ liệu.",
            "CHAIR"), Ct);
        Assert.True(evalAns.Score > 0);
        Assert.False(string.IsNullOrWhiteSpace(evalAns.Feedback));

        // Independent gate reviewer (user 5) generates gate script (obeying D17/QĐ-06 COI)
        var gateScript = await evaluatorSvc.GenerateGateScriptDraftAsync(70, new GateScriptDraftRequest(), Ct);
        Assert.Equal("G3", gateScript.GateCode);
        Assert.NotEmpty(gateScript.AgendaStages);
        Assert.NotEmpty(gateScript.SuggestedQuestions);

        // #2 Auto Progress Report Draft
        var reportDraft = await studentSvc.GenerateProgressReportDraftAsync(20, new ProgressReportDraftRequest("WEEKLY"), Ct);
        Assert.Contains("Xây dựng API Video AI", reportDraft.CompletedWork);
        Assert.NotEmpty(reportDraft.Evidence);

        // #3 Multi-dimensional Contribution Fairness (student hides private signals, supervisor sees them)
        var fairnessStudent = await studentSvc.AnalyzeContributionFairnessAsync(20, Ct);
        Assert.False(fairnessStudent.IncludesPrivateSignals);
        Assert.Equal(2, fairnessStudent.Members.Count);
        Assert.Contains(fairnessStudent.Members, m => m.UserId == 1 && m.SpeechSegments == 1);

        var fairnessSup = await supervisorSvc.AnalyzeContributionFairnessAsync(20, Ct);
        Assert.True(fairnessSup.IncludesPrivateSignals);
        Assert.NotEmpty(fairnessSup.SupervisorVerificationQuestions);

        // #4 Rubric Grading Copilot (anti-anchoring), Score Calibration & Appeal Assist
        var rubricCopilot = await evaluatorSvc.GenerateRubricGradingCopilotAsync(83, Ct);
        Assert.Single(rubricCopilot.Criteria);
        Assert.Equal(82, rubricCopilot.Criteria[0].RubricCriterionId);
        Assert.True(rubricCopilot.AntiAnchoringEnforced);
        Assert.Null(rubricCopilot.Criteria[0].SuggestedScoreMin);
        Assert.Null(rubricCopilot.Criteria[0].SuggestedScoreMax);

        var calibration = await staffSvc.CheckScoreCalibrationAsync(20, Ct);
        Assert.Equal(2, calibration.EvaluatedCount);
        Assert.Contains(calibration.Alerts, a => a.Category == "DISCREPANCY" && a.Severity == "HIGH");

        var appealAssist = await staffSvc.AssistAppealReviewAsync(90, Ct);
        Assert.Equal(90, appealAssist.AppealId);
        Assert.Equal("NEEDS_MANUAL_CHECK", appealAssist.MeritAssessment);
        Assert.False(string.IsNullOrWhiteSpace(appealAssist.DraftResponse));

        // #5 Task Breakdown & Sprint Retro
        var breakdown = await studentSvc.BreakdownTaskAsync(20, new TaskBreakdownRequest(Title: "Xây dựng trang phân tích công bằng đóng góp"), Ct);
        Assert.True(breakdown.Criteria.Count >= 3);
        Assert.NotEmpty(breakdown.RecommendedAssignees);

        var retro = await studentSvc.GenerateSprintRetroAsync(20, 60, Ct);
        Assert.Equal(3, retro.TotalTasks);
        Assert.Equal(1, retro.CompletedTasks);

        // #6 Pre-flight Submission Checker (FinalSubmission & ReviewGate)
        var preflightFinal = await studentSvc.CheckSubmissionPreflightAsync(20, new SubmissionPreflightRequest("FINAL_SUBMISSION"), Ct);
        Assert.Equal("BLOCKING_ISSUES", preflightFinal.Verdict);
        Assert.Contains(preflightFinal.Checks, c => c.Status == "FAIL");

        var preflightGate = await studentSvc.CheckSubmissionPreflightAsync(20, new SubmissionPreflightRequest("REVIEW_GATE", 70), Ct);
        Assert.Equal("BLOCKING_ISSUES", preflightGate.Verdict);

        // #7 Team Skill-Gap & Working Agreement Draft
        var skillGap = await studentSvc.AnalyzeTeamSkillGapAsync(10, new TeamSkillGapRequest(), Ct);
        Assert.Equal("INTERDISCIPLINARY", skillGap.ProjectMode);
        Assert.NotEmpty(skillGap.CoverageMatrix);

        var agreement = await studentSvc.DraftWorkingAgreementAsync(20, new WorkingAgreementDraftRequest(), Ct);
        Assert.True(agreement.Sections.Count >= 4);

        // #8 Department Weekly Executive Brief
        var brief = await staffSvc.GenerateDepartmentBriefAsync(new DepartmentBriefRequest(), Ct);
        Assert.Equal(1, brief.TotalActiveProjects);
        Assert.Equal(1, brief.PendingAppealsCount);
        Assert.NotEmpty(brief.TopRiskProjects);
        var latestBrief = await staffSvc.GetLatestDepartmentBriefAsync(null, Ct);
        Assert.NotNull(latestBrief);

        // Verify snapshots persisted in unified table
        Assert.True(await db.AiCopilotSnapshots.CountAsync() >= 10);
    }

    [Fact]
    public async Task SensitiveEndpoints_EnforceStrictRbac()
    {
        using var db = SeedFullScenario();
        var studentSvc = CreateService(db, 1, [AppRoles.Student]);

        // Student cannot access calibration, appeals assist, department brief, or evaluator rubric copilot
        await Assert.ThrowsAsync<ForbiddenException>(() => studentSvc.CheckScoreCalibrationAsync(20, Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => studentSvc.AssistAppealReviewAsync(90, Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => studentSvc.GenerateDepartmentBriefAsync(new DepartmentBriefRequest(), Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => studentSvc.GenerateRubricGradingCopilotAsync(83, Ct));

        // Another lecturer not assigned to assignment 83 cannot run rubric copilot for it
        var unassignedLecturerSvc = CreateService(db, 8, [AppRoles.Lecturer]);
        await Assert.ThrowsAsync<ForbiddenException>(() => unassignedLecturerSvc.GenerateRubricGradingCopilotAsync(83, Ct));

        // TK-W0-09: Out-of-scope department staff cannot run score calibration check (Cross-Dept IDOR -> 403 SCOPE_ACCESS_DENIED)
        var otherDeptStaffSvc = CreateService(db, 6, [AppRoles.DepartmentStaff]);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => otherDeptStaffSvc.CheckScoreCalibrationAsync(20, Ct));
        Assert.Equal(WorkflowErrorCodes.ScopeAccessDenied, ex.Code);

        // V4.0 TK-W1-01: Pure ADMIN is blocked from academic AI endpoints (ADMIN_NOT_ACADEMIC_AUTHORITY)
        var pureAdminSvc = CreateService(db, 7, [AppRoles.Admin]);
        var adminBriefEx = await Assert.ThrowsAsync<ForbiddenException>(() => pureAdminSvc.GenerateDepartmentBriefAsync(new DepartmentBriefRequest(), Ct));
        Assert.Equal(WorkflowErrorCodes.AdminNotAcademicAuthority, adminBriefEx.Code);
        var adminCalEx = await Assert.ThrowsAsync<ForbiddenException>(() => pureAdminSvc.CheckScoreCalibrationAsync(20, Ct));
        Assert.Equal(WorkflowErrorCodes.AdminNotAcademicAuthority, adminCalEx.Code);
        var adminAppealEx = await Assert.ThrowsAsync<ForbiddenException>(() => pureAdminSvc.AssistAppealReviewAsync(90, Ct));
        Assert.Equal(WorkflowErrorCodes.AdminNotAcademicAuthority, adminAppealEx.Code);
        var adminSchemeEx = await Assert.ThrowsAsync<ForbiddenException>(() => pureAdminSvc.SuggestEvaluationSchemeAsync(20, Ct));
        Assert.Equal(WorkflowErrorCodes.AdminNotAcademicAuthority, adminSchemeEx.Code);
    }

    [Fact]
    public async Task V4_RubricCopilot_EnforcesCoiAndRevokedAssignment()
    {
        using var db = SeedFullScenario();

        // COI: supervisor (user 3) assigned as evaluator must fail with 409 EVALUATOR_ROLE_CONFLICT
        db.Set<EvaluationAssignment>().Add(new EvaluationAssignment
        {
            Id = 830, ProjectId = 20, EvaluatorId = 3, RubricId = 81, ProjectPeriodId = 1, DepartmentId = 1,
            EvaluationType = "COUNCIL", Status = "ACTIVE", AssignedBy = 4, AssignedAt = Now
        });
        // Revoked assignment for independent evaluator (user 5) must fail with 403 ASSIGNMENT_REVOKED
        db.Set<EvaluationAssignment>().Add(new EvaluationAssignment
        {
            Id = 831, ProjectId = 20, EvaluatorId = 5, RubricId = 81, ProjectPeriodId = 1, DepartmentId = 1,
            EvaluationType = "COUNCIL", Status = "REVOKED", RevokedAt = Now.AddHours(-1), AssignedBy = 4, AssignedAt = Now.AddDays(-1)
        });
        await db.SaveChangesAsync(Ct);

        var supervisorSvc = CreateService(db, 3, [AppRoles.Lecturer]);
        var coiEx = await Assert.ThrowsAsync<ConflictException>(() => supervisorSvc.GenerateRubricGradingCopilotAsync(830, Ct));
        Assert.Equal(WorkflowErrorCodes.EvaluatorRoleConflict, coiEx.Code);

        var evaluatorSvc = CreateService(db, 5, [AppRoles.Lecturer]);
        var revEx = await Assert.ThrowsAsync<ForbiddenException>(() => evaluatorSvc.GenerateRubricGradingCopilotAsync(831, Ct));
        Assert.Equal(WorkflowErrorCodes.AssignmentRevoked, revEx.Code);
    }

    [Fact]
    public async Task V4_SuggestEvaluationScheme_ProducesExactPolV4Decimal_ForInterdisciplinaryAndSingleMajor()
    {
        using var db = SeedFullScenario();
        var staffSvc = CreateService(db, 4, [AppRoles.DepartmentStaff]);

        // Interdisciplinary IT_MKT project 20: 12% COLD + 28% DEFENSE(IsIntegration) + 40% MAJOR_SPECIFIC + 20% INDIVIDUAL(POL-03, RubricId=0, ReqEval=0)
        var inter = await staffSvc.SuggestEvaluationSchemeAsync(20, Ct);
        Assert.Equal("POL_V4_DECIMAL", inter.CalculationVersion);
        Assert.Equal(4.00m, inter.FailingComponentFloor);
        Assert.Equal(5.0m, inter.SuggestedPassThreshold);
        Assert.Equal("IT_MKT", inter.TrackCode);
        Assert.Equal(100m, inter.Components.Sum(c => c.ProjectWeightPercent));
        Assert.Contains(inter.Components, c => c.Scope == "COMMON" && c.Stage == "COLD" && c.StudentWeightPercent == 12m);
        Assert.Contains(inter.Components, c => c.Scope == "COMMON" && c.Stage == "DEFENSE" && c.StudentWeightPercent == 28m && c.IsIntegration);
        Assert.Contains(inter.Components, c => c.Scope == "MAJOR_SPECIFIC" && c.MajorId == 1 && c.StudentWeightPercent == 40m);
        Assert.Contains(inter.Components, c => c.Scope == "MAJOR_SPECIFIC" && c.MajorId == 2 && c.StudentWeightPercent == 40m);
        Assert.Equal(2, inter.Components.Count(c => c.Scope == "INDIVIDUAL" && c.Stage == "ON_GOING" && c.RubricId == 0 && c.RequiredEvaluators == 0 && c.StudentWeightPercent == 20m));

        // Single-major project 21: 30% COLD + 50% DEFENSE + 20% INDIVIDUAL(POL-03)
        db.Teams.Add(new M.Team { Id = 11, AcademicSemesterId = 1, Code = "T11", Name = "Nhóm Đơn Ngành 11", Status = "ACTIVE", CreatedBy = 1 });
        db.Projects.Add(new M.Project { Id = 21, TeamId = 11, Code = "SE-2026-11", Title = "Đồ án SE đơn ngành", Status = "ACTIVE", TrackCode = "IT_ONLY", RowVersion = [1] });
        db.ProjectMajors.Add(new M.ProjectMajor { ProjectId = 21, MajorId = 1 });
        await db.SaveChangesAsync(Ct);

        var single = await staffSvc.SuggestEvaluationSchemeAsync(21, Ct);
        Assert.Equal("POL_V4_DECIMAL", single.CalculationVersion);
        Assert.Equal("IT_ONLY", single.TrackCode);
        Assert.Equal(100m, single.Components.Sum(c => c.ProjectWeightPercent));
        Assert.Contains(single.Components, c => c.Scope == "COMMON" && c.Stage == "COLD" && c.StudentWeightPercent == 30m);
        Assert.Contains(single.Components, c => c.Scope == "COMMON" && c.Stage == "DEFENSE" && c.StudentWeightPercent == 50m);
        Assert.Contains(single.Components, c => c.Scope == "INDIVIDUAL" && c.Stage == "ON_GOING" && c.RubricId == 0 && c.RequiredEvaluators == 0 && c.StudentWeightPercent == 20m);
    }

    [Fact]
    public async Task V4_AppealReviewAssist_SupportsAllThreeQd04Scopes_AndAlwaysNeedsManualCheck()
    {
        using var db = SeedFullScenario();
        db.Set<ResultAppealRequest>().AddRange(
            new ResultAppealRequest { Id = 91, ProjectId = 20, PolicyId = 1, TargetKey = 1, ResultVersion = 1, ComponentId = 1, Scope = "PROCESS_20_ERRATA", ResponsibleDepartmentId = 1, Status = "UNDER_REVIEW", RequestedBy = 1, RequestedAt = Now, Reason = "Sai sót nhập liệu điểm quá trình G2." },
            new ResultAppealRequest { Id = 92, ProjectId = 20, PolicyId = 1, TargetKey = 1, ResultVersion = 1, ComponentId = 1, Scope = "DEFENSE_ABSENT_CONTEST", ResponsibleDepartmentId = 1, Status = "PENDING", RequestedBy = 1, RequestedAt = Now, Reason = "Sinh viên có mặt tại hội đồng nhưng bị đánh vắng." });
        await db.SaveChangesAsync(Ct);

        var staffSvc = CreateService(db, 4, [AppRoles.DepartmentStaff]);
        var r1 = await staffSvc.AssistAppealReviewAsync(91, Ct);
        var r2 = await staffSvc.AssistAppealReviewAsync(92, Ct);
        Assert.Equal("NEEDS_MANUAL_CHECK", r1.MeritAssessment);
        Assert.Equal("NEEDS_MANUAL_CHECK", r2.MeritAssessment);
    }

    [Fact]
    public async Task V4_SubmissionPreflight_FlagsQuarantinedFile_AsBlockingFail()
    {
        using var db = SeedFullScenario();
        db.Deliverables.Add(new M.Deliverable { Id = 500, ProjectId = 20, Title = "Báo cáo tổng kết", Status = "SUBMITTED", EvidenceScope = "COMMON", CreatedBy = 1 });
        db.Set<FinalSubmissionRequirement>().Add(new FinalSubmissionRequirement { ProjectId = 20, DeliverableId = 500 });
        db.DeliverableVersions.Add(new M.DeliverableVersion
        {
            Id = 501, DeliverableId = 500, VersionNumber = 1, Status = "SUBMITTED", SubmittedBy = 1, SubmittedAt = Now, CreatedAt = Now, UpdatedAt = Now,
            Files =
            [
                new M.File
                {
                    Id = 502, UploadedBy = 1, DeliverableVersionId = 501, OriginalFileName = "report.pdf",
                    StoragePath = "k", FileSizeBytes = 100, MimeType = "application/pdf", ScanStatus = "QUARANTINED",
                    CreatedAt = Now, UpdatedAt = Now
                }
            ]
        });
        db.Set<FinalSubmissionDraft>().Add(new FinalSubmissionDraft
        {
            Id = 505, ProjectId = 20, UpdatedBy = 1, UpdatedAt = Now, LockWorkflowStatus = "OPEN",
            Items = [new FinalSubmissionDraftItem { DraftId = 505, DeliverableVersionId = 501 }]
        });
        await db.SaveChangesAsync(Ct);

        var studentSvc = CreateService(db, 1, [AppRoles.Student]);
        var preflight = await studentSvc.CheckSubmissionPreflightAsync(20, new SubmissionPreflightRequest("FINAL_SUBMISSION"), Ct);
        Assert.Equal("BLOCKING_ISSUES", preflight.Verdict);
        Assert.Contains(preflight.Checks, c => c.Status == "FAIL" && c.Detail.Contains("QUARANTINED", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task V4_GroundedProvider_ParsesChatSummaryJson_AndAnswersVietnameseQuestions()
    {
        var provider = new GroundedAiTextGenerationProvider();
        var chatJson = """{"conversationTitle":"Nhóm 10","messages":[{"messageId":"1","senderName":"Khang","body":"Chốt API xong vào thứ 6 nhé, Hà phụ trách OpenAPI?"},{"messageId":"2","senderName":"Hà","body":"Ok chốt! Đang bị chặn bởi VNPay sandbox."}]}""";
        var summaryRaw = await provider.GenerateTextAsync("[CHAT_AI_SUMMARY] Tóm tắt cuộc trò chuyện", chatJson, Ct);
        Assert.Contains("summary", summaryRaw);
        Assert.Contains("decisions", summaryRaw);

        var projectAnswer = await provider.GenerateTextAsync("[PROJECT_ASSISTANT]", "Còn những công việc quá hạn nào và ai đang phụ trách?\n---\nProject: AI-2026-10\nTask quá hạn: Tích hợp thanh toán VNPay (BLOCKED)", Ct);
        Assert.False(string.IsNullOrWhiteSpace(projectAnswer));
    }

    private sealed class StubCurrentUser(long? userId, string[] roles) : ICurrentUser
    {
        public long? UserId => userId;
        public string? Email => "test@test.vn";
        public string? FullName => "Test User";
        public IReadOnlyCollection<string> Roles => roles;
        public IReadOnlyCollection<string> Permissions => [];
        public bool IsAuthenticated => userId.HasValue;
        public bool HasPermission(string permission) => false;
    }

    private sealed class StubProjectAccess(bool allowed) : IProjectAccessService
    {
        public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default) => Task.FromResult(allowed);
    }

    private sealed class StubFileStorage : IFileStorage
    {
        public Task WriteAsync(string key, Stream content, CancellationToken ct) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());
        public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }
}
