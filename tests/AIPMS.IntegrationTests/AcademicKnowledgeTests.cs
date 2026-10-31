using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests;

public sealed class KnowledgeDatabaseFixture : IAsyncLifetime
{
    private readonly IsolatedSqlDatabase database = new();
    public string ConnectionString => database.ConnectionString;
    public AipmsDbContext Context() => new(new DbContextOptionsBuilder<AipmsDbContext>().UseSqlServer(ConnectionString).Options);
    public Task InitializeAsync() => database.StartAsync(Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION"));
    public async Task DisposeAsync() => await database.DisposeAsync();
}

public sealed class AcademicKnowledgeTests(KnowledgeDatabaseFixture fixture) : IClassFixture<KnowledgeDatabaseFixture>
{
    [Fact]
    public async Task ReviewUsesOnlyPublishedOwnDepartmentAndSemesterAndRequiresBothSources()
    {
        await using var db = fixture.Context();
        var (admin, student, department) = await Seed(db);
        var semester = new AcademicSemester { OrganizationId = department.OrganizationId, Code = Guid.NewGuid().ToString("N"), Name = "Study tools", Status = "ACTIVE", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) };
        db.AcademicSemesters.Add(semester);
        await db.SaveChangesAsync();
        var project = new Project { Code = Guid.NewGuid().ToString("N"), Title = "StudySpace", Status = "ACTIVE", CreatedBy = student.Id,
            Team = new Team { Code = Guid.NewGuid().ToString("N"), Name = "StudySpace", Status = "LOCKED", CreatedBy = student.Id, AcademicSemesterId = semester.Id, AcademicSemester = semester } };
        db.Add(project); await db.SaveChangesAsync();
        var other = new Department { OrganizationId = department.OrganizationId, Name = "Other", Code = "OTH" + Guid.NewGuid().ToString("N")[..8], IsActive = true };
        db.Add(other); await db.SaveChangesAsync();
        async Task<Rubric> Rubric(string name, long dep, string status)
        {
            var row = new Rubric { Name = name, Code = Guid.NewGuid().ToString("N"), CreatedBy = admin.Id, DepartmentId = dep, AcademicSemesterId = semester.Id, IsActive = true };
            row.RubricCriteria.Add(new RubricCriterion { Criterion = new EvaluationCriterion { Code = Guid.NewGuid().ToString("N"), Name = "Kiểm thử", Description = "Có test bảo mật", IsActive = true }, WeightPercent = 100, MaxScore = 10, IsRequired = true });
            db.Add(row); await db.SaveChangesAsync();
            db.Add(new RubricVersion { RubricId = row.Id, RootRubricId = row.Id, VersionNumber = 2, Status = status }); await db.SaveChangesAsync(); return row;
        }
        var valid = await Rubric("Rubric IT", department.Id, "PUBLISHED");
        var draft = await Rubric("Private draft", department.Id, "DRAFT");
        var foreign = await Rubric("Other department", other.Id, "PUBLISHED");
        var provider = new Provider("""{"answer":"Cần bổ sung test bảo mật [PROJECT-1], báo cáo chưa có minh chứng [FILE-1]","answerType":"GROUNDED","citationIds":["PROJECT-1","FILE-1"],"insufficientEvidence":false}""");
        var service = Service(db, student, provider, context: new AllowedProject());
        Assert.Equal(valid.Id, Assert.Single(await service.ReviewRubricsAsync(project.Id, default)).Id);
        AssistantUpload[] files = [new("report.txt", System.Text.Encoding.UTF8.GetBytes("Đã triển khai đăng nhập. Chưa ghi nhận kiểm thử."))];
        var request = new AcademicAssistantAskRequest("Nhận xét báo cáo", "COMBINED", project.Id, ToolMode: "REPORT_REVIEW", RubricId: valid.Id);
        var before = await db.Evaluations.CountAsync();
        var answer = await service.AskWithFilesAsync(request, files, default);
        Assert.Equal("COMPLETED", answer.GenerationStatus);
        Assert.Contains("v2", Assert.Single(answer.ProjectEvidence).Title);
        Assert.Single(answer.Attachments!); Assert.Contains("không phải điểm", answer.LimitationNote);
        await Assert.ThrowsAsync<NotFoundException>(() => service.AskWithFilesAsync(request with { RubricId = draft.Id }, files, default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.AskWithFilesAsync(request with { RubricId = foreign.Id }, files, default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AskWithFilesAsync(request, [], default));
        var invalid = Service(db, student, new Provider("""{"answer":"Unsupported review","answerType":"GENERAL","citationIds":[],"insufficientEvidence":false}"""), context: new AllowedProject());
        Assert.Equal("FALLBACK", (await invalid.AskWithFilesAsync(request, files, default)).GenerationStatus);
        foreach (var format in new[] { "docx", "xlsx", "pdf" })
        {
            Assert.NotEmpty((await service.ExportAsync(answer.RunId!.Value, format, default)).Content);
            await Assert.ThrowsAsync<NotFoundException>(() => Service(db, admin).ExportAsync(answer.RunId.Value, format, default));
        }
        Assert.Equal(before, await db.Evaluations.CountAsync());
        db.Set<RubricVersion>().Single(v => v.RubricId == valid.Id).Status = "RETIRED"; await db.SaveChangesAsync();
        Assert.Empty(await service.ReviewRubricsAsync(project.Id, default));
        var saved = await service.ExportAsync(answer.RunId!.Value, "txt", default);
        Assert.Contains("v2", System.Text.Encoding.UTF8.GetString(saved.Content));
    }

    [Fact]
    public async Task ProgressWorksWithoutProviderAndPersistsSnapshot()
    {
        await using var db = fixture.Context();
        var (_, student, department) = await Seed(db);
        var semester = new AcademicSemester { OrganizationId = department.OrganizationId, Code = Guid.NewGuid().ToString("N"), Name = "Progress test", Status = "ACTIVE", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) };
        db.AcademicSemesters.Add(semester);
        await db.SaveChangesAsync();
        var project = new Project { Code = Guid.NewGuid().ToString("N"), Title = "StudySpace", Status = "ACTIVE", CreatedBy = student.Id,
            Team = new Team { Code = Guid.NewGuid().ToString("N"), Name = "StudySpace", Status = "LOCKED", CreatedBy = student.Id, AcademicSemesterId = semester.Id, AcademicSemester = semester } };
        db.Add(project); await db.SaveChangesAsync();
        var service = Service(db, student, context: new AllowedProject());
        var result = await service.AskAsync(new("Tiến độ", "PROJECT", project.Id, ToolMode: "PROGRESS"), default);
        Assert.Equal("COMPLETED", result.GenerationStatus);
        Assert.Contains("3/30", result.Answer);
        Assert.Contains("quá hạn", result.Answer);
        Assert.Contains("Công việc cần sửa", System.Text.Encoding.UTF8.GetString((await service.ExportAsync(result.RunId!.Value, "txt", default)).Content));
    }

    [Theory]
    [InlineData("not json", "FALLBACK")]
    [InlineData("{\"answer\":\"Xin chào!\",\"answerType\":\"GENERAL\",\"citationIds\":[],\"insufficientEvidence\":false}", "COMPLETED")]
    public async Task ProjectContextIsNotShownForFailedOrGeneralAnswers(string output, string expectedStatus)
    {
        await using var db = fixture.Context();
        var (_, student, department) = await Seed(db);
        var semester = new AcademicSemester { OrganizationId = department.OrganizationId, Code = Guid.NewGuid().ToString("N"), Name = "Project context", Status = "ACTIVE", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) };
        db.AcademicSemesters.Add(semester);
        await db.SaveChangesAsync();
        var project = new Project { Code = Guid.NewGuid().ToString("N"), Title = "StudySpace", Status = "ACTIVE", CreatedBy = student.Id,
            Team = new Team { Code = Guid.NewGuid().ToString("N"), Name = "StudySpace", Status = "LOCKED", CreatedBy = student.Id, AcademicSemesterId = semester.Id, AcademicSemester = semester } };
        db.Add(project); await db.SaveChangesAsync();
        var service = Service(db, student, new Provider(output), context: new AllowedProject());
        var response = await service.AskAsync(new("Xin chào", "COMBINED", project.Id), default);
        Assert.Equal(expectedStatus, response.GenerationStatus);
        Assert.Empty(response.ProjectEvidence);
        Assert.Empty(response.Citations);
        Assert.DoesNotContain("Công việc cần sửa", response.Answer);
        var saved = await db.AcademicKnowledgeQueryRuns.FindAsync(response.RunId);
        using var evidence = System.Text.Json.JsonDocument.Parse(saved!.EvidenceJson);
        Assert.Single(evidence.RootElement.GetProperty("projectEvidence").EnumerateArray());
        Assert.DoesNotContain("Công việc cần sửa", System.Text.Encoding.UTF8.GetString((await service.ExportAsync(response.RunId!.Value, "txt", default)).Content));
    }

    private sealed class AllowedProject : IAiContextRetriever
    {
        public Task<ProjectBoundedContext> RetrieveProjectContextAsync(long id, string query, CancellationToken ct = default)
        {
            var payload = new ProjectEvidencePayload(id, "ACTIVE", 30, 3, true, [],
                [new("TASK-1", 1, "Công việc cần sửa", "TODO", null, "2026-10-20", true, false, null)], [], [], null);
            return Task.FromResult(new ProjectBoundedContext(id, "ACTIVE", [new("TASK", "TASK-1", "Công việc cần sửa", "2026-10-20", null, "Quá hạn")],
                System.Text.Json.JsonSerializer.Serialize(payload), true, 1, 30, 3, true));
        }
        public Task<ReportBoundedContext> RetrieveReportContextAsync(long id, long report, CancellationToken ct = default) => throw new NotSupportedException();
    }
    [Fact]
    public async Task ConversationsPersistMemoryAndEnforceOwnershipRevisionAndDeletion()
    {
        await using var db = fixture.Context();
        var (admin, student, _) = await Seed(db);
        var provider = new Provider("""{"answer":"Tên bạn chọn là Sao Mai","answerType":"GENERAL","citationIds":[],"insufficientEvidence":false}""");
        var service = Service(db, student, provider);
        var conversation = await service.CreateConversationAsync(new(), default);
        var first = await service.AskAsync(new("Gọi tôi là Sao Mai", ConversationId: conversation.Id, ExpectedRevision: 0), default);
        Assert.Equal(1, first.Revision);
        await service.AskAsync(new("Tên vừa chọn là gì?", ConversationId: conversation.Id, ExpectedRevision: 1, PageContext: "Công việc"), default);
        Assert.Contains("conversationHistory", provider.LastPrompt);
        using var prompt = System.Text.Json.JsonDocument.Parse(provider.LastPrompt);
        Assert.Equal("Gọi tôi là Sao Mai", prompt.RootElement.GetProperty("conversationHistory")[0].GetProperty("Query").GetString());
        Assert.Equal("Công việc", prompt.RootElement.GetProperty("pageContext").GetString());
        var detail = await service.ConversationAsync(conversation.Id, null, default);
        Assert.Equal(2, detail.Turns.Count);
        Assert.Equal(first.RunId, detail.Turns[0].Response.RunId);
        Assert.Contains(await service.ConversationsAsync(null, null, default), x => x.Id == conversation.Id);
        Assert.DoesNotContain(await Service(db, admin).ConversationsAsync(null, null, default), x => x.Id == conversation.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db, admin).ConversationAsync(conversation.Id, null, default));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db, admin).DeleteConversationAsync(conversation.Id, default));
        await Assert.ThrowsAsync<ConflictException>(() => service.AskAsync(new("Stale question", ConversationId: conversation.Id, ExpectedRevision: 0), default));
        await Assert.ThrowsAsync<ConflictException>(() => service.AskAsync(new("Wrong scope", "PROJECT", 999, conversation.Id, 2), default));
        await service.DeleteConversationAsync(conversation.Id, default);
        await Assert.ThrowsAsync<NotFoundException>(() => service.ConversationAsync(conversation.Id, null, default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ExportAsync(first.RunId!.Value, "txt", default));
        Assert.False(await db.Set<AssistantConversationTurn>().AnyAsync(x => x.ConversationId == conversation.Id));
    }

    [Fact]
    public async Task ConversationAuditFailureRollsBackRunTurnAndRevision()
    {
        await using var db = fixture.Context();
        var (_, student, _) = await Seed(db);
        var conversation = await Service(db, student).CreateConversationAsync(new(), default);
        var before = await db.AcademicKnowledgeQueryRuns.CountAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db, student, audit: new Audit(true))
            .AskAsync(new("Hello friend", ConversationId: conversation.Id, ExpectedRevision: 0), default));
        db.ChangeTracker.Clear();
        Assert.Equal(before, await db.AcademicKnowledgeQueryRuns.CountAsync());
        Assert.Equal(0, (await db.Set<AssistantConversation>().FindAsync(conversation.Id))!.Revision);
        Assert.False(await db.Set<AssistantConversationTurn>().AnyAsync(x => x.ConversationId == conversation.Id));
    }

    [Fact]
    public async Task SourceSnapshotIsOwnerOnlyAndStableAfterPublishAndDraftHasNoMutation()
    {
        await using var db = fixture.Context();
        var (admin, student, department) = await Seed(db);
        var manager = Service(db, admin);
        var word = "citation" + Guid.NewGuid().ToString("N");
        var doc = await manager.CreateAsync(Request(word, department.Id), default);
        await manager.PublishAsync(doc.Id, default);
        var reader = Service(db, student);
        var answer = await reader.AskAsync(new(word), default);
        await manager.AddVersionAsync(doc.Id, new("v2", word + " changed", null, null, null, null), default);
        await manager.PublishAsync(doc.Id, default);
        Assert.Equal("v1", (await reader.CitationAsync(answer.RunId!.Value, 0, default)).Version);
        await Assert.ThrowsAsync<NotFoundException>(() => manager.CitationAsync(answer.RunId.Value, 0, default));
        await Assert.ThrowsAsync<NotFoundException>(() => reader.CitationAsync(answer.RunId.Value, 99, default));
        var provider = new Provider("""{"answer":"Bản nháp để bạn xem lại","answerType":"GENERAL","citationIds":[],"insufficientEvidence":false,"draft":{"kind":"TASK","title":"Kiểm thử đăng nhập","description":"Kiểm tra lỗi và quyền truy cập"}}""");
        var count = await db.Tasks.CountAsync();
        var draft = await Service(db, student, provider).AskAsync(new("Soạn công việc kiểm thử", DraftKind: "TASK"), default);
        Assert.Equal("TASK", draft.Draft!.Kind);
        Assert.Equal(count, await db.Tasks.CountAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => reader.AskAsync(new("Change grades", DraftKind: "PUBLISH_GRADES"), default));
    }

    [Fact]
    public async Task ConversationMigrationRerunsAndConstraintsStayTrusted()
    {
        var sql = await System.IO.File.ReadAllTextAsync(Path.Combine(SqlSchemaBootstrap.Root, "db", "changes", "20261025_add_assistant_conversations.sql"));
        await SqlSchemaBootstrap.ExecuteAsync(fixture.ConnectionString, sql);
        await SqlSchemaBootstrap.ExecuteAsync(fixture.ConnectionString, sql);
        await using var db = fixture.Context();
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('assistant_conversations'),OBJECT_ID('assistant_conversation_turns')) AND (is_disabled=1 OR is_not_trusted=1)").SingleAsync());
    }

    [Theory]
    [InlineData("{\"answer\":\"Hello", "Hello")]
    [InlineData("{\"answer\":\"line\\nnext", "line\nnext")]
    [InlineData("{\"answer\":\"Hi\\u00", "Hi")]
    public void StreamingPreviewHandlesPartialJson(string raw, string expected) => Assert.Equal(expected, AcademicKnowledgeService.PartialAnswer(raw));

    [Fact]
    public async Task ConcurrentRepliesCommitExactlyOneTurn()
    {
        await using var seed = fixture.Context();
        var (_, student, _) = await Seed(seed);
        var conversation = await Service(seed, student).CreateConversationAsync(new(), default);
        await using var left = fixture.Context();
        await using var right = fixture.Context();
        var provider = new BarrierProvider();
        async Task<bool> Send(AipmsDbContext db)
        {
            try { await Service(db, student, provider).AskAsync(new("Concurrent question", ConversationId: conversation.Id, ExpectedRevision: 0), default); return true; }
            catch (ConflictException) { return false; }
        }
        var outcomes = await Task.WhenAll(Send(left), Send(right));
        Assert.Single(outcomes.Where(x => x));
        Assert.Equal(1, await seed.Set<AssistantConversationTurn>().CountAsync(x => x.ConversationId == conversation.Id));
    }

    [Fact]
    public async Task CancellationDuringStreamingDoesNotPersistPartialAnswer()
    {
        await using var db = fixture.Context();
        var (_, student, _) = await Seed(db);
        var conversation = await Service(db, student).CreateConversationAsync(new(), default);
        using var cancel = new CancellationTokenSource();
        var service = Service(db, student, new CancelStreamingProvider());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AskStreamingAsync(
            new("Stream this", ConversationId: conversation.Id, ExpectedRevision: 0), [], _ => { cancel.Cancel(); return Task.CompletedTask; }, cancel.Token));
        Assert.False(await db.Set<AssistantConversationTurn>().AnyAsync(x => x.ConversationId == conversation.Id));
    }

    private sealed class BarrierProvider : IAiTextGenerationProvider
    {
        private int count;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ModelVersion => "TEST_REMOTE";
        public async Task<string> GenerateTextAsync(string system, string prompt, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref count) == 2) ready.SetResult();
            await ready.Task.WaitAsync(ct);
            return """{"answer":"Hello","answerType":"GENERAL","citationIds":[],"insufficientEvidence":false}""";
        }
    }

    private sealed class CancelStreamingProvider : IAiTextGenerationProvider, IAiStreamingProvider
    {
        public string ModelVersion => "TEST_REMOTE";
        public Task<string> GenerateTextAsync(string system, string prompt, CancellationToken ct = default) => throw new InvalidOperationException();
        public async Task<string> GenerateStreamingAsync(string system, string prompt, IReadOnlyList<AiImageInput> images, Func<string, Task> delta, CancellationToken ct = default)
        {
            await delta("{\"answer\":\"partial");
            await Task.Delay(Timeout.Infinite, ct);
            return "";
        }
    }

    [Fact]
    public async Task DemoSeedAndMigrationAreRerunnableAndExplicitlyLabelled()
    {
        await using var db = fixture.Context();
        var (_, _, department) = await Seed(db);
        db.AcademicSemesters.Add(new AcademicSemester { OrganizationId = department.OrganizationId, Code = Guid.NewGuid().ToString("N"), Name = "Demo semester", Status = "ACTIVE", StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)) });
        await db.SaveChangesAsync();
        var migration = await System.IO.File.ReadAllTextAsync(Path.Combine(SqlSchemaBootstrap.Root, "db", "changes", "20261023_add_academic_knowledge.sql"));
        var seed = await System.IO.File.ReadAllTextAsync(Path.Combine(SqlSchemaBootstrap.Root, "db", "seed_academic_knowledge_demo.sql"));
        await SqlSchemaBootstrap.ExecuteAsync(fixture.ConnectionString, migration);
        await SqlSchemaBootstrap.ExecuteAsync(fixture.ConnectionString, seed);
        var count = await db.AcademicKnowledgeDocuments.CountAsync(d => d.DocumentType == "DEMO");
        await SqlSchemaBootstrap.ExecuteAsync(fixture.ConnectionString, migration);
        await SqlSchemaBootstrap.ExecuteAsync(fixture.ConnectionString, seed);
        Assert.Equal(3, count);
        Assert.Equal(count, await db.AcademicKnowledgeDocuments.CountAsync(d => d.DocumentType == "DEMO"));
        Assert.True(await db.AcademicKnowledgeDocuments.Where(d => d.DocumentType == "DEMO").AllAsync(d => d.AcademicSemesterId != null && d.Title.StartsWith("[DEMO]")));
    }

    [Fact]
    public async Task SqlLifecyclePreservesSourcesAndFiltersDraftExpiredRetiredAndOtherDepartment()
    {
        await using var db = fixture.Context();
        var (admin, student, department) = await Seed(db);
        var manager = Service(db, admin);
        var reader = Service(db, student);
        var word = "rubric" + Guid.NewGuid().ToString("N");
        var doc = await manager.CreateAsync(Request(word, department.Id), default);
        Assert.True((await reader.AskAsync(new(word), default)).InsufficientEvidence);
        await manager.PublishAsync(doc.Id, default);
        var answer = await reader.AskAsync(new(word), default);
        Assert.Equal("FALLBACK", answer.GenerationStatus);
        Assert.Contains("Source content", answer.Answer);
        Assert.Equal("v1", Assert.Single(answer.Citations).Version);
        var snapshot = (await db.AcademicKnowledgeQueryRuns.FindAsync(answer.RunId))!.EvidenceJson;
        await manager.AddVersionAsync(doc.Id, new("v2", word + " New content", null, null, null, null), default);
        await manager.PublishAsync(doc.Id, default);
        Assert.Equal("v2", Assert.Single((await reader.AskAsync(new(word), default)).Citations).Version);
        Assert.Equal(snapshot, (await db.AcademicKnowledgeQueryRuns.FindAsync(answer.RunId))!.EvidenceJson);
        await manager.RetireAsync(doc.Id, default);
        Assert.True((await reader.AskAsync(new(word), default)).InsufficientEvidence);
        await Assert.ThrowsAsync<ConflictException>(() => manager.PublishAsync(doc.Id, default));

        var expired = await manager.CreateAsync(Request(word, department.Id) with { EffectiveFrom = DateTime.UtcNow.AddDays(-3), EffectiveTo = DateTime.UtcNow.AddDays(-2) }, default);
        await manager.PublishAsync(expired.Id, default);
        var other = new Department { Code = Guid.NewGuid().ToString("N"), Name = "Other", OrganizationId = department.OrganizationId, IsActive = true };
        db.Departments.Add(other); await db.SaveChangesAsync();
        var wrongScope = await manager.CreateAsync(Request(word, other.Id), default);
        await manager.PublishAsync(wrongScope.Id, default);
        Assert.True((await reader.AskAsync(new(word), default)).InsufficientEvidence);
    }

    [Fact]
    public async Task AuthorizationAndAuditRollbackAreEnforcedByDatabaseState()
    {
        await using var db = fixture.Context();
        var (admin, student, department) = await Seed(db);
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, student).CreateAsync(Request("denied", department.Id), default));
        var fail = Service(db, admin, audit: new Audit(true));
        var title = Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fail.CreateAsync(Request(title, department.Id), default));
        db.ChangeTracker.Clear();
        Assert.False(await db.AcademicKnowledgeDocuments.AnyAsync(d => d.Title == title));
        var role = await db.Roles.SingleAsync(r => r.Code == "DEPARTMENT_STAFF");
        db.UserRoles.Add(new UserRole { UserId = student.Id, RoleId = role.Id }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, student).CreateAsync(Request("global", null), default));
        student.Status = "INACTIVE"; db.Users.Update(student); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, student).AskAsync(new("rubric"), default));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"answer\":\"fabricated\",\"citationIds\":[\"DOC-999\"],\"insufficientEvidence\":false}")]
    [InlineData("{\"answer\":\"missing citation\",\"citationIds\":[],\"insufficientEvidence\":false}")]
    public async Task InvalidProviderOutputFallsBackToExactRetrievedContent(string output)
    {
        await using var db = fixture.Context();
        var (admin, student, department) = await Seed(db);
        var word = "rubric" + Guid.NewGuid().ToString("N");
        var manager = Service(db, admin);
        var doc = await manager.CreateAsync(Request(word, department.Id), default);
        await manager.PublishAsync(doc.Id, default);
        var response = await Service(db, student, new Provider(output)).AskAsync(new(word), default);
        Assert.Equal("FALLBACK", response.GenerationStatus);
        Assert.Contains("Source content", response.Answer);
        Assert.DoesNotContain("fabricated", response.Answer);
    }

    [Fact]
    public async Task GeneralConversationCallsProviderWithoutDocumentsAndSuppliesVietnamTime()
    {
        await using var db = fixture.Context();
        var (_, student, _) = await Seed(db);
        var provider = new Provider("""{"answer":"Xin chào!","answerType":"GENERAL","citationIds":[],"insufficientEvidence":false}""");
        var response = await Service(db, student, provider).AskAsync(new("Xin chào"), default);
        Assert.Equal("COMPLETED", response.GenerationStatus);
        Assert.Equal("GENERAL", response.AnswerType);
        Assert.Empty(response.Citations);
        Assert.False(response.InsufficientEvidence);
        Assert.Contains("Asia/Ho_Chi_Minh", provider.LastPrompt);
        Assert.Contains("+07:00", provider.LastPrompt.Replace("\\u002B", "+"));
        Assert.NotNull(await db.AcademicKnowledgeQueryRuns.FindAsync(response.RunId));
    }

    [Theory]
    [InlineData("GENERAL", false)]
    [InlineData("INSUFFICIENT_EVIDENCE", true)]
    public async Task UnusedRetrievedSourcesRemainInHistoryButAreNotShown(string kind, bool insufficient)
    {
        await using var db = fixture.Context();
        var (admin, student, department) = await Seed(db);
        var word = "rubric" + Guid.NewGuid().ToString("N");
        var manager = Service(db, admin);
        var document = await manager.CreateAsync(Request(word, department.Id), default);
        await manager.PublishAsync(document.Id, default);
        var output = System.Text.Json.JsonSerializer.Serialize(new { answer = "General explanation or missing official evidence", answerType = kind, citationIds = Array.Empty<string>(), insufficientEvidence = insufficient });
        var response = await Service(db, student, new Provider(output)).AskAsync(new(word), default);
        Assert.Equal(kind, response.AnswerType);
        Assert.Equal(insufficient, response.InsufficientEvidence);
        Assert.Empty(response.Citations);
        Assert.Contains(word, (await db.AcademicKnowledgeQueryRuns.FindAsync(response.RunId))!.EvidenceJson);
    }

    [Fact]
    public async Task ConversationIncludesOnlyTheCurrentActorsDisplayName()
    {
        await using var db = fixture.Context();
        var (admin, student, _) = await Seed(db);
        admin.FullName = "Another private user";
        student.FullName = "Current student";
        await db.SaveChangesAsync();
        var provider = new Provider("""{"answer":"Current student","answerType":"GENERAL","citationIds":[],"insufficientEvidence":false}""");
        await Service(db, student, provider).AskAsync(new("What is my name?"), default);
        using var prompt = System.Text.Json.JsonDocument.Parse(provider.LastPrompt);
        var profile = prompt.RootElement.GetProperty("currentUser");
        Assert.Equal(student.FullName, profile.GetProperty("displayName").GetString());
        Assert.Single(profile.EnumerateObject());
        Assert.DoesNotContain(admin.FullName, provider.LastPrompt);
        Assert.DoesNotContain(student.Email, provider.LastPrompt);
        Assert.DoesNotContain(student.PasswordHash, provider.LastPrompt);
    }

    [Fact]
    public async Task GroundedResponseDisplaysOnlySelectedSourceAndRejectsFakeGeneralCitations()
    {
        await using var db = fixture.Context();
        var (admin, student, department) = await Seed(db);
        var word = "rubric" + Guid.NewGuid().ToString("N");
        var manager = Service(db, admin);
        foreach (var suffix in new[] { " first", " second" })
        {
            var doc = await manager.CreateAsync(Request(word + suffix, department.Id), default);
            await manager.PublishAsync(doc.Id, default);
        }
        var output = """{"answer":"See [DOC-2]","answerType":"GROUNDED","citationIds":["DOC-2"],"insufficientEvidence":false}""";
        var result = await Service(db, student, new Provider(output)).AskAsync(new(word), default);
        Assert.Equal("GROUNDED", result.AnswerType);
        Assert.EndsWith("second", Assert.Single(result.Citations).Title);
        var fake = """{"answer":"Fake [DOC-99]","answerType":"GENERAL","citationIds":[],"insufficientEvidence":false}""";
        Assert.Equal("FALLBACK", (await Service(db, student, new Provider(fake)).AskAsync(new(word), default)).GenerationStatus);
    }

    [Fact]
    public async Task ProviderFailureWithoutSourcesReturnsUnavailableInsteadOfFabricatedAnswer()
    {
        await using var db = fixture.Context();
        var (_, student, _) = await Seed(db);
        var invalidJsonResult = await Service(db, student, new Provider("not json")).AskAsync(new("Hello"), default);
        Assert.Equal("FALLBACK", invalidJsonResult.GenerationStatus);
        Assert.Empty(invalidJsonResult.Citations);
        Assert.Contains("câu trả lời hợp lệ", invalidJsonResult.Answer);

        db.ChangeTracker.Clear();
        var connectionFailureResult = await Service(db, student, new FailingProvider()).AskAsync(new("Hello"), default);
        Assert.Equal("FALLBACK", connectionFailureResult.GenerationStatus);
        Assert.Empty(connectionFailureResult.Citations);
        Assert.Contains("kết nối", connectionFailureResult.Answer);
    }

    [Fact]
    public async Task FallbackWithEvidenceContainingConnectKeyword_DoesNotTriggerNetworkErrorMessageAndDoesNotLeakEvidence()
    {
        await using var db = fixture.Context();
        var (_, student, department) = await Seed(db);
        var semester = new AcademicSemester { OrganizationId = department.OrganizationId, Code = Guid.NewGuid().ToString("N"), Name = "Connect test", Status = "ACTIVE", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) };
        db.AcademicSemesters.Add(semester);
        await db.SaveChangesAsync();
        var project = new Project { Code = Guid.NewGuid().ToString("N"), Title = "ConnectSpace", Status = "ACTIVE", CreatedBy = student.Id,
            Team = new Team { Code = Guid.NewGuid().ToString("N"), Name = "ConnectSpace", Status = "LOCKED", CreatedBy = student.Id, AcademicSemesterId = semester.Id, AcademicSemester = semester } };
        db.Add(project); await db.SaveChangesAsync();

        var retriever = new ConnectKeywordProjectContextRetriever();

        // 1. Invalid JSON output from provider ("not json")
        var invalidJsonService = Service(db, student, new Provider("not json"), context: retriever);
        var invalidJsonResponse = await invalidJsonService.AskAsync(new("Xin chào", "COMBINED", project.Id), default);
        Assert.Equal("FALLBACK", invalidJsonResponse.GenerationStatus);
        Assert.Empty(invalidJsonResponse.ProjectEvidence);
        Assert.Empty(invalidJsonResponse.Citations);
        Assert.Contains("câu trả lời hợp lệ", invalidJsonResponse.Answer);
        Assert.DoesNotContain("kết nối được với mô hình AI", invalidJsonResponse.Answer);
        Assert.DoesNotContain("kết nối doanh nghiệp", invalidJsonResponse.Answer);

        // 2. Valid JSON but missing required properties (KeyNotFoundException)
        var invalidSchemaService = Service(db, student, new Provider("""{"unexpected":"payload"}"""), context: retriever);
        var invalidSchemaResponse = await invalidSchemaService.AskAsync(new("Xin chào", "COMBINED", project.Id), default);
        Assert.Equal("FALLBACK", invalidSchemaResponse.GenerationStatus);
        Assert.Empty(invalidSchemaResponse.ProjectEvidence);
        Assert.Empty(invalidSchemaResponse.Citations);
        Assert.Contains("câu trả lời hợp lệ", invalidSchemaResponse.Answer);
        Assert.DoesNotContain("kết nối được với mô hình AI", invalidSchemaResponse.Answer);
        Assert.DoesNotContain("kết nối doanh nghiệp", invalidSchemaResponse.Answer);

        // 3. True network transport failure (HttpRequestException)
        var connectionFailService = Service(db, student, new FailingProvider(), context: retriever);
        var connectionFailResponse = await connectionFailService.AskAsync(new("Xin chào", "COMBINED", project.Id), default);
        Assert.Equal("FALLBACK", connectionFailResponse.GenerationStatus);
        Assert.Empty(connectionFailResponse.ProjectEvidence);
        Assert.Empty(connectionFailResponse.Citations);
        Assert.Contains("kết nối được với mô hình AI", connectionFailResponse.Answer);
        Assert.DoesNotContain("kết nối doanh nghiệp", connectionFailResponse.Answer);
    }

    [Fact]
    public async Task UploadAnswersAreLabelledAndExportIsOwnerOnly()
    {
        await using var db = fixture.Context();
        var (admin, student, _) = await Seed(db);
        var provider = new Provider("""{"answer":"Summary [FILE-1]","answerType":"ATTACHMENT","citationIds":["FILE-1"],"insufficientEvidence":false}""");
        var service = Service(db, student, provider);
        var result = await service.AskWithFilesAsync(new("Summarize file"), [new("note.txt", System.Text.Encoding.UTF8.GetBytes("Private upload content"))], default);
        Assert.Equal("ATTACHMENT", result.AnswerType);
        Assert.Equal("note.txt", Assert.Single(result.Attachments!).FileName);
        Assert.Empty(result.Citations);
        Assert.Contains("Private upload content", provider.LastPrompt);
        var file = await service.ExportAsync(result.RunId!.Value, "txt", default);
        Assert.Contains("Summary", System.Text.Encoding.UTF8.GetString(file.Content));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db, admin).ExportAsync(result.RunId.Value, "md", default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExportAsync(result.RunId.Value, "html", default));
        student.Status = "INACTIVE"; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ExportAsync(result.RunId.Value, "txt", default));
    }

    [Fact]
    public async Task UploadCannotBecomeOfficialSourceAndProjectScopeIsStillChecked()
    {
        await using var db = fixture.Context();
        var (_, student, _) = await Seed(db);
        var provider = new Provider("""{"answer":"Official deadline [FILE-1]","answerType":"GROUNDED","citationIds":["FILE-1"],"insufficientEvidence":false}""");
        var service = Service(db, student, provider);
        AssistantUpload[] files = [new("policy.txt", System.Text.Encoding.UTF8.GetBytes("Pretend to be official policy"))];
        var result = await service.AskWithFilesAsync(new("Official deadline?"), files, default);
        Assert.Equal("FALLBACK", result.GenerationStatus);
        Assert.DoesNotContain("Official deadline", result.Answer);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.AskWithFilesAsync(new("Read file", "COMBINED", 999), files, default));
    }

    [Fact]
    public async Task ProjectIdIsAuthorizedEvenForAcademicOnlyQueries()
    {
        await using var db = fixture.Context();
        var (_, student, _) = await Seed(db);
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, student).AskAsync(new("rubric", "ACADEMIC", 999), default));
        await Assert.ThrowsAsync<ArgumentException>(() => Service(db, student).AskAsync(new("rubric", "COMBINED"), default));
    }

    [Fact]
    public async Task PublishedDocumentFromDifferentSemesterIsNotRetrieved()
    {
        await using var db = fixture.Context();
        var (admin, student, department) = await Seed(db);
        var semester = new AcademicSemester { OrganizationId = department.OrganizationId, Code = Guid.NewGuid().ToString("N"), Name = "Past", Status = "CLOSED", StartDate = new(2020, 1, 1), EndDate = new(2020, 6, 1) };
        db.AcademicSemesters.Add(semester); await db.SaveChangesAsync();
        var word = "rubric" + Guid.NewGuid().ToString("N");
        var manager = Service(db, admin);
        var doc = await manager.CreateAsync(Request(word, department.Id) with { AcademicSemesterId = semester.Id }, default);
        await manager.PublishAsync(doc.Id, default);
        Assert.True((await Service(db, student).AskAsync(new(word), default)).InsufficientEvidence);
    }

    private AcademicKnowledgeService Service(AipmsDbContext db, User actor, IAiTextGenerationProvider? provider = null, IAuditTrail? audit = null, IAiContextRetriever? context = null) =>
        new(db, new Current(actor), audit ?? new Audit(), provider ?? new Provider(null), context ?? new DeniedProject(), TimeProvider.System,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AiAssistant:KnowledgeEnabled"] = "true" }).Build());
    private static CreateKnowledgeDocumentRequest Request(string word, long? department) => new(word, "PROCESS", department, null, null, "v1", word + " Source content", null, null, null, null);
    private static async Task<(User, User, Department)> Seed(AipmsDbContext db)
    {
        foreach (var code in new[] { "ADMIN", "STUDENT", "DEPARTMENT_STAFF" })
            if (!await db.Roles.AnyAsync(r => r.Code == code)) db.Roles.Add(new Role { Code = code, Name = code });
        await db.SaveChangesAsync();
        var org = new Organization { Code = Guid.NewGuid().ToString("N"), Name = "Test university", IsActive = true };
        var dep = new Department { Code = "IT", Name = "IT", Organization = org, IsActive = true };
        User Make(string role) => new() { Email = Guid.NewGuid() + "@example.test", FullName = "Knowledge test", PasswordHash = "unused", Status = "ACTIVE", Department = dep,
            UserRoleUsers = [new() { RoleId = db.Roles.Single(r => r.Code == role).Id }] };
        var admin = Make("ADMIN"); var student = Make("STUDENT");
        db.Users.AddRange(admin, student); await db.SaveChangesAsync();
        return (admin, student, dep);
    }
    private sealed class Current(User user) : ICurrentUser
    {
        public bool IsAuthenticated => true; public long? UserId => user.Id;
        public string? Email => user.Email; public string? FullName => user.FullName;
        public IReadOnlyCollection<string> Roles => [];
    }
    private sealed class Audit(bool fail = false) : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken ct = default) => fail ? throw new InvalidOperationException("Audit unavailable") : Task.CompletedTask;
    }
    private sealed class Provider(string? output) : IAiTextGenerationProvider
    {
        public string LastPrompt { get; private set; } = "";
        public string ModelVersion => output is null ? "GROUNDED_LOCAL_V1" : "TEST_REMOTE";
        public string ConfigurationVersion => "test";
        public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        {
            LastPrompt = userPrompt;
            return Task.FromResult(output ?? "");
        }
    }
    private sealed class FailingProvider : IAiTextGenerationProvider
    {
        public string ModelVersion => "TEST_REMOTE";
        public string ConfigurationVersion => "test";
        public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken ct = default) =>
            throw new HttpRequestException("Simulated connection failure to AI provider");
    }
    private sealed class ConnectKeywordProjectContextRetriever : IAiContextRetriever
    {
        public Task<ProjectBoundedContext> RetrieveProjectContextAsync(long id, string query, CancellationToken ct = default)
        {
            var payload = new ProjectEvidencePayload(id, "ACTIVE", 30, 3, true, [],
                [new("TASK-1", 1, "Công việc kết nối doanh nghiệp", "TODO", null, "2026-10-20", true, false, null)], [], [], null);
            return Task.FromResult(new ProjectBoundedContext(id, "ACTIVE", [new("TASK", "TASK-1", "Công việc kết nối doanh nghiệp", "2026-10-20", null, "Quá hạn")],
                System.Text.Json.JsonSerializer.Serialize(payload), true, 1, 30, 3, true));
        }
        public Task<ReportBoundedContext> RetrieveReportContextAsync(long id, long report, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class DeniedProject : IAiContextRetriever
    {
        public Task<ProjectBoundedContext> RetrieveProjectContextAsync(long id, string query, CancellationToken ct = default) => throw new ForbiddenException("No project access");
        public Task<ReportBoundedContext> RetrieveReportContextAsync(long id, long report, CancellationToken ct = default) => throw new ForbiddenException("No project access");
    }
}
