using System.Text.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.AcademicTracking;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.Notifications.DTOs;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.Results.Services;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.Infrastructure.Services.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class PhaseBCNotificationsAndDtoEnrichmentTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTime Now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

    private static AipmsDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task ScheduleNotificationQueue_Gate3_And_Defense_UseNaturalVietnameseAndProjectContext()
    {
        using var db = CreateDb();
        db.Users.AddRange(
            new M.User { Id = 1, Email = "student@test.vn", FullName = "Nguyễn Văn A", PasswordHash = "h", Status = "ACTIVE" },
            new M.User { Id = 2, Email = "reviewer@test.vn", FullName = "TS. Trần Văn B", PasswordHash = "h", Status = "ACTIVE" },
            new M.User { Id = 3, Email = "staff@test.vn", FullName = "Cán bộ Khoa", PasswordHash = "h", Status = "ACTIVE" });
        db.Teams.Add(new M.Team { Id = 1, AcademicSemesterId = 1, Code = "T01", Name = "Nhóm 01", Status = "ACTIVE", CreatedBy = 1 });
        db.TeamMembers.Add(new M.TeamMember { Id = 1, TeamId = 1, UserId = 1, IsLeader = true, AcademicSemesterId = 1 });
        db.Projects.Add(new M.Project { Id = 10, TeamId = 1, Code = "SE-2026-01", Title = "Hệ thống quản lý đồ án", Status = "ACTIVE", RowVersion = [1] });
        db.GateReviewerAssignments.Add(new GateReviewerAssignment { Id = 1, GateInstanceId = 100, ReviewerId = 2, Scope = "COMMON", Status = "PENDING_REVIEW", CreatedAt = Now });
        db.Set<DefenseCommitteeMember>().Add(new DefenseCommitteeMember { CommitteeId = 200, UserId = 2, MajorId = 1, Role = "CHAIR" });
        await db.SaveChangesAsync();

        await ScheduleNotificationQueue.Gate3(db, 3, 10, 100, 501, Now, changed: false, Ct);
        await ScheduleNotificationQueue.Gate3(db, 3, 10, 100, 501, Now.AddMinutes(5), changed: true, Ct);
        await ScheduleNotificationQueue.Defense(db, 3, 10, 200, 601, Now, changed: true, Ct);

        var notifications = await db.Notifications.OrderBy(n => n.Id).ToArrayAsync();
        Assert.Equal(3, notifications.Length);

        Assert.Equal("REVIEW_GATE_3_SCHEDULED", notifications[0].NotificationType);
        Assert.Contains("Cổng kiểm duyệt 3 (G3)", notifications[0].Title);
        Assert.Contains("SE-2026-01", notifications[0].Title);
        Assert.DoesNotContain("Review Gate 3", notifications[0].Title);
        Assert.DoesNotContain("Review Gate 3", notifications[0].Content);
        Assert.Contains("Hệ thống quản lý đồ án", notifications[0].Content);

        Assert.Equal("REVIEW_GATE_3_SCHEDULE_CHANGED", notifications[1].NotificationType);
        Assert.Contains("đã thay đổi", notifications[1].Title);
        Assert.DoesNotContain("Review Gate 3", notifications[1].Title);

        Assert.Equal("DEFENSE_SCHEDULE_CHANGED", notifications[2].NotificationType);
        Assert.Contains("SE-2026-01", notifications[2].Title);
        Assert.Contains("Hệ thống quản lý đồ án", notifications[2].Content);
    }

    [Fact]
    public async Task AccountSecurityRepository_GetAuditLogs_EnrichesActorNameAndEntityDisplayName()
    {
        using var db = CreateDb();
        db.Users.AddRange(
            new M.User { Id = 1, Email = "admin@test.vn", FullName = "Quản trị viên", PasswordHash = "h", Status = "ACTIVE" },
            new M.User { Id = 2, Email = "sv01@test.vn", FullName = "Lê Thị C", PasswordHash = "h", Status = "ACTIVE" });
        db.Teams.Add(new M.Team { Id = 5, AcademicSemesterId = 1, Code = "TM5", Name = "Nhóm Nghiên Cứu AI", Status = "ACTIVE", CreatedBy = 2 });
        db.Projects.Add(new M.Project { Id = 7, TeamId = 5, Code = "AI-101", Title = "Phân tích tiến độ", Status = "ACTIVE", RowVersion = [1] });
        db.AuditLogs.AddRange(
            new M.AuditLog { Id = 1, ActorUserId = 1, Action = "USER_UPDATED", EntityType = "USER", EntityId = "2", Outcome = "SUCCESS", OccurredAt = Now },
            new M.AuditLog { Id = 2, ActorUserId = 1, Action = "PROJECT_APPROVED", EntityType = "PROJECT", EntityId = "7", Outcome = "SUCCESS", OccurredAt = Now.AddMinutes(1) },
            new M.AuditLog { Id = 3, ActorUserId = 2, Action = "TEAM_CREATED", EntityType = "TEAM", EntityId = "5", Outcome = "SUCCESS", OccurredAt = Now.AddMinutes(2) });
        await db.SaveChangesAsync();

        var repo = new AccountSecurityRepository(db);
        var page = await repo.GetAuditLogsAsync(null, null, null, null, null, null, 1, 10, Ct);

        Assert.Equal(3, page.Items.Count);
        var teamLog = page.Items.Single(x => x.EntityType == "TEAM");
        Assert.Equal("Lê Thị C", teamLog.ActorName);
        Assert.Equal("Nhóm Nghiên Cứu AI", teamLog.EntityDisplayName);

        var projectLog = page.Items.Single(x => x.EntityType == "PROJECT");
        Assert.Equal("Quản trị viên", projectLog.ActorName);
        Assert.Equal("AI-101 - Phân tích tiến độ", projectLog.EntityDisplayName);

        var userLog = page.Items.Single(x => x.EntityType == "USER");
        Assert.Equal("Quản trị viên", userLog.ActorName);
        Assert.Equal("Lê Thị C (sv01@test.vn)", userLog.EntityDisplayName);
    }

    [Fact]
    public async Task DeliverableRepository_EnrichesUploadedByAndSubmittedByNames()
    {
        using var db = CreateDb();
        db.Users.AddRange(
            new M.User { Id = 1, Email = "s1@test.vn", FullName = "Phạm Minh D", PasswordHash = "h", Status = "ACTIVE" },
            new M.User { Id = 2, Email = "s2@test.vn", FullName = "Hoàng Anh E", PasswordHash = "h", Status = "ACTIVE" });
        db.Teams.Add(new M.Team { Id = 1, AcademicSemesterId = 1, Code = "T1", Name = "T1", Status = "ACTIVE", CreatedBy = 1 });
        db.Projects.Add(new M.Project { Id = 1, TeamId = 1, Code = "PRJ-01", Title = "Đồ án 01", Status = "ACTIVE", RowVersion = [1] });
        db.Deliverables.Add(new M.Deliverable { Id = 10, ProjectId = 1, Title = "Báo cáo SRS", Status = "SUBMITTED", CreatedBy = 1, CreatedAt = Now, UpdatedAt = Now });
        db.DeliverableVersions.Add(new M.DeliverableVersion { Id = 100, DeliverableId = 10, VersionNumber = 2, SubmittedBy = 1, Status = "SUBMITTED", SubmittedAt = Now, CreatedAt = Now, UpdatedAt = Now });
        db.Files.Add(new M.File
        {
            Id = 500, DeliverableVersionId = 100, UploadedBy = 2, OriginalFileName = "srs-v2.pdf",
            StoredFileName = "k1", StoragePath = "k1", MimeType = "application/pdf", FileSizeBytes = 1024,
            ChecksumSha256 = new string('a', 64), CreatedAt = Now, UpdatedAt = Now
        });
        await db.SaveChangesAsync();

        var repo = new DeliverableRepository(db, new ConfigurationBuilder().Build(), NullLogger<DeliverableRepository>.Instance);
        var version = await repo.GetVersionAsync(100, Ct);
        Assert.NotNull(version);
        Assert.Equal("Phạm Minh D", version.SubmittedByName);
        Assert.Equal("Hoàng Anh E", Assert.Single(version.Files).UploadedByName);

        var versionsPage = await repo.VersionsAsync(10, 1, 10, Ct);
        Assert.Equal("Phạm Minh D", Assert.Single(versionsPage.Items).SubmittedByName);

        var storedFile = await repo.GetFileAsync(500, Ct);
        Assert.NotNull(storedFile);
        Assert.Equal("Hoàng Anh E", storedFile.Dto.UploadedByName);

        var filesPage = await repo.FilesAsync(new(1, null, 1, 10), Ct);
        Assert.Equal("Hoàng Anh E", Assert.Single(filesPage.Items).UploadedByName);
    }

    [Fact]
    public async Task StudentQualificationRepository_EnrichesVerifiedByName()
    {
        using var db = CreateDb();
        var org = new M.Organization { Id = 1, Code = "ORG", Name = "ĐH", IsActive = true };
        var dept = new M.Department { Id = 1, Organization = org, Code = "CNTT", Name = "CNTT", IsActive = true };
        var major = new M.Major { Id = 1, Department = dept, Code = "SE", Name = "Kỹ thuật phần mềm", IsActive = true };
        db.Majors.Add(major);
        db.Users.AddRange(
            new M.User { Id = 10, Email = "sv@test.vn", FullName = "Sinh viên K", StudentCode = "SV001", PasswordHash = "h", Status = "ACTIVE", Department = dept, Major = major },
            new M.User { Id = 20, Email = "cb@test.vn", FullName = "Cán bộ Xác Minh", PasswordHash = "h", Status = "ACTIVE", Department = dept });
        db.Set<StudentQualification>().Add(new StudentQualification
        {
            Id = 1, UserId = 10, OrganizationId = 1, QualificationType = "CAPSTONE_READINESS",
            TrainingStatus = "TRAINING_COMPLETED", VerificationStatus = "VERIFIED",
            VerifiedBy = 20, VerifiedAt = Now, CreatedAt = Now, UpdatedAt = Now, ConcurrencyToken = Guid.NewGuid()
        });
        await db.SaveChangesAsync();

        var repo = new StudentQualificationRepository(db);
        var one = await repo.GetAsync(1, Ct);
        Assert.NotNull(one);
        Assert.Equal("Cán bộ Xác Minh", one.VerifiedByName);

        var queue = await repo.SearchVerificationQueueAsync(1, 1, null, null, 1, 10, Ct);
        Assert.Equal("Cán bộ Xác Minh", Assert.Single(queue.Items).VerifiedByName);
    }

    [Fact]
    public async Task SupervisorAssignmentRepository_EnrichesReplacesSupervisorName()
    {
        using var db = CreateDb();
        db.Users.AddRange(
            new M.User { Id = 1, Email = "gv1@test.vn", FullName = "TS. Nguyễn Cũ", PasswordHash = "h", Status = "ACTIVE" },
            new M.User { Id = 2, Email = "gv2@test.vn", FullName = "ThS. Trần Mới", PasswordHash = "h", Status = "ACTIVE" });
        db.SupervisorProfiles.AddRange(
            new M.SupervisorProfile { Id = 1, UserId = 1, IsAvailable = true },
            new M.SupervisorProfile { Id = 2, UserId = 2, IsAvailable = true });
        db.Teams.Add(new M.Team { Id = 1, AcademicSemesterId = 1, Code = "T1", Name = "T1", Status = "ACTIVE", CreatedBy = 1 });
        db.Projects.Add(new M.Project { Id = 1, TeamId = 1, Code = "PRJ-1", Title = "Project", Status = "ACTIVE", RowVersion = [1] });
        db.SupervisorAssignments.AddRange(
            new M.SupervisorAssignment { Id = 10, ProjectId = 1, SupervisorProfileId = 1, SupervisorRequestId = 1, IsPrimary = true, AssignmentType = "PRIMARY", AssignedAt = Now.AddDays(-5), EndedAt = Now },
            new M.SupervisorAssignment { Id = 11, ProjectId = 1, SupervisorProfileId = 2, SupervisorRequestId = 2, IsPrimary = true, AssignmentType = "PRIMARY", AssignedAt = Now, ReplacesAssignmentId = 10 });
        await db.SaveChangesAsync();

        var repo = new SupervisorAssignmentRepository(db, null!);
        var replacement = await repo.GetAsync(11, Ct);
        Assert.NotNull(replacement);
        Assert.Equal("TS. Nguyễn Cũ", replacement.ReplacesSupervisorName);
        Assert.Equal("TS. Nguyễn Cũ", replacement.ToDto().ReplacesSupervisorName);
    }

    [Fact]
    public async Task MeetingGovernanceService_DecisionsAndActions_EnrichUserNames()
    {
        using var db = CreateDb();
        db.Users.AddRange(
            new M.User { Id = 1, Email = "lead@test.vn", FullName = "Trưởng nhóm A", PasswordHash = "h", Status = "ACTIVE" },
            new M.User { Id = 2, Email = "mem@test.vn", FullName = "Thành viên B", PasswordHash = "h", Status = "ACTIVE" });
        db.Teams.Add(new M.Team { Id = 1, AcademicSemesterId = 1, Code = "T1", Name = "T1", Status = "ACTIVE", CreatedBy = 1 });
        db.Projects.Add(new M.Project { Id = 1, TeamId = 1, Code = "PRJ-M", Title = "Đồ án Họp", Status = "ACTIVE", RowVersion = [1] });
        db.Meetings.Add(new M.Meeting { Id = 10, ProjectId = 1, Title = "Họp Sprint 1", CreatedBy = 1, Status = "COMPLETED", StartAt = Now.AddHours(-2), EndAt = Now.AddHours(-1), ConcurrencyToken = Guid.NewGuid() });
        db.MeetingDecisions.Add(new MeetingDecision { Id = 1, MeetingId = 10, Content = "Chốt kiến trúc", DecidedBy = 1, DecidedAt = Now, CreatedAt = Now });
        db.MeetingActionItems.Add(new MeetingActionItem { Id = 1, MeetingId = 10, Title = "Thiết kế DB", AssigneeUserId = 2, CreatedBy = 1, Status = "OPEN", CreatedAt = Now, UpdatedAt = Now, ConcurrencyToken = Guid.NewGuid() });
        await db.SaveChangesAsync();

        var service = new MeetingGovernanceService(db, new StubUser(1), new AllowAccess(), new NullAudit(), TimeProvider.System);
        var decisions = await service.Decisions(10, 1, 10, Ct);
        Assert.Equal("Trưởng nhóm A", Assert.Single(decisions.Items).DecidedByName);

        var actions = await service.Actions(10, 1, 10, Ct);
        var action = Assert.Single(actions.Items);
        Assert.Equal("Thành viên B", action.AssigneeName);
        Assert.Equal("Trưởng nhóm A", action.CreatedByName);
    }

    [Fact]
    public async Task ProjectRepository_GetAcademicReview_EnrichesDepartmentDecisionDecidedByName()
    {
        using var db = CreateDb();
        db.Users.Add(new M.User { Id = 50, Email = "dean@test.vn", FullName = "PGS.TS. Khoa CNTT", PasswordHash = "h", Status = "ACTIVE" });
        db.Teams.Add(new M.Team { Id = 1, AcademicSemesterId = 1, Code = "T1", Name = "T1", Status = "ACTIVE", CreatedBy = 1 });
        db.Projects.Add(new M.Project { Id = 1, TeamId = 1, Code = "PRJ-H", Title = "Liên ngành", Status = "UNDER_REVIEW", RowVersion = [1, 2, 3] });
        var evidence = new RegistrationEvidence(
            new("INTERDISCIPLINARY", null, 1, [new(1, 1, 3, "SE"), new(2, 1, 3, "BA")], Guid.NewGuid()),
            new(2, 5, 2, "v1"), 1, Now.AddDays(-1), Now.AddDays(5), [], [1, 2],
            MajorDepartmentIds: new Dictionary<long, long> { [1] = 1, [2] = 2 });
        db.Set<ProjectRegistrationSnapshot>().Add(new ProjectRegistrationSnapshot
        {
            Id = 10, ProjectId = 1, ProjectPeriodId = 1, LeadDepartmentId = 1, SubmittedBy = 1, SubmittedAt = Now,
            SnapshotJson = JsonSerializer.Serialize(evidence),
            Decisions =
            [
                new ProjectDepartmentDecision { SnapshotId = 10, DepartmentId = 1, Decision = "APPROVED", DecidedBy = 50, DecidedAt = Now },
                new ProjectDepartmentDecision { SnapshotId = 10, DepartmentId = 2, Decision = "PENDING", DecidedBy = null }
            ]
        });
        await db.SaveChangesAsync();

        var repo = new ProjectRepository(db);
        var review = await repo.GetAcademicReviewAsync(1, Ct);
        Assert.NotNull(review.LatestSubmission);
        var d1 = review.LatestSubmission.Decisions.Single(d => d.DepartmentId == 1);
        var d2 = review.LatestSubmission.Decisions.Single(d => d.DepartmentId == 2);
        Assert.Equal("PGS.TS. Khoa CNTT", d1.DecidedByName);
        Assert.Null(d2.DecidedByName);
    }

    [Fact]
    public async Task PublishedResultResolver_EnrichesLegacyResultEvaluatorAndMajorNames()
    {
        using var db = CreateDb();
        var org = new M.Organization { Id = 1, Code = "O", Name = "O", IsActive = true };
        var dept = new M.Department { Id = 1, Organization = org, Code = "D", Name = "D", IsActive = true };
        db.Majors.Add(new M.Major { Id = 7, Department = dept, Code = "SE", Name = "Kỹ thuật phần mềm", IsActive = true });
        db.Users.Add(new M.User { Id = 99, Email = "ev@test.vn", FullName = "TS. Giám Khảo", PasswordHash = "h", Status = "ACTIVE", Department = dept });
        await db.SaveChangesAsync();

        var legacyDto = new ProjectResultDto(
            1, 1, 1, 8.5m, 5.0m, "PASSED", "V2", 1, Now, "tok",
            [new ResultContributionDto(1, 10, 99, 1, 100m, 8.5m, "ct")],
            Breakdown: new ResultBreakdownDto("Scheme", 1, "DECIMAL_V2", false, false, "APPROVED", "READY", 8.5m, 8.5m, 8.5m,
                [new ResultMajorScoreDto(7, 8.5m)], []));

        var enriched = await PublishedResultResolver.EnrichAsync(db, legacyDto, Ct);
        Assert.Equal("TS. Giám Khảo", Assert.Single(enriched.Contributions).EvaluatorName);
        Assert.Equal("Kỹ thuật phần mềm", Assert.Single(enriched.Breakdown!.MajorBreakdown).MajorName);
    }

    [Fact]
    public void AdditiveDtoRecords_RemainBackwardsCompatibleWhenOptionalFieldsAreOmittedFromJson()
    {
        var member = JsonSerializer.Deserialize<CommitteeMemberInput>("""{"UserId":5,"MajorId":2,"Role":"CHAIR"}""")!;
        Assert.Equal(5, member.UserId);
        Assert.Equal(2, member.MajorId);
        Assert.Equal("CHAIR", member.Role);
        Assert.Null(member.FullName);
        Assert.Null(member.MajorName);

        var alloc = JsonSerializer.Deserialize<DefenseAllocation>("""{"ProjectId":1,"CommitteeId":2,"RoomId":3,"RoomCode":"A201","Date":"2026-12-01","SlotNumber":1,"StartAt":"2026-12-01T01:00:00Z","EndAt":"2026-12-01T02:00:00Z"}""")!;
        Assert.Equal("A201", alloc.RoomCode);
        Assert.Null(alloc.ProjectCode);
        Assert.Null(alloc.ProjectTitle);

        var g3 = JsonSerializer.Deserialize<ReviewGate3Allocation>("""{"ProjectId":1,"GateInstanceId":9,"RoomId":3,"RoomCode":"B302","Date":"2026-12-01","SlotNumber":2,"StartAt":"2026-12-01T02:00:00Z","EndAt":"2026-12-01T03:00:00Z"}""")!;
        Assert.Equal("B302", g3.RoomCode);
        Assert.Null(g3.ProjectCode);

        var weight = JsonSerializer.Deserialize<SchemeEvaluatorWeight>("""{"EvaluatorId":11,"WeightPercent":50}""")!;
        Assert.Equal(11, weight.EvaluatorId);
        Assert.Null(weight.EvaluatorName);

        var amendment = JsonSerializer.Deserialize<ScoreAmendmentInput>("""{"EvaluationId":1,"CriterionId":2,"Score":8.5,"Reason":"Fix"}""")!;
        Assert.Equal(8.5m, amendment.Score);
        Assert.Null(amendment.CriterionName);

        var req = new FinalRequirementDto(10, "SRS", 20, true, 3);
        Assert.Equal(3, req.SelectedVersionNumber);
    }

    [Fact]
    public void NotificationDtoMapper_DropsDeadKeysAndMapsAllActiveEmittedTypes()
    {
        Assert.Equal("notification.appeal.requested", NotificationDtoMapper.ResolveTitleKey("APPEAL_REQUESTED"));
        Assert.Equal("notification.appeal.reviewed", NotificationDtoMapper.ResolveTitleKey("APPEAL_REVIEWED"));
        Assert.Equal("notification.reviewGate.updated", NotificationDtoMapper.ResolveTitleKey("REVIEW_GATE_UPDATED"));
        Assert.Equal("notification.projectRisk.alert", NotificationDtoMapper.ResolveTitleKey("PROJECT_RISK_ALERT"));
        Assert.Equal("notification.defense.scheduleChanged", NotificationDtoMapper.ResolveTitleKey("DEFENSE_SCHEDULE_CHANGED"));
        Assert.Equal("notification.reviewGate3.scheduled", NotificationDtoMapper.ResolveTitleKey("REVIEW_GATE_3_SCHEDULED"));
        Assert.Equal("notification.reviewGate3.scheduleChanged", NotificationDtoMapper.ResolveTitleKey("REVIEW_GATE_3_SCHEDULE_CHANGED"));
        // Dead keys fall through to generic camelCase fallback instead of stale explicit branches
        Assert.Equal("notification.appealSubmitted", NotificationDtoMapper.ResolveTitleKey("APPEAL_SUBMITTED"));
        Assert.Equal("notification.deadlineReminder", NotificationDtoMapper.ResolveTitleKey("DEADLINE_REMINDER"));
    }

    [Fact]
    public async Task ReviewGateService_Matrix_And_Timeline_PopulateProjectCode_SubmittedByName_And_VietnameseNotifications()
    {
        using var db = CreateDb();
        var org = new M.Organization { Id = 1, Code = "ORG", Name = "ĐH", IsActive = true };
        var dept = new M.Department { Id = 1, Organization = org, Code = "CNTT", Name = "Khoa CNTT", IsActive = true };
        var major = new M.Major { Id = 1, Department = dept, Code = "SE", Name = "Kỹ thuật phần mềm", IsActive = true };
        db.Majors.Add(major);
        db.Roles.AddRange(
            new M.Role { Id = 1, Code = "ADMIN", Name = "Admin" },
            new M.Role { Id = 2, Code = "STUDENT", Name = "Student" },
            new M.Role { Id = 3, Code = "DEPARTMENT_STAFF", Name = "Staff" });
        db.Users.AddRange(
            new M.User { Id = 1, Email = "admin@test.vn", FullName = "Quản trị viên", PasswordHash = "h", Status = "ACTIVE", Department = dept, UserRoleUsers = [new M.UserRole { RoleId = 1 }] },
            new M.User { Id = 2, Email = "lead@test.vn", FullName = "Nguyễn Trưởng Nhóm", PasswordHash = "h", Status = "ACTIVE", Department = dept, Major = major, UserRoleUsers = [new M.UserRole { RoleId = 2 }] },
            new M.User { Id = 3, Email = "staff@test.vn", FullName = "Cán bộ Bộ môn", PasswordHash = "h", Status = "ACTIVE", Department = dept, UserRoleUsers = [new M.UserRole { RoleId = 3 }] });
        db.Teams.Add(new M.Team { Id = 1, AcademicSemesterId = 1, Code = "T1", Name = "Nhóm 1", Status = "ACTIVE", CreatedBy = 2 });
        db.TeamMembers.Add(new M.TeamMember { Id = 1, TeamId = 1, UserId = 2, IsLeader = true, AcademicSemesterId = 1 });
        db.Projects.Add(new M.Project { Id = 1, TeamId = 1, Code = "SE-G1-01", Title = "Đồ án Kiểm duyệt", Status = "ACTIVE", RowVersion = [1] });
        db.ProjectMajors.Add(new M.ProjectMajor { ProjectId = 1, MajorId = 1 });
        var gateToken = Guid.NewGuid();
        var gate = new ProjectGateInstance
        {
            Id = 10, ProjectId = 1, TemplateId = 1, TemplateVersion = 1, Code = "G4", Sequence = 4,
            Title = "Cổng 4", Objective = "Tổng kết", Status = "IN_REVIEW", ReviewEligibility = "READY",
            Required = true, BlockNextGate = false, BlocksDefense = true, CreatedAt = Now, ConcurrencyToken = gateToken,
            Requirements =
            [
                new ProjectGateRequirement
                {
                    Id = 20, Code = "REQ1", Title = "Báo cáo", Type = "EXTERNAL_LINK", Scope = "COMMON",
                    Required = true, VerificationStatus = "SUBMITTED",
                    Evidence =
                    [
                        new GateEvidence
                        {
                            Id = 30, ProjectId = 1, RequirementId = 20, Version = 1, ArtifactName = "https://example.com/report",
                            LinkUrl = "https://example.com/report", ContentType = "text/uri-list", SubmittedBy = 2, SubmittedAt = Now, Status = "SUBMITTED"
                        }
                    ]
                }
            ]
        };
        db.ProjectGateInstances.Add(gate);
        var evidence = new RegistrationEvidence(
            new("SINGLE_MAJOR", 1, 1, [new(1, 1, 3, "SE")], Guid.NewGuid()),
            new(1, 5, 1, "v1"), 1, Now.AddDays(-1), Now.AddDays(5), [new(2, "Nguyễn Trưởng Nhóm", 1, true)], [1],
            MajorDepartmentIds: new Dictionary<long, long> { [1] = 1 });
        db.Set<ProjectRegistrationSnapshot>().Add(new ProjectRegistrationSnapshot
        {
            Id = 1, ProjectId = 1, ProjectPeriodId = 1, LeadDepartmentId = 1, SubmittedBy = 2, SubmittedAt = Now,
            SnapshotJson = JsonSerializer.Serialize(evidence)
        });
        await db.SaveChangesAsync();

        var adminService = new ReviewGateService(db, new StubUser(1), PassthroughProjectRepo.Create(), new NullAudit(), new FixedClock(Now), null!, null!);
        var matrix = await adminService.Matrix(null, null, Ct);
        var row = Assert.Single(matrix);
        Assert.Equal("SE-G1-01", row.ProjectCode);

        var timeline = await adminService.Timeline(1, Ct);
        var artifact = timeline.Gates.Single().Requirements.Single().LatestArtifact;
        Assert.NotNull(artifact);
        Assert.Equal("Nguyễn Trưởng Nhóm", artifact.SubmittedByName);

        var staffService = new ReviewGateService(db, new StubUser(3), PassthroughProjectRepo.Create(), new NullAudit(), new FixedClock(Now), null!, null!);
        await staffService.UpdateDecision(1, "G4", new("PASSED", gateToken.ToString("N")), Ct);

        var notice = Assert.Single(await db.Notifications.ToArrayAsync());
        Assert.Equal("REVIEW_GATE_UPDATED", notice.NotificationType);
        Assert.Contains("G4", notice.Title);
        Assert.Contains("SE-G1-01", notice.Title);
        Assert.Contains("Đạt", notice.Content);
        Assert.Contains("Cán bộ Bộ môn", notice.Content);
        Assert.DoesNotContain("PASSED", notice.Content);
        Assert.DoesNotContain("Review gate", notice.Title);
    }

    [Theory]
    [InlineData("CRITICAL", "Nghiêm trọng")]
    [InlineData("HIGH", "Cao")]
    [InlineData("MEDIUM", "Trung bình")]
    [InlineData("LOW", "Thấp")]
    public void RiskAlertService_RiskLevelLabel_MapsRawEnumsToNaturalVietnamese(string rawLevel, string expectedVietnamese)
    {
        var label = RiskAlertService.RiskLevelLabel(rawLevel);
        Assert.Equal(expectedVietnamese, label);
        Assert.DoesNotContain(rawLevel, label);
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private class PassthroughProjectRepo : System.Reflection.DispatchProxy
    {
        public static AIPMS.Application.Features.Projects.Abstractions.IProjectRepository Create() =>
            Create<AIPMS.Application.Features.Projects.Abstractions.IProjectRepository, PassthroughProjectRepo>();

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "InTransactionAsync" && args is [Delegate action, CancellationToken ct])
                return action.DynamicInvoke(ct);
            throw new NotImplementedException(targetMethod?.Name);
        }
    }

    private sealed class StubUser(long id) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => id;
        public string? Email => null;
        public string? FullName => null;
        public IReadOnlyCollection<string> Roles => ["STUDENT"];
    }

    private sealed class AllowAccess : IProjectAccessService
    {
        public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class NullAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
