using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.AI.Providers;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.Meetings.Video;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Services.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class MeetingVideoAiServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc);
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static AipmsDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (AipmsDbContext Db, M.Meeting Meeting) SeedMeeting(string status = "SCHEDULED")
    {
        var db = CreateDb();
        db.Users.AddRange(
            new M.User { Id = 1, Email = "leader@test.vn", FullName = "Nguyễn Minh Khang", PasswordHash = "h", Status = "ACTIVE" },
            new M.User { Id = 2, Email = "member@test.vn", FullName = "Trần Thu Hà", PasswordHash = "h", Status = "ACTIVE" },
            new M.User { Id = 3, Email = "gvhd@test.vn", FullName = "TS. Lê Quang Huy", PasswordHash = "h", Status = "ACTIVE" },
            new M.User { Id = 4, Email = "outsider@test.vn", FullName = "Người ngoài", PasswordHash = "h", Status = "ACTIVE" });

        db.Teams.Add(new M.Team { Id = 10, AcademicSemesterId = 1, Code = "T10", Name = "Nhóm 10", Status = "ACTIVE", CreatedBy = 1 });
        db.TeamMembers.AddRange(
            new M.TeamMember { Id = 1, TeamId = 10, UserId = 1, IsLeader = true, AcademicSemesterId = 1 },
            new M.TeamMember { Id = 2, TeamId = 10, UserId = 2, IsLeader = false, AcademicSemesterId = 1 });

        db.Projects.Add(new M.Project { Id = 20, TeamId = 10, Code = "AI-2026-10", Title = "Nền tảng hỗ trợ họp AI", Status = "ACTIVE", RowVersion = [1] });
        db.SupervisorProfiles.Add(new M.SupervisorProfile { Id = 30, UserId = 3 });
        db.SupervisorAssignments.Add(new M.SupervisorAssignment { Id = 31, ProjectId = 20, SupervisorProfileId = 30, IsPrimary = true });

        db.Milestones.Add(new M.Milestone { Id = 40, ProjectId = 20, Title = "Mốc 2: Hoàn thiện API", Status = "IN_PROGRESS", SortOrder = 1, CreatedBy = 1 });
        db.Tasks.AddRange(
            new M.Task { Id = 41, MilestoneId = 40, Title = "Tích hợp thanh toán VNPay", Status = "BLOCKED", Priority = "HIGH", DueAt = Now.AddDays(-1), CreatedBy = 1 },
            new M.Task { Id = 42, MilestoneId = 40, Title = "Viết tài liệu OpenAPI", Status = "IN_PROGRESS", Priority = "MEDIUM", DueAt = Now.AddDays(3), CreatedBy = 1 });

        var meeting = new M.Meeting
        {
            Id = 50,
            ProjectId = 20,
            Title = "Họp tiến độ Sprint 4",
            Agenda = "- Kiểm tra tiến độ API\n- Rà soát vướng mắc tích hợp\n- Chốt kế hoạch demo",
            StartAt = Now.AddMinutes(-15),
            Status = status,
            MeetingDeliveryMode = "REMOTE",
            VideoChannel = "IN_APP_VIDEO",
            CreatedBy = 1,
            ConcurrencyToken = Guid.NewGuid()
        };
        db.Meetings.Add(meeting);
        db.MeetingParticipants.AddRange(
            new M.MeetingParticipant { Id = 1, MeetingId = 50, UserId = 1, AttendanceStatus = "INVITED" },
            new M.MeetingParticipant { Id = 2, MeetingId = 50, UserId = 2, AttendanceStatus = "INVITED" },
            new M.MeetingParticipant { Id = 3, MeetingId = 50, UserId = 3, AttendanceStatus = "INVITED" });

        db.SaveChanges();
        return (db, meeting);
    }

    private static MeetingVideoAiService CreateService(
        AipmsDbContext db,
        long actorId,
        bool canAccess = true,
        IAiTextGenerationProvider? provider = null,
        string[]? roles = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AiAssistant:TimeoutSeconds"] = "30" })
            .Build();
        return new MeetingVideoAiService(
            db,
            new StubCurrentUser(actorId, roles ?? ["STUDENT"]),
            new StubProjectAccess(canAccess),
            new FixedTimeProvider(Now),
            config,
            new StubHttpClientFactory(),
            provider ?? new GroundedAiTextGenerationProvider());
    }

    [Fact]
    public async Task AppendSegment_IsIdempotentByClientSegmentId_AndComputesSpeakerStats()
    {
        var (db, meeting) = SeedMeeting();
        using var _ = db;

        var leaderSvc = CreateService(db, actorId: 1);
        var memberSvc = CreateService(db, actorId: 2);

        var first = await leaderSvc.AppendSegmentAsync(meeting.Id, new CreateTranscriptSegmentRequest(
            "Chào cả nhóm, hôm nay mình chốt tiến độ API và phần demo tuần sau.",
            "vi-VN", "WEB_SPEECH", null, false, "seg-uuid-1"), Ct);

        var duplicate = await leaderSvc.AppendSegmentAsync(meeting.Id, new CreateTranscriptSegmentRequest(
            "Nội dung trùng client id không được ghi đè",
            "vi-VN", "WEB_SPEECH", null, false, "seg-uuid-1"), Ct);

        Assert.Equal(first.Id, duplicate.Id);
        Assert.Equal("Nguyễn Minh Khang", first.SpeakerName);

        var second = await memberSvc.AppendSegmentAsync(meeting.Id, new CreateTranscriptSegmentRequest(
            "Em sẽ hoàn thành phần viết tài liệu OpenAPI vào thứ Sáu.",
            "vi-VN", "WEB_SPEECH", null, true, "seg-uuid-2"), Ct);

        var feed = await leaderSvc.GetWorkspaceAsync(meeting.Id, null, Ct);
        Assert.Equal(2, feed.TotalSegments);
        Assert.Equal(second.Id, feed.LastSegmentId);
        Assert.Equal(2, feed.SpeakerStats.Count);
        Assert.True(feed.AiAvailable);
        Assert.False(feed.AudioSttConfigured);

        var delta = await leaderSvc.GetWorkspaceAsync(meeting.Id, first.Id, Ct);
        Assert.Single(delta.Segments);
        Assert.Equal(second.Id, delta.Segments[0].Id);
    }

    [Fact]
    public async Task GenerateSummary_GroundsMembersAndCitations_AndPersistsInsight()
    {
        var (db, meeting) = SeedMeeting();
        using var _ = db;

        var stubAi = new StubAiProvider(_ => JsonSerializer.Serialize(new
        {
            executiveSummary = "Nhóm thống nhất hoàn thiện API và giao Trần Thu Hà viết tài liệu OpenAPI.",
            currentTopic = "Phân công Sprint 4",
            formattedMinutes = "# Biên bản Sprint 4\n- Chốt giao tài liệu OpenAPI cho Trần Thu Hà.",
            keyTopics = new[] { new { title = "Tiến độ API", summary = "Rà soát API và OpenAPI", speakers = new[] { "Nguyễn Minh Khang", "Trần Thu Hà" } } },
            decisions = new[] { "Thống nhất giữ nguyên cấu trúc API hiện tại." },
            actionItems = new object[]
            {
                new { title = "Hoàn thành tài liệu OpenAPI", description = "Cập nhật đủ endpoint mới", ownerName = "Trần Thu Hà", dueAt = "2026-11-28T10:00:00Z", priority = "HIGH", evidenceQuote = "Em sẽ hoàn thành OpenAPI", sourceSegmentId = 1 },
                new { title = "Việc với người không tồn tại", description = "Kiểm tra grounding", ownerName = "Người Bịa Đặt", dueAt = (string?)null, priority = "INVALID", evidenceQuote = "quote", sourceSegmentId = 999999 }
            },
            blockersAndRisks = new[] { "Tích hợp VNPay đang bị chặn." },
            openQuestions = new[] { "Khi nào có tài khoản sandbox VNPay?" },
            agendaCoverage = new[] { new { item = "Kiểm tra tiến độ API", status = "COVERED", note = "Đã trao đổi kỹ" } },
            speakerInsights = new[] { new { speakerName = "Trần Thu Hà", summary = "Chủ động nhận viết tài liệu.", keyContributions = new[] { "Nhận hoàn thành OpenAPI" } } },
            suggestedNextQuestions = new[] { "Ai hỗ trợ tháo gỡ lỗi VNPay?" },
            nextMeetingSuggestion = new { title = "Review trước demo", purpose = "Chạy thử luồng hoàn chỉnh", suggestedTimeNote = "Thứ Sáu 15:00" }
        }));

        var svc = CreateService(db, actorId: 1, provider: stubAi);
        var seg = await svc.AppendSegmentAsync(meeting.Id, new CreateTranscriptSegmentRequest(
            "Thống nhất giữ nguyên API và Trần Thu Hà sẽ hoàn thành OpenAPI vào thứ Bảy."), Ct);

        var insight = await svc.GenerateSummaryAsync(meeting.Id, new GenerateMeetingAiSummaryRequest("FULL_MINUTES"), Ct);

        Assert.Equal("COMPLETED", insight.Status);
        Assert.Equal("FULL_MINUTES", insight.InsightType);
        Assert.Equal(2, insight.ActionItems.Count);
        Assert.Equal(2L, insight.ActionItems[0].AssigneeUserId);
        Assert.Equal("Trần Thu Hà", insight.ActionItems[0].OwnerName);
        Assert.Equal(seg.Id, insight.ActionItems[0].SourceSegmentId);
        Assert.Equal("HIGH", insight.ActionItems[0].Priority);

        Assert.Null(insight.ActionItems[1].AssigneeUserId);
        Assert.Null(insight.ActionItems[1].OwnerName);
        Assert.Null(insight.ActionItems[1].SourceSegmentId);
        Assert.Equal("MEDIUM", insight.ActionItems[1].Priority);

        var feed = await svc.GetWorkspaceAsync(meeting.Id, null, Ct);
        Assert.NotNull(feed.LatestInsight);
        Assert.Equal(insight.Id, feed.LatestInsight!.Id);

        var history = await svc.GetInsightsAsync(meeting.Id, 10, Ct);
        Assert.Single(history);
    }

    [Fact]
    public async Task AskAndCoach_WorkEndToEndWithGroundedProvider()
    {
        var (db, meeting) = SeedMeeting();
        using var _ = db;

        var svc = CreateService(db, actorId: 3, roles: ["SUPERVISOR"]);
        var seg1 = await svc.AppendSegmentAsync(meeting.Id, new CreateTranscriptSegmentRequest(
            "Nhóm đã hoàn thành xong phần viết tài liệu OpenAPI rồi ạ."), Ct);
        await svc.ToggleHighlightAsync(meeting.Id, seg1.Id, true, Ct);

        var answer = await svc.AskAsync(meeting.Id, new AskMeetingAiRequest("Tài liệu OpenAPI thế nào rồi?"), Ct);
        Assert.Equal("COMPLETED", answer.Status);
        Assert.Contains("OpenAPI", answer.Answer);
        Assert.Single(answer.Citations);
        Assert.Equal(seg1.Id, answer.Citations[0].SegmentId);

        var audit = await svc.CoachAsync(meeting.Id, new MeetingAiCoachRequest("TASK_AUDIT"), Ct);
        Assert.Equal("COMPLETED", audit.Status);
        Assert.Contains(audit.Findings, f => f.Category == "UNMENTIONED_TASK" && f.Title.Contains("VNPay"));

        var draft = await svc.CoachAsync(meeting.Id, new MeetingAiCoachRequest("SUPERVISOR_FEEDBACK_DRAFT"), Ct);
        Assert.Equal("COMPLETED", draft.Status);
        Assert.False(string.IsNullOrWhiteSpace(draft.DraftText));
    }

    [Fact]
    public async Task DeleteSegment_BlocksNonAuthorNonManager_AndAllowsManager()
    {
        var (db, meeting) = SeedMeeting();
        using var _ = db;

        var leaderSvc = CreateService(db, actorId: 1);
        var memberSvc = CreateService(db, actorId: 2);

        var seg = await leaderSvc.AppendSegmentAsync(meeting.Id, new CreateTranscriptSegmentRequest("Phát biểu của trưởng nhóm."), Ct);

        await Assert.ThrowsAsync<ForbiddenException>(() => memberSvc.DeleteSegmentAsync(meeting.Id, seg.Id, Ct));
        await leaderSvc.DeleteSegmentAsync(meeting.Id, seg.Id, Ct);

        var feed = await leaderSvc.GetWorkspaceAsync(meeting.Id, null, Ct);
        Assert.Equal(0, feed.TotalSegments);
    }

    [Fact]
    public async Task UnauthorizedUser_AndCancelledMeeting_AreBlocked()
    {
        var (db, meeting) = SeedMeeting(status: "CANCELLED");
        using var _ = db;

        var outsiderSvc = CreateService(db, actorId: 4, canAccess: false);
        await Assert.ThrowsAsync<ForbiddenException>(() => outsiderSvc.GetWorkspaceAsync(meeting.Id, null, Ct));

        var leaderSvc = CreateService(db, actorId: 1);
        await Assert.ThrowsAsync<ConflictException>(() =>
            leaderSvc.AppendSegmentAsync(meeting.Id, new CreateTranscriptSegmentRequest("Không được phép thêm."), Ct));
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

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class StubAiProvider(Func<string, string> responder) : IAiTextGenerationProvider
    {
        public string ModelVersion => "TEST_AI_V1";
        public string ConfigurationVersion => "TEST_CFG";
        public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
            Task.FromResult(responder(userPrompt));
    }
}
