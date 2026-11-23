using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using UglyToad.PdfPig;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.UnitTests.AI;

public sealed class ChatbotOverhaulTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 2, 0, 0, DateTimeKind.Utc);
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static AipmsDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static AcademicKnowledgeService CreateService(
        AipmsDbContext db,
        long userId,
        string[] roles,
        IAiTextGenerationProvider? provider = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiAssistant:KnowledgeEnabled"] = "true",
                ["AiAssistant:TimeoutSeconds"] = "30"
            }).Build();

        return new AcademicKnowledgeService(
            db,
            new FakeUser(userId, roles),
            new NoopAudit(),
            provider ?? new FakeAiProvider(),
            new FakeContextRetriever(),
            new FixedClock(Now),
            config);
    }

    private static void SeedUsersAndProject(AipmsDbContext db)
    {
        db.Departments.Add(new M.Department { Id = 1, OrganizationId = 1, Code = "SE", Name = "Kỹ thuật Phần mềm", IsActive = true });
        db.Majors.Add(new M.Major { Id = 1, DepartmentId = 1, Code = "SE", Name = "Software Engineering", IsActive = true });
        db.Roles.AddRange(
            new M.Role { Id = 1, Code = AppRoles.Student, Name = "Student" },
            new M.Role { Id = 2, Code = AppRoles.Lecturer, Name = "Lecturer" },
            new M.Role { Id = 3, Code = AppRoles.DepartmentStaff, Name = "Department Staff" });
        db.Users.AddRange(
            new M.User { Id = 10, Email = "sv@test.vn", FullName = "Nguyễn Minh Khang", PasswordHash = "h", Status = "ACTIVE", DepartmentId = 1, MajorId = 1 },
            new M.User { Id = 11, Email = "other@test.vn", FullName = "Trần Thu Hà", PasswordHash = "h", Status = "ACTIVE", DepartmentId = 1, MajorId = 1 },
            new M.User { Id = 12, Email = "gv@test.vn", FullName = "TS. Lê Quang Huy", PasswordHash = "h", Status = "ACTIVE", DepartmentId = 1 });
        db.UserRoles.AddRange(
            new M.UserRole { Id = 1, UserId = 10, RoleId = 1 },
            new M.UserRole { Id = 2, UserId = 11, RoleId = 1 },
            new M.UserRole { Id = 3, UserId = 12, RoleId = 2 });
        db.Teams.Add(new M.Team { Id = 50, AcademicSemesterId = 1, Code = "T50", Name = "Nhóm 50", Status = "ACTIVE", CreatedBy = 10 });
        db.TeamMembers.Add(new M.TeamMember { Id = 1, TeamId = 50, UserId = 10, IsLeader = true, AcademicSemesterId = 1 });
        db.Projects.Add(new M.Project { Id = 100, TeamId = 50, Code = "PRJ-100", Title = "AI-PMS Capstone", Status = "ACTIVE", CreatedBy = 10, RowVersion = [1] });
        db.SupervisorProfiles.Add(new M.SupervisorProfile { Id = 20, UserId = 12 });
        db.SupervisorAssignments.Add(new M.SupervisorAssignment { Id = 21, ProjectId = 100, SupervisorProfileId = 20, IsPrimary = true, AssignmentType = "PRIMARY" });
        db.SaveChanges();
    }

    [Fact]
    public void ConversationExport_ProducesValidMarkdownWordAndPdf_AndEscapesHtml()
    {
        var turns = new[]
        {
            new AssistantTurnDto(1, "Quy định nộp bài cuối kỳ là gì?", Now.AddMinutes(-10),
                new AcademicAssistantResponseDto(
                    "## Quy định nộp bài\nCần nộp đủ PDF và slide <script>alert(1)</script>.",
                    "GROUNDED",
                    [new KnowledgeCitationDto(1, 1, "Quy chế Đồ án", "v2.0", "POLICY", "Điều 4", 2, Now.AddDays(-30), "Nộp trước ngày bảo vệ 7 ngày.")],
                    [new EvidenceReferenceDto("TASK", "T-1", "Hoàn thiện báo cáo", "2026-11-25", "/api/v1/tasks/1", "Đang thực hiện")],
                    false, null, "COMPLETED", Now.AddMinutes(-9), RunId: 501)),
            new AssistantTurnDto(2, "Cụ thể cần những file nào?", Now.AddMinutes(-5),
                new AcademicAssistantResponseDto(
                    "Gồm 3 tệp: Báo cáo PDF, Slide trình chiếu và Mã nguồn.",
                    "GENERAL", [], [], false, null, "COMPLETED", Now.AddMinutes(-4), RunId: 502))
        };

        var md = AssistantDocumentExporter.CreateConversationExport("Tra cứu nộp bài cuối kỳ", turns, "md");
        var mdText = Encoding.UTF8.GetString(md.Content);
        Assert.Contains("# Tra cứu nộp bài cuối kỳ", mdText);
        Assert.Contains("Lượt 1", mdText);
        Assert.Contains("Lượt 2", mdText);
        Assert.Contains("Quy chế Đồ án", mdText);

        var docx = AssistantDocumentExporter.CreateConversationExport("Tra cứu nộp bài cuối kỳ", turns, "docx");
        using (var archive = new ZipArchive(new MemoryStream(docx.Content)))
        using (var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open()))
        {
            var xml = reader.ReadToEnd();
            Assert.Contains("Tra cứu nộp bài cuối kỳ", xml);
            Assert.Contains("&lt;script&gt;", xml);
        }
        using (var wordDoc = WordprocessingDocument.Open(new MemoryStream(docx.Content), false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(wordDoc));
        }

        var pdf = AssistantDocumentExporter.CreateConversationExport("Tra cứu nộp bài cuối kỳ", turns, "pdf");
        using var pdfDoc = PdfDocument.Open(pdf.Content);
        var pdfText = string.Join(" ", pdfDoc.GetPages().Select(p => p.Text));
        Assert.Contains("Tra cứu nộp bài cuối kỳ", pdfText);
        Assert.Contains("Lượt 2", pdfText);
    }

    [Fact]
    public async Task Conversations_SupportsPinRenameSearchTurnCountAndIsolatesOwnership()
    {
        await using var db = CreateDb();
        SeedUsersAndProject(db);
        var svc = CreateService(db, 10, [AppRoles.Student]);
        var otherSvc = CreateService(db, 11, [AppRoles.Student]);

        var c1 = await svc.CreateConversationAsync(new(null, "ACADEMIC"), Ct);
        var c2 = await svc.CreateConversationAsync(new(null, "ACADEMIC"), Ct);

        await svc.UpdateConversationAsync(c1.Id, new(Title: "Chuẩn bị bảo vệ hội đồng", IsPinned: true), Ct);
        await svc.UpdateConversationAsync(c2.Id, new(Title: "Tra cứu biểu mẫu COLD"), Ct);

        // Pin sorts first regardless of newer creation
        var all = await svc.ConversationsAsync(null, null, Ct);
        Assert.Equal(2, all.Count);
        Assert.Equal(c1.Id, all[0].Id);
        Assert.True(all[0].IsPinned);
        Assert.Equal("Chuẩn bị bảo vệ hội đồng", all[0].Title);

        // Search filters accurately
        var searched = await svc.ConversationsAsync(null, null, Ct, search: "COLD");
        Assert.Single(searched);
        Assert.Equal(c2.Id, searched[0].Id);

        // Cross-user update rejected
        await Assert.ThrowsAsync<NotFoundException>(() =>
            otherSvc.UpdateConversationAsync(c1.Id, new(Title: "Hack"), Ct));

        // Invalid empty title rejected
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.UpdateConversationAsync(c1.Id, new(Title: "   "), Ct));
    }

    [Fact]
    public async Task Feedback_PersistsRatingAndNote_AndRejectsInvalidOrForeignRun()
    {
        await using var db = CreateDb();
        SeedUsersAndProject(db);
        db.AcademicKnowledgeQueryRuns.Add(new AcademicKnowledgeQueryRun
        {
            Id = 900,
            ActorId = 10,
            Scope = "ACADEMIC",
            QueryHash = "abc",
            Status = "COMPLETED",
            ModelVersion = "test-v1",
            EvidenceJson = "{}",
            ResultJson = JsonSerializer.Serialize(new AcademicAssistantResponseDto("Câu trả lời mẫu", "GENERAL", [], [], false, null, "COMPLETED", Now)),
            StartedAt = Now,
            CompletedAt = Now
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, 10, [AppRoles.Student]);
        var otherSvc = CreateService(db, 11, [AppRoles.Student]);

        var saved = await svc.SubmitFeedbackAsync(900, new(-1, "Thiếu dẫn chứng cụ thể"), Ct);
        Assert.Equal(-1, saved.Rating);
        Assert.Equal("Thiếu dẫn chứng cụ thể", saved.Note);

        var persisted = await db.AcademicKnowledgeQueryRuns.AsNoTracking().SingleAsync(x => x.Id == 900);
        Assert.Equal((short)-1, persisted.FeedbackRating);
        Assert.Equal("Thiếu dẫn chứng cụ thể", persisted.FeedbackNote);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.SubmitFeedbackAsync(900, new(5, null), Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => otherSvc.SubmitFeedbackAsync(900, new(1, null), Ct));
    }

    [Fact]
    public async Task AccessibleProjects_ReturnsRoleScopedProjectsForStudentAndSupervisor()
    {
        await using var db = CreateDb();
        SeedUsersAndProject(db);

        var studentProjects = await CreateService(db, 10, [AppRoles.Student]).AccessibleProjectsAsync(Ct);
        var sp = Assert.Single(studentProjects);
        Assert.Equal(100, sp.Id);
        Assert.Equal("Trưởng nhóm", sp.RoleLabel);

        var lecturerProjects = await CreateService(db, 12, [AppRoles.Lecturer]).AccessibleProjectsAsync(Ct);
        var lp = Assert.Single(lecturerProjects);
        Assert.Equal(100, lp.Id);
        Assert.Equal("GVHD chính", lp.RoleLabel);

        var outsiderProjects = await CreateService(db, 11, [AppRoles.Student]).AccessibleProjectsAsync(Ct);
        Assert.Empty(outsiderProjects);
    }

    [Fact]
    public async Task Ask_ParsesFollowUpSuggestionsAndSmartTitle_AndRetainsBoundedMemoryOnFollowUp()
    {
        await using var db = CreateDb();
        SeedUsersAndProject(db);

        var capturedPrompts = new List<string>();
        var provider = new CapturingAiProvider(
            [
                JsonSerializer.Serialize(new
                {
                    answer = "Để chuẩn bị bảo vệ, nhóm cần chốt slide demo và chạy thử luồng chính.",
                    answerType = "GENERAL",
                    citationIds = Array.Empty<string>(),
                    insufficientEvidence = false,
                    followUpSuggestions = new[] { "Gợi ý cấu trúc slide 10 phút", "Các câu hỏi hội đồng hay hỏi" },
                    conversationTitle = "Chuẩn bị bảo vệ đồ án"
                }),
                JsonSerializer.Serialize(new
                {
                    answer = "Slide 10 phút nên chia thành 5 phần rõ ràng.",
                    answerType = "GENERAL",
                    citationIds = Array.Empty<string>(),
                    insufficientEvidence = false,
                    followUpSuggestions = new[] { "Phân bổ thời gian từng thành viên" }
                })
            ],
            capturedPrompts);

        var svc = CreateService(db, 10, [AppRoles.Student], provider);
        var conv = await svc.CreateConversationAsync(new(null, "ACADEMIC"), Ct);

        var first = await svc.AskAsync(new("Cho tôi lời khuyên chuẩn bị bảo vệ đồ án tốt nghiệp thật tốt", "ACADEMIC", null, conv.Id, 0), Ct);
        Assert.Equal(2, first.FollowUpSuggestions?.Count);
        Assert.Contains("Gợi ý cấu trúc slide 10 phút", first.FollowUpSuggestions!);

        var detailAfterFirst = await svc.ConversationAsync(conv.Id, null, Ct);
        Assert.Equal("Chuẩn bị bảo vệ đồ án", detailAfterFirst.Conversation.Title);

        var second = await svc.AskAsync(new("Chi tiết phần slide 10 phút đi", "ACADEMIC", null, conv.Id, 1), Ct);
        Assert.Single(second.FollowUpSuggestions!);
        using var promptDoc = JsonDocument.Parse(capturedPrompts[1]);
        var historyArr = promptDoc.RootElement.GetProperty("conversationHistory");
        Assert.Equal(1, historyArr.GetArrayLength());
        Assert.Contains("chuẩn bị bảo vệ", historyArr[0].GetProperty("Answer").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeUser(long id, string[] roles) : ICurrentUser
    {
        public long? UserId => id;
        public string? Email => "test@aipms.vn";
        public string? FullName => "Nguyễn Minh Khang";
        public IReadOnlyCollection<string> Roles => roles;
        public bool IsAuthenticated => true;
    }

    private sealed class NoopAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class FakeContextRetriever : IAiContextRetriever
    {
        public Task<ProjectBoundedContext> RetrieveProjectContextAsync(long projectId, string query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectBoundedContext(projectId, "ACTIVE", [], "{}", true, 0, 0, 0, false));
        public Task<ReportBoundedContext> RetrieveReportContextAsync(long projectId, long reportId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAiProvider : IAiTextGenerationProvider
    {
        public string ModelVersion => "OPENAI_test";
        public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
            Task.FromResult("{\"answer\":\"Xin chào\",\"answerType\":\"GENERAL\",\"citationIds\":[],\"insufficientEvidence\":false}");
    }

    private sealed class CapturingAiProvider(string[] responses, List<string> capturedPrompts) : IAiTextGenerationProvider
    {
        private int _index;
        public string ModelVersion => "OPENAI_test";
        public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            capturedPrompts.Add(userPrompt);
            var idx = Math.Min(_index++, responses.Length - 1);
            return Task.FromResult(responses[idx]);
        }
    }
}
