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
        var result = await Service(db, student, new Provider("not json")).AskAsync(new("Hello"), default);
        Assert.Equal("FALLBACK", result.GenerationStatus);
        Assert.Empty(result.Citations);
        Assert.Contains("kết nối", result.Answer);
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

    private AcademicKnowledgeService Service(AipmsDbContext db, User actor, IAiTextGenerationProvider? provider = null, IAuditTrail? audit = null) =>
        new(db, new Current(actor), audit ?? new Audit(), provider ?? new Provider(null), new DeniedProject(), TimeProvider.System,
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
    private sealed class DeniedProject : IAiContextRetriever
    {
        public Task<ProjectBoundedContext> RetrieveProjectContextAsync(long id, string query, CancellationToken ct = default) => throw new ForbiddenException("No project access");
        public Task<ReportBoundedContext> RetrieveReportContextAsync(long id, long report, CancellationToken ct = default) => throw new ForbiddenException("No project access");
    }
}
