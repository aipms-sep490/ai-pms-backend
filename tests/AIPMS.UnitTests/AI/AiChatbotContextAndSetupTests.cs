using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.Models;
using AIPMS.Application.Features.Chat;
using AIPMS.Application.Features.Milestones.Abstractions;
using AIPMS.Application.Features.Tasks.DTOs;
using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Infrastructure.Chat;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Services;
using AIPMS.Infrastructure.Services.Projects;
using AIPMS.Infrastructure.Services.Topics;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.UnitTests.AI;

public sealed class AiChatbotContextAndSetupTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task TopicSimilarityAnalyzer_AcceptsMarkdownFencedJson_ShortTitles_AndEmptyDifferencesOnHighMatch()
    {
        var input = new TopicAiSimilarityRequest(1, "AI-PMS", null, null, null, "Education", ["AI"]);
        var candidate = new TopicSimilarityCandidate(10, "TOPIC", "AI-PMS Platform", "FA26",
            "Quản lý đồ án tốt nghiệp", "Hỗ trợ sinh viên và giảng viên", "Web app", "Education", ["AI"]);
        var fenced = "```json\n" + JsonSerializer.Serialize(new
        {
            matches = new[]
            {
                new
                {
                    key = 1,
                    level = "HIGH",
                    reason = "Tên và phạm vi đồ án trùng khớp trực tiếp.",
                    sharedAspects = new[] { "Quản lý đồ án AI-PMS" },
                    differences = Array.Empty<string>(),
                    candidateExcerpt = "ai-pms   platform",
                    proposalExcerpt = "AI-PMS"
                }
            }
        }) + "\n```";
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["AiAssistant:TimeoutSeconds"] = "30" }).Build();

        var result = await TopicSimilarityAnalyzer.Analyze(new StubProvider(fenced), config, input, [candidate], false, Ct);

        Assert.Equal("COMPLETED", result.Status);
        var match = Assert.Single(result.Matches);
        Assert.Equal("HIGH", match.SimilarityLevel);
        Assert.Single(match.Differences);
        Assert.Contains("Chưa ghi nhận điểm khác biệt", match.Differences[0]);
    }

    [Fact]
    public void SanitizePersona_ReplacesLeakedVendorIdentityWithAiPmsIdentity()
    {
        var leaked = "Tôi là Kiro, trợ lý hội thoại hỗ trợ sinh viên làm đồ án tốt nghiệp.";
        var cleaned = AcademicKnowledgeService.SanitizePersona(leaked);
        Assert.DoesNotContain("Kiro", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trợ lý AI của hệ thống AI-PMS", cleaned);
    }

    [Fact]
    public void StructuredReport_BuildsFullDocumentAndTaskDiagnostics_AndMapsOnlyVerifiedTargets()
    {
        var payload = new ProjectEvidencePayload(
            1, "ACTIVE", 5, 2, true,
            [new("MS-1", 1, "Mốc 1: SRS", "IN_PROGRESS", "2026-12-01", 1, null, 3, 1)],
            [new("TASK-10", 10, "Viết sơ đồ tuần tự", "BLOCKED", "HIGH", "2026-11-20T00:00:00Z", true, true, null,
                1, "Mốc 1: SRS", ["Nguyễn An"], ["SE"], 0, 3, 1, ["Chốt Use Case (IN_PROGRESS)"], 1,
                "[PENDING] ảnh hưởng từ \"Chốt Use Case\": Bổ sung actor", "GVHD: cần vẽ lại luồng duyệt")],
            [], [], null,
            new("PRJ-1", "AI-PMS", null, null, null, null, "SINGLE_MAJOR", [], [], []),
            [new("DEL-5", 5, "Tài liệu SRS", "DOCUMENT", "Mốc 1: SRS", "COMMON", null, "REVISION_REQUIRED",
                "2026-11-25T00:00:00Z", false, 2, "REVISION_REQUIRED", "2026-11-18T00:00:00Z", ["srs-v2.pdf"], "Bổ sung yêu cầu phi chức năng")],
            [new("COLD-8", 8, "SRS", "Báo cáo đặc tả", "COMMON", null, true, 1, "DRAFT", 1, ["draft.docx"], "2026-12-10T00:00:00Z", null)],
            [new("GATE-G1", "G1", "Review Gate 1", "IN_PROGRESS", "ELIGIBLE", "2026-12-05T00:00:00Z",
                [new(1, "REQ-1", "Nộp SRS", "DOCUMENT", "COMMON", true, "REJECTED", "srs.pdf", "Thiếu bảng traceability")])],
            [],
            new(5, 1, 1, 1, 1, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0));

        var context = new ProjectBoundedContext(1, "ACTIVE", [], JsonSerializer.Serialize(payload), true, 4, 5, 2, true);
        var report = AssistantSetupPlanParser.BuildDeterministicStructuredReport(context);

        Assert.NotNull(report);
        Assert.Equal("AI-PMS", report!.Coverage.ProjectTitle);
        Assert.Equal(1, report.Coverage.DeliverablesCount);
        Assert.Equal(1, report.Coverage.ColdItemsCount);
        Assert.Equal(1, report.Coverage.ReviewGatesCount);
        Assert.Contains(report.Items, i => i.Category == "DOCUMENT" && i.Status == "NEEDS_REVISION" && i.Target == "deliverables" && i.IssueDetail!.Contains("phi chức năng"));
        Assert.Contains(report.Items, i => i.Category == "COLD_REPORT" && i.Status == "IN_PROGRESS" && i.Target == "cold-reports");
        Assert.Contains(report.Items, i => i.Category == "REVIEW_GATE" && i.Status == "NEEDS_REVISION" && i.Target == "timeline");
        Assert.Contains(report.Items, i => i.Category == "TASK" && i.Status == "BLOCKED" && i.Target == "tasks" && i.ResourceId == 10 && i.Location.Contains("Mốc 1: SRS"));
    }

    [Fact]
    public async Task ProjectAiSetupService_AppliesMilestonesDeliverablesTasksCriteriaAndDependencies_Idempotently_AndEnforcesLeaderAuth()
    {
        await using var db = SeedDb();
        var audit = new FakeAudit();
        var clock = new FixedClock();
        var guard = new PassGuard();

        // Non-leader member is rejected (403)
        var memberService = new ProjectAiSetupService(db, new FakeUser(2, ["STUDENT"]), guard, new NoopSender(), audit, clock);
        var request = new ApplyProjectSetupRequest(
            Guid.NewGuid(),
            99,
            [new("M1", null, "Mốc 2: Triển khai API", "Xây dựng các API cốt lõi", "2026-11-23", "2026-12-05")],
            [new("D1", "Tài liệu SDD & API Spec", "Đặc tả kiến trúc", "DOCUMENT", "M1", "COMMON", null, null,
                ["Có sơ đồ lớp & tuần tự", "Có OpenAPI đầy đủ"])],
            [
                new("T1", "Thiết kế database & migration", "Tạo bảng tiêu chí", "HIGH", "M1", null, ["Nguyễn Khải"], "Software Engineering",
                    ["Script idempotent", "Đủ ràng buộc check & FK"], []),
                new("T2", "Viết API quản lý tiêu chí", "CRUD & toggle", "HIGH", "M1", null, ["Võ Tín"], "Software Engineering",
                    ["Đủ test unit phân quyền"], ["T1"])
            ]);

        await Assert.ThrowsAsync<ForbiddenException>(() => memberService.ApplyProjectSetupAsync(1, request, Ct));
        Assert.Equal(0, await db.TaskCriteria.CountAsync());

        // Team leader applies successfully
        var leaderService = new ProjectAiSetupService(db, new FakeUser(1, ["STUDENT"]), guard, new NoopSender(), audit, clock);
        var applied = await leaderService.ApplyProjectSetupAsync(1, request, Ct);

        Assert.False(applied.Replay);
        Assert.Equal(1, applied.MilestonesCreated);
        Assert.Equal(1, applied.DeliverablesCreated);
        Assert.Equal(2, applied.TasksCreated);
        Assert.Equal(3, applied.TaskCriteriaCreated);
        Assert.Equal(1, applied.DependenciesCreated);
        Assert.Equal(3, await db.TaskCriteria.CountAsync());
        Assert.Equal(1, await db.TaskDependencies.CountAsync());
        Assert.Single(audit.Entries, e => e.Action == "AI_PROJECT_SETUP_APPLIED");

        // Replay with same idempotency key does NOT duplicate
        var replay = await leaderService.ApplyProjectSetupAsync(1, request, Ct);
        Assert.True(replay.Replay);
        Assert.Equal(2, await db.Tasks.CountAsync());
        Assert.Equal(3, await db.TaskCriteria.CountAsync());

        // TaskCriteriaService: assignee toggles criterion & locked task rejects toggle
        var taskId = applied.Items.First(i => i.Kind == "TASK" && i.TempKey == "T2").Id;
        var criteriaService = new TaskCriteriaService(db, new FakeUser(2, ["STUDENT"]), new AllowAccess(), guard, audit, clock);
        var list = await criteriaService.ListAsync(taskId, Ct);
        Assert.Equal(1, list.TotalCount);
        Assert.True(list.CanToggle);
        Assert.False(list.CanManage);

        var toggled = await criteriaService.ToggleAsync(taskId, list.Items[0].Id, new ToggleTaskCriterionRequest(true), Ct);
        Assert.Equal(1, toggled.CompletedCount);
        Assert.True(toggled.Items[0].IsCompleted);
        Assert.Equal(2, toggled.Items[0].CompletedBy);

        var dbTask = await db.Tasks.SingleAsync(t => t.Id == taskId);
        dbTask.Status = "DONE";
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() =>
            criteriaService.ToggleAsync(taskId, list.Items[0].Id, new ToggleTaskCriterionRequest(false), Ct));
    }

    [Fact]
    public async Task ProjectAiSetupService_RejectsCyclicTaskDependencies()
    {
        await using var db = SeedDb();
        var service = new ProjectAiSetupService(db, new FakeUser(1, ["STUDENT"]), new PassGuard(), new NoopSender(), new FakeAudit(), new FixedClock());
        var cyclic = new ApplyProjectSetupRequest(
            Guid.NewGuid(), null, [], [],
            [
                new("T1", "Task 1", null, "MEDIUM", null, null, [], null, [], ["T2"]),
                new("T2", "Task 2", null, "MEDIUM", null, null, [], null, [], ["T1"])
            ]);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyProjectSetupAsync(1, cyclic, Ct));
        Assert.Equal(0, await db.Tasks.CountAsync());
    }

    private static AipmsDbContext SeedDb()
    {
        var db = new AipmsDbContext(new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = new DateTime(2026, 11, 22, 1, 0, 0, DateTimeKind.Utc);
        db.Majors.Add(new M.Major { Id = 1, DepartmentId = 1, Code = "SE", Name = "Software Engineering", IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.Users.AddRange(
            new M.User { Id = 1, Email = "leader@test.local", FullName = "Nguyễn Khải", PasswordHash = "x", Status = "ACTIVE", MajorId = 1, CreatedAt = now, UpdatedAt = now },
            new M.User { Id = 2, Email = "member@test.local", FullName = "Võ Tín", PasswordHash = "x", Status = "ACTIVE", MajorId = 1, CreatedAt = now, UpdatedAt = now });
        db.Teams.Add(new M.Team { Id = 1, AcademicSemesterId = 1, Code = "G01", Name = "AI-PMS Team", Status = "ACTIVE", CreatedBy = 1, CreatedAt = now, UpdatedAt = now });
        db.TeamMembers.AddRange(
            new M.TeamMember { Id = 1, TeamId = 1, AcademicSemesterId = 1, UserId = 1, IsLeader = true, JoinedAt = now, CreatedAt = now, UpdatedAt = now },
            new M.TeamMember { Id = 2, TeamId = 1, AcademicSemesterId = 1, UserId = 2, IsLeader = false, JoinedAt = now, CreatedAt = now, UpdatedAt = now });
        db.Projects.Add(new M.Project { Id = 1, TeamId = 1, Code = "PRJ-1", Title = "AI-PMS", Status = "ACTIVE", CreatedBy = 1, CreatedAt = now, UpdatedAt = now, RegisteredAt = now, RowVersion = [1] });
        db.ProjectMajors.Add(new M.ProjectMajor { Id = 1, ProjectId = 1, MajorId = 1, CreatedAt = now });
        db.SaveChanges();
        return db;
    }

    private static AipmsDbContext SeedChatDb()
    {
        var db = SeedDb();
        var now = new FixedClock().GetUtcNow().UtcDateTime;
        db.Organizations.Add(new M.Organization { Id = 1, Code = "FPT", Name = "FPT University", IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.Departments.Add(new M.Department { Id = 1, OrganizationId = 1, Code = "IT", Name = "Information Technology", IsActive = true, CreatedAt = now, UpdatedAt = now });
        foreach (var u in db.Users) u.DepartmentId = 1;
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task ChatService_SummarizeAsync_ParsesFencedJson_Blockers_SmartReplies_AndSuggestedMeeting()
    {
        await using var db = SeedChatDb();
        var now = new FixedClock().GetUtcNow().UtcDateTime;
        db.Set<M.ChatConversation>().Add(new M.ChatConversation
        {
            Id = 50, Kind = "PROJECT", ProjectId = 1, Status = "READ_ONLY", Sequence = 2, Version = 2, CreatedAt = now, UpdatedAt = now
        });
        db.Set<M.ChatMembershipInterval>().AddRange(
            new M.ChatMembershipInterval { Id = 1, ConversationId = 50, UserId = 1, FromSequence = 1, SourceKey = "PROJECT:1", Retained = true, JoinedAt = now },
            new M.ChatMembershipInterval { Id = 2, ConversationId = 50, UserId = 2, FromSequence = 1, SourceKey = "PROJECT:1", Retained = true, JoinedAt = now });
        db.Set<M.ChatMessage>().AddRange(
            new M.ChatMessage { Id = 101, ConversationId = 50, Sequence = 1, SenderId = 1, ClientMessageId = Guid.NewGuid(), RequestHash = "h1", Body = "Tối thứ 5 20h mình họp chốt API nhé, hiện backend đang vướng lỗi CORS.", CreatedAt = now.AddMinutes(-5) },
            new M.ChatMessage { Id = 102, ConversationId = 50, Sequence = 2, SenderId = 2, ClientMessageId = Guid.NewGuid(), RequestHash = "h2", Body = "Ok mình sẽ xử lý lỗi CORS và cập nhật SRS trước 2026-11-25.", CreatedAt = now.AddMinutes(-2) });
        await db.SaveChangesAsync(Ct);

        var fenced = "```json\n" + JsonSerializer.Serialize(new
        {
            summary = "Tôi là Kiro: Nhóm bàn về việc sửa lỗi CORS và họp tối thứ 5.",
            decisions = new[] { "Võ Tín nhận xử lý lỗi CORS và cập nhật SRS." },
            blockers = new[] { "Backend đang vướng lỗi CORS" },
            openQuestions = new[] { "Link họp online là gì?" },
            actionItems = new[]
            {
                new { title = "Xử lý lỗi CORS và cập nhật SRS", ownerName = "Võ Tín", dueAt = (string?)"2026-11-25T17:00:00+07:00", sourceMessageId = "102" },
                new { title = "Bỏ qua người ngoài", ownerName = "Người lạ", dueAt = (string?)null, sourceMessageId = "9999" }
            },
            smartReplies = new[] { "Mình đã gửi link họp vào lịch nhóm rồi nhé!", "Cảm ơn Tín, cần hỗ trợ CORS thì báo mình." },
            suggestedMeeting = new { title = "Họp chốt API nhóm AI-PMS", description = "Chốt API và kiểm tra lỗi CORS", suggestedTimeNote = "Tối thứ 5 20h", sourceMessageId = "101" }
        }) + "\n```";

        using var wake = new ChatWakeSignal();
        var service = new ChatService(db, new ChatAccessService(db, new FixedClock()),
            Options.Create(new ChatSettings { Enabled = true, AiSummaryTimeoutSeconds = 30 }),
            new FixedClock(), wake, aiProvider: new StubProvider(fenced));

        var result = await service.SummarizeAsync(1, 50, new ChatAiSummaryRequest(40), Ct);

        Assert.Equal("COMPLETED", result.GenerationStatus);
        Assert.DoesNotContain("Kiro", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trợ lý AI của hệ thống AI-PMS", result.Summary);
        Assert.Single(result.Blockers ?? []);
        Assert.Equal(2, result.SmartReplies?.Count);
        Assert.NotNull(result.SuggestedMeeting);
        Assert.Equal("101", result.SuggestedMeeting!.SourceMessageId);
        Assert.Equal("Võ Tín", result.ActionItems[0].OwnerName);
        Assert.Equal("102", result.ActionItems[0].SourceMessageId);
        Assert.Null(result.ActionItems[1].OwnerName);
        Assert.Null(result.ActionItems[1].SourceMessageId);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "CHAT_AI_SUMMARY_USED" && a.EntityId == "50", Ct));
    }

    [Fact]
    public async Task ChatService_AssistAsync_AskThread_GroundsReferencesOnlyToVisibleMessages()
    {
        await using var db = SeedChatDb();
        var now = new FixedClock().GetUtcNow().UtcDateTime;
        db.Set<M.ChatConversation>().Add(new M.ChatConversation
        {
            Id = 51, Kind = "PROJECT", ProjectId = 1, Status = "READ_ONLY", Sequence = 1, Version = 1, CreatedAt = now, UpdatedAt = now
        });
        db.Set<M.ChatMembershipInterval>().Add(new M.ChatMembershipInterval
        {
            Id = 3, ConversationId = 51, UserId = 1, FromSequence = 1, SourceKey = "PROJECT:1", Retained = true, JoinedAt = now
        });
        db.Set<M.ChatMessage>().Add(new M.ChatMessage
        {
            Id = 201, ConversationId = 51, Sequence = 1, SenderId = 2, ClientMessageId = Guid.NewGuid(), RequestHash = "h3", Body = "Mình sẽ nộp bản SRS v2 vào thứ 6.", CreatedAt = now.AddMinutes(-3)
        });
        await db.SaveChangesAsync(Ct);

        var fenced = "```json\n" + JsonSerializer.Serialize(new
        {
            answer = "Võ Tín xác nhận sẽ nộp bản SRS v2 vào thứ 6.",
            suggestions = new[] { "Nhắc Tín gửi link file SRS v2 nhé." },
            references = new[]
            {
                new { messageId = "201", excerpt = "Mình sẽ nộp bản SRS v2 vào thứ 6." },
                new { messageId = "999999", excerpt = "Tin nhắn bịa đặt" }
            }
        }) + "\n```";

        using var wake = new ChatWakeSignal();
        var service = new ChatService(db, new ChatAccessService(db, new FixedClock()),
            Options.Create(new ChatSettings { Enabled = true, AiSummaryTimeoutSeconds = 30 }),
            new FixedClock(), wake, aiProvider: new StubProvider(fenced));

        var result = await service.AssistAsync(1, 51, new ChatAiAssistRequest("ASK_THREAD", Query: "Ai nộp SRS v2?"), Ct);

        Assert.Equal("COMPLETED", result.GenerationStatus);
        Assert.Equal("ASK_THREAD", result.Mode);
        var citation = Assert.Single(result.References);
        Assert.Equal("201", citation.MessageId);
        Assert.Equal("Võ Tín", citation.SenderName);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "CHAT_AI_ASK_THREAD" && a.EntityId == "51", Ct));
    }

    private sealed class StubProvider(string output) : IAiTextGenerationProvider
    {
        public string ModelVersion => "TEST_MODEL";
        public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) => Task.FromResult(output);
    }

    private sealed class FakeUser(long id, string[] roles) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => id;
        public string? Email => null;
        public string? FullName => null;
        public IReadOnlyCollection<string> Roles => roles;
    }

    private sealed class PassGuard : IProjectExecutionGuard
    {
        public Task MustBeActiveAsync(long projectId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MustBeActiveForMilestoneAsync(long milestoneId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MustBeActiveForTaskAsync(long taskId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class AllowAccess : IProjectAccessService
    {
        public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakeAudit : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        { Entries.Add(entry); return Task.CompletedTask; }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 22, 1, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoopSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
